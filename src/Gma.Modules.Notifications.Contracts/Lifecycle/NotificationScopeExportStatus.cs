namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(NotificationScopeExportStatusJsonConverter))]
public enum NotificationScopeExportStatus
{
    Unknown = 0,
    Invalid = 1,
    Completed = 2,
    Missing = 3,
    Closed = 4,
    Stale = 5,
    ScopeUnavailable = 6
}
