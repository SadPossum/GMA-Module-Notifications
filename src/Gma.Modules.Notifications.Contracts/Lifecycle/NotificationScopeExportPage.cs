namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationScopeExportPage(
    NotificationScopeExportStatus Status,
    long ScopeRevision,
    NotificationScopeExportStore Store,
    IReadOnlyList<NotificationScopeExportRecord> Records,
    string? NextCursor,
    bool HasMore);
