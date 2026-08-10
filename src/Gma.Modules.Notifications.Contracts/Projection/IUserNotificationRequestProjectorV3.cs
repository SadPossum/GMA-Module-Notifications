namespace Gma.Modules.Notifications.Contracts;

public interface IUserNotificationRequestProjectorV3
{
    Task ProjectAsync(
        UserNotificationRequestedIntegrationEventV3 integrationEvent,
        CancellationToken cancellationToken);
}
