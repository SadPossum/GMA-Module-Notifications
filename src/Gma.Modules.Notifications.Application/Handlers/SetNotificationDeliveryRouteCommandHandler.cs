namespace Gma.Modules.Notifications.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Modules.Notifications.Application.Commands;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.ValueObjects;

internal sealed class SetNotificationDeliveryRouteCommandHandler(
    INotificationRoutingRepository repository,
    INotificationDeliveryAdapterCatalog adapterCatalog,
    IIdGenerator idGenerator,
    ISystemClock clock)
    : ICommandHandler<SetNotificationDeliveryRouteCommand, AdminNotificationDeliveryRouteItem>
{
    public async Task<Result<AdminNotificationDeliveryRouteItem>> HandleAsync(
        SetNotificationDeliveryRouteCommand command,
        CancellationToken cancellationToken)
    {
        Result<NotificationTagKey> tagKey = NotificationTagKey.Create(command.DeliveryTag);
        if (tagKey.IsFailure || !tagKey.Value.IsDelivery)
        {
            return Result.Failure<AdminNotificationDeliveryRouteItem>(
                tagKey.IsFailure ? tagKey.Error : NotificationsApplicationErrors.TagDefinitionNotFound);
        }

        NotificationTagDefinition? definition = await repository
            .GetTagDefinitionAsync(tagKey.Value.Value, cancellationToken)
            .ConfigureAwait(false);
        if (definition is null)
        {
            return Result.Failure<AdminNotificationDeliveryRouteItem>(NotificationsApplicationErrors.TagDefinitionNotFound);
        }

        if (!definition.IsActive ||
            definition.Kind != Domain.ValueObjects.NotificationTagKind.Delivery)
        {
            return Result.Failure<AdminNotificationDeliveryRouteItem>(NotificationsApplicationErrors.TagDefinitionInactive);
        }

        NotificationDeliveryRoute? route = await repository
            .GetRouteAsync(tagKey.Value.Value, cancellationToken)
            .ConfigureAwait(false);
        bool providerSupported = adapterCatalog.Supports(command.Provider.Value, tagKey.Value.Value);
        bool deactivatingExistingProvider = route is not null &&
                                            !command.IsActive &&
                                            string.Equals(
                                                route.Provider.Value,
                                                command.Provider.Value,
                                                StringComparison.Ordinal);
        if (!providerSupported && !deactivatingExistingProvider)
        {
            return Result.Failure<AdminNotificationDeliveryRouteItem>(NotificationsApplicationErrors.DeliveryProviderUnsupported);
        }

        if (route is null)
        {
            Result<NotificationDeliveryRoute> created = NotificationDeliveryRoute.Create(
                idGenerator.NewId(),
                command.ScopeId,
                tagKey.Value.Value,
                command.Provider.Value,
                command.ActorId,
                clock.UtcNow);
            if (created.IsFailure)
            {
                return Result.Failure<AdminNotificationDeliveryRouteItem>(created.Error);
            }

            route = created.Value;
            if (!command.IsActive)
            {
                Result disabled = route.Update(command.Provider.Value, active: false, command.ActorId, clock.UtcNow);
                if (disabled.IsFailure)
                {
                    return Result.Failure<AdminNotificationDeliveryRouteItem>(disabled.Error);
                }
            }

            await repository.AddRouteAsync(route, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            Result update = route.Update(command.Provider.Value, command.IsActive, command.ActorId, clock.UtcNow);
            if (update.IsFailure)
            {
                return Result.Failure<AdminNotificationDeliveryRouteItem>(update.Error);
            }
        }

        return Result.Success(NotificationRoutingContractMapper.Route(route));
    }
}
