namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationScopeDestroyRequest(
    Guid OperationId,
    string ScopeId,
    long ExpectedRevision,
    int BatchSize);
