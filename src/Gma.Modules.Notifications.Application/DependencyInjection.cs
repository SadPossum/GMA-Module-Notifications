namespace Gma.Modules.Notifications.Application;

using Gma.Framework.Application.Composition;
using Gma.Framework.Messaging;
using Gma.Framework.Notifications;
using Gma.Modules.Notifications.Application.Handlers;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

public static class DependencyInjection
{
    public const string UserNotificationRequestHandlerNameSuffix = "notification-request";

    public static IServiceCollection AddNotificationsApplication(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(NotificationStreamOptionsRegistrationMarker)))
        {
            if (configuration is not null)
            {
                NotificationStreamOptionsValidation.GetValidatedOptions(configuration);
            }

            services.AddSingleton<NotificationStreamOptionsRegistrationMarker>();
            OptionsBuilder<NotificationStreamOptions> optionsBuilder = services.AddOptions<NotificationStreamOptions>();
            if (configuration is not null)
            {
                optionsBuilder.Bind(configuration.GetSection(NotificationStreamOptions.SectionName));
            }

            optionsBuilder.ValidateOnStart();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<NotificationStreamOptions>, NotificationStreamOptionsValidator>());
        }

        services.AddApplicationServicesFromAssembly(typeof(DependencyInjection).Assembly);
        services.TryAddScoped<UserNotificationRequestedIntegrationEventV2Handler>();
        services.TryAddScoped<IUserNotificationRequestProjector>(provider =>
            provider.GetRequiredService<UserNotificationRequestedIntegrationEventV2Handler>());
        services.TryAddScoped<IUserNotificationRequestProjectorV3>(provider =>
            provider.GetRequiredService<UserNotificationRequestedIntegrationEventV2Handler>());
        services.TryAddSingleton<INotificationPreferenceEvaluator, AllowAllNotificationPreferenceEvaluator>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<
            IUserNotificationDeliveryPolicyEvaluator,
            NotificationBestEffortDeliveryPolicyEvaluator>());
        services.TryAddSingleton<INotificationDeliveryAdapterCatalog, NotificationDeliveryAdapterCatalog>();
        OptionsBuilder<NotificationDeliveryOptions> deliveryOptions = services.AddOptions<NotificationDeliveryOptions>();
        if (configuration is not null)
        {
            deliveryOptions.Bind(configuration.GetSection(NotificationDeliveryOptions.SectionName));
        }
        deliveryOptions.ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<NotificationDeliveryOptions>, NotificationDeliveryOptionsValidator>());

        return services;
    }

    public static IServiceCollection AddUserNotificationRequestSubscription(
        this IServiceCollection services,
        string producerModule,
        string? handlerName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        string normalizedProducerModule = IntegrationEventNaming.NormalizeModuleName(
            producerModule,
            nameof(producerModule));
        string normalizedHandlerName = IntegrationEventNaming.NormalizeHandlerName(
            handlerName ?? $"{normalizedProducerModule}-{UserNotificationRequestHandlerNameSuffix}",
            nameof(handlerName));

        services.AddIntegrationEventHandler<UserNotificationRequestedIntegrationEvent, UserNotificationRequestedIntegrationEventHandler>(
            NotificationsModuleMetadata.Name,
            normalizedProducerModule,
            UserNotificationRequestedIntegrationEvent.EventType,
            UserNotificationRequestedIntegrationEvent.EventVersion,
            normalizedHandlerName);
        services.AddIntegrationEventHandler<UserNotificationRequestedIntegrationEventV2, UserNotificationRequestedIntegrationEventV2Handler>(
            NotificationsModuleMetadata.Name,
            normalizedProducerModule,
            UserNotificationRequestedIntegrationEventV2.EventType,
            UserNotificationRequestedIntegrationEventV2.EventVersion,
            $"{normalizedHandlerName}-v2");
        services.AddIntegrationEventHandler<UserNotificationRequestedIntegrationEventV3, UserNotificationRequestedIntegrationEventV2Handler>(
            NotificationsModuleMetadata.Name,
            normalizedProducerModule,
            UserNotificationRequestedIntegrationEventV3.EventType,
            UserNotificationRequestedIntegrationEventV3.EventVersion,
            $"{normalizedHandlerName}-v3");

        return services;
    }

    private sealed class NotificationStreamOptionsRegistrationMarker;
}
