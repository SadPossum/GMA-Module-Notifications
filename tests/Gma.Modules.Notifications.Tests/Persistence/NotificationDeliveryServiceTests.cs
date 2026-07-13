namespace Gma.Modules.Notifications.Tests;

using Gma.Framework.Notifications;
using Gma.Framework.Runtime;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Framework.Scoping;
using Gma.Modules.Notifications.Application;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using DomainAttemptOutcome = Domain.ValueObjects.NotificationDeliveryAttemptOutcome;
using DomainDeliveryStatus = Domain.ValueObjects.NotificationDeliveryStatus;
using DomainSeverity = Domain.ValueObjects.NotificationSeverity;

[Trait("Category", "Unit")]
public sealed class NotificationDeliveryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Durable_worker_records_successful_attempt_and_provider_receipt()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        string databaseName = $"delivery-worker-{Guid.NewGuid():N}";
        ServiceCollection services = new();
        services.AddMetrics();
        services.AddSingleton<IScopeContext>(new TestScopeContext("tenant-a"));
        services.AddDbContext<NotificationsDbContext>(options =>
            options.UseInMemoryDatabase(databaseName, databaseRoot));
        await using ServiceProvider serviceProvider = services.BuildServiceProvider();
        Guid notificationId = Guid.CreateVersion7();
        Guid deliveryId = Guid.CreateVersion7();
        const string workerId = "notification-test-worker";

        await using (AsyncServiceScope seedScope = serviceProvider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = seedScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            UserNotification notification = UserNotification.Create(
                notificationId,
                "tenant-a",
                "user-a",
                "auth",
                "auth.account-accessed",
                1,
                "New sign-in",
                "A new sign-in was detected.",
                DomainSeverity.Warning,
                Now,
                Now,
                "{}").Value;
            NotificationDelivery delivery = NotificationDelivery.CreatePending(
                deliveryId,
                "tenant-a",
                notificationId,
                NotificationTags.Email,
                TestDurableSink.Provider,
                Now).Value;
            Assert.True(delivery.Claim(workerId, Now, TimeSpan.FromMinutes(1)).IsSuccess);
            dbContext.UserNotifications.Add(notification);
            dbContext.NotificationDeliveries.Add(delivery);
            await dbContext.SaveChangesAsync();
        }

        NotificationDeliveryOptions deliveryOptions = new()
        {
            WorkerId = workerId,
            LeaseSeconds = 60
        };
        NotificationDeliveryMetrics metrics = new(
            serviceProvider.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(),
            Options.Create(new ApplicationIdentityOptions { Namespace = "notification-tests" }));
        NotificationDeliveryService worker = new(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new NotificationDeliveryAdapterCatalog([new TestDurableSink()]),
            new FixedClock(Now.AddSeconds(1)),
            new TestIdGenerator(),
            Options.Create(deliveryOptions),
            metrics,
            NullLogger<NotificationDeliveryService>.Instance);

        await worker.DeliverAsync(deliveryId, CancellationToken.None);

        await using AsyncServiceScope assertionScope = serviceProvider.CreateAsyncScope();
        NotificationsDbContext assertionDb = assertionScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        NotificationDelivery stored = await assertionDb.NotificationDeliveries.SingleAsync();
        NotificationDeliveryAttempt attempt = await assertionDb.NotificationDeliveryAttempts.SingleAsync();
        Assert.Equal(DomainDeliveryStatus.Delivered, stored.Status);
        Assert.Equal("provider-message", stored.ProviderMessageId);
        Assert.Equal(1, stored.Attempts);
        Assert.Equal(DomainAttemptOutcome.Delivered, attempt.Outcome);
        Assert.Equal(TestDurableSink.Provider, attempt.Provider.Value);
        Assert.Null(attempt.Code);
    }

    [Fact]
    public async Task Durable_worker_exhausts_retry_and_persists_only_safe_exception_code()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        string databaseName = $"delivery-worker-failure-{Guid.NewGuid():N}";
        ServiceCollection services = new();
        services.AddMetrics();
        services.AddSingleton<IScopeContext>(new TestScopeContext("tenant-a"));
        services.AddDbContext<NotificationsDbContext>(options =>
            options.UseInMemoryDatabase(databaseName, databaseRoot));
        await using ServiceProvider serviceProvider = services.BuildServiceProvider();
        Guid notificationId = Guid.CreateVersion7();
        Guid deliveryId = Guid.CreateVersion7();
        const string workerId = "notification-test-worker";

        await using (AsyncServiceScope seedScope = serviceProvider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = seedScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            dbContext.UserNotifications.Add(UserNotification.Create(
                notificationId,
                "tenant-a",
                "user-a",
                "auth",
                "auth.account-accessed",
                1,
                "New sign-in",
                null,
                DomainSeverity.Warning,
                Now,
                Now,
                "{}").Value);
            NotificationDelivery delivery = NotificationDelivery.CreatePending(
                deliveryId,
                "tenant-a",
                notificationId,
                NotificationTags.Email,
                ThrowingDurableSink.Provider,
                Now,
                maxAttempts: 1).Value;
            Assert.True(delivery.Claim(workerId, Now, TimeSpan.FromMinutes(1)).IsSuccess);
            dbContext.NotificationDeliveries.Add(delivery);
            await dbContext.SaveChangesAsync();
        }

        NotificationDeliveryMetrics metrics = new(
            serviceProvider.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(),
            Options.Create(new ApplicationIdentityOptions { Namespace = "notification-tests" }));
        NotificationDeliveryService worker = new(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new NotificationDeliveryAdapterCatalog([new ThrowingDurableSink()]),
            new FixedClock(Now.AddSeconds(1)),
            new TestIdGenerator(),
            Options.Create(new NotificationDeliveryOptions { WorkerId = workerId, LeaseSeconds = 60 }),
            metrics,
            NullLogger<NotificationDeliveryService>.Instance);

        await worker.DeliverAsync(deliveryId, CancellationToken.None);

        await using AsyncServiceScope assertionScope = serviceProvider.CreateAsyncScope();
        NotificationsDbContext assertionDb = assertionScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        NotificationDelivery stored = await assertionDb.NotificationDeliveries.SingleAsync();
        NotificationDeliveryAttempt attempt = await assertionDb.NotificationDeliveryAttempts.SingleAsync();
        Assert.Equal(DomainDeliveryStatus.Exhausted, stored.Status);
        Assert.Equal("adapter-exception", stored.LastCode);
        Assert.Equal(DomainAttemptOutcome.Exception, attempt.Outcome);
        Assert.Equal("adapter-exception", attempt.Code);
        Assert.DoesNotContain("secret-address", attempt.Code, StringComparison.Ordinal);
    }

    private sealed class TestDurableSink : IUserNotificationSink
    {
        public const string Provider = "test-email";

        public string ProviderName => Provider;
        public IReadOnlyCollection<string> DeliveryTags => [NotificationTags.Email];
        public NotificationSinkDeliveryMode DeliveryModes => NotificationSinkDeliveryMode.Durable;

        public ValueTask<NotificationSinkDeliveryResult> DeliverAsync(
            NotificationSinkDeliveryRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(NotificationSinkDeliveryResult.Delivered("provider-message"));
    }

    private sealed class ThrowingDurableSink : IUserNotificationSink
    {
        public const string Provider = "throwing-email";

        public string ProviderName => Provider;
        public IReadOnlyCollection<string> DeliveryTags => [NotificationTags.Email];
        public NotificationSinkDeliveryMode DeliveryModes => NotificationSinkDeliveryMode.Durable;

        public ValueTask<NotificationSinkDeliveryResult> DeliverAsync(
            NotificationSinkDeliveryRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("secret-address@example.com");
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class TestIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.CreateVersion7();
    }

    private sealed class TestScopeContext(string scopeId) : IScopeContext
    {
        public bool IsEnabled => true;
        public bool HasScope => !string.IsNullOrWhiteSpace(scopeId);
        public string? ScopeId => scopeId;
        public string RequireScopeId() => scopeId;
    }
}
