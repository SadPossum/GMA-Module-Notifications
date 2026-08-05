namespace Gma.Modules.Notifications.Domain.Entities;

public enum NotificationScopeCloseTransition
{
    Unknown = 0,
    Invalid = 1,
    Completed = 2,
    Replayed = 3,
    Conflict = 4
}
