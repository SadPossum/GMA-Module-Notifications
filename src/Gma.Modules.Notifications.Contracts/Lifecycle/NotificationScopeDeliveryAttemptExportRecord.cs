namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationScopeDeliveryAttemptExportRecord(
    Guid AttemptId,
    Guid DeliveryId,
    int AttemptNumber,
    NotificationDeliveryProviderCode Provider,
    NotificationDeliveryAttemptOutcome Outcome,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string? Code,
    string? ProviderMessageId)
    : NotificationScopeExportRecord;
