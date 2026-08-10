namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationScopeDestroyReceipt(
    Guid OperationId,
    long ResultingRevision,
    int BatchSize,
    long RemovedRecordCount,
    int CompletedBatchCount,
    int RemovalProofVersion,
    string RemovalProofSha256,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);
