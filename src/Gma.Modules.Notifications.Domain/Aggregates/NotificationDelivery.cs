namespace Gma.Modules.Notifications.Domain.Aggregates;

using Gma.Framework.Domain.Models;
using Gma.Framework.Naming;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Domain.Errors;
using Gma.Modules.Notifications.Domain.ValueObjects;

public sealed class NotificationDelivery : ScopedAggregateRoot<Guid>
{
    public const int ProviderMaxLength = NotificationDeliveryProvider.MaxLength;
    public const int WorkerIdMaxLength = 256;
    public const int CodeMaxLength = 128;
    public const int ProviderMessageIdMaxLength = 512;
    public const int DefaultMaxAttempts = 8;

    private NotificationDelivery() { }
    private NotificationDelivery(Guid id, string scopeId) : base(id, scopeId) { }

    public Guid NotificationId { get; private set; }
    public NotificationTagKey DeliveryTag { get; private set; } = null!;
    public NotificationDeliveryProvider Provider { get; private set; } = null!;
    public NotificationDeliveryStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? NextAttemptAtUtc { get; private set; }
    public string? LockedBy { get; private set; }
    public DateTimeOffset? LockedUntilUtc { get; private set; }
    public DateTimeOffset? DeliveredAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string? LastCode { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public Guid ConcurrencyStamp { get; private set; }

    public static Result<NotificationDelivery> CreatePending(
        Guid id,
        string scopeId,
        Guid notificationId,
        string deliveryTag,
        string provider,
        DateTimeOffset nowUtc,
        int maxAttempts = DefaultMaxAttempts) =>
        Create(id, scopeId, notificationId, deliveryTag, provider, NotificationDeliveryStatus.Pending, code: null, nowUtc, maxAttempts);

    public static Result<NotificationDelivery> CreateTerminal(
        Guid id,
        string scopeId,
        Guid notificationId,
        string deliveryTag,
        string provider,
        NotificationDeliveryStatus status,
        string code,
        DateTimeOffset nowUtc) =>
        status is NotificationDeliveryStatus.Suppressed or NotificationDeliveryStatus.Unroutable
            ? Create(id, scopeId, notificationId, deliveryTag, provider, status, code, nowUtc, DefaultMaxAttempts)
            : Result.Failure<NotificationDelivery>(NotificationsDomainErrors.DeliveryStatusInvalid);

    public static Result<NotificationDelivery> CreateDelivered(
        Guid id,
        string scopeId,
        Guid notificationId,
        string deliveryTag,
        string provider,
        DateTimeOffset nowUtc)
    {
        Result<NotificationDelivery> result = Create(
            id,
            scopeId,
            notificationId,
            deliveryTag,
            provider,
            NotificationDeliveryStatus.Delivered,
            code: null,
            nowUtc,
            DefaultMaxAttempts);
        if (result.IsFailure)
        {
            return result;
        }

        result.Value.DeliveredAtUtc = nowUtc;
        result.Value.CompletedAtUtc = nowUtc;
        return result;
    }

    public bool CanClaim(DateTimeOffset nowUtc) =>
        nowUtc != default &&
        this.Attempts < this.MaxAttempts &&
        (this.Status is NotificationDeliveryStatus.Pending or NotificationDeliveryStatus.RetryScheduled ||
         (this.Status == NotificationDeliveryStatus.Processing && this.LockedUntilUtc <= nowUtc)) &&
        (this.NextAttemptAtUtc is null || this.NextAttemptAtUtc <= nowUtc) &&
        (this.LockedUntilUtc is null || this.LockedUntilUtc <= nowUtc);

    public Result Claim(string workerId, DateTimeOffset nowUtc, TimeSpan leaseDuration)
    {
        if (!this.CanClaim(nowUtc) ||
            !TryNormalizeWorker(workerId, out string? normalizedWorker) ||
            leaseDuration <= TimeSpan.Zero)
        {
            return Result.Failure(NotificationsDomainErrors.DeliveryCannotBeClaimed);
        }

        this.Status = NotificationDeliveryStatus.Processing;
        this.Attempts++;
        this.LockedBy = normalizedWorker;
        this.LockedUntilUtc = nowUtc.Add(leaseDuration);
        this.NextAttemptAtUtc = null;
        this.LastCode = null;
        this.ConcurrencyStamp = Guid.CreateVersion7();
        return Result.Success();
    }

    public Result MarkDelivered(string workerId, DateTimeOffset nowUtc, string? providerMessageId)
    {
        Result ownership = this.EnsureOwned(workerId, nowUtc);
        if (ownership.IsFailure || !TryNormalizeOptional(providerMessageId, ProviderMessageIdMaxLength, out string? normalizedMessageId))
        {
            return ownership.IsFailure ? ownership : Result.Failure(NotificationsDomainErrors.DeliveryResultInvalid);
        }

        this.Status = NotificationDeliveryStatus.Delivered;
        this.DeliveredAtUtc = nowUtc;
        this.CompletedAtUtc = nowUtc;
        this.ProviderMessageId = normalizedMessageId;
        this.LastCode = null;
        this.ReleaseLease();
        return Result.Success();
    }

    public Result MarkRejected(string workerId, DateTimeOffset nowUtc, string code)
    {
        Result ownership = this.EnsureOwned(workerId, nowUtc);
        if (ownership.IsFailure || !TryNormalizeCode(code, out string? normalizedCode))
        {
            return ownership.IsFailure ? ownership : Result.Failure(NotificationsDomainErrors.DeliveryResultInvalid);
        }

        this.Status = NotificationDeliveryStatus.Rejected;
        this.CompletedAtUtc = nowUtc;
        this.LastCode = normalizedCode;
        this.ReleaseLease();
        return Result.Success();
    }

    public Result MarkRetry(string workerId, DateTimeOffset nowUtc, string code, DateTimeOffset retryAtUtc)
    {
        Result ownership = this.EnsureOwned(workerId, nowUtc);
        if (ownership.IsFailure ||
            !TryNormalizeCode(code, out string? normalizedCode) ||
            retryAtUtc <= nowUtc)
        {
            return ownership.IsFailure ? ownership : Result.Failure(NotificationsDomainErrors.DeliveryResultInvalid);
        }

        this.LastCode = normalizedCode;
        if (this.Attempts >= this.MaxAttempts)
        {
            this.Status = NotificationDeliveryStatus.Exhausted;
            this.CompletedAtUtc = nowUtc;
            this.NextAttemptAtUtc = null;
        }
        else
        {
            this.Status = NotificationDeliveryStatus.RetryScheduled;
            this.NextAttemptAtUtc = retryAtUtc;
        }

        this.ReleaseLease(clearNextAttempt: false);
        return Result.Success();
    }

    public Result RetryManually(DateTimeOffset nowUtc, string? provider = null)
    {
        if (nowUtc == default || this.Status is not (
            NotificationDeliveryStatus.Rejected or
            NotificationDeliveryStatus.Exhausted or
            NotificationDeliveryStatus.Unroutable))
        {
            return Result.Failure(NotificationsDomainErrors.DeliveryCannotBeRetried);
        }

        if (provider is not null)
        {
            if (!TryNormalizeProvider(provider, out NotificationDeliveryProvider? normalizedProvider))
            {
                return Result.Failure(NotificationsDomainErrors.DeliveryInvalid);
            }

            this.Provider = normalizedProvider!;
        }

        this.Status = NotificationDeliveryStatus.Pending;
        this.Attempts = 0;
        this.CompletedAtUtc = null;
        this.DeliveredAtUtc = null;
        this.NextAttemptAtUtc = nowUtc;
        this.LastCode = null;
        this.ProviderMessageId = null;
        this.ReleaseLease(clearNextAttempt: false);
        return Result.Success();
    }

    private static Result<NotificationDelivery> Create(
        Guid id,
        string scopeId,
        Guid notificationId,
        string deliveryTag,
        string provider,
        NotificationDeliveryStatus status,
        string? code,
        DateTimeOffset nowUtc,
        int maxAttempts)
    {
        Result<NotificationTagKey> tag = NotificationTagKey.Create(deliveryTag);
        if (id == Guid.Empty ||
            notificationId == Guid.Empty ||
            !ScopeIds.TryNormalize(scopeId, out string? normalizedScopeId) ||
            tag.IsFailure ||
            !tag.Value.IsDelivery ||
            !TryNormalizeProvider(provider, out NotificationDeliveryProvider? normalizedProvider) ||
            nowUtc == default ||
            maxAttempts < 1 ||
            (code is not null && !TryNormalizeCode(code, out _)))
        {
            return Result.Failure<NotificationDelivery>(NotificationsDomainErrors.DeliveryInvalid);
        }

        return Result.Success(new NotificationDelivery(id, normalizedScopeId!)
        {
            NotificationId = notificationId,
            DeliveryTag = tag.Value,
            Provider = normalizedProvider!,
            Status = status,
            MaxAttempts = maxAttempts,
            CreatedAtUtc = nowUtc,
            CompletedAtUtc = status is NotificationDeliveryStatus.Suppressed or NotificationDeliveryStatus.Unroutable
                ? nowUtc
                : null,
            LastCode = code,
            ConcurrencyStamp = Guid.CreateVersion7()
        });
    }

    private Result EnsureOwned(string workerId, DateTimeOffset nowUtc)
    {
        if (!TryNormalizeWorker(workerId, out string? normalizedWorker) ||
            nowUtc == default ||
            this.Status != NotificationDeliveryStatus.Processing ||
            this.LockedUntilUtc <= nowUtc ||
            !string.Equals(this.LockedBy, normalizedWorker, StringComparison.Ordinal))
        {
            return Result.Failure(NotificationsDomainErrors.DeliveryLeaseLost);
        }

        return Result.Success();
    }

    private void ReleaseLease(bool clearNextAttempt = true)
    {
        this.LockedBy = null;
        this.LockedUntilUtc = null;
        if (clearNextAttempt)
        {
            this.NextAttemptAtUtc = null;
        }

        this.ConcurrencyStamp = Guid.CreateVersion7();
    }

    private static bool TryNormalizeProvider(string value, out NotificationDeliveryProvider? normalized)
    {
        Result<NotificationDeliveryProvider> provider = NotificationDeliveryProvider.Create(value);
        normalized = provider.IsSuccess ? provider.Value : null;
        return provider.IsSuccess;
    }

    private static bool TryNormalizeWorker(string value, out string? normalized)
    {
        normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return normalized is not null &&
               normalized.Length <= WorkerIdMaxLength &&
               !normalized.Any(character => char.IsWhiteSpace(character) || char.IsControl(character));
    }

    private static bool TryNormalizeCode(string value, out string? normalized)
    {
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
