namespace Gma.Modules.Notifications.Persistence;

using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gma.Framework.Naming;
using Gma.Framework.Runtime.Time;
using Gma.Framework.Scoping;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ContractDeliveryPolicy = Contracts.NotificationDeliveryPolicy;
using ContractSeverity = Contracts.NotificationSeverity;
using DomainDeliveryPolicy = Domain.ValueObjects.NotificationDeliveryPolicy;
using DomainDeliveryStatus = Domain.ValueObjects.NotificationDeliveryStatus;
using DomainSeverity = Domain.ValueObjects.NotificationSeverity;

internal sealed class NotificationHistoryLifecycleService(
    NotificationsDbContext dbContext,
    IScopeContext scopeContext,
    ISystemClock clock)
    : INotificationHistoryLifecycle
{
    public async Task<NotificationHistoryReferenceSnapshot> EnsureOpenAsync(
        string scopeId,
        NotificationHistoryReference reference,
        CancellationToken cancellationToken)
    {
        if (reference is null ||
            !TryValidateScope(scopeId, scopeContext, out string normalizedScope))
        {
            return MissingSnapshot();
        }

        await using IDbContextTransaction? transaction =
            await this.BeginSerializableTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
        try
        {
            NotificationHistoryReferenceState? state =
                await this.FindStateAsync(
                        normalizedScope,
                        reference,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (state is null)
            {
                state = NotificationHistoryReferenceState
                    .Create(normalizedScope, ToDomain(reference)).Value;
                await dbContext.NotificationHistoryReferenceStates
                    .AddAsync(state, cancellationToken)
                    .ConfigureAwait(false);
            }

            long previousVersion = state.Version;
            if (!state.EnsureOpen())
            {
                await CommitAsync(transaction, cancellationToken)
                    .ConfigureAwait(false);
                return new NotificationHistoryReferenceSnapshot(
                    NotificationHistoryReferenceStatus.Closed,
                    state.Version,
                    0,
                    0);
            }

            if (state.Version != previousVersion ||
                dbContext.Entry(state).State == EntityState.Added)
            {
                await dbContext.SaveChangesAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            (int recordCount, long latestSequence) =
                await this.GetRecordSummaryAsync(
                        normalizedScope,
                        reference,
                        cancellationToken)
                    .ConfigureAwait(false);
            await CommitAsync(transaction, cancellationToken)
                .ConfigureAwait(false);
            return new NotificationHistoryReferenceSnapshot(
                state.IsClosed
                    ? NotificationHistoryReferenceStatus.Closed
                    : NotificationHistoryReferenceStatus.Open,
                state.Version,
                recordCount,
                latestSequence);
        }
        catch
        {
            await RollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<NotificationHistoryReferenceSnapshot> GetSnapshotAsync(
        string scopeId,
        NotificationHistoryReference reference,
        CancellationToken cancellationToken)
    {
        if (reference is null ||
            !TryValidateScope(scopeId, scopeContext, out string normalizedScope))
        {
            return MissingSnapshot();
        }

        NotificationHistoryReferenceState? state = await this.FindStateAsync(
                normalizedScope,
                reference,
                cancellationToken)
            .ConfigureAwait(false);
        if (state is null)
        {
            return MissingSnapshot();
        }

        (int recordCount, long latestSequence) = await this.GetRecordSummaryAsync(
                normalizedScope,
                reference,
                cancellationToken)
            .ConfigureAwait(false);
        return new NotificationHistoryReferenceSnapshot(
            state.IsClosed
                ? NotificationHistoryReferenceStatus.Closed
                : NotificationHistoryReferenceStatus.Open,
            state.Version,
            recordCount,
            latestSequence);
    }

    public async Task<NotificationHistoryReferencePage> ListAsync(
        string scopeId,
        NotificationHistoryReference reference,
        long afterStreamSequence,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (reference is null ||
            !TryValidateScope(scopeId, scopeContext, out string normalizedScope) ||
            afterStreamSequence < 0 ||
            pageSize is < 1 or >
                NotificationHistoryLifecycleLimits.MaximumPageSize)
        {
            return MissingPage();
        }

        NotificationHistoryReferenceState? state = await this.FindStateAsync(
                normalizedScope,
                reference,
                cancellationToken)
            .ConfigureAwait(false);
        if (state is null)
        {
            return MissingPage();
        }

        if (state.IsClosed)
        {
            return new NotificationHistoryReferencePage(
                NotificationHistoryReferenceStatus.Closed,
                state.Version,
                [],
                afterStreamSequence,
                false);
        }

        UserNotification[] loaded = await this.ReferencedNotifications(
                normalizedScope,
                reference)
            .Where(notification =>
                notification.StreamSequence > afterStreamSequence)
            .Include(notification => notification.Tags)
            .AsNoTracking()
            .OrderBy(notification => notification.StreamSequence)
            .ThenBy(notification => notification.Id)
            .Take(pageSize + 1)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        bool hasMore = loaded.Length > pageSize;
        UserNotification[] selected = loaded.Take(pageSize).ToArray();
        return new NotificationHistoryReferencePage(
            NotificationHistoryReferenceStatus.Open,
            state.Version,
            selected
                .Select(Map)
                .ToArray(),
            selected.Length == 0
                ? afterStreamSequence
                : selected[^1].StreamSequence,
            hasMore);
    }

    public async Task<NotificationHistoryReferenceCloseResult> CloseAsync(
        NotificationHistoryReferenceCloseRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            request.OperationId == Guid.Empty ||
            request.Reference is null ||
            request.ExpectedVersion < 0 ||
            request.ExpectedVersion == long.MaxValue ||
            request.MaximumRecords is < 1 or >
                NotificationHistoryLifecycleLimits.MaximumCloseRecords)
        {
            return Result(NotificationHistoryReferenceCloseStatus.Invalid);
        }

        if (!TryValidateScope(
                request.ScopeId,
                scopeContext,
                out string normalizedScope))
        {
            return Result(
                NotificationHistoryReferenceCloseStatus.ScopeUnavailable);
        }

        string requestSha256 = RequestSha256(request, normalizedScope);
        await using IDbContextTransaction? transaction =
            await this.BeginSerializableTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
        try
        {
            NotificationHistoryCloseReceipt? existing = await dbContext
                .NotificationHistoryCloseReceipts
                .SingleOrDefaultAsync(
                    receipt =>
                        receipt.ScopeId == normalizedScope &&
                        receipt.OperationId == request.OperationId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                NotificationHistoryReferenceCloseResult replay =
                    Matches(existing, request.Reference, requestSha256)
                        ? Result(
                            NotificationHistoryReferenceCloseStatus.Replayed,
                            Map(existing))
                        : Result(
                            NotificationHistoryReferenceCloseStatus.Conflict);
                await CommitAsync(transaction, cancellationToken)
                    .ConfigureAwait(false);
                return replay;
            }

            NotificationHistoryReferenceKey key = ToDomain(
                request.Reference);
            NotificationHistoryReferenceState? state = await this.FindStateAsync(
                    normalizedScope,
                    request.Reference,
                    cancellationToken)
                .ConfigureAwait(false);
            if (state is null)
            {
                if (request.ExpectedVersion != 0)
                {
                    await RollbackAsync(transaction).ConfigureAwait(false);
                    return Result(
                        NotificationHistoryReferenceCloseStatus.Stale);
                }

                state = NotificationHistoryReferenceState
                    .Create(normalizedScope, key).Value;
                await dbContext.NotificationHistoryReferenceStates
                    .AddAsync(state, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (state.Version != request.ExpectedVersion)
            {
                await RollbackAsync(transaction).ConfigureAwait(false);
                return Result(NotificationHistoryReferenceCloseStatus.Stale);
            }

            if (state.IsClosed)
            {
                await RollbackAsync(transaction).ConfigureAwait(false);
                return Result(
                    NotificationHistoryReferenceCloseStatus.Conflict);
            }

            Guid[] notificationIds = await this.ReferencedNotifications(
                    normalizedScope,
                    request.Reference)
                .OrderBy(notification => notification.Id)
                .Select(notification => notification.Id)
                .Take(request.MaximumRecords + 1)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            if (notificationIds.Length > request.MaximumRecords)
            {
                await RollbackAsync(transaction).ConfigureAwait(false);
                return Result(
                    NotificationHistoryReferenceCloseStatus.Overflow);
            }

            DateTimeOffset nowUtc = clock.UtcNow;
            bool activeDelivery = await dbContext.NotificationDeliveries
                .AnyAsync(
                    delivery =>
                        delivery.ScopeId == normalizedScope &&
                        notificationIds.Contains(delivery.NotificationId) &&
                        delivery.Status == DomainDeliveryStatus.Processing &&
                        delivery.LockedUntilUtc > nowUtc,
                    cancellationToken)
                .ConfigureAwait(false);
            if (activeDelivery)
            {
                await RollbackAsync(transaction).ConfigureAwait(false);
                return Result(NotificationHistoryReferenceCloseStatus.Busy);
            }

            NotificationHistoryReferenceCloseTransition transition =
                state.Close(
                    request.OperationId,
                    requestSha256,
                    nowUtc);
            if (transition !=
                NotificationHistoryReferenceCloseTransition.Completed)
            {
                await RollbackAsync(transaction).ConfigureAwait(false);
                return Result(
                    transition ==
                    NotificationHistoryReferenceCloseTransition.Conflict
                        ? NotificationHistoryReferenceCloseStatus.Conflict
                        : NotificationHistoryReferenceCloseStatus.Invalid);
            }

            string removedIdsSha256 = NotificationIdsSha256(
                notificationIds);
            NotificationHistoryCloseReceipt receipt =
                NotificationHistoryCloseReceipt.Create(
                    request.OperationId,
                    normalizedScope,
                    key,
                    requestSha256,
                    state.Version,
                    notificationIds.Length,
                    removedIdsSha256,
                    nowUtc).Value;
            await dbContext.NotificationHistoryCloseReceipts
                .AddAsync(receipt, cancellationToken)
                .ConfigureAwait(false);

            if (notificationIds.Length > 0)
            {
                await this.AdvanceCompanionReferenceVersionsAsync(
                        normalizedScope,
                        request.Reference,
                        notificationIds,
                        cancellationToken)
                    .ConfigureAwait(false);
                await this.DeleteNotificationsAsync(
                        normalizedScope,
                        notificationIds,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await dbContext.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            await CommitAsync(transaction, cancellationToken)
                .ConfigureAwait(false);
            return Result(
                NotificationHistoryReferenceCloseStatus.Completed,
                Map(receipt));
        }
        catch
        {
            await RollbackAsync(transaction).ConfigureAwait(false);
            throw;
        }
    }

    private Task<NotificationHistoryReferenceState?> FindStateAsync(
        string scopeId,
        NotificationHistoryReference reference,
        CancellationToken cancellationToken) =>
        dbContext.NotificationHistoryReferenceStates.SingleOrDefaultAsync(
            state =>
                state.ScopeId == scopeId &&
                state.Namespace == reference.Namespace &&
                state.Digest == reference.Digest,
            cancellationToken);

    private async Task<(int RecordCount, long LatestSequence)>
        GetRecordSummaryAsync(
            string scopeId,
            NotificationHistoryReference reference,
            CancellationToken cancellationToken)
    {
        IQueryable<UserNotification> query = this.ReferencedNotifications(
            scopeId,
            reference);
        var summary = await query
            .GroupBy(_ => 1)
            .Select(group => new
            {
                RecordCount = group.Count(),
                LatestSequence = group.Max(
                    notification => notification.StreamSequence)
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return summary is null
            ? (0, 0)
            : (summary.RecordCount, summary.LatestSequence);
    }

    private IQueryable<UserNotification> ReferencedNotifications(
        string scopeId,
        NotificationHistoryReference reference) =>
        dbContext.UserNotifications.Where(notification =>
            notification.ScopeId == scopeId &&
            dbContext.UserNotificationReferences.Any(candidate =>
                candidate.ScopeId == scopeId &&
                candidate.NotificationId == notification.Id &&
                candidate.Namespace == reference.Namespace &&
                candidate.Digest == reference.Digest));

    private async Task AdvanceCompanionReferenceVersionsAsync(
        string scopeId,
        NotificationHistoryReference closedReference,
        Guid[] notificationIds,
        CancellationToken cancellationToken)
    {
        IQueryable<NotificationHistoryReferenceState> affectedStates =
            dbContext.NotificationHistoryReferenceStates.Where(state =>
                state.ScopeId == scopeId &&
                !state.IsClosed &&
                (state.Namespace != closedReference.Namespace ||
                 state.Digest != closedReference.Digest) &&
                dbContext.UserNotificationReferences.Any(assignment =>
                    assignment.ScopeId == scopeId &&
                    notificationIds.Contains(assignment.NotificationId) &&
                    assignment.Namespace == state.Namespace &&
                    assignment.Digest == state.Digest));

        if (dbContext.Database.IsRelational())
        {
            await affectedStates.ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        state => state.Version,
                        state => state.Version + 1),
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        NotificationHistoryReferenceState[] loaded = await affectedStates
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (NotificationHistoryReferenceState state in loaded)
        {
            state.RecordRemoval();
        }
    }

    private async Task DeleteNotificationsAsync(
        string scopeId,
        Guid[] notificationIds,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
        {
            await dbContext.UserNotifications
                .Where(notification =>
                    notification.ScopeId == scopeId &&
                    notificationIds.Contains(notification.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        UserNotification[] notifications = await dbContext.UserNotifications
            .Where(notification =>
                notification.ScopeId == scopeId &&
                notificationIds.Contains(notification.Id))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        dbContext.UserNotifications.RemoveRange(notifications);
    }

    private async Task<IDbContextTransaction?>
        BeginSerializableTransactionAsync(
            CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational() ||
            dbContext.Database.CurrentTransaction is not null)
        {
            return null;
        }

        return await dbContext.Database
            .BeginTransactionAsync(
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

    private static bool TryValidateScope(
        string? scopeId,
        IScopeContext scopeContext,
        out string normalized)
    {
        normalized = string.Empty;
        if (!ScopeIds.TryNormalize(scopeId, out string? candidate) ||
            !scopeContext.IsEnabled ||
            !string.Equals(
                scopeContext.ScopeId,
                candidate,
                StringComparison.Ordinal))
        {
            return false;
        }

        normalized = candidate;
        return true;
    }

    private static NotificationHistoryReferenceKey ToDomain(
        NotificationHistoryReference reference) =>
        NotificationHistoryReferenceKey.Create(
            reference.Namespace,
            reference.Digest).Value;

    private static string RequestSha256(
        NotificationHistoryReferenceCloseRequest request,
        string scopeId) =>
        Sha256(
            $"gma-notification-history-close/v1|{scopeId}|{request.Reference.Namespace}|{request.Reference.Digest}|{request.ExpectedVersion}|{request.MaximumRecords}");

    private static string NotificationIdsSha256(IEnumerable<Guid> ids) =>
        Sha256(string.Join(
            '\n',
            ids.Order()
                .Select(id => id.ToString("D"))));

    private static string Sha256(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static bool Matches(
        NotificationHistoryCloseReceipt receipt,
        NotificationHistoryReference reference,
        string requestSha256) =>
        string.Equals(
            receipt.Namespace,
            reference.Namespace,
            StringComparison.Ordinal) &&
        string.Equals(
            receipt.Digest,
            reference.Digest,
            StringComparison.Ordinal) &&
        string.Equals(
            receipt.RequestSha256,
            requestSha256,
            StringComparison.Ordinal);

    private static NotificationHistoryReferenceCloseReceipt Map(
        NotificationHistoryCloseReceipt receipt) =>
        new(
            receipt.OperationId,
            new NotificationHistoryReference(
                receipt.Namespace,
                receipt.Digest),
            receipt.ResultingVersion,
            receipt.RemovedRecordCount,
            receipt.RemovedRecordIdsSha256,
            receipt.CompletedAtUtc);

    private static NotificationHistoryReferenceRecord Map(
        UserNotification notification)
    {
        using JsonDocument document = JsonDocument.Parse(
            notification.Payload.Json);
        return new NotificationHistoryReferenceRecord(
            notification.Id,
            notification.Recipient.UserId,
            notification.Source.Module,
            notification.Source.Name,
            notification.Source.Version,
            notification.Content.Title,
            notification.Content.Body,
            ToContractSeverity(notification.Severity),
            notification.StreamSequence,
            notification.OccurredAtUtc,
            notification.CreatedAtUtc,
            document.RootElement.Clone(),
            notification.Tags
                .Select(tag => tag.Key.Value)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            ToContractDeliveryPolicy(notification.DeliveryPolicy));
    }

    private static ContractSeverity ToContractSeverity(
        DomainSeverity severity) =>
        severity switch
        {
            DomainSeverity.Info => ContractSeverity.Info,
            DomainSeverity.Success => ContractSeverity.Success,
            DomainSeverity.Warning => ContractSeverity.Warning,
            DomainSeverity.Error => ContractSeverity.Error,
            _ => ContractSeverity.Unknown
        };

    private static ContractDeliveryPolicy ToContractDeliveryPolicy(
        DomainDeliveryPolicy policy) =>
        policy switch
        {
            DomainDeliveryPolicy.RespectPreferences =>
                ContractDeliveryPolicy.RespectPreferences,
            DomainDeliveryPolicy.Mandatory =>
                ContractDeliveryPolicy.Mandatory,
            _ => ContractDeliveryPolicy.Unknown
        };

    private static NotificationHistoryReferenceCloseResult Result(
        NotificationHistoryReferenceCloseStatus status,
        NotificationHistoryReferenceCloseReceipt? receipt = null) =>
        new(status, receipt);

    private static NotificationHistoryReferenceSnapshot MissingSnapshot() =>
        new(NotificationHistoryReferenceStatus.Missing, 0, 0, 0);

    private static NotificationHistoryReferencePage MissingPage() =>
        new(
            NotificationHistoryReferenceStatus.Missing,
            0,
            [],
            0,
            false);
}
