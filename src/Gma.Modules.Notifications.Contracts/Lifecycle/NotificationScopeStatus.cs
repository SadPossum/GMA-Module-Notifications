namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(NotificationScopeStatusJsonConverter))]
public enum NotificationScopeStatus
{
    Unknown = 0,
    Missing = 1,
    Open = 2,
    Closed = 3,
    ScopeUnavailable = 4
}
