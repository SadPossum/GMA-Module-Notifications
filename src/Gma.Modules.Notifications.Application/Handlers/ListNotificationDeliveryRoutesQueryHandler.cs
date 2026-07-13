namespace Gma.Modules.Notifications.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Application.Queries;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.Aggregates;

internal sealed class ListNotificationDeliveryRoutesQueryHandler(INotificationRoutingRepository repository)
    : IQueryHandler<ListNotificationDeliveryRoutesQuery, IReadOnlyList<AdminNotificationDeliveryRouteItem>>
{
    public async Task<Result<IReadOnlyList<AdminNotificationDeliveryRouteItem>>> HandleAsync(
        ListNotificationDeliveryRoutesQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<NotificationDeliveryRoute> routes = await repository
            .ListRoutesAsync(cancellationToken)
            .ConfigureAwait(false);
        return Result.Success<IReadOnlyList<AdminNotificationDeliveryRouteItem>>(
            routes.Select(NotificationRoutingContractMapper.Route).ToArray());
    }
}
