namespace Gma.Modules.Notifications.Tests;

using System.Security.Claims;
using Gma.Modules.Notifications.Application;
using Xunit;

[Trait("Category", "Unit")]
public sealed class NotificationStreamAccessLeaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Lease_revalidates_only_after_the_configured_interval()
    {
        MutableTimeProvider timeProvider = new(Now);
        NotificationStreamAccessLease lease = CreateLease(timeProvider);
        int authorizationCalls = 0;

        Assert.Equal(
            NotificationStreamAccessOutcome.Active,
            await lease.EvaluateAsync(Authorize, CancellationToken.None));
        timeProvider.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(
            NotificationStreamAccessOutcome.Active,
            await lease.EvaluateAsync(Authorize, CancellationToken.None));
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(
            NotificationStreamAccessOutcome.Active,
            await lease.EvaluateAsync(Authorize, CancellationToken.None));
        Assert.Equal(
            NotificationStreamAccessOutcome.Active,
            await lease.EvaluateAsync(Authorize, CancellationToken.None));

        Assert.Equal(1, authorizationCalls);

        Task<bool> Authorize(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            authorizationCalls++;
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task Lease_closes_when_periodic_authorization_is_denied()
    {
        MutableTimeProvider timeProvider = new(Now);
        NotificationStreamAccessLease lease = CreateLease(timeProvider);
        timeProvider.Advance(TimeSpan.FromMinutes(1));

        NotificationStreamAccessOutcome outcome = await lease.EvaluateAsync(
            _ => Task.FromResult(false),
            CancellationToken.None);

        Assert.Equal(NotificationStreamAccessOutcome.AuthorizationDenied, outcome);
    }

    [Fact]
    public async Task Authentication_expiry_caps_the_configured_connection_lifetime()
    {
        MutableTimeProvider timeProvider = new(Now);
        ClaimsPrincipal principal = PrincipalWithExpiry(Now.AddMinutes(2));
        NotificationStreamAccessLease lease = CreateLease(timeProvider, principal);
        timeProvider.Advance(TimeSpan.FromMinutes(2));

        NotificationStreamAccessOutcome outcome = await lease.EvaluateAsync(
            _ => Task.FromResult(true),
            CancellationToken.None);

        Assert.Equal(NotificationStreamAccessOutcome.AuthenticationExpired, outcome);
    }

    [Fact]
    public async Task Malformed_authentication_expiry_falls_back_to_the_bounded_connection_lifetime()
    {
        MutableTimeProvider timeProvider = new(Now);
        ClaimsPrincipal principal = new(new ClaimsIdentity([new Claim("exp", "invalid")], "test"));
        NotificationStreamAccessLease lease = CreateLease(
            timeProvider,
            principal,
            maximumConnectionLifetime: TimeSpan.FromMinutes(5));
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        NotificationStreamAccessOutcome outcome = await lease.EvaluateAsync(
            _ => Task.FromResult(true),
            CancellationToken.None);

        Assert.Equal(NotificationStreamAccessOutcome.ConnectionLifetimeExpired, outcome);
    }

    [Fact]
    public void Idle_wait_is_shortened_to_the_next_access_check()
    {
        MutableTimeProvider timeProvider = new(Now);
        NotificationStreamAccessLease lease = CreateLease(timeProvider);

        TimeSpan initial = lease.LimitWaitInterval(TimeSpan.FromMinutes(5));
        timeProvider.Advance(TimeSpan.FromSeconds(59));
        TimeSpan remaining = lease.LimitWaitInterval(TimeSpan.FromMinutes(5));
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        TimeSpan due = lease.LimitWaitInterval(TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromMinutes(1), initial);
        Assert.Equal(TimeSpan.FromSeconds(1), remaining);
        Assert.Equal(TimeSpan.FromMilliseconds(1), due);
    }

    [Fact]
    public void Lease_rejects_non_positive_or_inconsistent_intervals()
    {
        MutableTimeProvider timeProvider = new(Now);
        ClaimsPrincipal principal = new(new ClaimsIdentity(authenticationType: "test"));

        Assert.Throws<ArgumentOutOfRangeException>(() => NotificationStreamAccessLease.Create(
            new NotificationStreamOptions
            {
                AuthorizationRevalidationInterval = TimeSpan.Zero,
                MaximumConnectionLifetime = TimeSpan.FromMinutes(1)
            },
            principal,
            timeProvider));
        Assert.Throws<ArgumentOutOfRangeException>(() => NotificationStreamAccessLease.Create(
            new NotificationStreamOptions
            {
                AuthorizationRevalidationInterval = TimeSpan.FromSeconds(1),
                MaximumConnectionLifetime = TimeSpan.Zero
            },
            principal,
            timeProvider));
        Assert.Throws<ArgumentException>(() => NotificationStreamAccessLease.Create(
            new NotificationStreamOptions
            {
                AuthorizationRevalidationInterval = TimeSpan.FromMinutes(2),
                MaximumConnectionLifetime = TimeSpan.FromMinutes(1)
            },
            principal,
            timeProvider));
    }

    private static NotificationStreamAccessLease CreateLease(
        MutableTimeProvider timeProvider,
        ClaimsPrincipal? principal = null,
        TimeSpan? maximumConnectionLifetime = null) =>
        NotificationStreamAccessLease.Create(
            new NotificationStreamOptions
            {
                AuthorizationRevalidationInterval = TimeSpan.FromMinutes(1),
                MaximumConnectionLifetime = maximumConnectionLifetime ?? TimeSpan.FromMinutes(15)
            },
            principal ?? new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "test")),
            timeProvider);

    private static ClaimsPrincipal PrincipalWithExpiry(DateTimeOffset expiresAtUtc) =>
        new(new ClaimsIdentity(
            [new Claim("exp", expiresAtUtc.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture))],
            "test"));

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => this.utcNow;

        public void Advance(TimeSpan duration) => this.utcNow = this.utcNow.Add(duration);
    }
}
