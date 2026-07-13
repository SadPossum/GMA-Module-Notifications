namespace Gma.Modules.Notifications.Contracts;

public sealed record AdminNotificationDeliveryAttemptItem(
    Guid Id,
    int AttemptNumber,
    NotificationDeliveryProviderCode Provider,
    NotificationDeliveryAttemptOutcome Outcome,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string? Code,
    string? ProviderMessageId);
