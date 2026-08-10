namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json;

public sealed record NotificationHistoryReferenceRecord(
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
    JsonElement Payload,
    IReadOnlyList<string> Tags,
    NotificationDeliveryPolicy DeliveryPolicy);
