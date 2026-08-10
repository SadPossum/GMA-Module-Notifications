namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationHistoryReferenceCloseRequest(
    Guid OperationId,
    string ScopeId,
    NotificationHistoryReference Reference,
    long ExpectedVersion,
    int MaximumRecords);
