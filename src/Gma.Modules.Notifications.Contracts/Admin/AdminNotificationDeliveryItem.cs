namespace Gma.Modules.Notifications.Contracts;

public sealed record AdminNotificationDeliveryItem(
    Guid Id,
    Guid NotificationId,
    string UserId,
    string DeliveryTag,
    NotificationDeliveryProviderCode Provider,
    NotificationDeliveryStatus Status,
    int Attempts,
    int MaxAttempts,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? NextAttemptAtUtc,
    DateTimeOffset? DeliveredAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? LastCode,
    string? ProviderMessageId,
    IReadOnlyList<AdminNotificationDeliveryAttemptItem> AttemptHistory);
