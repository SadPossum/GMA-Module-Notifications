namespace Gma.Modules.Notifications.Application;

using Gma.Framework.Notifications;
using Gma.Modules.Notifications.Application.Ports;

internal sealed class NotificationBestEffortDeliveryPolicyEvaluator(
    INotificationRoutingRepository repository)
    : IUserNotificationDeliveryPolicyEvaluator
{
    public async ValueTask<bool> ShouldDeliverAsync(
        UserNotificationMessage message,
        string deliveryTag,
        CancellationToken cancellationToken = default) =>
        await repository
            .GetDeliveryPlanAllowedAsync(message.Id, deliveryTag, cancellationToken)
            .ConfigureAwait(false) == true;
}
