namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationScopeDeliveryExportRecord(
    Guid DeliveryId,
    Guid NotificationId,
    string DeliveryTag,
    NotificationDeliveryProviderCode Provider,
    NotificationDeliveryStatus Status,
    int Attempts,
    int MaxAttempts,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? NextAttemptAtUtc,
    string? LockedBy,
    DateTimeOffset? LockedUntilUtc,
    DateTimeOffset? DeliveredAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? LastCode,
    string? ProviderMessageId,
    Guid ConcurrencyStamp)
    : NotificationScopeExportRecord;
