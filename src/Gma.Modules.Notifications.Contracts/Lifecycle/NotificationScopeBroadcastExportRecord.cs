namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json;

public sealed record NotificationScopeBroadcastExportRecord(
    Guid BroadcastId,
    NotificationBroadcastAudience Audience,
    string SourceModule,
    string NotificationName,
    int NotificationVersion,
    string Title,
    string? Body,
    NotificationSeverity Severity,
    long StreamSequence,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset CreatedAtUtc,
    JsonElement Payload)
    : NotificationScopeExportRecord;
