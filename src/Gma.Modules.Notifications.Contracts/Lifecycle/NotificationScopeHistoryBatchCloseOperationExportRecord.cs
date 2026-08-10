namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationScopeHistoryBatchCloseOperationExportRecord(
    Guid OperationId,
    NotificationHistoryReference Reference,
    string RequestSha256,
    long ExpectedVersion,
    long ResultingVersion,
    int BatchSize,
    long RemovedRecordCount,
    int CompletedBatchCount,
    int RemovalProofVersion,
    string RemovalProofSha256,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc)
    : NotificationScopeExportRecord;
