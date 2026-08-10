namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(NotificationHistoryReferenceCloseBatchStatusJsonConverter))]
public enum NotificationHistoryReferenceCloseBatchStatus
{
    Unknown = 0,
    Invalid = 1,
    InProgress = 2,
    Completed = 3,
    Replayed = 4,
    Stale = 5,
    Busy = 6,
    Conflict = 7,
    ScopeUnavailable = 8
}
