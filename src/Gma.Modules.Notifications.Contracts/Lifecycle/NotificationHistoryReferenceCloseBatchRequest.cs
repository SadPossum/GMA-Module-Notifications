namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationHistoryReferenceCloseBatchRequest(
    Guid OperationId,
    string ScopeId,
    NotificationHistoryReference Reference,
    long ExpectedVersion,
    int BatchSize);
