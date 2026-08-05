namespace Gma.Modules.Notifications.Persistence.Repositories;

using System.Text.Json;
using Gma.Framework.AccessControl;
using Gma.Framework.Pagination;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using ContractDeliveryPolicy = Contracts.NotificationDeliveryPolicy;
using ContractSeverity = Contracts.NotificationSeverity;
using DomainDeliveryPolicy = Domain.ValueObjects.NotificationDeliveryPolicy;
using DomainSeverity = Domain.ValueObjects.NotificationSeverity;

internal sealed class NotificationHistoryRepository(NotificationsDbContext dbContext) : INotificationHistoryRepository
{
    public async Task AddAsync(UserNotification notification, CancellationToken cancellationToken)
    {
        await dbContext.UserNotifications.AddAsync(notification, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> ExistsAsync(Guid notificationId, CancellationToken cancellationToken) =>
        dbContext.UserNotifications.Local.Any(notification => notification.Id == notificationId)
            ? Task.FromResult(true)
            : dbContext.UserNotifications.AnyAsync(
                notification => notification.Id == notificationId,
                cancellationToken);

    public async Task<NotificationHistoryItem?> GetAsync(
        Guid notificationId,
        AccessSubject subject,
        string? scopeId,
        CancellationToken cancellationToken)
    {
        UserNotification? notification = await ApplyUserSubjectScope(dbContext.UserNotifications, subject, scopeId)
            .Include(item => item.Tags)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == notificationId,
                cancellationToken)
            .ConfigureAwait(false);

        return notification is null ? null : Map(notification);
    }

    public async Task<AdminNotificationHistoryItem?> GetTenantAsync(
        Guid notificationId,
        CancellationToken cancellationToken)
    {
        UserNotification? notification = await dbContext.UserNotifications
            .Where(item => item.IsInboxVisible)
            .Include(item => item.Tags)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item => item.Id == notificationId,
                cancellationToken)
            .ConfigureAwait(false);

        return notification is null ? null : MapAdmin(notification);
    }

    public async Task<NotificationHistoryListResponse> ListAsync(
        AccessSubject subject,
        string? scopeId,
        bool unreadOnly,
        PageRequest pageRequest,
        CancellationToken cancellationToken)
    {
        IQueryable<UserNotification> userNotifications = ApplyUserSubjectScope(dbContext.UserNotifications, subject, scopeId)
            .Include(item => item.Tags)
            .AsNoTracking();

        int unreadCount = await userNotifications
            .CountAsync(notification => notification.ReadAtUtc == null, cancellationToken)
            .ConfigureAwait(false);

        IQueryable<UserNotification> filtered = unreadOnly
            ? userNotifications.Where(notification => notification.ReadAtUtc == null)
            : userNotifications;

        int totalCount = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);
        UserNotification[] items = await filtered
            .OrderByDescending(notification => notification.OccurredAtUtc)
            .ThenByDescending(notification => notification.CreatedAtUtc)
            .ThenBy(notification => notification.Id)
            .Skip(pageRequest.SkipCount)
            .Take(pageRequest.PageSize)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return new NotificationHistoryListResponse(
            items.Select(Map).ToArray(),
            pageRequest.Page,
            pageRequest.PageSize,
            totalCount,
            unreadCount);
    }

    public async Task<AdminNotificationHistoryListResponse> ListTenantAsync(
        string? userId,
        bool unreadOnly,
        PageRequest pageRequest,
        CancellationToken cancellationToken)
    {
        IQueryable<UserNotification> notifications = dbContext.UserNotifications
            .Where(notification => notification.IsInboxVisible)
            .Include(notification => notification.Tags)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(userId))
        {
            NotificationRecipient recipient = NormalizeRecipient(userId);
            notifications = notifications.Where(notification => notification.Recipient == recipient);
        }

        int unreadCount = await notifications
            .CountAsync(notification => notification.ReadAtUtc == null, cancellationToken)
            .ConfigureAwait(false);

        IQueryable<UserNotification> filtered = unreadOnly
            ? notifications.Where(notification => notification.ReadAtUtc == null)
            : notifications;

        int totalCount = await filtered.CountAsync(cancellationToken).ConfigureAwait(false);
        UserNotification[] items = await filtered
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .ThenBy(notification => notification.Id)
            .Skip(pageRequest.SkipCount)
            .Take(pageRequest.PageSize)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AdminNotificationHistoryListResponse(
            items.Select(MapAdmin).ToArray(),
            pageRequest.Page,
            pageRequest.PageSize,
            totalCount,
            unreadCount);
    }

    public async Task<long> GetCurrentStreamSequenceForUserAsync(
        AccessSubject subject,
        string? scopeId,
        CancellationToken cancellationToken)
    {
        long? cursor = await ApplyUserSubjectScope(dbContext.UserNotifications, subject, scopeId)
            .AsNoTracking()
            .MaxAsync(notification => (long?)notification.StreamSequence, cancellationToken)
            .ConfigureAwait(false);

        return cursor ?? 0;
    }

    public async Task<long> GetCurrentStreamSequenceForTenantAsync(
        string? userId,
        CancellationToken cancellationToken)
    {
        IQueryable<UserNotification> query = dbContext.UserNotifications
            .Where(notification => notification.IsInboxVisible)
            .AsNoTracking();
        if (!string.IsNullOrWhiteSpace(userId))
        {
            NotificationRecipient recipient = NormalizeRecipient(userId);
            query = query.Where(notification => notification.Recipient == recipient);
        }

        long? cursor = await query
            .MaxAsync(notification => (long?)notification.StreamSequence, cancellationToken)
            .ConfigureAwait(false);

        return cursor ?? 0;
    }

    public async Task<IReadOnlyList<NotificationHistoryItem>> ListNewForUserAsync(
        AccessSubject subject,
        string? scopeId,
        long afterStreamSequence,
        int batchSize,
        CancellationToken cancellationToken)
    {
        UserNotification[] items = await ApplyUserSubjectScope(dbContext.UserNotifications, subject, scopeId)
            .Include(item => item.Tags)
            .AsNoTracking()
            .Where(notification =>
                notification.StreamSequence > afterStreamSequence)
            .OrderBy(notification => notification.StreamSequence)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return items.Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<AdminNotificationHistoryItem>> ListNewForTenantAsync(
        string? userId,
        long afterStreamSequence,
        int batchSize,
        CancellationToken cancellationToken)
    {
        IQueryable<UserNotification> query = dbContext.UserNotifications
            .Where(notification => notification.IsInboxVisible)
            .Include(notification => notification.Tags)
            .AsNoTracking();
        if (!string.IsNullOrWhiteSpace(userId))
        {
            NotificationRecipient recipient = NormalizeRecipient(userId);
            query = query.Where(notification => notification.Recipient == recipient);
        }

        UserNotification[] items = await query
            .Where(notification => notification.StreamSequence > afterStreamSequence)
            .OrderBy(notification => notification.StreamSequence)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return items.Select(MapAdmin).ToArray();
    }

    public async Task<bool> MarkReadAsync(
        Guid notificationId,
        AccessSubject subject,
        string? scopeId,
        DateTimeOffset readAtUtc,
        CancellationToken cancellationToken)
    {
        UserNotification? notification = await ApplyUserSubjectScope(dbContext.UserNotifications, subject, scopeId)
            .FirstOrDefaultAsync(
                item => item.Id == notificationId,
                cancellationToken)
            .ConfigureAwait(false);

        if (notification is null)
        {
            return false;
        }

        notification.MarkRead(readAtUtc);
        return true;
    }

    public async Task<int> MarkAllReadAsync(
        AccessSubject subject,
        string? scopeId,
        DateTimeOffset readAtUtc,
        CancellationToken cancellationToken)
    {
        IQueryable<UserNotification> query = ApplyUserSubjectScope(dbContext.UserNotifications, subject, scopeId);

        if (dbContext.Database.IsRelational())
        {
            IQueryable<UserNotification> unread = query
                .Where(notification => notification.ReadAtUtc == null);
            string[] affectedScopeIds = await unread
                .Select(notification => notification.ScopeId)
                .Distinct()
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            if (affectedScopeIds.Length == 0)
            {
                return 0;
            }

            if (!await dbContext.TryRegisterMaintenanceScopeMutationsAsync(
                    affectedScopeIds,
                    cancellationToken).ConfigureAwait(false))
            {
                throw new NotificationScopeClosedException();
            }

            return await unread
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(notification => notification.ReadAtUtc, readAtUtc),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        UserNotification[] unreadNotifications = await query
            .Where(notification => notification.ReadAtUtc == null)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        int updatedCount = 0;
        foreach (UserNotification notification in unreadNotifications)
        {
            if (notification.MarkRead(readAtUtc))
            {
                updatedCount++;
            }
        }

        return updatedCount;
    }

    private static NotificationHistoryItem Map(UserNotification notification)
    {
        using JsonDocument document = JsonDocument.Parse(notification.Payload.Json);
        return new NotificationHistoryItem(
            notification.Id,
            notification.Source.Module,
            notification.Source.Name,
            notification.Source.Version,
            notification.Content.Title,
            notification.Content.Body,
            ToContractSeverity(notification.Severity),
            notification.StreamSequence,
            notification.OccurredAtUtc,
            notification.CreatedAtUtc,
            notification.ReadAtUtc,
            document.RootElement.Clone(),
            notification.Tags.Select(tag => tag.Key.Value).Order(StringComparer.Ordinal).ToArray(),
            ToContractDeliveryPolicy(notification.DeliveryPolicy));
    }

    private static AdminNotificationHistoryItem MapAdmin(UserNotification notification)
    {
        using JsonDocument document = JsonDocument.Parse(notification.Payload.Json);
        return new AdminNotificationHistoryItem(
            notification.Id,
            notification.ScopeId,
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
            notification.ReadAtUtc,
            document.RootElement.Clone(),
            notification.Tags.Select(tag => tag.Key.Value).Order(StringComparer.Ordinal).ToArray(),
            ToContractDeliveryPolicy(notification.DeliveryPolicy));
    }

    private static NotificationRecipient NormalizeRecipient(string userId) =>
        NotificationRecipient.Create(userId).Value;

    private static IQueryable<UserNotification> ApplyUserSubjectScope(
        IQueryable<UserNotification> query,
        AccessSubject subject,
        string? scopeId)
    {
        NotificationRecipient recipient = NormalizeRecipient(subject.Id);
        query = query.Where(notification =>
            notification.IsInboxVisible &&
            notification.Recipient == recipient);

        return string.IsNullOrWhiteSpace(scopeId)
            ? query
            : query.Where(notification => notification.ScopeId == scopeId);
    }

    private static ContractSeverity ToContractSeverity(DomainSeverity severity) =>
        severity switch
        {
            DomainSeverity.Info => ContractSeverity.Info,
            DomainSeverity.Success => ContractSeverity.Success,
            DomainSeverity.Warning => ContractSeverity.Warning,
            DomainSeverity.Error => ContractSeverity.Error,
            _ => ContractSeverity.Unknown
        };

    private static ContractDeliveryPolicy ToContractDeliveryPolicy(DomainDeliveryPolicy policy) =>
        policy switch
        {
            DomainDeliveryPolicy.RespectPreferences => ContractDeliveryPolicy.RespectPreferences,
            DomainDeliveryPolicy.Mandatory => ContractDeliveryPolicy.Mandatory,
            _ => ContractDeliveryPolicy.Unknown
        };
}
