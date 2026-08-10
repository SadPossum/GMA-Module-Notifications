namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationScopeDestroyProgress(
    Guid OperationId,
    long ResultingRevision,
    int BatchSize,
    NotificationScopeDestructionStage Stage,
    long RemovedRecordCount,
    int CompletedBatchCount,
    int RemovalProofVersion,
    string RemovalProofSha256,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc);
