namespace Gma.Modules.Notifications.Domain.Entities;

using Gma.Framework.Domain.Models;
using Gma.Framework.Naming;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Domain.Errors;
using Gma.Modules.Notifications.Domain.ValueObjects;

public sealed class NotificationDeliveryAttempt : ScopedEntity<Guid>
{
    public const int ProviderMaxLength = NotificationDeliveryProvider.MaxLength;
    public const int CodeMaxLength = 128;
    public const int ProviderMessageIdMaxLength = 512;

    private NotificationDeliveryAttempt() { }
    private NotificationDeliveryAttempt(Guid id, string scopeId) : base(id, scopeId) { }

    public Guid DeliveryId { get; private set; }
    public int AttemptNumber { get; private set; }
    public NotificationDeliveryProvider Provider { get; private set; } = null!;
    public NotificationDeliveryAttemptOutcome Outcome { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset CompletedAtUtc { get; private set; }
    public string? Code { get; private set; }
    public string? ProviderMessageId { get; private set; }

    public static Result<NotificationDeliveryAttempt> Create(
        Guid id,
        string scopeId,
        Guid deliveryId,
        int attemptNumber,
        string provider,
        NotificationDeliveryAttemptOutcome outcome,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        string? code,
        string? providerMessageId)
    {
        if (id == Guid.Empty ||
            deliveryId == Guid.Empty ||
            attemptNumber < 1 ||
            !ScopeIds.TryNormalize(scopeId, out string? normalizedScopeId) ||
            !TryNormalizeProvider(provider, out NotificationDeliveryProvider? normalizedProvider) ||
            !IsValidOutcome(outcome) ||
            startedAtUtc == default ||
            completedAtUtc == default ||
            completedAtUtc < startedAtUtc ||
            !TryNormalizeOptionalCode(code, out string? normalizedCode) ||
            !TryNormalizeOptional(providerMessageId, ProviderMessageIdMaxLength, out string? normalizedMessageId))
        {
            return Result.Failure<NotificationDeliveryAttempt>(NotificationsDomainErrors.DeliveryAttemptInvalid);
        }

        return Result.Success(new NotificationDeliveryAttempt(id, normalizedScopeId!)
        {
            DeliveryId = deliveryId,
            AttemptNumber = attemptNumber,
            Provider = normalizedProvider!,
            Outcome = outcome,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            Code = normalizedCode,
            ProviderMessageId = normalizedMessageId
        });
    }

    private static bool IsValidOutcome(NotificationDeliveryAttemptOutcome outcome) =>
        outcome is NotificationDeliveryAttemptOutcome.Delivered or
            NotificationDeliveryAttemptOutcome.Retry or
            NotificationDeliveryAttemptOutcome.Rejected or
            NotificationDeliveryAttemptOutcome.Exception;

    private static bool TryNormalizeProvider(string value, out NotificationDeliveryProvider? normalized)
    {
        Result<NotificationDeliveryProvider> provider = NotificationDeliveryProvider.Create(value);
        normalized = provider.IsSuccess ? provider.Value : null;
        return provider.IsSuccess;
    }

    private static bool TryNormalizeOptionalCode(string? value, out string? normalized)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            normalized = null;
            return true;
        }

        try
        {
            normalized = SharedNameSegments.NormalizeKebabSegment(value, "delivery result code", nameof(value));
            return normalized.Length <= CodeMaxLength;
        }
        catch (ArgumentException)
        {
            normalized = null;
            return false;
        }
    }

    private static bool TryNormalizeOptional(string? value, int maxLength, out string? normalized)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            normalized = null;
            return true;
        }

        normalized = value.Trim();
        return normalized.Length <= maxLength && !normalized.Any(char.IsControl);
    }
}
