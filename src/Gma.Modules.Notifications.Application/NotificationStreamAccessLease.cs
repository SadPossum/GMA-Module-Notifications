namespace Gma.Modules.Notifications.Application;

using System.Globalization;
using System.Security.Claims;

public sealed class NotificationStreamAccessLease
{
    private const string ExpiresAtClaim = "exp";
    private static readonly TimeSpan MinimumWaitInterval = TimeSpan.FromMilliseconds(1);

    private readonly TimeProvider timeProvider;
    private readonly TimeSpan authorizationRevalidationInterval;
    private readonly DateTimeOffset expiresAtUtc;
    private readonly NotificationStreamAccessOutcome expiryOutcome;
    private DateTimeOffset revalidateAtUtc;

    private NotificationStreamAccessLease(
        TimeProvider timeProvider,
        TimeSpan authorizationRevalidationInterval,
        DateTimeOffset revalidateAtUtc,
        DateTimeOffset expiresAtUtc,
        NotificationStreamAccessOutcome expiryOutcome)
    {
        this.timeProvider = timeProvider;
        this.authorizationRevalidationInterval = authorizationRevalidationInterval;
        this.revalidateAtUtc = revalidateAtUtc;
        this.expiresAtUtc = expiresAtUtc;
        this.expiryOutcome = expiryOutcome;
    }

    public static NotificationStreamAccessLease Create(
        NotificationStreamOptions options,
        ClaimsPrincipal principal,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            options.AuthorizationRevalidationInterval,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            options.MaximumConnectionLifetime,
            TimeSpan.Zero);
        if (options.MaximumConnectionLifetime < options.AuthorizationRevalidationInterval)
        {
            throw new ArgumentException(
                "The maximum connection lifetime cannot be shorter than the authorization revalidation interval.",
                nameof(options));
        }

        DateTimeOffset nowUtc = timeProvider.GetUtcNow();
        DateTimeOffset configuredExpiryUtc = nowUtc.Add(options.MaximumConnectionLifetime);
        DateTimeOffset expiresAtUtc = configuredExpiryUtc;
        NotificationStreamAccessOutcome expiryOutcome = NotificationStreamAccessOutcome.ConnectionLifetimeExpired;

        if (TryResolveAuthenticationExpiry(principal, out DateTimeOffset authenticationExpiryUtc) &&
            authenticationExpiryUtc <= configuredExpiryUtc)
        {
            expiresAtUtc = authenticationExpiryUtc;
            expiryOutcome = NotificationStreamAccessOutcome.AuthenticationExpired;
        }

        return new NotificationStreamAccessLease(
            timeProvider,
            options.AuthorizationRevalidationInterval,
            nowUtc.Add(options.AuthorizationRevalidationInterval),
            expiresAtUtc,
            expiryOutcome);
    }

    public async Task<NotificationStreamAccessOutcome> EvaluateAsync(
        Func<CancellationToken, Task<bool>> authorize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorize);

        DateTimeOffset nowUtc = this.timeProvider.GetUtcNow();
        if (nowUtc >= this.expiresAtUtc)
        {
            return this.expiryOutcome;
        }

        if (nowUtc < this.revalidateAtUtc)
        {
            return NotificationStreamAccessOutcome.Active;
        }

        if (!await authorize(cancellationToken).ConfigureAwait(false))
        {
            return NotificationStreamAccessOutcome.AuthorizationDenied;
        }

        nowUtc = this.timeProvider.GetUtcNow();
        if (nowUtc >= this.expiresAtUtc)
        {
            return this.expiryOutcome;
        }

        this.revalidateAtUtc = nowUtc.Add(this.authorizationRevalidationInterval);
        return NotificationStreamAccessOutcome.Active;
    }

    public TimeSpan LimitWaitInterval(TimeSpan requestedInterval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(requestedInterval, TimeSpan.Zero);

        DateTimeOffset nowUtc = this.timeProvider.GetUtcNow();
        DateTimeOffset nextAccessCheckUtc = this.revalidateAtUtc <= this.expiresAtUtc
            ? this.revalidateAtUtc
            : this.expiresAtUtc;
        TimeSpan untilAccessCheck = nextAccessCheckUtc - nowUtc;
        if (untilAccessCheck <= TimeSpan.Zero)
        {
            return MinimumWaitInterval;
        }

        return untilAccessCheck < requestedInterval ? untilAccessCheck : requestedInterval;
    }

    private static bool TryResolveAuthenticationExpiry(
        ClaimsPrincipal principal,
        out DateTimeOffset expiresAtUtc)
    {
        expiresAtUtc = default;
        string? value = principal.FindFirst(ExpiresAtClaim)?.Value;
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long unixTimeSeconds))
        {
            return false;
        }

        try
        {
            expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(unixTimeSeconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}

public enum NotificationStreamAccessOutcome
{
    Active = 0,
    AuthorizationDenied = 1,
    AuthenticationExpired = 2,
    ConnectionLifetimeExpired = 3
}
