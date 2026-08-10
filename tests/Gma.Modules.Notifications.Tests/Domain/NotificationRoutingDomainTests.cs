namespace Gma.Modules.Notifications.Tests;

using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Xunit;

[Trait("Category", "Unit")]
public sealed class NotificationRoutingDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Tag_definition_enforces_namespace_kind_and_tracks_operator_changes()
    {
        var invalid = NotificationTagDefinition.Create(
            Guid.CreateVersion7(),
            "tenant-a",
            "domain:security",
            NotificationTagKind.Delivery,
            "Security",
            "Security notifications.",
            NotificationTagOrigin.Operator,
            "notifications",
            "admin-a",
            Now);
        var created = NotificationTagDefinition.Create(
            Guid.CreateVersion7(),
            "tenant-a",
            "domain:security",
            NotificationTagKind.Domain,
            "Security",
            "Security notifications.",
            NotificationTagOrigin.Operator,
            "notifications",
            "admin-a",
            Now);

        Assert.True(invalid.IsFailure);
        Assert.True(created.IsSuccess);
        Assert.True(created.Value.SetActive(false, "admin-b", Now.AddMinutes(1)).IsSuccess);
        Assert.Equal(2, created.Value.Version);
        Assert.False(created.Value.IsActive);
        Assert.Equal("admin-b", created.Value.UpdatedBy);
    }

    [Fact]
    public void Delivery_retries_with_lease_ownership_and_exhausts_at_bound()
    {
        NotificationDelivery delivery = NotificationDelivery.CreatePending(
            Guid.CreateVersion7(),
            "tenant-a",
            Guid.CreateVersion7(),
            "delivery:email",
            "email",
            Now,
            maxAttempts: 2).Value;

        Assert.True(delivery.Claim("worker:1", Now, TimeSpan.FromMinutes(1)).IsSuccess);
        Assert.True(delivery.MarkRetry("worker:1", Now.AddSeconds(5), "rate-limited", Now.AddMinutes(1)).IsSuccess);
        Assert.Equal(NotificationDeliveryStatus.RetryScheduled, delivery.Status);
        Assert.True(delivery.Claim("worker:1", Now.AddMinutes(1), TimeSpan.FromMinutes(1)).IsSuccess);
        Assert.True(delivery.MarkRetry("worker:1", Now.AddMinutes(1).AddSeconds(5), "rate-limited", Now.AddMinutes(2)).IsSuccess);

        Assert.Equal(NotificationDeliveryStatus.Exhausted, delivery.Status);
        Assert.Equal(2, delivery.Attempts);
        Assert.NotNull(delivery.CompletedAtUtc);
        Assert.False(delivery.CanClaim(Now.AddHours(1)));
    }

    [Fact]
    public void Unroutable_delivery_can_be_rebound_and_retried_manually()
    {
        NotificationDelivery delivery = NotificationDelivery.CreateTerminal(
            Guid.CreateVersion7(),
            "tenant-a",
            Guid.CreateVersion7(),
            "delivery:email",
            "routing",
            NotificationDeliveryStatus.Unroutable,
            "adapter-unavailable",
            Now).Value;

        Assert.True(delivery.RetryManually(
            Now.AddMinutes(1),
            additionalAttempts: 3,
            "email-primary").IsSuccess);
        Assert.Equal(NotificationDeliveryStatus.Pending, delivery.Status);
        Assert.Equal("email-primary", delivery.Provider.Value);
        Assert.Equal(0, delivery.Attempts);
        Assert.Equal(3, delivery.MaxAttempts);
        Assert.Equal(Now.AddMinutes(1), delivery.NextAttemptAtUtc);
    }

    [Fact]
    public void Manual_retry_preserves_attempt_numbering_and_adds_a_fresh_budget()
    {
        NotificationDelivery delivery = NotificationDelivery.CreatePending(
            Guid.CreateVersion7(),
            "tenant-a",
            Guid.CreateVersion7(),
            "delivery:email",
            "email",
            Now,
            maxAttempts: 2).Value;
        Assert.True(delivery.Claim(
            "worker:1",
            Now,
            TimeSpan.FromMinutes(1)).IsSuccess);
        Assert.True(delivery.MarkRejected(
            "worker:1",
            Now.AddSeconds(1),
            "provider-rejected").IsSuccess);

        Assert.True(delivery.RetryManually(
            Now.AddMinutes(1),
            additionalAttempts: 2).IsSuccess);

        Assert.Equal(NotificationDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(1, delivery.Attempts);
        Assert.Equal(3, delivery.MaxAttempts);
        Assert.True(delivery.Claim(
            "worker:2",
            Now.AddMinutes(1),
            TimeSpan.FromMinutes(1)).IsSuccess);
        Assert.Equal(2, delivery.Attempts);
    }

    [Fact]
    public void Expired_lease_is_retryable_before_the_limit_and_exhausted_at_the_limit()
    {
        NotificationDelivery retryable = NotificationDelivery.CreatePending(
            Guid.CreateVersion7(),
            "tenant-a",
            Guid.CreateVersion7(),
            "delivery:email",
            "email",
            Now,
            maxAttempts: 2).Value;
        NotificationDelivery final = NotificationDelivery.CreatePending(
            Guid.CreateVersion7(),
            "tenant-a",
            Guid.CreateVersion7(),
            "delivery:email",
            "email",
            Now,
            maxAttempts: 1).Value;
        Assert.True(retryable.Claim(
            "worker:1",
            Now,
            TimeSpan.FromMinutes(1)).IsSuccess);
        Assert.True(final.Claim(
            "worker:1",
            Now,
            TimeSpan.FromMinutes(1)).IsSuccess);

        Assert.True(retryable.RecordExpiredLease(
            Now.AddMinutes(1),
            "worker-lease-expired").IsSuccess);
        Assert.True(final.RecordExpiredLease(
            Now.AddMinutes(1),
            "worker-lease-expired").IsSuccess);

        Assert.Equal(NotificationDeliveryStatus.RetryScheduled, retryable.Status);
        Assert.Equal(Now.AddMinutes(1), retryable.NextAttemptAtUtc);
        Assert.True(retryable.CanClaim(Now.AddMinutes(1)));
        Assert.Equal(NotificationDeliveryStatus.Exhausted, final.Status);
        Assert.Equal(Now.AddMinutes(1), final.CompletedAtUtc);
        Assert.False(final.CanClaim(Now.AddHours(1)));
    }

    [Fact]
    public void Delivery_cannot_be_completed_at_or_after_lease_expiry()
    {
        NotificationDelivery delivery = NotificationDelivery.CreatePending(
            Guid.CreateVersion7(),
            "tenant-a",
            Guid.CreateVersion7(),
            "delivery:email",
            "email",
            Now).Value;

        Assert.True(delivery.Claim("worker:1", Now, TimeSpan.FromMinutes(1)).IsSuccess);

        Assert.True(delivery.MarkDelivered("worker:1", Now.AddMinutes(1), providerMessageId: null).IsFailure);
        Assert.Equal(NotificationDeliveryStatus.Processing, delivery.Status);
        Assert.True(delivery.CanClaim(Now.AddMinutes(1)));
    }
}
