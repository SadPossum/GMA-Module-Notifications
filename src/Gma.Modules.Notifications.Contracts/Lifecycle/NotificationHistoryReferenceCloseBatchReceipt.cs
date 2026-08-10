namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationHistoryReferenceCloseBatchReceipt(
    Guid OperationId,
    NotificationHistoryReference Reference,
    long ResultingVersion,
    long RemovedRecordCount,
    int CompletedBatchCount,
    int RemovalProofVersion,
    string RemovalProofSha256,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);
