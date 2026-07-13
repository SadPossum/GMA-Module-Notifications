namespace Gma.Modules.Notifications.Adapters.Email;

public interface IUserNotificationEmailAddressResolver
{
    ValueTask<NotificationEmailDestinationResult> ResolveAsync(
        string scopeId,
        string userId,
        CancellationToken cancellationToken = default);
}
