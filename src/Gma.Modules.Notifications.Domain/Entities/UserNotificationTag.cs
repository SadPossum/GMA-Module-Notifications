namespace Gma.Modules.Notifications.Domain.Entities;

using Gma.Framework.Domain;
using Gma.Framework.Naming;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Domain.Errors;
using Gma.Modules.Notifications.Domain.ValueObjects;

public sealed class UserNotificationTag : IScopedEntity
{
    private UserNotificationTag() { }

    private UserNotificationTag(Guid notificationId, string scopeId, NotificationTagKey key)
    {
        this.NotificationId = notificationId;
        this.ScopeId = scopeId;
        this.Key = key;
    }

    public Guid NotificationId { get; private set; }
    public string ScopeId { get; private set; } = string.Empty;
    public NotificationTagKey Key { get; private set; } = null!;

    public static Result<UserNotificationTag> Create(Guid notificationId, string scopeId, string key)
    {
        Result<NotificationTagKey> tagKey = NotificationTagKey.Create(key);
        if (notificationId == Guid.Empty ||
            !ScopeIds.TryNormalize(scopeId, out string? normalizedScopeId) ||
            tagKey.IsFailure)
        {
            return Result.Failure<UserNotificationTag>(NotificationsDomainErrors.TagKeyInvalid);
        }

        return Result.Success(new UserNotificationTag(notificationId, normalizedScopeId!, tagKey.Value));
    }
}
