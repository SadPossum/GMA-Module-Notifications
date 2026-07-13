namespace Gma.Modules.Notifications.Integrations.Auth;

using System.Text.Json;
using Gma.Framework.Messaging;
using Gma.Modules.Auth.Contracts;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Contracts;

[IntegrationEventHandler("auth-member-authenticated-notification", RequiresExplicitProducerBinding = true)]
internal sealed class MemberAuthenticatedNotificationHandler(IUserNotificationRequestProjector projector)
    : IIntegrationEventHandler<MemberAuthenticatedIntegrationEvent>
{
    public Task HandleAsync(MemberAuthenticatedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        projector.ProjectAsync(
            new UserNotificationRequestedIntegrationEventV2(
                integrationEvent.EventId,
                integrationEvent.ScopeId,
                integrationEvent.OccurredAtUtc,
                integrationEvent.MemberId.ToString("D"),
                AuthModuleMetadata.Name,
                "account-signed-in",
                1,
                "New sign-in to your account",
                CreateSignInBody(integrationEvent),
                NotificationSeverity.Warning,
                JsonSerializer.Serialize(new
                {
                    integrationEvent.SessionId,
                    integrationEvent.AuthenticationMethod,
                    integrationEvent.IpAddress,
                    integrationEvent.UserAgent,
                    integrationEvent.OccurredAtUtc,
                }),
                AuthNotificationTags.SignIn,
                NotificationDeliveryPolicy.Mandatory),
            cancellationToken);

    private static string CreateSignInBody(MemberAuthenticatedIntegrationEvent integrationEvent)
    {
        string location = string.IsNullOrWhiteSpace(integrationEvent.IpAddress)
            ? string.Empty
            : $" from {integrationEvent.IpAddress}";
        return $"A new session was created using {integrationEvent.AuthenticationMethod}{location}.";
    }
}

[IntegrationEventHandler("auth-method-changed-notification", RequiresExplicitProducerBinding = true)]
internal sealed class MemberAuthenticationMethodChangedNotificationHandler(IUserNotificationRequestProjector projector)
    : IIntegrationEventHandler<MemberAuthenticationMethodChangedIntegrationEvent>
{
    public Task HandleAsync(
        MemberAuthenticationMethodChangedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        projector.ProjectAsync(
            new UserNotificationRequestedIntegrationEventV2(
                integrationEvent.EventId,
                integrationEvent.ScopeId,
                integrationEvent.OccurredAtUtc,
                integrationEvent.MemberId.ToString("D"),
                AuthModuleMetadata.Name,
                "authentication-method-changed",
                1,
                "Account sign-in method changed",
                $"The {integrationEvent.AuthenticationMethod} sign-in method was {AuthenticationMethodChangeNames.ToWireName(integrationEvent.Change)}.",
                NotificationSeverity.Warning,
                JsonSerializer.Serialize(new
                {
                    integrationEvent.AuthenticationMethod,
                    integrationEvent.Change,
                    integrationEvent.OccurredAtUtc,
                }),
                AuthNotificationTags.AuthenticationMethodChanged,
                NotificationDeliveryPolicy.Mandatory),
            cancellationToken);
}

[IntegrationEventHandler("auth-email-verification-request-notification", RequiresExplicitProducerBinding = true)]
internal sealed class MemberEmailVerificationRequestedNotificationHandler(IUserNotificationRequestProjector projector)
    : IIntegrationEventHandler<MemberEmailVerificationRequestedIntegrationEvent>
{
    public Task HandleAsync(
        MemberEmailVerificationRequestedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken) =>
        projector.ProjectAsync(
            new UserNotificationRequestedIntegrationEventV2(
                integrationEvent.EventId,
                integrationEvent.ScopeId,
                integrationEvent.OccurredAtUtc,
                integrationEvent.MemberId.ToString("D"),
                AuthModuleMetadata.Name,
                "email-verification-requested",
                1,
                "Verify your email address",
                $"Use this one-time verification code: {integrationEvent.VerificationCode}",
                NotificationSeverity.Info,
                JsonSerializer.Serialize(new
                {
                    integrationEvent.Email,
                    integrationEvent.ExpiresAtUtc,
                }),
                AuthNotificationTags.VerificationRequest,
                NotificationDeliveryPolicy.Mandatory),
            cancellationToken);
}

[IntegrationEventHandler("auth-email-verified-notification", RequiresExplicitProducerBinding = true)]
internal sealed class MemberEmailVerifiedNotificationHandler(IUserNotificationRequestProjector projector)
    : IIntegrationEventHandler<MemberEmailVerifiedIntegrationEvent>
{
    public Task HandleAsync(MemberEmailVerifiedIntegrationEvent integrationEvent, CancellationToken cancellationToken) =>
        projector.ProjectAsync(
            new UserNotificationRequestedIntegrationEventV2(
                integrationEvent.EventId,
                integrationEvent.ScopeId,
                integrationEvent.OccurredAtUtc,
                integrationEvent.MemberId.ToString("D"),
                AuthModuleMetadata.Name,
                "email-verified",
                1,
                "Email address verified",
                "Your email address was verified successfully.",
                NotificationSeverity.Success,
                JsonSerializer.Serialize(new { integrationEvent.Email }),
                AuthNotificationTags.VerificationCompleted,
                NotificationDeliveryPolicy.Mandatory),
            cancellationToken);
}
