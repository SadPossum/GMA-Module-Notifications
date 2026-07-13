namespace Gma.Modules.Notifications.Contracts;

public sealed record AdminNotificationDeliveryListResponse(
    IReadOnlyList<AdminNotificationDeliveryItem> Items,
    int Page,
    int PageSize,
    int TotalCount);
