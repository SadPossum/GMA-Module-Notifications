namespace Gma.Modules.Notifications.Domain.Entities;

using Gma.Framework.Domain;
using Gma.Framework.Naming;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Domain.Errors;
using Gma.Modules.Notifications.Domain.ValueObjects;

public sealed class UserNotificationReference : IScopedEntity
{
    private UserNotificationReference() { }

    private UserNotificationReference(
        Guid notificationId,
        string scopeId,
        NotificationHistoryReferenceKey reference)
    {
        this.NotificationId = notificationId;
        this.ScopeId = scopeId;
        this.Namespace = reference.Namespace;
        this.Digest = reference.Digest;
    }

    public Guid NotificationId { get; private set; }
    public string ScopeId { get; private set; } = string.Empty;
    public string Namespace { get; private set; } = string.Empty;
    public string Digest { get; private set; } = string.Empty;

    public static Result<UserNotificationReference> Create(
        Guid notificationId,
        string scopeId,
        NotificationHistoryReferenceKey reference)
    {
        if (notificationId == Guid.Empty ||
            !ScopeIds.TryNormalize(
                scopeId,
                out string? normalizedScopeId) ||
            reference is null)
        {
            return Result.Failure<UserNotificationReference>(
                NotificationsDomainErrors.HistoryReferenceInvalid);
        }

        return Result.Success(
            new UserNotificationReference(
                notificationId,
                normalizedScopeId!,
                reference));
    }

    public NotificationHistoryReferenceKey ToKey() =>
        NotificationHistoryReferenceKey.Create(
            this.Namespace,
            this.Digest).Value;
}
