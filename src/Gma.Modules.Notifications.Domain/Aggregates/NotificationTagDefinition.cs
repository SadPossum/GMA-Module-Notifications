namespace Gma.Modules.Notifications.Domain.Aggregates;

using Gma.Framework.Domain.Models;
using Gma.Framework.Naming;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Domain.Errors;
using Gma.Modules.Notifications.Domain.ValueObjects;

public sealed class NotificationTagDefinition : ScopedAggregateRoot<Guid>
{
    public const int DisplayNameMaxLength = 128;
    public const int DescriptionMaxLength = 512;
    public const int OwnerMaxLength = 128;
    public const int ActorIdMaxLength = 256;

    private NotificationTagDefinition() { }

    private NotificationTagDefinition(Guid id, string scopeId) : base(id, scopeId) { }

    public NotificationTagKey Key { get; private set; } = null!;
    public NotificationTagKind Kind { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public NotificationTagOrigin Origin { get; private set; }
    public string Owner { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public string UpdatedBy { get; private set; } = string.Empty;

    public static Result<NotificationTagDefinition> Create(
        Guid id,
        string scopeId,
        string key,
        NotificationTagKind kind,
        string displayName,
        string description,
        NotificationTagOrigin origin,
        string owner,
        string actorId,
        DateTimeOffset nowUtc)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<NotificationTagDefinition>(NotificationsDomainErrors.TagDefinitionIdRequired);
        }

        if (!ScopeIds.TryNormalize(scopeId, out string? normalizedScopeId))
        {
            return Result.Failure<NotificationTagDefinition>(NotificationsDomainErrors.TenantInvalid);
        }

        Result<NotificationTagKey> tagKey = NotificationTagKey.Create(key);
        if (tagKey.IsFailure || !KindMatches(tagKey.Value, kind))
        {
            return Result.Failure<NotificationTagDefinition>(NotificationsDomainErrors.TagKindInvalid);
        }

        if (!TryNormalizeText(displayName, DisplayNameMaxLength, out string? normalizedDisplayName) ||
            !TryNormalizeText(description, DescriptionMaxLength, out string? normalizedDescription) ||
            !TryNormalizeOwner(owner, out string? normalizedOwner) ||
            !TryNormalizeActor(actorId, out string? normalizedActor) ||
            !IsValidOrigin(origin) ||
            nowUtc == default)
        {
            return Result.Failure<NotificationTagDefinition>(NotificationsDomainErrors.TagDefinitionInvalid);
        }

        return Result.Success(new NotificationTagDefinition(id, normalizedScopeId!)
        {
            Key = tagKey.Value,
            Kind = kind,
            DisplayName = normalizedDisplayName!,
            Description = normalizedDescription!,
            Origin = origin,
            Owner = normalizedOwner!,
            IsActive = true,
            Version = 1,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            CreatedBy = normalizedActor!,
            UpdatedBy = normalizedActor!
        });
    }

    public Result Update(string displayName, string description, string actorId, DateTimeOffset nowUtc)
    {
        if (!TryNormalizeText(displayName, DisplayNameMaxLength, out string? normalizedDisplayName) ||
            !TryNormalizeText(description, DescriptionMaxLength, out string? normalizedDescription) ||
            !TryNormalizeActor(actorId, out string? normalizedActor) ||
            nowUtc == default)
        {
            return Result.Failure(NotificationsDomainErrors.TagDefinitionInvalid);
        }

        this.DisplayName = normalizedDisplayName!;
        this.Description = normalizedDescription!;
        this.UpdatedBy = normalizedActor!;
        this.UpdatedAtUtc = nowUtc;
        this.Version++;
        return Result.Success();
    }

    public Result SetActive(bool active, string actorId, DateTimeOffset nowUtc)
    {
        if (!TryNormalizeActor(actorId, out string? normalizedActor) || nowUtc == default)
        {
            return Result.Failure(NotificationsDomainErrors.TagDefinitionInvalid);
        }

        if (this.IsActive == active)
        {
            return Result.Success();
        }

        this.IsActive = active;
        this.UpdatedBy = normalizedActor!;
        this.UpdatedAtUtc = nowUtc;
        this.Version++;
        return Result.Success();
    }

    private static bool KindMatches(NotificationTagKey key, NotificationTagKind kind) =>
        (kind == NotificationTagKind.Delivery && key.IsDelivery) ||
        (kind == NotificationTagKind.Domain && key.IsDomain);

    private static bool IsValidOrigin(NotificationTagOrigin origin) =>
        origin is NotificationTagOrigin.System or NotificationTagOrigin.Module or NotificationTagOrigin.Operator;

    private static bool TryNormalizeText(string value, int maxLength, out string? normalized)
    {
        normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is not null && normalized.Length <= maxLength && !normalized.Any(char.IsControl);
    }

    private static bool TryNormalizeOwner(string value, out string? normalized)
    {
        try
        {
            normalized = SharedNameSegments.NormalizeKebabSegment(value, "tag owner", nameof(value));
            return normalized.Length <= OwnerMaxLength;
        }
        catch (ArgumentException)
        {
            normalized = null;
            return false;
        }
    }

    private static bool TryNormalizeActor(string value, out string? normalized)
    {
        normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is not null && normalized.Length <= ActorIdMaxLength && !normalized.Any(char.IsControl);
    }
}
