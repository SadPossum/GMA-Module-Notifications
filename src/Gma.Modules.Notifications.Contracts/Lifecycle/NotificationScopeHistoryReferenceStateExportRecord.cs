namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationScopeHistoryReferenceStateExportRecord(
    NotificationHistoryReference Reference,
    long Version,
    bool IsClosed,
    DateTimeOffset? ClosedAtUtc,
    Guid? CloseOperationId,
    string? CloseRequestSha256)
    : NotificationScopeExportRecord;
