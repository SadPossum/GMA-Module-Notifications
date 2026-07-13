namespace Gma.Modules.Notifications.Persistence.Repositories;

using Gma.Framework.Pagination;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using ContractAttemptOutcome = Contracts.NotificationDeliveryAttemptOutcome;
using ContractDeliveryStatus = Contracts.NotificationDeliveryStatus;
using ContractTagKind = Contracts.NotificationTagKind;
using DomainAttemptOutcome = Domain.ValueObjects.NotificationDeliveryAttemptOutcome;
using DomainDeliveryStatus = Domain.ValueObjects.NotificationDeliveryStatus;
using DomainTagKind = Domain.ValueObjects.NotificationTagKind;

internal sealed class NotificationRoutingRepository(NotificationsDbContext dbContext)
    : INotificationRoutingRepository
{
    public Task<NotificationTagDefinition?> GetTagDefinitionAsync(
        string key,
        CancellationToken cancellationToken)
    {
        NotificationTagKey normalized = NotificationTagKey.Create(key).Value;
        NotificationTagDefinition? tracked = dbContext.NotificationTagDefinitions.Local
            .FirstOrDefault(definition => definition.Key == normalized);
        if (tracked is not null)
        {
            return Task.FromResult<NotificationTagDefinition?>(tracked);
        }

        return dbContext.NotificationTagDefinitions
            .FirstOrDefaultAsync(definition => definition.Key == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationTagDefinition>> ListTagDefinitionsAsync(
        bool activeOnly,
        CancellationToken cancellationToken)
    {
        IQueryable<NotificationTagDefinition> query = dbContext.NotificationTagDefinitions.AsNoTracking();
        if (activeOnly)
        {
            query = query.Where(definition => definition.IsActive);
        }

        NotificationTagDefinition[] definitions = await query
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return definitions
            .OrderBy(definition => definition.Kind)
            .ThenBy(definition => definition.Key.Value, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task AddTagDefinitionAsync(
        NotificationTagDefinition definition,
        CancellationToken cancellationToken) =>
        await dbContext.NotificationTagDefinitions.AddAsync(definition, cancellationToken).ConfigureAwait(false);

    public Task<NotificationPreference?> GetPreferenceAsync(
        string userId,
        string tagKey,
        CancellationToken cancellationToken)
    {
        NotificationTagKey normalized = NotificationTagKey.Create(tagKey).Value;
        return dbContext.NotificationPreferences.FirstOrDefaultAsync(
            preference => preference.UserId == userId && preference.TagKey == normalized,
            cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationPreference>> ListPreferencesAsync(
        string userId,
        CancellationToken cancellationToken) =>
        (await dbContext.NotificationPreferences
            .AsNoTracking()
            .Where(preference => preference.UserId == userId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false))
        .OrderBy(preference => preference.TagKey.Value, StringComparer.Ordinal)
        .ToArray();

    public async Task AddPreferenceAsync(
        NotificationPreference preference,
        CancellationToken cancellationToken) =>
        await dbContext.NotificationPreferences.AddAsync(preference, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlySet<string>> GetDisabledTagsAsync(
        string userId,
        IReadOnlyCollection<string> tagKeys,
        CancellationToken cancellationToken)
    {
        string[] normalizedKeys = tagKeys
            .Select(key => NotificationTagKey.Create(key).Value.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var preferences = await dbContext.NotificationPreferences
            .AsNoTracking()
            .Where(preference => preference.UserId == userId && !preference.Enabled)
            .Select(preference => new { preference.TagKey })
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return preferences
            .Select(preference => preference.TagKey.Value)
            .Where(normalizedKeys.Contains)
            .ToHashSet(StringComparer.Ordinal);
    }

    public async Task<string?> GetActiveProviderAsync(
        string deliveryTag,
        CancellationToken cancellationToken)
    {
        NotificationTagKey normalized = NotificationTagKey.Create(deliveryTag).Value;
        NotificationDeliveryRoute? route = await dbContext.NotificationDeliveryRoutes
            .AsNoTracking()
            .SingleOrDefaultAsync(route => route.DeliveryTag == normalized && route.IsActive, cancellationToken)
            .ConfigureAwait(false);
        return route?.Provider.Value;
    }

    public Task<NotificationDeliveryRoute?> GetRouteAsync(
        string deliveryTag,
        CancellationToken cancellationToken)
    {
        NotificationTagKey normalized = NotificationTagKey.Create(deliveryTag).Value;
        return dbContext.NotificationDeliveryRoutes.FirstOrDefaultAsync(
            route => route.DeliveryTag == normalized,
            cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationDeliveryRoute>> ListRoutesAsync(
        CancellationToken cancellationToken) =>
        (await dbContext.NotificationDeliveryRoutes
            .AsNoTracking()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false))
        .OrderBy(route => route.DeliveryTag.Value, StringComparer.Ordinal)
        .ToArray();

    public async Task AddRouteAsync(
        NotificationDeliveryRoute route,
        CancellationToken cancellationToken) =>
        await dbContext.NotificationDeliveryRoutes.AddAsync(route, cancellationToken).ConfigureAwait(false);

    public async Task AddDeliveriesAsync(
        IReadOnlyCollection<NotificationDelivery> deliveries,
        CancellationToken cancellationToken)
    {
        if (deliveries.Count > 0)
        {
            await dbContext.NotificationDeliveries.AddRangeAsync(deliveries, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<bool?> GetDeliveryPlanAllowedAsync(
        Guid notificationId,
        string deliveryTag,
        CancellationToken cancellationToken)
    {
        NotificationTagKey normalized = NotificationTagKey.Create(deliveryTag).Value;
        DomainDeliveryStatus[] statuses = await dbContext.NotificationDeliveries
            .AsNoTracking()
            .Where(delivery =>
                delivery.NotificationId == notificationId &&
                delivery.DeliveryTag == normalized)
            .Select(delivery => delivery.Status)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        return statuses.Length == 0
            ? null
            : statuses.Any(status => status is DomainDeliveryStatus.Pending or DomainDeliveryStatus.Delivered);
    }

    public async Task<AdminNotificationDeliveryItem?> GetDeliveryAsync(
        Guid deliveryId,
        CancellationToken cancellationToken)
    {
        NotificationDelivery? delivery = await dbContext.NotificationDeliveries
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == deliveryId, cancellationToken)
            .ConfigureAwait(false);
        return delivery is null
            ? null
            : await this.MapDeliveryAsync(delivery, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AdminNotificationDeliveryListResponse> ListDeliveriesAsync(
        DomainDeliveryStatus? status,
        string? userId,
        string? deliveryTag,
        PageRequest pageRequest,
        CancellationToken cancellationToken)
    {
        IQueryable<NotificationDelivery> query = dbContext.NotificationDeliveries.AsNoTracking();
        if (status is not null)
        {
            query = query.Where(delivery => delivery.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(deliveryTag))
        {
            NotificationTagKey normalizedTag = NotificationTagKey.Create(deliveryTag).Value;
            query = query.Where(delivery => delivery.DeliveryTag == normalizedTag);
        }

        if (!string.IsNullOrWhiteSpace(userId))
        {
            NotificationRecipient recipient = NotificationRecipient.Create(userId).Value;
            query = query.Where(delivery => dbContext.UserNotifications.Any(notification =>
                notification.Id == delivery.NotificationId &&
                notification.Recipient == recipient));
        }

        int totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        NotificationDelivery[] deliveries = await query
            .OrderByDescending(delivery => delivery.CreatedAtUtc)
            .ThenBy(delivery => delivery.Id)
            .Skip(pageRequest.SkipCount)
            .Take(pageRequest.PageSize)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        Guid[] notificationIds = deliveries.Select(delivery => delivery.NotificationId).Distinct().ToArray();
        Dictionary<Guid, string> usersByNotification = await dbContext.UserNotifications
            .AsNoTracking()
            .Where(notification => notificationIds.Contains(notification.Id))
            .ToDictionaryAsync(
                notification => notification.Id,
                notification => notification.Recipient.UserId,
                cancellationToken)
            .ConfigureAwait(false);
        Guid[] deliveryIds = deliveries.Select(delivery => delivery.Id).ToArray();
        NotificationDeliveryAttempt[] attempts = await dbContext.NotificationDeliveryAttempts
            .AsNoTracking()
            .Where(attempt => deliveryIds.Contains(attempt.DeliveryId))
            .OrderBy(attempt => attempt.AttemptNumber)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        ILookup<Guid, NotificationDeliveryAttempt> attemptsByDelivery = attempts.ToLookup(attempt => attempt.DeliveryId);
        AdminNotificationDeliveryItem[] items = deliveries
            .Select(delivery => MapDelivery(
                delivery,
                usersByNotification[delivery.NotificationId],
                attemptsByDelivery[delivery.Id]))
            .ToArray();

        return new AdminNotificationDeliveryListResponse(
            items,
            pageRequest.Page,
            pageRequest.PageSize,
            totalCount);
    }

    public Task<NotificationDelivery?> GetDeliveryForUpdateAsync(
        Guid deliveryId,
        CancellationToken cancellationToken) =>
        dbContext.NotificationDeliveries.SingleOrDefaultAsync(delivery => delivery.Id == deliveryId, cancellationToken);

    private async Task<AdminNotificationDeliveryItem> MapDeliveryAsync(
        NotificationDelivery delivery,
        CancellationToken cancellationToken)
    {
        string userId = await dbContext.UserNotifications
            .AsNoTracking()
            .Where(notification => notification.Id == delivery.NotificationId)
            .Select(notification => notification.Recipient.UserId)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
        NotificationDeliveryAttempt[] attempts = await dbContext.NotificationDeliveryAttempts
            .AsNoTracking()
            .Where(attempt => attempt.DeliveryId == delivery.Id)
            .OrderBy(attempt => attempt.AttemptNumber)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return MapDelivery(delivery, userId, attempts);
    }

    private static AdminNotificationDeliveryItem MapDelivery(
        NotificationDelivery delivery,
        string userId,
        IEnumerable<NotificationDeliveryAttempt> attempts) =>
        new(
            delivery.Id,
            delivery.NotificationId,
            userId,
            delivery.DeliveryTag.Value,
            new NotificationDeliveryProviderCode(delivery.Provider.Value),
            ToContractStatus(delivery.Status),
            delivery.Attempts,
            delivery.MaxAttempts,
            delivery.CreatedAtUtc,
            delivery.NextAttemptAtUtc,
            delivery.DeliveredAtUtc,
            delivery.CompletedAtUtc,
            delivery.LastCode,
            delivery.ProviderMessageId,
            attempts.Select(MapAttempt).ToArray());

    private static AdminNotificationDeliveryAttemptItem MapAttempt(NotificationDeliveryAttempt attempt) =>
        new(
            attempt.Id,
            attempt.AttemptNumber,
            new NotificationDeliveryProviderCode(attempt.Provider.Value),
            ToContractOutcome(attempt.Outcome),
            attempt.StartedAtUtc,
            attempt.CompletedAtUtc,
            attempt.Code,
            attempt.ProviderMessageId);

    private static ContractDeliveryStatus ToContractStatus(DomainDeliveryStatus status) => status switch
    {
        DomainDeliveryStatus.Pending => ContractDeliveryStatus.Pending,
        DomainDeliveryStatus.Processing => ContractDeliveryStatus.Processing,
        DomainDeliveryStatus.RetryScheduled => ContractDeliveryStatus.RetryScheduled,
        DomainDeliveryStatus.Delivered => ContractDeliveryStatus.Delivered,
        DomainDeliveryStatus.Rejected => ContractDeliveryStatus.Rejected,
        DomainDeliveryStatus.Exhausted => ContractDeliveryStatus.Exhausted,
        DomainDeliveryStatus.Suppressed => ContractDeliveryStatus.Suppressed,
        DomainDeliveryStatus.Unroutable => ContractDeliveryStatus.Unroutable,
        _ => ContractDeliveryStatus.Unknown
    };

    private static ContractAttemptOutcome ToContractOutcome(DomainAttemptOutcome outcome) => outcome switch
    {
        DomainAttemptOutcome.Delivered => ContractAttemptOutcome.Delivered,
        DomainAttemptOutcome.Retry => ContractAttemptOutcome.Retry,
        DomainAttemptOutcome.Rejected => ContractAttemptOutcome.Rejected,
        DomainAttemptOutcome.Exception => ContractAttemptOutcome.Exception,
        _ => ContractAttemptOutcome.Unknown
    };
}
