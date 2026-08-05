namespace Gma.Modules.Notifications.Persistence;

internal sealed class NotificationScopeClosedException()
    : InvalidOperationException("The notification scope is closed.");
