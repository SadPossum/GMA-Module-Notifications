namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

public static class NotificationScopeDestructionStageNames
{
    public static string ToWireName(NotificationScopeDestructionStage stage) =>
        stage switch
        {
            NotificationScopeDestructionStage.InboxMessages => "inbox-messages",
            NotificationScopeDestructionStage.TenantBroadcastReads =>
                "tenant-broadcast-reads",
            NotificationScopeDestructionStage.TenantBroadcasts =>
                "tenant-broadcasts",
            NotificationScopeDestructionStage.Preferences => "preferences",
            NotificationScopeDestructionStage.DeliveryRoutes =>
                "delivery-routes",
            NotificationScopeDestructionStage.TagDefinitions =>
                "tag-definitions",
            NotificationScopeDestructionStage.UserNotifications =>
                "user-notifications",
            NotificationScopeDestructionStage.Completed => "completed",
            _ => throw new ArgumentOutOfRangeException(
                nameof(stage),
                stage,
                "Notification scope destruction stage is invalid.")
        };

    public static bool TryParse(
        string? value,
        out NotificationScopeDestructionStage stage)
    {
        stage = (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "inbox-messages" => NotificationScopeDestructionStage.InboxMessages,
            "tenant-broadcast-reads" =>
                NotificationScopeDestructionStage.TenantBroadcastReads,
            "tenant-broadcasts" =>
                NotificationScopeDestructionStage.TenantBroadcasts,
            "preferences" => NotificationScopeDestructionStage.Preferences,
            "delivery-routes" =>
                NotificationScopeDestructionStage.DeliveryRoutes,
            "tag-definitions" =>
                NotificationScopeDestructionStage.TagDefinitions,
            "user-notifications" =>
                NotificationScopeDestructionStage.UserNotifications,
            "completed" => NotificationScopeDestructionStage.Completed,
            _ => NotificationScopeDestructionStage.Unknown
        };
        return stage is not NotificationScopeDestructionStage.Unknown;
    }
}

internal sealed class NotificationScopeDestructionStageJsonConverter
    : JsonConverter<NotificationScopeDestructionStage>
{
    public override NotificationScopeDestructionStage Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        NotificationContractEnumJson.ReadString(
            ref reader,
            "Notification scope destruction stage",
            Parse);

    public override void Write(
        Utf8JsonWriter writer,
        NotificationScopeDestructionStage value,
        JsonSerializerOptions options) =>
        NotificationContractEnumJson.WriteString(
            writer,
            value,
            "Notification scope destruction stage",
            NotificationScopeDestructionStageNames.ToWireName);

    private static NotificationScopeDestructionStage? Parse(string? value) =>
        NotificationScopeDestructionStageNames.TryParse(value, out var stage)
            ? stage
            : null;
}
