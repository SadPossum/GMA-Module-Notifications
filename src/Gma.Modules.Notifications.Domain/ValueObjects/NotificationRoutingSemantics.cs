namespace Gma.Modules.Notifications.Domain.ValueObjects;

public enum NotificationTagKind
{
    Unknown = 0,
    Delivery = 1,
    Domain = 2
}

public enum NotificationDeliveryPolicy
{
    Unknown = 0,
    RespectPreferences = 1,
    Mandatory = 2
}

public enum NotificationTagOrigin
{
    Unknown = 0,
    System = 1,
    Module = 2,
    Operator = 3
}

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

public enum NotificationDeliveryAttemptOutcome
{
    Unknown = 0,
    Delivered = 1,
    Retry = 2,
    Rejected = 3,
    Exception = 4
}

public static class NotificationRoutingSemanticNames
{
    public const int MaxLength = 32;

    public static string TagKind(NotificationTagKind value) => value switch
    {
        NotificationTagKind.Delivery => "delivery",
        NotificationTagKind.Domain => "domain",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Notification tag kind is invalid.")
    };

    public static NotificationTagKind ParseTagKind(string value) => value switch
    {
        "delivery" => NotificationTagKind.Delivery,
        "domain" => NotificationTagKind.Domain,
        _ => NotificationTagKind.Unknown
    };

    public static string DeliveryPolicy(NotificationDeliveryPolicy value) => value switch
    {
        NotificationDeliveryPolicy.RespectPreferences => "respect-preferences",
        NotificationDeliveryPolicy.Mandatory => "mandatory",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Notification delivery policy is invalid.")
    };

    public static NotificationDeliveryPolicy ParseDeliveryPolicy(string value) => value switch
    {
        "respect-preferences" => NotificationDeliveryPolicy.RespectPreferences,
        "mandatory" => NotificationDeliveryPolicy.Mandatory,
        _ => NotificationDeliveryPolicy.Unknown
    };

    public static string TagOrigin(NotificationTagOrigin value) => value switch
    {
        NotificationTagOrigin.System => "system",
        NotificationTagOrigin.Module => "module",
        NotificationTagOrigin.Operator => "operator",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Notification tag origin is invalid.")
    };

    public static NotificationTagOrigin ParseTagOrigin(string value) => value switch
    {
        "system" => NotificationTagOrigin.System,
        "module" => NotificationTagOrigin.Module,
        "operator" => NotificationTagOrigin.Operator,
        _ => NotificationTagOrigin.Unknown
    };

    public static string DeliveryStatus(NotificationDeliveryStatus value) => value switch
    {
        NotificationDeliveryStatus.Pending => "pending",
        NotificationDeliveryStatus.Processing => "processing",
        NotificationDeliveryStatus.RetryScheduled => "retry-scheduled",
        NotificationDeliveryStatus.Delivered => "delivered",
        NotificationDeliveryStatus.Rejected => "rejected",
        NotificationDeliveryStatus.Exhausted => "exhausted",
        NotificationDeliveryStatus.Suppressed => "suppressed",
        NotificationDeliveryStatus.Unroutable => "unroutable",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Notification delivery status is invalid.")
    };

    public static NotificationDeliveryStatus ParseDeliveryStatus(string value) => value switch
    {
        "pending" => NotificationDeliveryStatus.Pending,
        "processing" => NotificationDeliveryStatus.Processing,
        "retry-scheduled" => NotificationDeliveryStatus.RetryScheduled,
        "delivered" => NotificationDeliveryStatus.Delivered,
        "rejected" => NotificationDeliveryStatus.Rejected,
        "exhausted" => NotificationDeliveryStatus.Exhausted,
        "suppressed" => NotificationDeliveryStatus.Suppressed,
        "unroutable" => NotificationDeliveryStatus.Unroutable,
        _ => NotificationDeliveryStatus.Unknown
    };

    public static string AttemptOutcome(NotificationDeliveryAttemptOutcome value) => value switch
    {
        NotificationDeliveryAttemptOutcome.Delivered => "delivered",
        NotificationDeliveryAttemptOutcome.Retry => "retry",
        NotificationDeliveryAttemptOutcome.Rejected => "rejected",
        NotificationDeliveryAttemptOutcome.Exception => "exception",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Notification delivery attempt outcome is invalid.")
    };

    public static NotificationDeliveryAttemptOutcome ParseAttemptOutcome(string value) => value switch
    {
        "delivered" => NotificationDeliveryAttemptOutcome.Delivered,
        "retry" => NotificationDeliveryAttemptOutcome.Retry,
        "rejected" => NotificationDeliveryAttemptOutcome.Rejected,
        "exception" => NotificationDeliveryAttemptOutcome.Exception,
        _ => NotificationDeliveryAttemptOutcome.Unknown
    };
}
