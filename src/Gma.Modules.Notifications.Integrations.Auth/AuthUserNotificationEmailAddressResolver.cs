namespace Gma.Modules.Notifications.Integrations.Auth;

using Gma.Framework.Email;
using Gma.Framework.Notifications;
using Gma.Modules.Auth.Contracts;
using Gma.Modules.Notifications.Adapters.Email;
using Microsoft.Extensions.DependencyInjection;

internal sealed class AuthUserNotificationEmailAddressResolver(IServiceScopeFactory scopeFactory)
    : IUserNotificationEmailAddressResolver
{
    public async ValueTask<NotificationEmailDestinationResult> ResolveAsync(
        UserNotificationMessage message,
        CancellationToken cancellationToken = default)
    {
        if (TryGetPayloadEmail(message, out string? payloadEmail))
        {
            return NotificationEmailDestinationResult.Resolved(payloadEmail);
        }

        if (!Guid.TryParse(message.UserId, out Guid memberId))
        {
            return NotificationEmailDestinationResult.Unavailable("auth-member-id-invalid");
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IAuthMemberContactReader reader = scope.ServiceProvider.GetRequiredService<IAuthMemberContactReader>();
        string? email = await reader
            .GetPreferredVerifiedEmailAsync(message.ScopeId, memberId, cancellationToken)
            .ConfigureAwait(false);
        return EmailSendRequest.IsValidAddress(email)
            ? NotificationEmailDestinationResult.Resolved(email!)
            : NotificationEmailDestinationResult.Unavailable("auth-verified-email-unavailable");
    }

    private static bool TryGetPayloadEmail(
        UserNotificationMessage message,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? email)
    {
        email = null;
        if (!string.Equals(message.Module, AuthModuleMetadata.Name, StringComparison.Ordinal) ||
            !string.Equals(message.Name, "email-verification-requested", StringComparison.Ordinal) ||
            message.Payload.ValueKind != System.Text.Json.JsonValueKind.Object ||
            !message.Payload.TryGetProperty("Email", out System.Text.Json.JsonElement property))
        {
            return false;
        }

        email = property.GetString();
        return EmailSendRequest.IsValidAddress(email);
    }
}
