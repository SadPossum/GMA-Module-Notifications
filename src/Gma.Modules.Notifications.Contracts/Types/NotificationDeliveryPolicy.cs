namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(NotificationDeliveryPolicyJsonConverter))]
public enum NotificationDeliveryPolicy
{
    Unknown = 0,
    RespectPreferences = 1,
    Mandatory = 2
}
