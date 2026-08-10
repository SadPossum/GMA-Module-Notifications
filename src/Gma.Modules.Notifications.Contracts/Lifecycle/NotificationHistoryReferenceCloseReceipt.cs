namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationHistoryReferenceCloseReceipt(
    Guid OperationId,
    NotificationHistoryReference Reference,
    long ResultingVersion,
    int RemovedRecordCount,
    string RemovedRecordIdsSha256,
    DateTimeOffset CompletedAtUtc);
