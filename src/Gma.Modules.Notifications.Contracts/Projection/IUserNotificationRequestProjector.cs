namespace Gma.Modules.Notifications.Contracts;

public interface IUserNotificationRequestProjector
{
    Task ProjectAsync(
        UserNotificationRequestedIntegrationEventV2 integrationEvent,
        CancellationToken cancellationToken);
}
