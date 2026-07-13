namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(NotificationTagOriginJsonConverter))]
public enum NotificationTagOrigin
{
    Unknown = 0,
    System = 1,
    Module = 2,
    Operator = 3
}
