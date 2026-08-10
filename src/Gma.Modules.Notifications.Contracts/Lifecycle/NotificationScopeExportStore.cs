namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(NotificationScopeExportStoreJsonConverter))]
public enum NotificationScopeExportStore
{
    Unknown = 0,
    UserNotifications = 1,
    Preferences = 2,
    DeliveryRoutes = 3,
    TagDefinitions = 4,
    Deliveries = 5,
    DeliveryAttempts = 6,
    TenantBroadcasts = 7,
    TenantBroadcastReads = 8,
    HistoryReferenceStates = 9,
    HistoryCloseReceipts = 10,
    HistoryBatchCloseOperations = 11,
    HistoryBatchCloseReceipts = 12
}
