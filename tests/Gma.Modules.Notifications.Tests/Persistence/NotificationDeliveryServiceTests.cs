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
        services.AddScoped<NotificationMaintenanceDbContextFactory>();
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
        services.AddScoped<NotificationMaintenanceDbContextFactory>();
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

    [Fact]
    public async Task Durable_worker_clamps_adapter_retry_time_to_the_configured_maximum()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        string databaseName = $"delivery-worker-retry-bound-{Guid.NewGuid():N}";
        ServiceCollection services = new();
        services.AddMetrics();
        services.AddSingleton<IScopeContext>(new TestScopeContext("tenant-a"));
        services.AddDbContext<NotificationsDbContext>(options =>
            options.UseInMemoryDatabase(databaseName, databaseRoot));
        services.AddScoped<NotificationMaintenanceDbContextFactory>();
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
                FutureRetrySink.Provider,
                Now).Value;
            Assert.True(delivery.Claim(workerId, Now, TimeSpan.FromMinutes(1)).IsSuccess);
            dbContext.NotificationDeliveries.Add(delivery);
            await dbContext.SaveChangesAsync();
        }

        NotificationDeliveryOptions deliveryOptions = new()
        {
            WorkerId = workerId,
            LeaseSeconds = 60,
            RetryMaxMinutes = 30
        };
        NotificationDeliveryMetrics metrics = new(
            serviceProvider.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(),
            Options.Create(new ApplicationIdentityOptions { Namespace = "notification-tests" }));
        NotificationDeliveryService worker = new(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new NotificationDeliveryAdapterCatalog([new FutureRetrySink()]),
            new FixedClock(Now.AddSeconds(1)),
            new TestIdGenerator(),
            Options.Create(deliveryOptions),
            metrics,
            NullLogger<NotificationDeliveryService>.Instance);

        await worker.DeliverAsync(deliveryId, CancellationToken.None);

        await using AsyncServiceScope assertionScope = serviceProvider.CreateAsyncScope();
        NotificationDelivery stored = await assertionScope.ServiceProvider
            .GetRequiredService<NotificationsDbContext>()
            .NotificationDeliveries
            .SingleAsync();
        Assert.Equal(Now.AddSeconds(1).AddMinutes(30), stored.NextAttemptAtUtc);
    }

    [Fact]
    public async Task Durable_worker_processes_multiple_scopes_without_an_active_request_scope()
    {
        await using ServiceProvider serviceProvider = CreateDeliveryProvider(
            $"delivery-worker-multi-scope-{Guid.NewGuid():N}",
            new TestScopeContext(scopeId: null));
        Guid firstDeliveryId = Guid.CreateVersion7();
        Guid secondDeliveryId = Guid.CreateVersion7();

        await using (AsyncServiceScope seedScope =
                     serviceProvider.CreateAsyncScope())
        {
            NotificationMaintenanceDbContextFactory dbContextFactory =
                seedScope.ServiceProvider
                    .GetRequiredService<NotificationMaintenanceDbContextFactory>();
            await using NotificationsDbContext dbContext =
                dbContextFactory.CreateDbContext();
            UserNotification first = CreateNotification(
                "tenant-a",
                "user-a");
            UserNotification second = CreateNotification(
                "tenant-b",
                "user-b");
            dbContext.UserNotifications.AddRange(first, second);
            dbContext.NotificationDeliveries.AddRange(
                NotificationDelivery.CreatePending(
                    firstDeliveryId,
                    first.ScopeId,
                    first.Id,
                    NotificationTags.Email,
                    TestDurableSink.Provider,
                    Now).Value,
                NotificationDelivery.CreatePending(
                    secondDeliveryId,
                    second.ScopeId,
                    second.Id,
                    NotificationTags.Email,
                    TestDurableSink.Provider,
                    Now).Value);
            await dbContext.SaveChangesAsync();
        }

        NotificationDeliveryService worker = CreateWorker(
            serviceProvider,
            "notification-multi-scope-worker",
            Now.AddSeconds(1));

        Assert.Equal(
            2,
            await worker.ProcessAvailableBatchAsync(CancellationToken.None));

        await using AsyncServiceScope assertionScope =
            serviceProvider.CreateAsyncScope();
        NotificationMaintenanceDbContextFactory assertionFactory =
            assertionScope.ServiceProvider
                .GetRequiredService<NotificationMaintenanceDbContextFactory>();
        await using NotificationsDbContext assertionDb =
            assertionFactory.CreateDbContext();
        NotificationDelivery[] deliveries = await assertionDb
            .NotificationDeliveries
            .OrderBy(delivery => delivery.ScopeId)
            .ToArrayAsync();
        NotificationDeliveryAttempt[] attempts = await assertionDb
            .NotificationDeliveryAttempts
            .OrderBy(attempt => attempt.ScopeId)
            .ToArrayAsync();
        NotificationScopeState[] states = await assertionDb
            .NotificationScopeStates
            .OrderBy(state => state.ScopeId)
            .ToArrayAsync();
        Assert.Equal(
            [firstDeliveryId, secondDeliveryId],
            deliveries.Select(delivery => delivery.Id));
        Assert.All(
            deliveries,
            delivery => Assert.Equal(
                DomainDeliveryStatus.Delivered,
                delivery.Status));
        Assert.Equal(2, attempts.Length);
        Assert.All(
            attempts,
            attempt => Assert.Equal(
                DomainAttemptOutcome.Delivered,
                attempt.Outcome));
        Assert.Collection(
            states,
            state =>
            {
                Assert.Equal("tenant-a", state.ScopeId);
                Assert.Equal(3, state.Version);
            },
            state =>
            {
                Assert.Equal("tenant-b", state.ScopeId);
                Assert.Equal(3, state.Version);
            });
    }

    [Fact]
    public async Task Durable_worker_does_not_claim_or_deliver_a_closed_scope()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        string databaseName = $"delivery-worker-closed-{Guid.NewGuid():N}";
        ServiceCollection services = new();
        services.AddMetrics();
        services.AddSingleton<IScopeContext>(new TestScopeContext(scopeId: null));
        services.AddDbContext<NotificationsDbContext>(options =>
            options.UseInMemoryDatabase(databaseName, databaseRoot));
        services.AddScoped<NotificationMaintenanceDbContextFactory>();
        await using ServiceProvider serviceProvider =
            services.BuildServiceProvider();
        Guid notificationId = Guid.CreateVersion7();
        Guid deliveryId = Guid.CreateVersion7();
        Guid claimedNotificationId = Guid.CreateVersion7();
        Guid claimedDeliveryId = Guid.CreateVersion7();
        const string workerId = "notification-test-worker";

        await using (AsyncServiceScope seedScope =
                     serviceProvider.CreateAsyncScope())
        {
            NotificationMaintenanceDbContextFactory dbContextFactory =
                seedScope.ServiceProvider
                    .GetRequiredService<NotificationMaintenanceDbContextFactory>();
            await using NotificationsDbContext dbContext =
                dbContextFactory.CreateDbContext();
            dbContext.UserNotifications.AddRange(
                UserNotification.Create(
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
                    "{}").Value,
                UserNotification.Create(
                    claimedNotificationId,
                    "tenant-a",
                    "user-b",
                    "auth",
                    "auth.account-accessed",
                    1,
                    "Claimed sign-in",
                    null,
                    DomainSeverity.Warning,
                    Now,
                    Now,
                    "{}").Value);
            NotificationDelivery pending =
                NotificationDelivery.CreatePending(
                    deliveryId,
                    "tenant-a",
                    notificationId,
                    NotificationTags.Email,
                    TestDurableSink.Provider,
                    Now).Value;
            NotificationDelivery processing =
                NotificationDelivery.CreatePending(
                    claimedDeliveryId,
                    "tenant-a",
                    claimedNotificationId,
                    NotificationTags.Email,
                    TestDurableSink.Provider,
                    Now).Value;
            Assert.True(processing.Claim(
                workerId,
                Now,
                TimeSpan.FromMinutes(1)).IsSuccess);
            dbContext.NotificationDeliveries.AddRange(pending, processing);
            await dbContext.SaveChangesAsync();
            NotificationScopeState state =
                await dbContext.NotificationScopeStates.SingleAsync();
            Assert.Equal(
                NotificationScopeCloseTransition.Completed,
                state.Close(
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    new string('a', 64),
                    Now.AddSeconds(1)));
            await dbContext.SaveChangesAsync();
        }

        TestDurableSink sink = new();
        NotificationDeliveryMetrics metrics = new(
            serviceProvider
                .GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(),
            Options.Create(new ApplicationIdentityOptions
            {
                Namespace = "notification-tests"
            }));
        NotificationDeliveryService worker = new(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new NotificationDeliveryAdapterCatalog([sink]),
            new FixedClock(Now.AddSeconds(2)),
            new TestIdGenerator(),
            Options.Create(new NotificationDeliveryOptions
            {
                WorkerId = workerId,
                LeaseSeconds = 60
            }),
            metrics,
            NullLogger<NotificationDeliveryService>.Instance);

        Guid[] claimed = await worker.ClaimAsync(1, CancellationToken.None);
        await worker.DeliverAsync(claimedDeliveryId, CancellationToken.None);

        Assert.Empty(claimed);
        Assert.Equal(0, sink.CallCount);
        await using AsyncServiceScope assertionScope =
            serviceProvider.CreateAsyncScope();
        NotificationsDbContext assertionDb = assertionScope.ServiceProvider
            .GetRequiredService<NotificationsDbContext>();
        NotificationDelivery[] stored = await assertionDb
            .NotificationDeliveries
            .IgnoreQueryFilters()
            .OrderBy(delivery => delivery.Id)
            .ToArrayAsync();
        Assert.Equal(
            DomainDeliveryStatus.Pending,
            Assert.Single(stored, delivery => delivery.Id == deliveryId).Status);
        Assert.Equal(
            DomainDeliveryStatus.Processing,
            Assert.Single(
                stored,
                delivery => delivery.Id == claimedDeliveryId).Status);
        Assert.Empty(await assertionDb.NotificationDeliveryAttempts
            .IgnoreQueryFilters()
            .ToArrayAsync());
        NotificationScopeState storedState = await assertionDb
            .NotificationScopeStates
            .IgnoreQueryFilters()
            .SingleAsync();
        Assert.True(storedState.IsClosed);
        Assert.Equal(2, storedState.Version);
    }

    [Fact]
    public async Task Durable_worker_propagates_caller_cancellation_without_completing_the_attempt()
    {
        await using ServiceProvider serviceProvider = CreateDeliveryProvider(
            $"delivery-worker-cancellation-{Guid.NewGuid():N}");
        Guid deliveryId = await SeedClaimedDeliveryAsync(
            serviceProvider,
            "notification-test-worker",
            maxAttempts: 2);
        using CancellationTokenSource cancellation = new();
        CancelingDurableSink sink = new(cancellation.Cancel);
        NotificationDeliveryService worker = CreateWorker(
            serviceProvider,
            "notification-test-worker",
            Now.AddSeconds(1),
            sink);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            worker.DeliverAsync(deliveryId, cancellation.Token));

        Assert.Equal(1, sink.CallCount);
        await using AsyncServiceScope assertionScope =
            serviceProvider.CreateAsyncScope();
        NotificationMaintenanceDbContextFactory assertionFactory =
            assertionScope.ServiceProvider
                .GetRequiredService<NotificationMaintenanceDbContextFactory>();
        await using NotificationsDbContext assertionDb =
            assertionFactory.CreateDbContext();
        NotificationDelivery delivery = await assertionDb
            .NotificationDeliveries
            .SingleAsync(item => item.Id == deliveryId);
        Assert.Equal(DomainDeliveryStatus.Processing, delivery.Status);
        Assert.Equal(1, delivery.Attempts);
        Assert.Empty(await assertionDb.NotificationDeliveryAttempts
            .ToArrayAsync());
    }

    [Fact]
    public async Task Durable_worker_exhausts_an_expired_final_lease_and_records_the_abandoned_attempt()
    {
        await using ServiceProvider serviceProvider = CreateDeliveryProvider(
            $"delivery-worker-final-lease-{Guid.NewGuid():N}");
        Guid deliveryId = await SeedClaimedDeliveryAsync(
            serviceProvider,
            "lost-worker",
            maxAttempts: 1);
        NotificationDeliveryService recovery = CreateWorker(
            serviceProvider,
            "recovery-worker",
            Now.AddMinutes(2));

        Guid[] claimed = await recovery.ClaimAsync(1, CancellationToken.None);

        Assert.Empty(claimed);
        await using AsyncServiceScope assertionScope =
            serviceProvider.CreateAsyncScope();
        NotificationMaintenanceDbContextFactory assertionFactory =
            assertionScope.ServiceProvider
                .GetRequiredService<NotificationMaintenanceDbContextFactory>();
        await using NotificationsDbContext dbContext =
            assertionFactory.CreateDbContext();
        NotificationDelivery delivery = await dbContext
            .NotificationDeliveries
            .SingleAsync(item => item.Id == deliveryId);
        NotificationDeliveryAttempt attempt = Assert.Single(
            await dbContext.NotificationDeliveryAttempts.ToArrayAsync());
        Assert.Equal(DomainDeliveryStatus.Exhausted, delivery.Status);
        Assert.Equal(1, delivery.Attempts);
        Assert.Equal("worker-lease-expired", delivery.LastCode);
        Assert.Null(delivery.LockedBy);
        Assert.Null(delivery.LockedUntilUtc);
        Assert.Equal(1, attempt.AttemptNumber);
        Assert.Equal(DomainAttemptOutcome.Exception, attempt.Outcome);
        Assert.Equal("worker-lease-expired", attempt.Code);
    }

    [Fact]
    public async Task Durable_worker_records_an_expired_lease_before_reclaiming_the_next_attempt()
    {
        await using ServiceProvider serviceProvider = CreateDeliveryProvider(
            $"delivery-worker-reclaim-{Guid.NewGuid():N}");
        Guid deliveryId = await SeedClaimedDeliveryAsync(
            serviceProvider,
            "lost-worker",
            maxAttempts: 2);
        NotificationDeliveryService recovery = CreateWorker(
            serviceProvider,
            "recovery-worker",
            Now.AddMinutes(2));

        Guid claimed = Assert.Single(
            await recovery.ClaimAsync(1, CancellationToken.None));
        Assert.Equal(deliveryId, claimed);
        await recovery.DeliverAsync(deliveryId, CancellationToken.None);

        await using AsyncServiceScope assertionScope =
            serviceProvider.CreateAsyncScope();
        NotificationMaintenanceDbContextFactory assertionFactory =
            assertionScope.ServiceProvider
                .GetRequiredService<NotificationMaintenanceDbContextFactory>();
        await using NotificationsDbContext dbContext =
            assertionFactory.CreateDbContext();
        NotificationDelivery delivery = await dbContext
            .NotificationDeliveries
            .SingleAsync(item => item.Id == deliveryId);
        NotificationDeliveryAttempt[] attempts = await dbContext
            .NotificationDeliveryAttempts
            .OrderBy(attempt => attempt.AttemptNumber)
            .ToArrayAsync();
        Assert.Equal(DomainDeliveryStatus.Delivered, delivery.Status);
        Assert.Equal(2, delivery.Attempts);
        Assert.Equal([1, 2], attempts.Select(attempt => attempt.AttemptNumber));
        Assert.Equal(DomainAttemptOutcome.Exception, attempts[0].Outcome);
        Assert.Equal("worker-lease-expired", attempts[0].Code);
        Assert.Equal(DomainAttemptOutcome.Delivered, attempts[1].Outcome);
    }

    private static ServiceProvider CreateDeliveryProvider(
        string databaseName,
        IScopeContext? scopeContext = null)
    {
        InMemoryDatabaseRoot databaseRoot = new();
        ServiceCollection services = new();
        services.AddMetrics();
        services.AddSingleton(
            scopeContext ?? new TestScopeContext(scopeId: null));
        services.AddDbContext<NotificationsDbContext>(options =>
            options.UseInMemoryDatabase(databaseName, databaseRoot));
        services.AddScoped<NotificationMaintenanceDbContextFactory>();
        return services.BuildServiceProvider();
    }

    private static UserNotification CreateNotification(
        string scopeId,
        string userId) =>
        UserNotification.Create(
            Guid.CreateVersion7(),
            scopeId,
            userId,
            "auth",
            "auth.account-accessed",
            1,
            "New sign-in",
            null,
            DomainSeverity.Warning,
            Now,
            Now,
            "{}").Value;

    private static async Task<Guid> SeedClaimedDeliveryAsync(
        ServiceProvider serviceProvider,
        string workerId,
        int maxAttempts)
    {
        Guid notificationId = Guid.CreateVersion7();
        Guid deliveryId = Guid.CreateVersion7();
        await using AsyncServiceScope seedScope =
            serviceProvider.CreateAsyncScope();
        NotificationMaintenanceDbContextFactory dbContextFactory =
            seedScope.ServiceProvider
                .GetRequiredService<NotificationMaintenanceDbContextFactory>();
        await using NotificationsDbContext dbContext =
            dbContextFactory.CreateDbContext();
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
            TestDurableSink.Provider,
            Now,
            maxAttempts).Value;
        Assert.True(delivery.Claim(
            workerId,
            Now,
            TimeSpan.FromMinutes(1)).IsSuccess);
        dbContext.NotificationDeliveries.Add(delivery);
        await dbContext.SaveChangesAsync();
        return deliveryId;
    }

    private static NotificationDeliveryService CreateWorker(
        ServiceProvider serviceProvider,
        string workerId,
        DateTimeOffset nowUtc,
        IUserNotificationSink? sink = null)
    {
        NotificationDeliveryMetrics metrics = new(
            serviceProvider.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(),
            Options.Create(new ApplicationIdentityOptions
            {
                Namespace = "notification-tests"
            }));
        return new NotificationDeliveryService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new NotificationDeliveryAdapterCatalog(
                [sink ?? new TestDurableSink()]),
            new FixedClock(nowUtc),
            new TestIdGenerator(),
            Options.Create(new NotificationDeliveryOptions
            {
                WorkerId = workerId,
                LeaseSeconds = 60
            }),
            metrics,
            NullLogger<NotificationDeliveryService>.Instance);
    }

    private sealed class TestDurableSink : IUserNotificationSink
    {
        private int callCount;

        public const string Provider = "test-email";

        public string ProviderName => Provider;
        public IReadOnlyCollection<string> DeliveryTags => [NotificationTags.Email];
        public NotificationSinkDeliveryMode DeliveryModes => NotificationSinkDeliveryMode.Durable;
        public int CallCount => this.callCount;

        public ValueTask<NotificationSinkDeliveryResult> DeliverAsync(
            NotificationSinkDeliveryRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.callCount);
            return ValueTask.FromResult(
                NotificationSinkDeliveryResult.Delivered("provider-message"));
        }
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

    private sealed class CancelingDurableSink(Action cancel) : IUserNotificationSink
    {
        private int callCount;

        public string ProviderName => TestDurableSink.Provider;
        public IReadOnlyCollection<string> DeliveryTags => [NotificationTags.Email];
        public NotificationSinkDeliveryMode DeliveryModes =>
            NotificationSinkDeliveryMode.Durable;
        public int CallCount => this.callCount;

        public ValueTask<NotificationSinkDeliveryResult> DeliverAsync(
            NotificationSinkDeliveryRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.callCount);
            cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                NotificationSinkDeliveryResult.Delivered("unreachable"));
        }
    }

    private sealed class FutureRetrySink : IUserNotificationSink
    {
        public const string Provider = "future-retry-email";

        public string ProviderName => Provider;
        public IReadOnlyCollection<string> DeliveryTags => [NotificationTags.Email];
        public NotificationSinkDeliveryMode DeliveryModes => NotificationSinkDeliveryMode.Durable;

        public ValueTask<NotificationSinkDeliveryResult> DeliverAsync(
            NotificationSinkDeliveryRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(NotificationSinkDeliveryResult.Retry(
                "provider-backoff",
                Now.AddDays(2)));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class TestIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.CreateVersion7();
    }

    private sealed class TestScopeContext(string? scopeId) : IScopeContext
    {
        public bool IsEnabled => true;
        public bool HasScope => !string.IsNullOrWhiteSpace(scopeId);
        public string? ScopeId => scopeId;
        public string RequireScopeId() => scopeId ?? throw new InvalidOperationException();
    }
}
