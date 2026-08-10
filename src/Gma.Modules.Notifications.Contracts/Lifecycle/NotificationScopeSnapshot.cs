namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationScopeSnapshot(
    NotificationScopeStatus Status,
    long Revision);
