namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationPreferenceItem(
    string TagKey,
    NotificationTagKind Kind,
    string DisplayName,
    string Description,
    bool Enabled,
    bool IsActive);
