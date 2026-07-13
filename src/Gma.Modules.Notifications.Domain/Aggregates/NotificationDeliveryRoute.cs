namespace Gma.Modules.Notifications.Domain.Aggregates;

using Gma.Framework.Domain.Models;
using Gma.Framework.Naming;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Domain.Errors;
using Gma.Modules.Notifications.Domain.ValueObjects;

public sealed class NotificationDeliveryRoute : ScopedAggregateRoot<Guid>
{
    public const int ProviderMaxLength = NotificationDeliveryProvider.MaxLength;
    public const int ActorIdMaxLength = 256;

    private NotificationDeliveryRoute() { }
    private NotificationDeliveryRoute(Guid id, string scopeId) : base(id, scopeId) { }

    public NotificationTagKey DeliveryTag { get; private set; } = null!;
    public NotificationDeliveryProvider Provider { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public int Version { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    public static Result<NotificationDeliveryRoute> Create(
        Guid id,
        string scopeId,
        string deliveryTag,
        string provider,
        string actorId,
        DateTimeOffset nowUtc)
    {
        Result<NotificationTagKey> tag = NotificationTagKey.Create(deliveryTag);
        if (id == Guid.Empty ||
            !ScopeIds.TryNormalize(scopeId, out string? normalizedScopeId) ||
            tag.IsFailure ||
            !tag.Value.IsDelivery ||
            !TryNormalizeProvider(provider, out NotificationDeliveryProvider? normalizedProvider) ||
            !TryNormalizeActor(actorId, out string? normalizedActor) ||
            nowUtc == default)
        {
            return Result.Failure<NotificationDeliveryRoute>(NotificationsDomainErrors.DeliveryRouteInvalid);
        }

        return Result.Success(new NotificationDeliveryRoute(id, normalizedScopeId!)
        {
            DeliveryTag = tag.Value,
            Provider = normalizedProvider!,
            IsActive = true,
            Version = 1,
            UpdatedAtUtc = nowUtc,
            UpdatedBy = normalizedActor!
        });
    }

    public Result Update(string provider, bool active, string actorId, DateTimeOffset nowUtc)
    {
        if (!TryNormalizeProvider(provider, out NotificationDeliveryProvider? normalizedProvider) ||
            !TryNormalizeActor(actorId, out string? normalizedActor) ||
            nowUtc == default)
        {
            return Result.Failure(NotificationsDomainErrors.DeliveryRouteInvalid);
        }

        this.Provider = normalizedProvider!;
        this.IsActive = active;
        this.UpdatedBy = normalizedActor!;
        this.UpdatedAtUtc = nowUtc;
        this.Version++;
        return Result.Success();
    }

    private static bool TryNormalizeProvider(string value, out NotificationDeliveryProvider? normalized)
    {
        Result<NotificationDeliveryProvider> provider = NotificationDeliveryProvider.Create(value);
        normalized = provider.IsSuccess ? provider.Value : null;
        return provider.IsSuccess;
    }

    private static bool TryNormalizeActor(string value, out string? normalized)
    {
        normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is not null && normalized.Length <= ActorIdMaxLength && !normalized.Any(char.IsControl);
    }
}
