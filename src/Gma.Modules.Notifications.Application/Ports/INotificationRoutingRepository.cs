namespace Gma.Modules.Notifications.Application.Ports;

using Gma.Framework.Notifications;
using Gma.Framework.Pagination;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.ValueObjects;
using DomainDeliveryStatus = Domain.ValueObjects.NotificationDeliveryStatus;

public interface INotificationRoutingRepository
{
    Task<NotificationTagDefinition?> GetTagDefinitionAsync(string key, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationTagDefinition>> ListTagDefinitionsAsync(
        bool activeOnly,
        CancellationToken cancellationToken);
    Task AddTagDefinitionAsync(NotificationTagDefinition definition, CancellationToken cancellationToken);
    Task<NotificationPreference?> GetPreferenceAsync(
        string userId,
        string tagKey,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationPreference>> ListPreferencesAsync(
        string userId,
        CancellationToken cancellationToken);
    Task AddPreferenceAsync(NotificationPreference preference, CancellationToken cancellationToken);
    Task<IReadOnlySet<string>> GetDisabledTagsAsync(
        string userId,
        IReadOnlyCollection<string> tagKeys,
        CancellationToken cancellationToken);
    Task<NotificationDeliveryRoute?> GetRouteAsync(string deliveryTag, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationDeliveryRoute>> ListRoutesAsync(CancellationToken cancellationToken);
    Task<string?> GetActiveProviderAsync(string deliveryTag, CancellationToken cancellationToken);
    Task AddRouteAsync(NotificationDeliveryRoute route, CancellationToken cancellationToken);
    Task AddDeliveriesAsync(
        IReadOnlyCollection<NotificationDelivery> deliveries,
        CancellationToken cancellationToken);
    Task<bool?> GetDeliveryPlanAllowedAsync(
        Guid notificationId,
        string deliveryTag,
        CancellationToken cancellationToken);
    Task<AdminNotificationDeliveryItem?> GetDeliveryAsync(Guid deliveryId, CancellationToken cancellationToken);
    Task<AdminNotificationDeliveryListResponse> ListDeliveriesAsync(
        DomainDeliveryStatus? status,
        string? userId,
        string? deliveryTag,
        PageRequest pageRequest,
        CancellationToken cancellationToken);
    Task<NotificationDelivery?> GetDeliveryForUpdateAsync(Guid deliveryId, CancellationToken cancellationToken);
}

public interface INotificationDeliveryAdapterCatalog
{
    IReadOnlyList<string> GetProviders(string deliveryTag);
    bool Supports(string provider, string deliveryTag);
    IUserNotificationSink? GetProvider(string provider);
}
