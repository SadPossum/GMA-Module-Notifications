namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationScopeDeliveryRouteExportRecord(
    Guid RouteId,
    string DeliveryTag,
    NotificationDeliveryProviderCode Provider,
    bool IsActive,
    int Version,
    DateTimeOffset UpdatedAtUtc,
    string UpdatedBy)
    : NotificationScopeExportRecord;
