namespace Gma.Modules.Notifications.Contracts;

public sealed record AdminSetNotificationDeliveryRouteRequest(
    NotificationDeliveryProviderCode Provider,
    bool IsActive = true);
