namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json.Serialization;

[JsonConverter(typeof(NotificationDeliveryAttemptOutcomeJsonConverter))]
public enum NotificationDeliveryAttemptOutcome
{
    Unknown = 0,
    Delivered = 1,
    Retry = 2,
    Rejected = 3,
    Exception = 4
}
