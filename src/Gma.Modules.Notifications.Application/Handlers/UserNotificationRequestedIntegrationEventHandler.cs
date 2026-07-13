namespace Gma.Modules.Notifications.Application.Handlers;

using Gma.Framework.Messaging;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.ValueObjects;
using ContractNotificationSeverity = Contracts.NotificationSeverity;
using DomainNotificationSeverity = Domain.ValueObjects.NotificationSeverity;
using DomainTagOrigin = Domain.ValueObjects.NotificationTagOrigin;

[IntegrationEventHandler("user-notification-request", RequiresExplicitProducerBinding = true)]
internal sealed class UserNotificationRequestedIntegrationEventHandler(
    INotificationHistoryRepository repository,
    INotificationRoutingRepository routingRepository,
    INotificationPreferenceEvaluator preferenceEvaluator,
    ISystemClock clock,
    IIdGenerator idGenerator)
    : IIntegrationEventHandler<UserNotificationRequestedIntegrationEvent>
{
    public async Task HandleAsync(
        UserNotificationRequestedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (await repository.ExistsAsync(integrationEvent.EventId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        bool shouldStore = await preferenceEvaluator.ShouldStoreAsync(
                new NotificationPreferenceRequest(
                    integrationEvent.ScopeId,
                    integrationEvent.UserId,
                    integrationEvent.SourceModule,
                    integrationEvent.NotificationName,
                    integrationEvent.NotificationVersion,
                    integrationEvent.Severity),
                cancellationToken)
            .ConfigureAwait(false);
        if (!shouldStore)
        {
            return;
        }

        DateTimeOffset nowUtc = clock.UtcNow;
        Framework.Results.Result<UserNotification> notification = UserNotification.Create(
            integrationEvent.EventId,
            integrationEvent.ScopeId,
            integrationEvent.UserId,
            integrationEvent.SourceModule,
            integrationEvent.NotificationName,
            integrationEvent.NotificationVersion,
            integrationEvent.Title,
            integrationEvent.Body,
            ToDomainSeverity(integrationEvent.Severity),
            integrationEvent.OccurredAtUtc,
            nowUtc,
            integrationEvent.PayloadJson);

        if (notification.IsFailure)
        {
            throw new InvalidOperationException(
                $"Notification request {integrationEvent.EventId} could not be projected: {notification.Error.Code}.");
        }

        NotificationTagDefinition? webDefinition = await routingRepository
            .GetTagDefinitionAsync(Framework.Notifications.NotificationTags.Web, cancellationToken)
            .ConfigureAwait(false);
        if (webDefinition is null)
        {
            Framework.Results.Result<NotificationTagDefinition> definition = NotificationTagDefinition.Create(
                idGenerator.NewId(),
                integrationEvent.ScopeId,
                Framework.Notifications.NotificationTags.Web,
                Domain.ValueObjects.NotificationTagKind.Delivery,
                "Web inbox",
                "Stores the notification in the recipient's durable web inbox.",
                DomainTagOrigin.System,
                "notifications",
                "notifications-v1-compatibility",
                nowUtc);
            if (definition.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Legacy notification web tag could not be registered: {definition.Error.Code}.");
            }

            await routingRepository.AddTagDefinitionAsync(definition.Value, cancellationToken).ConfigureAwait(false);
        }

        Framework.Results.Result<NotificationDelivery> delivery = NotificationDelivery.CreateDelivered(
            idGenerator.NewId(),
            integrationEvent.ScopeId,
            integrationEvent.EventId,
            Framework.Notifications.NotificationTags.Web,
            "notifications-inbox",
            nowUtc);
        if (delivery.IsFailure)
        {
            throw new InvalidOperationException(
                $"Legacy notification delivery could not be recorded: {delivery.Error.Code}.");
        }

        await repository.AddAsync(notification.Value, cancellationToken).ConfigureAwait(false);
        await routingRepository.AddDeliveriesAsync([delivery.Value], cancellationToken).ConfigureAwait(false);
    }

    private static DomainNotificationSeverity ToDomainSeverity(ContractNotificationSeverity severity) =>
        severity switch
        {
            ContractNotificationSeverity.Info => DomainNotificationSeverity.Info,
            ContractNotificationSeverity.Success => DomainNotificationSeverity.Success,
            ContractNotificationSeverity.Warning => DomainNotificationSeverity.Warning,
            ContractNotificationSeverity.Error => DomainNotificationSeverity.Error,
            _ => throw new ArgumentOutOfRangeException(
                nameof(severity),
                severity,
                "Notification severity must be a defined non-unknown value.")
        };
}
