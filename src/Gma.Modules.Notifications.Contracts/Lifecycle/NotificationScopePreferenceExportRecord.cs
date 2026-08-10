namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationScopePreferenceExportRecord(
    Guid PreferenceId,
    string UserId,
    string TagKey,
    bool Enabled,
    int Version,
    DateTimeOffset UpdatedAtUtc)
    : NotificationScopeExportRecord;
