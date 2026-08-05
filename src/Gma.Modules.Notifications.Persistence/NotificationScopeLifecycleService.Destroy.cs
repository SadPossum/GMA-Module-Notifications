namespace Gma.Modules.Notifications.Persistence;

using System.Data;
using System.Security.Cryptography;
using System.Text;
using Gma.Framework.Messaging.Infrastructure;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ContractDestroyReceipt =
    Application.Ports.NotificationScopeDestroyReceipt;
using DomainDestroyOperation =
    Domain.Entities.NotificationScopeDestroyOperation;
using DomainDestroyReceipt = Domain.Entities.NotificationScopeDestroyReceipt;
using DomainDestroyStage = Domain.Entities.NotificationScopeDestroyStage;
using DomainDeliveryStatus = Domain.ValueObjects.NotificationDeliveryStatus;

internal sealed partial class NotificationScopeLifecycleService
{
    public async Task<NotificationScopeDestroyResult> DestroyBatchAsync(
        NotificationScopeDestroyRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            request.OperationId == Guid.Empty ||
            request.ExpectedRevision < 0 ||
            request.ExpectedRevision == long.MaxValue ||
            request.BatchSize is < 1 or >
                NotificationScopeLifecycleLimits.MaximumDestroyBatchSize)
        {
            return DestroyResult(NotificationScopeDestroyStatus.Invalid);
        }

        if (!TryValidateScope(
                request.ScopeId,
                scopeContext,
                out string normalizedScope))
        {
            return DestroyResult(
                NotificationScopeDestroyStatus.ScopeUnavailable);
        }

        string requestSha256 = DestroyRequestSha256(
            request,
            normalizedScope);
        await using IDbContextTransaction? transaction =
            await this.BeginSerializableTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
        try
        {
            DomainDestroyReceipt? existingReceipt = await dbContext
                .NotificationScopeDestroyReceipts
                .SingleOrDefaultAsync(
                    receipt => receipt.ScopeId == normalizedScope,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existingReceipt is not null)
            {
                NotificationScopeDestroyResult replay = existingReceipt.Matches(
                    request.OperationId,
                    requestSha256)
                    ? DestroyResult(
                        NotificationScopeDestroyStatus.Replayed,
                        receipt: Map(existingReceipt))
                    : DestroyResult(NotificationScopeDestroyStatus.Conflict);
                await CommitAsync(transaction, cancellationToken)
                    .ConfigureAwait(false);
                return replay;
            }

            DomainDestroyOperation? operation = await dbContext
                .NotificationScopeDestroyOperations
                .SingleOrDefaultAsync(
                    candidate => candidate.ScopeId == normalizedScope,
                    cancellationToken)
                .ConfigureAwait(false);
            if (operation is not null &&
                !operation.Matches(request.OperationId, requestSha256))
            {
                await RollbackAsync(transaction).ConfigureAwait(false);
                return DestroyResult(NotificationScopeDestroyStatus.Conflict);
            }

            NotificationScopeState? state = await dbContext
                .NotificationScopeStates
                .SingleOrDefaultAsync(
                    candidate => candidate.ScopeId == normalizedScope,
                    cancellationToken)
                .ConfigureAwait(false);
            bool activeWorkChecked = false;
            if (operation is null)
            {
                if (state is null)
                {
                    if (request.ExpectedRevision != 0)
                    {
                        await RollbackAsync(transaction).ConfigureAwait(false);
                        return DestroyResult(
                            NotificationScopeDestroyStatus.Stale);
                    }

                    state = NotificationScopeState.Create(normalizedScope).Value;
                    await dbContext.NotificationScopeStates.AddAsync(
                            state,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                if (state.Version != request.ExpectedRevision)
                {
                    await RollbackAsync(transaction).ConfigureAwait(false);
                    return DestroyResult(NotificationScopeDestroyStatus.Stale);
                }

                if (state.IsClosed)
                {
                    await RollbackAsync(transaction).ConfigureAwait(false);
                    return DestroyResult(NotificationScopeDestroyStatus.Conflict);
                }

                if (await this.HasActiveScopeWorkAsync(
                        normalizedScope,
                        cancellationToken).ConfigureAwait(false))
                {
                    await RollbackAsync(transaction).ConfigureAwait(false);
                    return DestroyResult(NotificationScopeDestroyStatus.Busy);
                }

                activeWorkChecked = true;

                DateTimeOffset startedAtUtc = clock.UtcNow;
                if (state.Close(
                        request.OperationId,
                        requestSha256,
                        startedAtUtc) !=
                    NotificationScopeCloseTransition.Completed)
                {
                    await RollbackAsync(transaction).ConfigureAwait(false);
                    return DestroyResult(NotificationScopeDestroyStatus.Conflict);
                }

                operation = DomainDestroyOperation.Create(
                    normalizedScope,
                    request.OperationId,
                    requestSha256,
                    request.ExpectedRevision,
                    state.Version,
                    request.BatchSize,
                    NotificationScopeLifecycleLimits.MaximumDestroyBatchSize,
                    startedAtUtc).Value;
                await dbContext.NotificationScopeDestroyOperations.AddAsync(
                        operation,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (!Matches(operation, state, requestSha256))
            {
                await RollbackAsync(transaction).ConfigureAwait(false);
                return DestroyResult(NotificationScopeDestroyStatus.Conflict);
            }

            if (!activeWorkChecked &&
                await this.HasActiveScopeWorkAsync(
                    normalizedScope,
                    cancellationToken).ConfigureAwait(false))
            {
                await RollbackAsync(transaction).ConfigureAwait(false);
                return DestroyResult(
                    NotificationScopeDestroyStatus.Busy,
                    Map(operation));
            }

            while (!operation.IsComplete)
            {
                Guid[] loadedIds = await this.LoadStageIdsAsync(
                        normalizedScope,
                        operation.Stage,
                        operation.BatchSize + 1,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (loadedIds.Length == 0)
                {
                    if (!operation.AdvanceEmptyStage(clock.UtcNow))
                    {
                        throw new InvalidDataException(
                            "Notification scope destruction stage progress is invalid.");
                    }

                    continue;
                }

                Guid[] selectedIds = loadedIds
                    .Take(operation.BatchSize)
                    .ToArray();
                await this.DeleteStageIdsAsync(
                        normalizedScope,
                        operation.Stage,
                        selectedIds,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!operation.RecordBatch(
                        operation.Stage,
                        selectedIds.Length,
                        IdsSha256(selectedIds),
                        stageCompleted: loadedIds.Length <= operation.BatchSize,
                        clock.UtcNow))
                {
                    throw new InvalidDataException(
                        "Notification scope destruction batch progress is invalid.");
                }

                break;
            }

            if (!operation.IsComplete)
            {
                await dbContext.SaveScopeDestructionChangesAsync(
                        normalizedScope,
                        request.OperationId,
                        cancellationToken)
                    .ConfigureAwait(false);
                await CommitAsync(transaction, cancellationToken)
                    .ConfigureAwait(false);
                return DestroyResult(
                    NotificationScopeDestroyStatus.InProgress,
                    Map(operation));
            }

            DomainDestroyReceipt receipt = DomainDestroyReceipt.Create(
                operation,
                clock.UtcNow).Value;
            await dbContext.NotificationScopeDestroyReceipts.AddAsync(
                    receipt,
                    cancellationToken)
                .ConfigureAwait(false);
            dbContext.NotificationScopeDestroyOperations.Remove(operation);
            await dbContext.SaveScopeDestructionChangesAsync(
                    normalizedScope,
                    request.OperationId,
                    cancellationToken)
                .ConfigureAwait(false);
            await CommitAsync(transaction, cancellationToken)
                .ConfigureAwait(false);
            return DestroyResult(
                NotificationScopeDestroyStatus.Completed,
                receipt: Map(receipt));
        }
        catch
        {
            await RollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<bool> HasActiveScopeWorkAsync(
        string scopeId,
        CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = clock.UtcNow;
        return await dbContext.NotificationHistoryBatchCloseOperations
                .AnyAsync(
                    operation => operation.ScopeId == scopeId,
                    cancellationToken)
                .ConfigureAwait(false) ||
            await dbContext.NotificationDeliveries.AnyAsync(
                    delivery =>
                        delivery.ScopeId == scopeId &&
                        delivery.Status == DomainDeliveryStatus.Processing &&
                        delivery.LockedUntilUtc > nowUtc,
                    cancellationToken)
                .ConfigureAwait(false);
    }

    private Task<Guid[]> LoadStageIdsAsync(
        string scopeId,
        DomainDestroyStage stage,
        int take,
        CancellationToken cancellationToken)
    {
        string recipientScope = NotificationBroadcastRead
            .CreateRecipientScope(scopeId).Value;
        return stage switch
        {
            DomainDestroyStage.InboxMessages => dbContext.InboxMessages
                .Where(message => message.ScopeId == scopeId)
                .OrderBy(message => message.Id)
                .Select(message => message.Id)
                .Take(take)
                .ToArrayAsync(cancellationToken),
            DomainDestroyStage.TenantBroadcastReads => dbContext
                .NotificationBroadcastReads
                .Where(read => read.RecipientScope == recipientScope)
                .OrderBy(read => read.Id)
                .Select(read => read.Id)
                .Take(take)
                .ToArrayAsync(cancellationToken),
            DomainDestroyStage.TenantBroadcasts => dbContext
                .NotificationBroadcasts
                .Where(broadcast => broadcast.ScopeId == scopeId)
                .OrderBy(broadcast => broadcast.Id)
                .Select(broadcast => broadcast.Id)
                .Take(take)
                .ToArrayAsync(cancellationToken),
            DomainDestroyStage.Preferences => dbContext
                .NotificationPreferences
                .Where(preference => preference.ScopeId == scopeId)
                .OrderBy(preference => preference.Id)
                .Select(preference => preference.Id)
                .Take(take)
                .ToArrayAsync(cancellationToken),
            DomainDestroyStage.DeliveryRoutes => dbContext
                .NotificationDeliveryRoutes
                .Where(route => route.ScopeId == scopeId)
                .OrderBy(route => route.Id)
                .Select(route => route.Id)
                .Take(take)
                .ToArrayAsync(cancellationToken),
            DomainDestroyStage.TagDefinitions => dbContext
                .NotificationTagDefinitions
                .Where(definition => definition.ScopeId == scopeId)
                .OrderBy(definition => definition.Id)
                .Select(definition => definition.Id)
                .Take(take)
                .ToArrayAsync(cancellationToken),
            DomainDestroyStage.UserNotifications => dbContext
                .UserNotifications
                .Where(notification => notification.ScopeId == scopeId)
                .OrderBy(notification => notification.Id)
                .Select(notification => notification.Id)
                .Take(take)
                .ToArrayAsync(cancellationToken),
            _ => throw new InvalidOperationException(
                "The notification scope destruction stage is invalid.")
        };
    }

    private async Task DeleteStageIdsAsync(
        string scopeId,
        DomainDestroyStage stage,
        Guid[] ids,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
        {
            await this.DeleteRelationalStageIdsAsync(
                    scopeId,
                    stage,
                    ids,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        string recipientScope = NotificationBroadcastRead
            .CreateRecipientScope(scopeId).Value;
        switch (stage)
        {
            case DomainDestroyStage.InboxMessages:
                dbContext.InboxMessages.RemoveRange(await dbContext.InboxMessages
                    .Where(message =>
                        message.ScopeId == scopeId && ids.Contains(message.Id))
                    .ToArrayAsync(cancellationToken).ConfigureAwait(false));
                break;
            case DomainDestroyStage.TenantBroadcastReads:
                dbContext.NotificationBroadcastReads.RemoveRange(
                    await dbContext.NotificationBroadcastReads
                        .Where(read =>
                            read.RecipientScope == recipientScope &&
                            ids.Contains(read.Id))
                        .ToArrayAsync(cancellationToken).ConfigureAwait(false));
                break;
            case DomainDestroyStage.TenantBroadcasts:
                dbContext.NotificationBroadcasts.RemoveRange(
                    await dbContext.NotificationBroadcasts
                        .Where(broadcast =>
                            broadcast.ScopeId == scopeId &&
                            ids.Contains(broadcast.Id))
                        .ToArrayAsync(cancellationToken).ConfigureAwait(false));
                break;
            case DomainDestroyStage.Preferences:
                dbContext.NotificationPreferences.RemoveRange(
                    await dbContext.NotificationPreferences
                        .Where(preference =>
                            preference.ScopeId == scopeId &&
                            ids.Contains(preference.Id))
                        .ToArrayAsync(cancellationToken).ConfigureAwait(false));
                break;
            case DomainDestroyStage.DeliveryRoutes:
                dbContext.NotificationDeliveryRoutes.RemoveRange(
                    await dbContext.NotificationDeliveryRoutes
                        .Where(route =>
                            route.ScopeId == scopeId && ids.Contains(route.Id))
                        .ToArrayAsync(cancellationToken).ConfigureAwait(false));
                break;
            case DomainDestroyStage.TagDefinitions:
                dbContext.NotificationTagDefinitions.RemoveRange(
                    await dbContext.NotificationTagDefinitions
                        .Where(definition =>
                            definition.ScopeId == scopeId &&
                            ids.Contains(definition.Id))
                        .ToArrayAsync(cancellationToken).ConfigureAwait(false));
                break;
            case DomainDestroyStage.UserNotifications:
                dbContext.UserNotifications.RemoveRange(
                    await dbContext.UserNotifications
                        .Where(notification =>
                            notification.ScopeId == scopeId &&
                            ids.Contains(notification.Id))
                        .ToArrayAsync(cancellationToken).ConfigureAwait(false));
                break;
            case DomainDestroyStage.Unknown:
            case DomainDestroyStage.Completed:
            default:
                throw new InvalidOperationException(
                    "The notification scope destruction stage is invalid.");
        }
    }

    private async Task DeleteRelationalStageIdsAsync(
        string scopeId,
        DomainDestroyStage stage,
        Guid[] ids,
        CancellationToken cancellationToken)
    {
        string recipientScope = NotificationBroadcastRead
            .CreateRecipientScope(scopeId).Value;
        _ = stage switch
        {
            DomainDestroyStage.InboxMessages => await dbContext.InboxMessages
                .Where(message =>
                    message.ScopeId == scopeId && ids.Contains(message.Id))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false),
            DomainDestroyStage.TenantBroadcastReads => await dbContext
                .NotificationBroadcastReads
                .Where(read =>
                    read.RecipientScope == recipientScope &&
                    ids.Contains(read.Id))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false),
            DomainDestroyStage.TenantBroadcasts => await dbContext
                .NotificationBroadcasts
                .Where(broadcast =>
                    broadcast.ScopeId == scopeId && ids.Contains(broadcast.Id))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false),
            DomainDestroyStage.Preferences => await dbContext
                .NotificationPreferences
                .Where(preference =>
                    preference.ScopeId == scopeId &&
                    ids.Contains(preference.Id))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false),
            DomainDestroyStage.DeliveryRoutes => await dbContext
                .NotificationDeliveryRoutes
                .Where(route =>
                    route.ScopeId == scopeId && ids.Contains(route.Id))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false),
            DomainDestroyStage.TagDefinitions => await dbContext
                .NotificationTagDefinitions
                .Where(definition =>
                    definition.ScopeId == scopeId &&
                    ids.Contains(definition.Id))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false),
            DomainDestroyStage.UserNotifications => await dbContext
                .UserNotifications
                .Where(notification =>
                    notification.ScopeId == scopeId &&
                    ids.Contains(notification.Id))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                "The notification scope destruction stage is invalid.")
        };
    }

    private async Task<IDbContextTransaction?> BeginSerializableTransactionAsync(
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational() ||
            dbContext.Database.CurrentTransaction is not null)
        {
            return null;
        }

        return await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task RollbackAsync(
        IDbContextTransaction? transaction)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private static bool Matches(
        DomainDestroyOperation operation,
        NotificationScopeState? state,
        string requestSha256) =>
        state is not null &&
        state.IsClosed &&
        state.CloseOperationId == operation.OperationId &&
        state.Version == operation.ResultingRevision &&
        string.Equals(
            state.CloseRequestSha256,
            requestSha256,
            StringComparison.Ordinal);

    private static string DestroyRequestSha256(
        NotificationScopeDestroyRequest request,
        string scopeId) =>
        Sha256(
            $"gma-notification-scope-destroy/v1|{scopeId}|" +
            $"{request.ExpectedRevision}|{request.BatchSize}");

    private static string IdsSha256(IEnumerable<Guid> ids) =>
        Sha256(string.Join(
            '\n',
            ids.Order().Select(id => id.ToString("D"))));

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static NotificationScopeDestroyProgress Map(
        DomainDestroyOperation operation) =>
        new(
            operation.OperationId,
            operation.ResultingRevision,
            operation.BatchSize,
            ToContract(operation.Stage),
            operation.RemovedRecordCount,
            operation.CompletedBatchCount,
            operation.ProofVersion,
            operation.RemovalProofSha256,
            operation.StartedAtUtc,
            operation.UpdatedAtUtc);

    private static ContractDestroyReceipt Map(DomainDestroyReceipt receipt) =>
        new(
            receipt.OperationId,
            receipt.ResultingRevision,
            receipt.BatchSize,
            receipt.RemovedRecordCount,
            receipt.CompletedBatchCount,
            receipt.RemovalProofVersion,
            receipt.RemovalProofSha256,
            receipt.StartedAtUtc,
            receipt.CompletedAtUtc);

    private static NotificationScopeDestructionStage ToContract(
        DomainDestroyStage stage) =>
        stage switch
        {
            DomainDestroyStage.InboxMessages =>
                NotificationScopeDestructionStage.InboxMessages,
            DomainDestroyStage.TenantBroadcastReads =>
                NotificationScopeDestructionStage.TenantBroadcastReads,
            DomainDestroyStage.TenantBroadcasts =>
                NotificationScopeDestructionStage.TenantBroadcasts,
            DomainDestroyStage.Preferences =>
                NotificationScopeDestructionStage.Preferences,
            DomainDestroyStage.DeliveryRoutes =>
                NotificationScopeDestructionStage.DeliveryRoutes,
            DomainDestroyStage.TagDefinitions =>
                NotificationScopeDestructionStage.TagDefinitions,
            DomainDestroyStage.UserNotifications =>
                NotificationScopeDestructionStage.UserNotifications,
            DomainDestroyStage.Completed =>
                NotificationScopeDestructionStage.Completed,
            _ => NotificationScopeDestructionStage.Unknown
        };

    private static NotificationScopeDestroyResult DestroyResult(
        NotificationScopeDestroyStatus status,
        NotificationScopeDestroyProgress? progress = null,
        ContractDestroyReceipt? receipt = null) =>
        new(status, progress, receipt);
}
