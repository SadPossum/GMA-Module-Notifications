namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationScopeBroadcastReadExportRecord(
    Guid ReadId,
    Guid BroadcastId,
    NotificationBroadcastRecipientKind RecipientKind,
    string RecipientId,
    DateTimeOffset ReadAtUtc)
    : NotificationScopeExportRecord;
