namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(NotificationDeliveryStatusJsonConverter))]
public enum NotificationDeliveryStatus
{
    Unknown = 0,
    Pending = 1,
    Processing = 2,
    RetryScheduled = 3,
    Delivered = 4,
    Rejected = 5,
    Exhausted = 6,
    Suppressed = 7,
    Unroutable = 8
}
