namespace Gma.Modules.Notifications.Api;

using System.Security.Claims;
using System.Diagnostics.CodeAnalysis;
using Gma.Framework.AccessControl;
using Gma.Framework.Security;
using Gma.Modules.Notifications.Contracts;

internal static class NotificationUserContext
{
    public static bool TryResolveSubject(
        ClaimsPrincipal principal,
        [NotNullWhen(true)] out AccessSubject? subject)
    {
        ArgumentNullException.ThrowIfNull(principal);
        subject = null;

        string? candidateUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
                                  principal.FindFirstValue(ApplicationClaimNames.Subject);
        if (!NotificationRecipientUserIds.TryNormalize(candidateUserId, out string normalizedUserId))
        {
            return false;
        }

        return AccessSubject.TryCreate(AccessSubjectKind.User, normalizedUserId, out subject);
    }
}
