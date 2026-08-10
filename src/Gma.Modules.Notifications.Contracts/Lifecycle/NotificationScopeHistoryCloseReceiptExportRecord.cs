namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationScopeHistoryCloseReceiptExportRecord(
    Guid OperationId,
    NotificationHistoryReference Reference,
    string RequestSha256,
    long ResultingVersion,
    int RemovedRecordCount,
    string RemovedRecordIdsSha256,
    DateTimeOffset CompletedAtUtc)
    : NotificationScopeExportRecord;
