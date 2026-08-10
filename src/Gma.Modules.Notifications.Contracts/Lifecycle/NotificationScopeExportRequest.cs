namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationScopeExportRequest(
    string ScopeId,
    long ExpectedRevision,
    NotificationScopeExportStore Store,
    string? AfterCursor,
    int PageSize);
