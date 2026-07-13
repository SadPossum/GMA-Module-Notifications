namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(NotificationTagKindJsonConverter))]
public enum NotificationTagKind
{
    Unknown = 0,
    Delivery = 1,
    Domain = 2
}
