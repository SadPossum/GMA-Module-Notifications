namespace Gma.Modules.Notifications.Persistence;

using System.Data;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using DomainDeliveryStatus =
    Domain.ValueObjects.NotificationDeliveryStatus;

internal sealed partial class NotificationHistoryLifecycleService
{
    public async Task<NotificationHistoryReferenceCloseBatchResult>
        CloseBatchAsync(
            NotificationHistoryReferenceCloseBatchRequest request,
            CancellationToken cancellationToken)
    {
        if (request is null ||
            request.OperationId == Guid.Empty ||
            request.Reference is null ||
            request.ExpectedVersion < 0 ||
            request.ExpectedVersion == long.MaxValue ||
            request.BatchSize is < 1 or >
                NotificationHistoryLifecycleLimits.MaximumCloseBatchSize)
        {
            return BatchResult(
                NotificationHistoryReferenceCloseBatchStatus.Invalid);
        }

        if (!TryValidateScope(
                request.ScopeId,
                scopeContext,
                out string normalizedScope))
        {
            return BatchResult(
                NotificationHistoryReferenceCloseBatchStatus.ScopeUnavailable);
        }

        NotificationHistoryReferenceKey key = ToDomain(request.Reference);
        string requestSha256 = BatchRequestSha256(request, normalizedScope);
        await using IDbContextTransaction? transaction =
            await this.BeginSerializableTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
        try
        {
            NotificationHistoryBatchCloseReceipt? existingReceipt =
                await dbContext.NotificationHistoryBatchCloseReceipts
                    .SingleOrDefaultAsync(
                        receipt =>
                            receipt.ScopeId == normalizedScope &&
                            receipt.OperationId == request.OperationId,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (existingReceipt is not null)
            {
                NotificationHistoryReferenceCloseBatchResult replay =
                    existingReceipt.Matches(key, requestSha256)
                        ? BatchResult(
                            NotificationHistoryReferenceCloseBatchStatus
                                .Replayed,
                            receipt: Map(existingReceipt))
                        : BatchResult(
                            NotificationHistoryReferenceCloseBatchStatus
                                .Conflict);
                await CommitAsync(transaction, cancellationToken)
                    .ConfigureAwait(false);
                return replay;
            }

            NotificationHistoryBatchCloseOperation? operation =
                await dbContext.NotificationHistoryBatchCloseOperations
                    .SingleOrDefaultAsync(
                        candidate =>
                            candidate.ScopeId == normalizedScope &&
                            candidate.OperationId == request.OperationId,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (operation is not null &&
                !operation.Matches(key, requestSha256))
            {
                await RollbackAsync(transaction).ConfigureAwait(false);
                return BatchResult(
                    NotificationHistoryReferenceCloseBatchStatus.Conflict);
            }

            NotificationHistoryReferenceState? state =
                await this.FindStateAsync(
                        normalizedScope,
                        request.Reference,
                        cancellationToken)
                    .ConfigureAwait(false);
            bool addState = false;
            if (operation is null)
            {
                NotificationHistoryBatchCloseOperation? referenceOperation =
                    await dbContext.NotificationHistoryBatchCloseOperations
                        .SingleOrDefaultAsync(
                            candidate =>
                                candidate.ScopeId == normalizedScope &&
                                candidate.Namespace == key.Namespace &&
                                candidate.Digest == key.Digest,
                            cancellationToken)
                        .ConfigureAwait(false);
                if (referenceOperation is not null)
                {
                    await RollbackAsync(transaction).ConfigureAwait(false);
                    return BatchResult(
                        NotificationHistoryReferenceCloseBatchStatus.Conflict);
                }

                if (state is null)
                {
                    if (request.ExpectedVersion != 0)
                    {
                        await RollbackAsync(transaction).ConfigureAwait(false);
                        return BatchResult(
                            NotificationHistoryReferenceCloseBatchStatus.Stale);
                    }

                    state = NotificationHistoryReferenceState
                        .Create(normalizedScope, key).Value;
                    addState = true;
                }

                if (state.Version != request.ExpectedVersion)
                {
                    await RollbackAsync(transaction).ConfigureAwait(false);
                    return BatchResult(
                        NotificationHistoryReferenceCloseBatchStatus.Stale);
                }

                if (state.IsClosed)
                {
                    await RollbackAsync(transaction).ConfigureAwait(false);
                    return BatchResult(
                        NotificationHistoryReferenceCloseBatchStatus.Conflict);
                }
            }
            else if (!Matches(operation, state, requestSha256))
            {
                await RollbackAsync(transaction).ConfigureAwait(false);
                return BatchResult(
                    NotificationHistoryReferenceCloseBatchStatus.Conflict);
            }

            Guid[] loadedIds = await this.ReferencedNotifications(
                    normalizedScope,
                    request.Reference)
                .OrderBy(notification => notification.Id)
                .Select(notification => notification.Id)
                .Take(request.BatchSize + 1)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            Guid[] selectedIds = loadedIds.Take(request.BatchSize).ToArray();
            DateTimeOffset nowUtc = clock.UtcNow;
            if (await this.HasActiveDeliveryAsync(
                    normalizedScope,
                    selectedIds,
                    nowUtc,
                    cancellationToken).ConfigureAwait(false))
            {
                await RollbackAsync(transaction).ConfigureAwait(false);
                return BatchResult(
                    NotificationHistoryReferenceCloseBatchStatus.Busy,
                    operation is null ? null : Map(operation));
            }

            if (operation is null)
            {
                if (addState)
                {
                    await dbContext.NotificationHistoryReferenceStates
                        .AddAsync(state!, cancellationToken)
                        .ConfigureAwait(false);
                }

                NotificationHistoryReferenceCloseTransition transition =
                    state!.Close(
                        request.OperationId,
                        requestSha256,
                        nowUtc);
                if (transition !=
                    NotificationHistoryReferenceCloseTransition.Completed)
                {
                    await RollbackAsync(transaction).ConfigureAwait(false);
                    return BatchResult(
                        NotificationHistoryReferenceCloseBatchStatus.Conflict);
                }

                operation = NotificationHistoryBatchCloseOperation.Create(
                    request.OperationId,
                    normalizedScope,
                    key,
                    requestSha256,
                    request.ExpectedVersion,
                    state.Version,
                    request.BatchSize,
                    nowUtc,
                    NotificationHistoryLifecycleLimits.MaximumCloseBatchSize)
                    .Value;
                await dbContext.NotificationHistoryBatchCloseOperations
                    .AddAsync(operation, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (selectedIds.Length > 0)
            {
                await this.AdvanceCompanionReferenceVersionsAsync(
                        normalizedScope,
                        request.Reference,
                        selectedIds,
                        cancellationToken)
                    .ConfigureAwait(false);
                await this.DeleteNotificationsAsync(
                        normalizedScope,
                        selectedIds,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!operation.RecordBatch(
                        selectedIds.Length,
                        NotificationIdsSha256(selectedIds),
                        nowUtc))
                {
                    throw new InvalidDataException(
                        "The notification history batch-close progress is invalid.");
                }
            }

            bool hasMore = loadedIds.Length > request.BatchSize;
            if (hasMore)
            {
                await dbContext.SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false);
                await CommitAsync(transaction, cancellationToken)
                    .ConfigureAwait(false);
                return BatchResult(
                    NotificationHistoryReferenceCloseBatchStatus.InProgress,
                    Map(operation));
            }

            NotificationHistoryBatchCloseReceipt receipt =
                NotificationHistoryBatchCloseReceipt.Create(
                    operation,
                    nowUtc).Value;
            await dbContext.NotificationHistoryBatchCloseReceipts
                .AddAsync(receipt, cancellationToken)
                .ConfigureAwait(false);
            dbContext.NotificationHistoryBatchCloseOperations.Remove(operation);
            await dbContext.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            await CommitAsync(transaction, cancellationToken)
                .ConfigureAwait(false);
            return BatchResult(
                NotificationHistoryReferenceCloseBatchStatus.Completed,
                receipt: Map(receipt));
        }
        catch
        {
            await RollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<bool> HasActiveDeliveryAsync(
        string scopeId,
        Guid[] notificationIds,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (notificationIds.Length == 0)
        {
            return false;
        }

        return await dbContext.NotificationDeliveries.AnyAsync(
                delivery =>
                    delivery.ScopeId == scopeId &&
                    notificationIds.Contains(delivery.NotificationId) &&
                    delivery.Status == DomainDeliveryStatus.Processing &&
                    delivery.LockedUntilUtc > nowUtc,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool Matches(
        NotificationHistoryBatchCloseOperation operation,
        NotificationHistoryReferenceState? state,
        string requestSha256) =>
        state is not null &&
        state.IsClosed &&
        state.CloseOperationId == operation.OperationId &&
        state.Version == operation.ResultingVersion &&
        string.Equals(
            state.CloseRequestSha256,
            requestSha256,
            StringComparison.Ordinal);

    private static string BatchRequestSha256(
        NotificationHistoryReferenceCloseBatchRequest request,
        string scopeId) =>
        Sha256(
            $"gma-notification-history-batch-close/v1|{scopeId}|" +
            $"{request.Reference.Namespace}|{request.Reference.Digest}|" +
            $"{request.ExpectedVersion}|{request.BatchSize}");

    private static NotificationHistoryReferenceCloseBatchProgress Map(
        NotificationHistoryBatchCloseOperation operation) =>
        new(
            operation.OperationId,
            new NotificationHistoryReference(
                operation.Namespace,
                operation.Digest),
            operation.ResultingVersion,
            operation.RemovedRecordCount,
            operation.CompletedBatchCount,
            operation.ProofVersion,
            operation.RemovalProofSha256,
            operation.StartedAtUtc,
            operation.UpdatedAtUtc);

    private static NotificationHistoryReferenceCloseBatchReceipt Map(
        NotificationHistoryBatchCloseReceipt receipt) =>
        new(
            receipt.OperationId,
            new NotificationHistoryReference(
                receipt.Namespace,
                receipt.Digest),
            receipt.ResultingVersion,
            receipt.RemovedRecordCount,
            receipt.CompletedBatchCount,
            receipt.RemovalProofVersion,
            receipt.RemovalProofSha256,
            receipt.StartedAtUtc,
            receipt.CompletedAtUtc);

    private static NotificationHistoryReferenceCloseBatchResult BatchResult(
        NotificationHistoryReferenceCloseBatchStatus status,
        NotificationHistoryReferenceCloseBatchProgress? progress = null,
        NotificationHistoryReferenceCloseBatchReceipt? receipt = null) =>
        new(status, progress, receipt);
}
