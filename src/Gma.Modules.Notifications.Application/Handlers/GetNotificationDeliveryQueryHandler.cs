namespace Gma.Modules.Notifications.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Application.Queries;
using Gma.Modules.Notifications.Contracts;

internal sealed class GetNotificationDeliveryQueryHandler(INotificationRoutingRepository repository)
    : IQueryHandler<GetNotificationDeliveryQuery, AdminNotificationDeliveryItem>
{
    public async Task<Result<AdminNotificationDeliveryItem>> HandleAsync(
        GetNotificationDeliveryQuery query,
        CancellationToken cancellationToken)
    {
        AdminNotificationDeliveryItem? delivery = await repository
            .GetDeliveryAsync(query.DeliveryId, cancellationToken)
            .ConfigureAwait(false);
        return delivery is null
            ? Result.Failure<AdminNotificationDeliveryItem>(NotificationsApplicationErrors.DeliveryNotFound)
            : Result.Success(delivery);
    }
}
