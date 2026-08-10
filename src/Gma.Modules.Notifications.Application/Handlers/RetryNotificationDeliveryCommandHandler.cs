namespace Gma.Modules.Notifications.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Time;
using Gma.Modules.Notifications.Application.Commands;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Domain.Aggregates;
using Microsoft.Extensions.Options;

internal sealed class RetryNotificationDeliveryCommandHandler(
    INotificationRoutingRepository repository,
    INotificationDeliveryAdapterCatalog adapterCatalog,
    ISystemClock clock,
    IOptions<NotificationDeliveryOptions> deliveryOptions)
    : ICommandHandler<RetryNotificationDeliveryCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        RetryNotificationDeliveryCommand command,
        CancellationToken cancellationToken)
    {
        NotificationDelivery? delivery = await repository
            .GetDeliveryForUpdateAsync(command.DeliveryId, cancellationToken)
            .ConfigureAwait(false);
        if (delivery is null)
        {
            return Result.Failure<Unit>(NotificationsApplicationErrors.DeliveryNotFound);
        }

        string? provider = adapterCatalog.Supports(delivery.Provider.Value, delivery.DeliveryTag.Value)
            ? delivery.Provider.Value
            : null;
        if (provider is null)
        {
            string? configuredProvider = await repository
                .GetActiveProviderAsync(delivery.DeliveryTag.Value, cancellationToken)
                .ConfigureAwait(false);
            if (configuredProvider is not null)
            {
                provider = adapterCatalog.Supports(configuredProvider, delivery.DeliveryTag.Value)
                    ? configuredProvider
                    : null;
            }
            else
            {
                IReadOnlyList<string> availableProviders = adapterCatalog.GetProviders(delivery.DeliveryTag.Value);
                provider = availableProviders.Count == 1 ? availableProviders[0] : null;
            }
        }

        if (provider is null || !adapterCatalog.Supports(provider, delivery.DeliveryTag.Value))
        {
            return Result.Failure<Unit>(NotificationsApplicationErrors.DeliveryProviderUnsupported);
        }

        Result retried = delivery.RetryManually(
            clock.UtcNow,
            deliveryOptions.Value.MaxAttempts,
            provider);
        return retried.IsFailure
            ? Result.Failure<Unit>(retried.Error)
            : Result.Success(Unit.Value);
    }
}
