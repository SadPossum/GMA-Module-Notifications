namespace Gma.Modules.Notifications.Adapters.Email;

public enum NotificationEmailDestinationOutcome
{
    Unknown = 0,
    Resolved = 1,
    Unavailable = 2,
    Retry = 3
}

public sealed record NotificationEmailDestinationResult(
    NotificationEmailDestinationOutcome Outcome,
    string? Address = null,
    string? Code = null,
    DateTimeOffset? RetryAtUtc = null)
{
    public static NotificationEmailDestinationResult Resolved(string address) =>
        new(NotificationEmailDestinationOutcome.Resolved, Address: address);

    public static NotificationEmailDestinationResult Unavailable(string code = "email-address-unavailable") =>
        new(NotificationEmailDestinationOutcome.Unavailable, Code: code);

    public static NotificationEmailDestinationResult Retry(
        string code = "email-address-resolution-retry",
        DateTimeOffset? retryAtUtc = null) =>
        new(NotificationEmailDestinationOutcome.Retry, Code: code, RetryAtUtc: retryAtUtc);
}
