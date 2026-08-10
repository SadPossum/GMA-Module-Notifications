namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(NotificationHistoryReferenceCloseStatusJsonConverter))]
public enum NotificationHistoryReferenceCloseStatus
{
    Unknown = 0,
    Invalid = 1,
    Completed = 2,
    Replayed = 3,
    Stale = 4,
    Busy = 5,
    Conflict = 6,
    Overflow = 7,
    ScopeUnavailable = 8
}
