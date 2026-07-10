namespace Gma.Modules.Notifications.Application.Validation;

using Gma.Modules.Notifications.Contracts;
using Gma.Framework.Naming;
using DomainRecipientKind = Gma.Modules.Notifications.Domain.ValueObjects.NotificationBroadcastRecipientKind;

internal static class NotificationBroadcastValidation
{
    public static IEnumerable<string> ValidateRecipient(NotificationBroadcastRecipientKind recipientKind, string recipientId)
    {
        DomainRecipientKind domainRecipientKind = NotificationBroadcastRecipientKindMapper.ToDomainValue(recipientKind);
        if (domainRecipientKind is not DomainRecipientKind.User and not DomainRecipientKind.Admin)
        {
            yield return "Notification broadcast recipient kind is invalid.";
        }

        if (!NotificationRecipientUserIds.TryNormalize(recipientId, out _))
        {
            yield return "Notification broadcast recipient id is required.";
        }
    }

    public static IEnumerable<string> ValidateScopeId(string? scopeId)
    {
        if (!string.IsNullOrWhiteSpace(scopeId) && !ScopeIds.TryNormalize(scopeId, out _))
        {
            yield return "Notification broadcast scope id is invalid.";
        }
    }
}
