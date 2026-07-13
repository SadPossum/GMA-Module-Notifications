namespace Gma.Modules.Notifications.Application.Handlers;

using Gma.Framework.Messaging;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Microsoft.Extensions.Options;
using ContractDeliveryPolicy = Contracts.NotificationDeliveryPolicy;
using ContractNotificationSeverity = Contracts.NotificationSeverity;
using ContractTagKind = Contracts.NotificationTagKind;
using DomainDeliveryPolicy = Domain.ValueObjects.NotificationDeliveryPolicy;
using DomainDeliveryStatus = Domain.ValueObjects.NotificationDeliveryStatus;
using DomainNotificationSeverity = Domain.ValueObjects.NotificationSeverity;
using DomainTagKind = Domain.ValueObjects.NotificationTagKind;
using DomainTagOrigin = Domain.ValueObjects.NotificationTagOrigin;

[IntegrationEventHandler("user-notification-request-v2", RequiresExplicitProducerBinding = true)]
internal sealed class UserNotificationRequestedIntegrationEventV2Handler(
    INotificationHistoryRepository historyRepository,
    INotificationRoutingRepository routingRepository,
    INotificationDeliveryAdapterCatalog adapterCatalog,
    INotificationPreferenceEvaluator preferenceEvaluator,
    IOptions<NotificationDeliveryOptions> deliveryOptions,
    ISystemClock clock,
    IIdGenerator idGenerator)
    : IIntegrationEventHandler<UserNotificationRequestedIntegrationEventV2>, IUserNotificationRequestProjector
{
    public Task HandleAsync(
        UserNotificationRequestedIntegrationEventV2 integrationEvent,
        CancellationToken cancellationToken) =>
        this.ProjectAsync(integrationEvent, cancellationToken);

    public async Task ProjectAsync(
        UserNotificationRequestedIntegrationEventV2 integrationEvent,
        CancellationToken cancellationToken)
    {
        if (await historyRepository.ExistsAsync(integrationEvent.EventId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        DateTimeOffset nowUtc = clock.UtcNow;
        IReadOnlySet<string> inactiveTags = await this.EnsureTagDefinitionsAsync(
                integrationEvent,
                nowUtc,
                cancellationToken)
            .ConfigureAwait(false);

        string[] tagKeys = integrationEvent.Tags.Select(tag => tag.Key).ToArray();
        IReadOnlySet<string> disabledTags = await routingRepository
            .GetDisabledTagsAsync(integrationEvent.UserId, tagKeys, cancellationToken)
            .ConfigureAwait(false);
        bool externalPreferenceAllowed = await preferenceEvaluator.ShouldStoreAsync(
                new NotificationPreferenceRequest(
                    integrationEvent.ScopeId,
                    integrationEvent.UserId,
                    integrationEvent.SourceModule,
                    integrationEvent.NotificationName,
                    integrationEvent.NotificationVersion,
                    integrationEvent.Severity),
                cancellationToken)
            .ConfigureAwait(false);
        bool domainSuppressed = integrationEvent.Tags
            .Where(tag => tag.Kind == ContractTagKind.Domain)
            .Any(tag => disabledTags.Contains(tag.Key));
        bool domainInactive = integrationEvent.Tags
            .Where(tag => tag.Kind == ContractTagKind.Domain)
            .Any(tag => inactiveTags.Contains(tag.Key));

        string[] deliveryTags = integrationEvent.Tags
            .Where(tag => tag.Kind == ContractTagKind.Delivery)
            .Select(tag => tag.Key)
            .ToArray();
        bool webVisible = !domainInactive &&
                          !inactiveTags.Contains(Framework.Notifications.NotificationTags.Web) &&
                          deliveryTags.Contains(Framework.Notifications.NotificationTags.Web, StringComparer.Ordinal) &&
                          !ShouldSuppress(
                              Framework.Notifications.NotificationTags.Web,
                              integrationEvent.DeliveryPolicy,
                              disabledTags,
                              domainSuppressed,
                              externalPreferenceAllowed);

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
            integrationEvent.PayloadJson,
            tagKeys,
            ToDomainPolicy(integrationEvent.DeliveryPolicy),
            webVisible);
        if (notification.IsFailure)
        {
            throw new InvalidOperationException(
                $"Notification request {integrationEvent.EventId} could not be projected: {notification.Error.Code}.");
        }

        List<NotificationDelivery> deliveries = [];
        foreach (string deliveryTag in deliveryTags)
        {
            string? suppressionCode = domainInactive || inactiveTags.Contains(deliveryTag)
                ? "tag-inactive"
                : ShouldSuppress(
                deliveryTag,
                integrationEvent.DeliveryPolicy,
                disabledTags,
                domainSuppressed,
                externalPreferenceAllowed)
                    ? "preference-disabled"
                    : null;
            deliveries.Add(await this.PlanDeliveryAsync(
                    integrationEvent,
                    deliveryTag,
                    suppressionCode,
                    nowUtc,
                    cancellationToken)
                .ConfigureAwait(false));
        }

        await historyRepository.AddAsync(notification.Value, cancellationToken).ConfigureAwait(false);
        await routingRepository.AddDeliveriesAsync(deliveries, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlySet<string>> EnsureTagDefinitionsAsync(
        UserNotificationRequestedIntegrationEventV2 integrationEvent,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        HashSet<string> inactive = new(StringComparer.Ordinal);
        foreach (NotificationTag tag in integrationEvent.Tags)
        {
            NotificationTagDefinition? existing = await routingRepository
                .GetTagDefinitionAsync(tag.Key, cancellationToken)
                .ConfigureAwait(false);
            DomainTagKind expectedKind = ToDomainTagKind(tag.Kind);
            if (existing is not null)
            {
                if (existing.Kind != expectedKind)
                {
                    throw new InvalidOperationException(
                        $"Notification tag '{tag.Key}' conflicts with its stored kind.");
                }

                if (!existing.IsActive)
                {
                    inactive.Add(tag.Key);
                }

                continue;
            }

            TagDefinitionDefaults defaults = Defaults(tag, integrationEvent.SourceModule);
            Framework.Results.Result<NotificationTagDefinition> definition = NotificationTagDefinition.Create(
                idGenerator.NewId(),
                integrationEvent.ScopeId,
                tag.Key,
                expectedKind,
                tag.DisplayName ?? defaults.DisplayName,
                tag.Description ?? defaults.Description,
                defaults.Origin,
                defaults.Owner,
                $"{integrationEvent.SourceModule}-notification-contract",
                nowUtc);
            if (definition.IsFailure)
            {
                throw new InvalidOperationException(
                    $"Notification tag '{tag.Key}' could not be registered: {definition.Error.Code}.");
            }

            await routingRepository.AddTagDefinitionAsync(definition.Value, cancellationToken).ConfigureAwait(false);
        }

        return inactive;
    }

    private async Task<NotificationDelivery> PlanDeliveryAsync(
        UserNotificationRequestedIntegrationEventV2 integrationEvent,
        string deliveryTag,
        string? suppressionCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (suppressionCode is not null)
        {
            return Required(NotificationDelivery.CreateTerminal(
                idGenerator.NewId(),
                integrationEvent.ScopeId,
                integrationEvent.EventId,
                deliveryTag,
                "preferences",
                DomainDeliveryStatus.Suppressed,
                suppressionCode,
                nowUtc));
        }

        if (string.Equals(deliveryTag, Framework.Notifications.NotificationTags.Web, StringComparison.Ordinal))
        {
            return Required(NotificationDelivery.CreateDelivered(
                idGenerator.NewId(),
                integrationEvent.ScopeId,
                integrationEvent.EventId,
                deliveryTag,
                "notifications-inbox",
                nowUtc));
        }

        string? configuredProvider = await routingRepository
            .GetActiveProviderAsync(deliveryTag, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<string> availableProviders = adapterCatalog.GetProviders(deliveryTag);
        if (configuredProvider is not null)
        {
            return adapterCatalog.Supports(configuredProvider, deliveryTag)
                ? Required(NotificationDelivery.CreatePending(
                    idGenerator.NewId(),
                    integrationEvent.ScopeId,
                    integrationEvent.EventId,
                    deliveryTag,
                    configuredProvider,
                    nowUtc,
                    deliveryOptions.Value.MaxAttempts))
                : this.Unroutable(integrationEvent, deliveryTag, "provider-unavailable", nowUtc);
        }

        return availableProviders.Count switch
        {
            1 => Required(NotificationDelivery.CreatePending(
                idGenerator.NewId(),
                integrationEvent.ScopeId,
                integrationEvent.EventId,
                deliveryTag,
                availableProviders[0],
                nowUtc,
                deliveryOptions.Value.MaxAttempts)),
            0 => this.Unroutable(integrationEvent, deliveryTag, "adapter-unavailable", nowUtc),
            _ => this.Unroutable(integrationEvent, deliveryTag, "route-ambiguous", nowUtc)
        };
    }

    private NotificationDelivery Unroutable(
        UserNotificationRequestedIntegrationEventV2 integrationEvent,
        string deliveryTag,
        string code,
        DateTimeOffset nowUtc) =>
        Required(NotificationDelivery.CreateTerminal(
            idGenerator.NewId(),
            integrationEvent.ScopeId,
            integrationEvent.EventId,
            deliveryTag,
            "routing",
            DomainDeliveryStatus.Unroutable,
            code,
            nowUtc));

    private static bool ShouldSuppress(
        string deliveryTag,
        ContractDeliveryPolicy policy,
        IReadOnlySet<string> disabledTags,
        bool domainSuppressed,
        bool externalPreferenceAllowed) =>
        policy == ContractDeliveryPolicy.RespectPreferences &&
        (!externalPreferenceAllowed || domainSuppressed || disabledTags.Contains(deliveryTag));

    private static NotificationDelivery Required(
        Framework.Results.Result<NotificationDelivery> result) =>
        result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException($"Notification delivery could not be planned: {result.Error.Code}.");

    private static TagDefinitionDefaults Defaults(NotificationTag tag, string sourceModule)
    {
        if (tag.Kind == ContractTagKind.Delivery)
        {
            return tag.Key switch
            {
                "delivery:web" => new("Web inbox", "Stores the notification in the recipient's durable web inbox.", DomainTagOrigin.System, "notifications"),
                "delivery:email" => new("Email", "Delivers the notification through the configured email adapter.", DomainTagOrigin.System, "notifications"),
                "delivery:push" => new("Push", "Delivers the notification through the configured mobile push adapter.", DomainTagOrigin.System, "notifications"),
                "delivery:sms" => new("SMS", "Delivers the notification through the configured SMS adapter.", DomainTagOrigin.System, "notifications"),
                _ => new(DisplayName(tag.Key), $"Delivery tag declared by the {sourceModule} module.", DomainTagOrigin.Module, sourceModule)
            };
        }

        return new(
            DisplayName(tag.Key),
            $"Domain notification tag declared by the {sourceModule} module.",
            DomainTagOrigin.Module,
            sourceModule);
    }

    private static string DisplayName(string key)
    {
        string name = key[(key.IndexOf(':', StringComparison.Ordinal) + 1)..];
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Replace('-', ' '));
    }

    private static DomainNotificationSeverity ToDomainSeverity(ContractNotificationSeverity severity) => severity switch
    {
        ContractNotificationSeverity.Info => DomainNotificationSeverity.Info,
        ContractNotificationSeverity.Success => DomainNotificationSeverity.Success,
        ContractNotificationSeverity.Warning => DomainNotificationSeverity.Warning,
        ContractNotificationSeverity.Error => DomainNotificationSeverity.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Notification severity is invalid.")
    };

    private static DomainDeliveryPolicy ToDomainPolicy(ContractDeliveryPolicy policy) => policy switch
    {
        ContractDeliveryPolicy.RespectPreferences => DomainDeliveryPolicy.RespectPreferences,
        ContractDeliveryPolicy.Mandatory => DomainDeliveryPolicy.Mandatory,
        _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Notification delivery policy is invalid.")
    };

    private static DomainTagKind ToDomainTagKind(ContractTagKind kind) => kind switch
    {
        ContractTagKind.Delivery => DomainTagKind.Delivery,
        ContractTagKind.Domain => DomainTagKind.Domain,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Notification tag kind is invalid.")
    };

    private sealed record TagDefinitionDefaults(
        string DisplayName,
        string Description,
        DomainTagOrigin Origin,
        string Owner);
}
