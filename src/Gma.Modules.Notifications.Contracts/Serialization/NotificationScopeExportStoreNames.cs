namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

public static class NotificationScopeExportStoreNames
{
    public static string ToWireName(NotificationScopeExportStore store) =>
        store switch
        {
            NotificationScopeExportStore.UserNotifications =>
                "user-notifications",
            NotificationScopeExportStore.Preferences => "preferences",
            NotificationScopeExportStore.DeliveryRoutes => "delivery-routes",
            NotificationScopeExportStore.TagDefinitions => "tag-definitions",
            NotificationScopeExportStore.Deliveries => "deliveries",
            NotificationScopeExportStore.DeliveryAttempts =>
                "delivery-attempts",
            NotificationScopeExportStore.TenantBroadcasts =>
                "tenant-broadcasts",
            NotificationScopeExportStore.TenantBroadcastReads =>
                "tenant-broadcast-reads",
            NotificationScopeExportStore.HistoryReferenceStates =>
                "history-reference-states",
            NotificationScopeExportStore.HistoryCloseReceipts =>
                "history-close-receipts",
            NotificationScopeExportStore.HistoryBatchCloseOperations =>
                "history-batch-close-operations",
            NotificationScopeExportStore.HistoryBatchCloseReceipts =>
                "history-batch-close-receipts",
            _ => throw new ArgumentOutOfRangeException(
                nameof(store),
                store,
                "Notification scope export store is invalid.")
        };

    public static bool TryParse(
        string? value,
        out NotificationScopeExportStore store)
    {
        store = (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "user-notifications" =>
                NotificationScopeExportStore.UserNotifications,
            "preferences" => NotificationScopeExportStore.Preferences,
            "delivery-routes" => NotificationScopeExportStore.DeliveryRoutes,
            "tag-definitions" => NotificationScopeExportStore.TagDefinitions,
            "deliveries" => NotificationScopeExportStore.Deliveries,
            "delivery-attempts" =>
                NotificationScopeExportStore.DeliveryAttempts,
            "tenant-broadcasts" =>
                NotificationScopeExportStore.TenantBroadcasts,
            "tenant-broadcast-reads" =>
                NotificationScopeExportStore.TenantBroadcastReads,
            "history-reference-states" =>
                NotificationScopeExportStore.HistoryReferenceStates,
            "history-close-receipts" =>
                NotificationScopeExportStore.HistoryCloseReceipts,
            "history-batch-close-operations" =>
                NotificationScopeExportStore.HistoryBatchCloseOperations,
            "history-batch-close-receipts" =>
                NotificationScopeExportStore.HistoryBatchCloseReceipts,
            _ => NotificationScopeExportStore.Unknown
        };
        return store is not NotificationScopeExportStore.Unknown;
    }
}

internal sealed class NotificationScopeExportStoreJsonConverter
    : JsonConverter<NotificationScopeExportStore>
{
    public override NotificationScopeExportStore Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        NotificationContractEnumJson.ReadString(
            ref reader,
            "Notification scope export store",
            Parse);

    public override void Write(
        Utf8JsonWriter writer,
        NotificationScopeExportStore value,
        JsonSerializerOptions options) =>
        NotificationContractEnumJson.WriteString(
            writer,
            value,
            "Notification scope export store",
            NotificationScopeExportStoreNames.ToWireName);

    private static NotificationScopeExportStore? Parse(string? value) =>
        NotificationScopeExportStoreNames.TryParse(value, out var store)
            ? store
            : null;
}
