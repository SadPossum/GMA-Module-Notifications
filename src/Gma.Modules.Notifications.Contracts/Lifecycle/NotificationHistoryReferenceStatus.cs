namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(NotificationHistoryReferenceStatusJsonConverter))]
public enum NotificationHistoryReferenceStatus
{
    Unknown = 0,
    Missing = 1,
    Open = 2,
    Closed = 3
}
