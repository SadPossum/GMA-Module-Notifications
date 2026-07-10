namespace Gma.Modules.Notifications.Application.Visibility;

using Gma.Framework.AccessControl;

internal static class NotificationHistoryAccess
{
    public static bool CanAccessUserHistory(AccessSubject subject) =>
        subject.Kind == AccessSubjectKind.User;
}
