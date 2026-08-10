namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationScopeHistoryBatchCloseReceiptExportRecord(
    Guid OperationId,
    NotificationHistoryReference Reference,
    string RequestSha256,
    long ResultingVersion,
    long RemovedRecordCount,
    int CompletedBatchCount,
    int RemovalProofVersion,
    string RemovalProofSha256,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc)
    : NotificationScopeExportRecord;
