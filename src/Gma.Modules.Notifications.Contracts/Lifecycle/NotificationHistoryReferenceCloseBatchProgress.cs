namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationHistoryReferenceCloseBatchProgress(
    Guid OperationId,
    NotificationHistoryReference Reference,
    long ResultingVersion,
    long RemovedRecordCount,
    int CompletedBatchCount,
    int RemovalProofVersion,
    string RemovalProofSha256,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc);
