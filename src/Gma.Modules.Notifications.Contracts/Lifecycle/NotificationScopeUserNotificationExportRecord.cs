namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json;

public sealed record NotificationScopeUserNotificationExportRecord(
    Guid NotificationId,
    string RecipientId,
    string SourceModule,
    string NotificationName,
    int NotificationVersion,
    string Title,
    string? Body,
    NotificationSeverity Severity,
    long StreamSequence,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReadAtUtc,
    JsonElement Payload,
    NotificationDeliveryPolicy DeliveryPolicy,
    bool IsInboxVisible,
    IReadOnlyList<string> Tags,
    IReadOnlyList<NotificationHistoryReference> References)
    : NotificationScopeExportRecord;
