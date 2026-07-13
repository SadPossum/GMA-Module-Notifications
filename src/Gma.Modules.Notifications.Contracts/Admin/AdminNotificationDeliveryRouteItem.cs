namespace Gma.Modules.Notifications.Contracts;

public sealed record AdminNotificationDeliveryRouteItem(
    Guid Id,
    string DeliveryTag,
    NotificationDeliveryProviderCode Provider,
    bool IsActive,
    int Version,
    DateTimeOffset UpdatedAtUtc,
    string UpdatedBy);
