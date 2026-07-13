namespace Gma.Modules.Notifications.Adapters.Email;

using Gma.Framework.Notifications;

public interface IUserNotificationEmailAddressResolver
{
    ValueTask<NotificationEmailDestinationResult> ResolveAsync(
        UserNotificationMessage message,
        CancellationToken cancellationToken = default);
}
