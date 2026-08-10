namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(NotificationScopeDestructionStageJsonConverter))]
public enum NotificationScopeDestructionStage
{
    Unknown = 0,
    InboxMessages = 1,
    TenantBroadcastReads = 2,
    TenantBroadcasts = 3,
    Preferences = 4,
    DeliveryRoutes = 5,
    TagDefinitions = 6,
    UserNotifications = 7,
    Completed = 8
}
