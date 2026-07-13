namespace Gma.Modules.Notifications.Domain.Aggregates;

using Gma.Framework.Domain.Models;
using Gma.Framework.Naming;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Domain.Errors;
using Gma.Modules.Notifications.Domain.ValueObjects;

public sealed class NotificationPreference : ScopedAggregateRoot<Guid>
{
    public const int UserIdMaxLength = 256;

    private NotificationPreference() { }
    private NotificationPreference(Guid id, string scopeId) : base(id, scopeId) { }

    public string UserId { get; private set; } = string.Empty;
    public NotificationTagKey TagKey { get; private set; } = null!;
    public bool Enabled { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Result<NotificationPreference> Create(
        Guid id,
        string scopeId,
        string userId,
        string tagKey,
        bool enabled,
        DateTimeOffset nowUtc)
    {
        if (id == Guid.Empty ||
            !ScopeIds.TryNormalize(scopeId, out string? normalizedScopeId) ||
            !TryNormalizeUserId(userId, out string? normalizedUserId) ||
            nowUtc == default)
        {
            return Result.Failure<NotificationPreference>(NotificationsDomainErrors.PreferenceInvalid);
        }

        Result<NotificationTagKey> key = NotificationTagKey.Create(tagKey);
        if (key.IsFailure)
        {
            return Result.Failure<NotificationPreference>(key.Error);
        }

        return Result.Success(new NotificationPreference(id, normalizedScopeId!)
        {
            UserId = normalizedUserId!,
            TagKey = key.Value,
            Enabled = enabled,
            Version = 1,
            UpdatedAtUtc = nowUtc
        });
    }

    public void SetEnabled(bool enabled, DateTimeOffset nowUtc)
    {
        if (nowUtc == default)
        {
            throw new ArgumentException("Updated timestamp is required.", nameof(nowUtc));
        }

        if (this.Enabled == enabled)
        {
            return;
        }

        this.Enabled = enabled;
        this.UpdatedAtUtc = nowUtc;
        this.Version++;
    }

    private static bool TryNormalizeUserId(string value, out string? normalized)
    {
        normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is not null && normalized.Length <= UserIdMaxLength && !normalized.Any(char.IsControl);
    }
}
