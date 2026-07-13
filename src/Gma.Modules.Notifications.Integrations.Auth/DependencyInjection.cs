namespace Gma.Modules.Notifications.Integrations.Auth;

using Gma.Framework.Messaging;
using Gma.Modules.Auth.Contracts;
using Gma.Modules.Notifications.Adapters.Email;
using Gma.Modules.Notifications.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class DependencyInjection
{
    public static IServiceCollection AddAuthNotificationIntegration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IUserNotificationEmailAddressResolver, AuthUserNotificationEmailAddressResolver>();
        services.AddIntegrationEventHandler<
            MemberAuthenticatedIntegrationEvent,
            MemberAuthenticatedNotificationHandler>(
            NotificationsModuleMetadata.Name,
            AuthModuleMetadata.Name,
            MemberAuthenticatedIntegrationEvent.EventType,
            MemberAuthenticatedIntegrationEvent.EventVersion,
            "auth-member-authenticated-notification");
        services.AddIntegrationEventHandler<
            MemberAuthenticationMethodChangedIntegrationEvent,
            MemberAuthenticationMethodChangedNotificationHandler>(
            NotificationsModuleMetadata.Name,
            AuthModuleMetadata.Name,
            MemberAuthenticationMethodChangedIntegrationEvent.EventType,
            MemberAuthenticationMethodChangedIntegrationEvent.EventVersion,
            "auth-method-changed-notification");
        services.AddIntegrationEventHandler<
            MemberEmailVerificationRequestedIntegrationEvent,
            MemberEmailVerificationRequestedNotificationHandler>(
            NotificationsModuleMetadata.Name,
            AuthModuleMetadata.Name,
            MemberEmailVerificationRequestedIntegrationEvent.EventType,
            MemberEmailVerificationRequestedIntegrationEvent.EventVersion,
            "auth-email-verification-request-notification");
        services.AddIntegrationEventHandler<
            MemberEmailVerifiedIntegrationEvent,
            MemberEmailVerifiedNotificationHandler>(
            NotificationsModuleMetadata.Name,
            AuthModuleMetadata.Name,
            MemberEmailVerifiedIntegrationEvent.EventType,
            MemberEmailVerifiedIntegrationEvent.EventVersion,
            "auth-email-verified-notification");

        return services;
    }
}
