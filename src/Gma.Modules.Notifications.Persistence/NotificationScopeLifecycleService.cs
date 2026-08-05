namespace Gma.Modules.Notifications.Persistence;

using Gma.Framework.Naming;
using Gma.Framework.Runtime.Time;
using Gma.Framework.Scoping;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

internal sealed partial class NotificationScopeLifecycleService(
    NotificationsDbContext dbContext,
    IScopeContext scopeContext,
    ISystemClock clock)
    : INotificationScopeLifecycle
{
    private const string IdCursorPrefix = "id:";
    private const string ReferenceCursorPrefix = "reference:";

    public async Task<NotificationScopeSnapshot> GetSnapshotAsync(
        string scopeId,
        CancellationToken cancellationToken)
    {
        if (!TryValidateScope(scopeId, scopeContext, out string normalizedScope))
        {
            return new NotificationScopeSnapshot(
                NotificationScopeStatus.ScopeUnavailable,
                0);
        }

        ScopeStateSnapshot? state = await this.ReadStateAsync(
                normalizedScope,
                cancellationToken)
            .ConfigureAwait(false);
        return state switch
        {
            null => new NotificationScopeSnapshot(
                NotificationScopeStatus.Missing,
                0),
            { IsClosed: true } => new NotificationScopeSnapshot(
                NotificationScopeStatus.Closed,
                state.Version),
            _ => new NotificationScopeSnapshot(
                NotificationScopeStatus.Open,
                state.Version)
        };
    }

    public async Task<NotificationScopeExportPage> ExportAsync(
        NotificationScopeExportRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            request.ExpectedRevision < 0 ||
            request.ExpectedRevision == long.MaxValue ||
            request.PageSize is < 1 or >
                NotificationScopeLifecycleLimits.MaximumPageSize ||
            request.Store is <= NotificationScopeExportStore.Unknown or >
                NotificationScopeExportStore.HistoryBatchCloseReceipts ||
            (request.AfterCursor is not null &&
             (request.AfterCursor.Length == 0 ||
              request.AfterCursor.Length >
                NotificationScopeLifecycleLimits.MaximumCursorLength ||
              request.AfterCursor.Any(char.IsControl))))
        {
            return EmptyPage(
                NotificationScopeExportStatus.Invalid,
                0,
                request?.Store ?? NotificationScopeExportStore.Unknown);
        }

        if (!TryValidateScope(
                request.ScopeId,
                scopeContext,
                out string normalizedScope))
        {
            return EmptyPage(
                NotificationScopeExportStatus.ScopeUnavailable,
                0,
                request.Store);
        }

        ScopeStateSnapshot? state = await this.ReadStateAsync(
                normalizedScope,
                cancellationToken)
            .ConfigureAwait(false);
        if (state is null)
        {
            return EmptyPage(
                request.ExpectedRevision == 0
                    ? NotificationScopeExportStatus.Missing
                    : NotificationScopeExportStatus.Stale,
                0,
                request.Store);
        }

        if (state.IsClosed)
        {
            return EmptyPage(
                NotificationScopeExportStatus.Closed,
                state.Version,
                request.Store);
        }

        if (state.Version != request.ExpectedRevision)
        {
            return EmptyPage(
                NotificationScopeExportStatus.Stale,
                state.Version,
                request.Store);
        }

        return request.Store ==
            NotificationScopeExportStore.HistoryReferenceStates
            ? await this.ExportReferenceStatesAsync(
                    normalizedScope,
                    request,
                    cancellationToken)
                .ConfigureAwait(false)
            : await this.ExportGuidStoreAsync(
                    normalizedScope,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
    }

    private async Task<NotificationScopeExportPage> ExportGuidStoreAsync(
        string scopeId,
        NotificationScopeExportRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdCursor(request.AfterCursor, out Guid? afterId))
        {
            return EmptyPage(
                NotificationScopeExportStatus.Invalid,
                request.ExpectedRevision,
                request.Store);
        }

        return request.Store switch
        {
            NotificationScopeExportStore.UserNotifications =>
                await this.ExportUserNotificationsAsync(
                    scopeId,
                    request,
                    afterId,
                    cancellationToken).ConfigureAwait(false),
            NotificationScopeExportStore.Preferences =>
                await this.ExportPreferencesAsync(
                    scopeId,
                    request,
                    afterId,
                    cancellationToken).ConfigureAwait(false),
            NotificationScopeExportStore.DeliveryRoutes =>
                await this.ExportDeliveryRoutesAsync(
                    scopeId,
                    request,
                    afterId,
                    cancellationToken).ConfigureAwait(false),
            NotificationScopeExportStore.TagDefinitions =>
                await this.ExportTagDefinitionsAsync(
                    scopeId,
                    request,
                    afterId,
                    cancellationToken).ConfigureAwait(false),
            NotificationScopeExportStore.Deliveries =>
                await this.ExportDeliveriesAsync(
                    scopeId,
                    request,
                    afterId,
                    cancellationToken).ConfigureAwait(false),
            NotificationScopeExportStore.DeliveryAttempts =>
                await this.ExportDeliveryAttemptsAsync(
                    scopeId,
                    request,
                    afterId,
                    cancellationToken).ConfigureAwait(false),
            NotificationScopeExportStore.TenantBroadcasts =>
                await this.ExportBroadcastsAsync(
                    scopeId,
                    request,
                    afterId,
                    cancellationToken).ConfigureAwait(false),
            NotificationScopeExportStore.TenantBroadcastReads =>
                await this.ExportBroadcastReadsAsync(
                    scopeId,
                    request,
                    afterId,
                    cancellationToken).ConfigureAwait(false),
            NotificationScopeExportStore.HistoryCloseReceipts =>
                await this.ExportCloseReceiptsAsync(
                    scopeId,
                    request,
                    afterId,
                    cancellationToken).ConfigureAwait(false),
            NotificationScopeExportStore.HistoryBatchCloseOperations =>
                await this.ExportBatchCloseOperationsAsync(
                    scopeId,
                    request,
                    afterId,
                    cancellationToken).ConfigureAwait(false),
            NotificationScopeExportStore.HistoryBatchCloseReceipts =>
                await this.ExportBatchCloseReceiptsAsync(
                    scopeId,
                    request,
                    afterId,
                    cancellationToken).ConfigureAwait(false),
            _ => EmptyPage(
                NotificationScopeExportStatus.Invalid,
                request.ExpectedRevision,
                request.Store)
        };
    }

    private async Task<NotificationScopeExportPage>
        ExportUserNotificationsAsync(
            string scopeId,
            NotificationScopeExportRequest request,
            Guid? afterId,
            CancellationToken cancellationToken)
    {
        IQueryable<UserNotification> query = dbContext.UserNotifications
            .Where(notification => notification.ScopeId == scopeId);
        if (afterId.HasValue)
        {
            Guid cursor = afterId.Value;
            query = query.Where(notification =>
                notification.Id.CompareTo(cursor) > 0);
        }

        UserNotification[] loaded = await query
            .Include(notification => notification.Tags)
            .Include(notification => notification.References)
            .AsNoTracking()
            .AsSplitQuery()
            .OrderBy(notification => notification.Id)
            .Take(request.PageSize + 1)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return await this.CompleteGuidPageAsync(
                scopeId,
                request,
                loaded,
                notification => notification.Id,
                Map,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<NotificationScopeExportPage> ExportPreferencesAsync(
        string scopeId,
        NotificationScopeExportRequest request,
        Guid? afterId,
        CancellationToken cancellationToken)
    {
        IQueryable<NotificationPreference> query = dbContext
            .NotificationPreferences
            .Where(preference => preference.ScopeId == scopeId);
        if (afterId.HasValue)
        {
            Guid cursor = afterId.Value;
            query = query.Where(preference =>
                preference.Id.CompareTo(cursor) > 0);
        }

        NotificationPreference[] loaded = await query
            .AsNoTracking()
            .OrderBy(preference => preference.Id)
            .Take(request.PageSize + 1)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return await this.CompleteGuidPageAsync(
                scopeId,
                request,
                loaded,
                preference => preference.Id,
                Map,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<NotificationScopeExportPage> ExportDeliveryRoutesAsync(
        string scopeId,
        NotificationScopeExportRequest request,
        Guid? afterId,
        CancellationToken cancellationToken)
    {
        IQueryable<NotificationDeliveryRoute> query = dbContext
            .NotificationDeliveryRoutes
            .Where(route => route.ScopeId == scopeId);
        if (afterId.HasValue)
        {
            Guid cursor = afterId.Value;
            query = query.Where(route => route.Id.CompareTo(cursor) > 0);
        }

        NotificationDeliveryRoute[] loaded = await query
            .AsNoTracking()
            .OrderBy(route => route.Id)
            .Take(request.PageSize + 1)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return await this.CompleteGuidPageAsync(
                scopeId,
                request,
                loaded,
                route => route.Id,
                Map,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<NotificationScopeExportPage> ExportTagDefinitionsAsync(
        string scopeId,
        NotificationScopeExportRequest request,
        Guid? afterId,
        CancellationToken cancellationToken)
    {
        IQueryable<NotificationTagDefinition> query = dbContext
            .NotificationTagDefinitions
            .Where(definition => definition.ScopeId == scopeId);
        if (afterId.HasValue)
        {
            Guid cursor = afterId.Value;
            query = query.Where(definition =>
                definition.Id.CompareTo(cursor) > 0);
        }

        NotificationTagDefinition[] loaded = await query
            .AsNoTracking()
            .OrderBy(definition => definition.Id)
            .Take(request.PageSize + 1)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return await this.CompleteGuidPageAsync(
                scopeId,
                request,
                loaded,
                definition => definition.Id,
                Map,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<NotificationScopeExportPage> ExportDeliveriesAsync(
        string scopeId,
        NotificationScopeExportRequest request,
        Guid? afterId,
        CancellationToken cancellationToken)
    {
        IQueryable<NotificationDelivery> query = dbContext
            .NotificationDeliveries
            .Where(delivery => delivery.ScopeId == scopeId);
        if (afterId.HasValue)
        {
            Guid cursor = afterId.Value;
            query = query.Where(delivery => delivery.Id.CompareTo(cursor) > 0);
        }

        NotificationDelivery[] loaded = await query
            .AsNoTracking()
            .OrderBy(delivery => delivery.Id)
            .Take(request.PageSize + 1)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return await this.CompleteGuidPageAsync(
                scopeId,
                request,
                loaded,
                delivery => delivery.Id,
                Map,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<NotificationScopeExportPage> ExportDeliveryAttemptsAsync(
        string scopeId,
        NotificationScopeExportRequest request,
        Guid? afterId,
        CancellationToken cancellationToken)
    {
        IQueryable<NotificationDeliveryAttempt> query = dbContext
            .NotificationDeliveryAttempts
            .Where(attempt => attempt.ScopeId == scopeId);
        if (afterId.HasValue)
        {
            Guid cursor = afterId.Value;
            query = query.Where(attempt => attempt.Id.CompareTo(cursor) > 0);
        }

        NotificationDeliveryAttempt[] loaded = await query
            .AsNoTracking()
            .OrderBy(attempt => attempt.Id)
            .Take(request.PageSize + 1)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return await this.CompleteGuidPageAsync(
                scopeId,
                request,
                loaded,
                attempt => attempt.Id,
                Map,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<NotificationScopeExportPage> ExportBroadcastsAsync(
        string scopeId,
        NotificationScopeExportRequest request,
        Guid? afterId,
        CancellationToken cancellationToken)
    {
        IQueryable<NotificationBroadcast> query = dbContext
            .NotificationBroadcasts
            .Where(broadcast => broadcast.ScopeId == scopeId);
        if (afterId.HasValue)
        {
            Guid cursor = afterId.Value;
            query = query.Where(broadcast =>
                broadcast.Id.CompareTo(cursor) > 0);
        }

        NotificationBroadcast[] loaded = await query
            .AsNoTracking()
            .OrderBy(broadcast => broadcast.Id)
            .Take(request.PageSize + 1)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return await this.CompleteGuidPageAsync(
                scopeId,
                request,
                loaded,
                broadcast => broadcast.Id,
                Map,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<NotificationScopeExportPage> ExportBroadcastReadsAsync(
        string scopeId,
        NotificationScopeExportRequest request,
        Guid? afterId,
        CancellationToken cancellationToken)
    {
        string recipientScope = NotificationBroadcastRead
            .CreateRecipientScope(scopeId).Value;
        IQueryable<NotificationBroadcastRead> query = dbContext
            .NotificationBroadcastReads
            .Where(read => read.RecipientScope == recipientScope);
        if (afterId.HasValue)
        {
            Guid cursor = afterId.Value;
            query = query.Where(read => read.Id.CompareTo(cursor) > 0);
        }

        NotificationBroadcastRead[] loaded = await query
            .AsNoTracking()
            .OrderBy(read => read.Id)
            .Take(request.PageSize + 1)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return await this.CompleteGuidPageAsync(
                scopeId,
                request,
                loaded,
                read => read.Id,
                Map,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<NotificationScopeExportPage> ExportReferenceStatesAsync(
        string scopeId,
        NotificationScopeExportRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryParseReferenceCursor(
                request.AfterCursor,
                out NotificationHistoryReference? afterReference))
        {
            return EmptyPage(
                NotificationScopeExportStatus.Invalid,
                request.ExpectedRevision,
                request.Store);
        }

        IQueryable<NotificationHistoryReferenceState> query = dbContext
            .NotificationHistoryReferenceStates
            .Where(state => state.ScopeId == scopeId);
        if (afterReference is not null)
        {
            string referenceNamespace = afterReference.Namespace;
            string digest = afterReference.Digest;
            query = query.Where(state =>
                state.Namespace.CompareTo(referenceNamespace) > 0 ||
                (state.Namespace == referenceNamespace &&
                 state.Digest.CompareTo(digest) > 0));
        }

        NotificationHistoryReferenceState[] loaded = await query
            .AsNoTracking()
            .OrderBy(state => state.Namespace)
            .ThenBy(state => state.Digest)
            .Take(request.PageSize + 1)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        bool hasMore = loaded.Length > request.PageSize;
        NotificationHistoryReferenceState[] selected = loaded
            .Take(request.PageSize)
            .ToArray();
        string? nextCursor = selected.Length == 0
            ? request.AfterCursor
            : ReferenceCursor(selected[^1].Namespace, selected[^1].Digest);
        return await this.CompletePageAsync(
                scopeId,
                request,
                selected.Select(Map).ToArray(),
                nextCursor,
                hasMore,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<NotificationScopeExportPage> ExportCloseReceiptsAsync(
        string scopeId,
        NotificationScopeExportRequest request,
        Guid? afterId,
        CancellationToken cancellationToken)
    {
        IQueryable<NotificationHistoryCloseReceipt> query = dbContext
            .NotificationHistoryCloseReceipts
            .Where(receipt => receipt.ScopeId == scopeId);
        if (afterId.HasValue)
        {
            Guid cursor = afterId.Value;
            query = query.Where(receipt =>
                receipt.OperationId.CompareTo(cursor) > 0);
        }

        NotificationHistoryCloseReceipt[] loaded = await query
            .AsNoTracking()
            .OrderBy(receipt => receipt.OperationId)
            .Take(request.PageSize + 1)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return await this.CompleteGuidPageAsync(
                scopeId,
                request,
                loaded,
                receipt => receipt.OperationId,
                Map,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<NotificationScopeExportPage>
        ExportBatchCloseOperationsAsync(
            string scopeId,
            NotificationScopeExportRequest request,
            Guid? afterId,
            CancellationToken cancellationToken)
    {
        IQueryable<NotificationHistoryBatchCloseOperation> query = dbContext
            .NotificationHistoryBatchCloseOperations
            .Where(operation => operation.ScopeId == scopeId);
        if (afterId.HasValue)
        {
            Guid cursor = afterId.Value;
            query = query.Where(operation =>
                operation.OperationId.CompareTo(cursor) > 0);
        }

        NotificationHistoryBatchCloseOperation[] loaded = await query
            .AsNoTracking()
            .OrderBy(operation => operation.OperationId)
            .Take(request.PageSize + 1)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return await this.CompleteGuidPageAsync(
                scopeId,
                request,
                loaded,
                operation => operation.OperationId,
                Map,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<NotificationScopeExportPage>
        ExportBatchCloseReceiptsAsync(
            string scopeId,
            NotificationScopeExportRequest request,
            Guid? afterId,
            CancellationToken cancellationToken)
    {
        IQueryable<NotificationHistoryBatchCloseReceipt> query = dbContext
            .NotificationHistoryBatchCloseReceipts
            .Where(receipt => receipt.ScopeId == scopeId);
        if (afterId.HasValue)
        {
            Guid cursor = afterId.Value;
            query = query.Where(receipt =>
                receipt.OperationId.CompareTo(cursor) > 0);
        }

        NotificationHistoryBatchCloseReceipt[] loaded = await query
            .AsNoTracking()
            .OrderBy(receipt => receipt.OperationId)
            .Take(request.PageSize + 1)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return await this.CompleteGuidPageAsync(
                scopeId,
                request,
                loaded,
                receipt => receipt.OperationId,
                Map,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<NotificationScopeExportPage> CompleteGuidPageAsync<T>(
        string scopeId,
        NotificationScopeExportRequest request,
        IReadOnlyList<T> loaded,
        Func<T, Guid> id,
        Func<T, NotificationScopeExportRecord> map,
        CancellationToken cancellationToken)
    {
        bool hasMore = loaded.Count > request.PageSize;
        T[] selected = loaded.Take(request.PageSize).ToArray();
        string? nextCursor = selected.Length == 0
            ? request.AfterCursor
            : IdCursor(id(selected[^1]));
        return await this.CompletePageAsync(
                scopeId,
                request,
                selected.Select(map).ToArray(),
                nextCursor,
                hasMore,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<NotificationScopeExportPage> CompletePageAsync(
        string scopeId,
        NotificationScopeExportRequest request,
        IReadOnlyList<NotificationScopeExportRecord> records,
        string? nextCursor,
        bool hasMore,
        CancellationToken cancellationToken)
    {
        ScopeStateSnapshot? current = await this.ReadStateAsync(
                scopeId,
                cancellationToken)
            .ConfigureAwait(false);
        if (current is null || current.Version != request.ExpectedRevision)
        {
            return EmptyPage(
                NotificationScopeExportStatus.Stale,
                current?.Version ?? 0,
                request.Store);
        }

        if (current.IsClosed)
        {
            return EmptyPage(
                NotificationScopeExportStatus.Closed,
                current.Version,
                request.Store);
        }

        return new NotificationScopeExportPage(
            NotificationScopeExportStatus.Completed,
            current.Version,
            request.Store,
            records,
            nextCursor,
            hasMore);
    }

    private Task<ScopeStateSnapshot?> ReadStateAsync(
        string scopeId,
        CancellationToken cancellationToken) =>
        dbContext.NotificationScopeStates
            .AsNoTracking()
            .Where(state => state.ScopeId == scopeId)
            .Select(state => new ScopeStateSnapshot(
                state.Version,
                state.IsClosed))
            .SingleOrDefaultAsync(cancellationToken);

    private static bool TryValidateScope(
        string? scopeId,
        IScopeContext context,
        out string normalized)
    {
        normalized = string.Empty;
        if (!ScopeIds.TryNormalize(scopeId, out string? candidate) ||
            !context.IsEnabled ||
            !string.Equals(context.ScopeId, candidate, StringComparison.Ordinal))
        {
            return false;
        }

        normalized = candidate;
        return true;
    }

    private static bool TryParseIdCursor(
        string? cursor,
        out Guid? afterId)
    {
        afterId = null;
        if (cursor is null)
        {
            return true;
        }

        if (!cursor.StartsWith(IdCursorPrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(
                cursor[IdCursorPrefix.Length..],
                "N",
                out Guid parsed) ||
            parsed == Guid.Empty)
        {
            return false;
        }

        afterId = parsed;
        return true;
    }

    private static bool TryParseReferenceCursor(
        string? cursor,
        out NotificationHistoryReference? reference)
    {
        reference = null;
        if (cursor is null)
        {
            return true;
        }

        if (!cursor.StartsWith(ReferenceCursorPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string[] segments = cursor[ReferenceCursorPrefix.Length..]
            .Split(':', StringSplitOptions.None);
        if (segments.Length != 2)
        {
            return false;
        }

        try
        {
            reference = new NotificationHistoryReference(
                segments[0],
                segments[1]);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string IdCursor(Guid id) =>
        IdCursorPrefix + id.ToString("N");

    private static string ReferenceCursor(
        string referenceNamespace,
        string digest) =>
        ReferenceCursorPrefix + referenceNamespace + ':' + digest;

    private static NotificationScopeExportPage EmptyPage(
        NotificationScopeExportStatus status,
        long revision,
        NotificationScopeExportStore store) =>
        new(status, revision, store, [], null, false);

    private sealed record ScopeStateSnapshot(long Version, bool IsClosed);
}
