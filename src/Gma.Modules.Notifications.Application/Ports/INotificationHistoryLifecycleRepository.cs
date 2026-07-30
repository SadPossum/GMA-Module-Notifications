namespace Gma.Modules.Notifications.Application.Ports;

using Gma.Modules.Notifications.Domain.Aggregates;

public interface INotificationHistoryLifecycleRepository
{
    Task<bool> RegisterAsync(
        UserNotification notification,
        CancellationToken cancellationToken);
}
