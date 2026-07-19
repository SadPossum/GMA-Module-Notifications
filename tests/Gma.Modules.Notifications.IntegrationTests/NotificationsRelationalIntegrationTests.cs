namespace Gma.Modules.Notifications.IntegrationTests;

using Gma.Framework.Notifications;
using Gma.Framework.Runtime;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Framework.Scoping;
using Gma.Modules.Notifications.Application;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.IntegrationTests.Support;
using Gma.Modules.Notifications.Persistence;
using Gma.Modules.Notifications.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;
using ContractRecipientKind = Gma.Modules.Notifications.Contracts.NotificationBroadcastRecipientKind;
using DomainAudience = Gma.Modules.Notifications.Domain.ValueObjects.NotificationBroadcastAudience;
using DomainDeliveryStatus = Gma.Modules.Notifications.Domain.ValueObjects.NotificationDeliveryStatus;
using DomainSeverity = Gma.Modules.Notifications.Domain.ValueObjects.NotificationSeverity;

[Trait("Category", "Docker")]
[Trait("Category", "Integration")]
public sealed class NotificationsRelationalIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 18, 0, 0, TimeSpan.Zero);

    [DockerFact]
    public async Task Migrations_generate_ordered_stream_sequences_and_monitor_database_heads()
    {
        await using PostgreSqlContainer postgreSql = await StartPostgreSqlAsync("notifications_stream_tests");
        await using ServiceProvider provider = CreateProvider(postgreSql.GetConnectionString());
        await MigrateAsync(provider);

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            dbContext.UserNotifications.AddRange(
                CreateNotification("user-a", "first-user-notification"),
                CreateNotification("user-a", "second-user-notification"));
            dbContext.NotificationBroadcasts.AddRange(
                CreateBroadcast("first-broadcast"),
                CreateBroadcast("second-broadcast"));
            await dbContext.SaveChangesAsync();
        }

        long[] notificationSequences;
        long[] broadcastSequences;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            notificationSequences = await dbContext.UserNotifications
                .AsNoTracking()
                .OrderBy(item => item.CreatedAtUtc)
                .ThenBy(item => item.Id)
                .Select(item => item.StreamSequence)
                .ToArrayAsync();
            broadcastSequences = await dbContext.NotificationBroadcasts
                .AsNoTracking()
                .OrderBy(item => item.CreatedAtUtc)
                .ThenBy(item => item.Id)
                .Select(item => item.StreamSequence)
                .ToArrayAsync();
        }

        Assert.Equal(2, notificationSequences.Distinct().Count());
        Assert.Equal(2, broadcastSequences.Distinct().Count());
        Assert.All(notificationSequences, sequence => Assert.True(sequence > 0));
        Assert.All(broadcastSequences, sequence => Assert.True(sequence > 0));

        NotificationStreamPulse pulse = new();
        NotificationStreamMonitorService monitor = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            pulse,
            Options.Create(new NotificationStreamOptions()),
            NullLogger<NotificationStreamMonitorService>.Instance);
        await monitor.RefreshAsync(CancellationToken.None);

        Assert.Equal(notificationSequences.Max(), pulse.CaptureVersion(NotificationStreamKind.History));
        Assert.Equal(broadcastSequences.Max(), pulse.CaptureVersion(NotificationStreamKind.Broadcasts));
    }

    [DockerFact]
    public async Task Delivery_claims_are_disjoint_and_batch_processing_honors_concurrency()
    {
        await using PostgreSqlContainer postgreSql = await StartPostgreSqlAsync("notifications_delivery_tests");
        await using ServiceProvider provider = CreateProvider(postgreSql.GetConnectionString());
        await MigrateAsync(provider);
        await SeedDeliveriesAsync(provider, count: 9);

        TrackingDeliverySink sink = new();
        NotificationDeliveryService first = CreateWorker(provider, "worker-one", Now, sink, batchSize: 5, maxConcurrency: 2);
        NotificationDeliveryService second = CreateWorker(provider, "worker-two", Now, sink, batchSize: 5, maxConcurrency: 2);

        Guid[][] claims = await Task.WhenAll(
            first.ClaimAsync(2, CancellationToken.None),
            second.ClaimAsync(2, CancellationToken.None));

        Assert.Equal(2, claims[0].Length);
        Assert.Equal(2, claims[1].Length);
        Assert.Empty(claims[0].Intersect(claims[1]));

        NotificationDeliveryService recovery = CreateWorker(
            provider,
            "worker-recovery",
            Now.AddMinutes(2),
            sink,
            batchSize: 5,
            maxConcurrency: 2);
        int processed = await recovery.ProcessAvailableBatchAsync(CancellationToken.None);
        int recoveredRemainder = await recovery.ProcessAvailableBatchAsync(CancellationToken.None);

        Assert.Equal(5, processed);
        Assert.Equal(4, recoveredRemainder);
        Assert.InRange(sink.MaximumConcurrency, 1, 2);
        await using AsyncServiceScope assertionScope = provider.CreateAsyncScope();
        NotificationsDbContext assertionDb = assertionScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        Assert.Equal(9, await assertionDb.NotificationDeliveries.CountAsync(
            delivery => delivery.Status == DomainDeliveryStatus.Delivered));
    }

    [DockerFact]
    public async Task Delivery_claims_are_disjoint_on_sql_server()
    {
        await using MsSqlContainer sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await sqlServer.StartAsync();
        await using ServiceProvider provider = CreateSqlServerProvider(sqlServer.GetConnectionString());
        await MigrateAsync(provider);
        await SeedDeliveriesAsync(provider, count: 4);

        TrackingDeliverySink sink = new();
        NotificationDeliveryService first = CreateWorker(provider, "sql-worker-one", Now, sink, batchSize: 2, maxConcurrency: 2);
        NotificationDeliveryService second = CreateWorker(provider, "sql-worker-two", Now, sink, batchSize: 2, maxConcurrency: 2);

        Guid[][] claims = await Task.WhenAll(
            first.ClaimAsync(2, CancellationToken.None),
            second.ClaimAsync(2, CancellationToken.None));

        Assert.Equal(2, claims[0].Length);
        Assert.Equal(2, claims[1].Length);
        Assert.Empty(claims[0].Intersect(claims[1]));
    }

    [DockerFact]
    public async Task Concurrent_broadcast_read_receipts_are_idempotent()
    {
        await using PostgreSqlContainer postgreSql = await StartPostgreSqlAsync("notifications_receipt_tests");
        await using ServiceProvider provider = CreateProvider(postgreSql.GetConnectionString());
        await MigrateAsync(provider);
        NotificationBroadcast broadcast = CreateBroadcast("concurrent-read");
        await using (AsyncServiceScope seedScope = provider.CreateAsyncScope())
        {
            NotificationsDbContext seed = seedScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            seed.NotificationBroadcasts.Add(broadcast);
            await seed.SaveChangesAsync();
        }

        NotificationBroadcastRecipientContext recipient = NotificationBroadcastRecipientContext.Create(
            "tenant-a",
            ContractRecipientKind.User,
            "user-a").Value;
        await using AsyncServiceScope firstScope = provider.CreateAsyncScope();
        await using AsyncServiceScope secondScope = provider.CreateAsyncScope();
        NotificationBroadcastRepository first = new(
            firstScope.ServiceProvider.GetRequiredService<NotificationsDbContext>(),
            new TestIdGenerator());
        NotificationBroadcastRepository second = new(
            secondScope.ServiceProvider.GetRequiredService<NotificationsDbContext>(),
            new TestIdGenerator());

        bool[] results = await Task.WhenAll(
            first.MarkReadAsync(broadcast.Id, recipient, Now, CancellationToken.None),
            second.MarkReadAsync(broadcast.Id, recipient, Now, CancellationToken.None));

        Assert.All(results, Assert.True);
        await using AsyncServiceScope assertionScope = provider.CreateAsyncScope();
        Assert.Equal(1, await assertionScope.ServiceProvider
            .GetRequiredService<NotificationsDbContext>()
            .NotificationBroadcastReads
            .CountAsync());
    }

    private static async Task<PostgreSqlContainer> StartPostgreSqlAsync(string database)
    {
        PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase(database)
            .Build();
        await container.StartAsync();
        return container;
    }

    private static ServiceProvider CreateProvider(string connectionString)
    {
        ServiceCollection services = new();
        services.AddMetrics();
        services.AddSingleton<IScopeContext>(new TestScopeContext("tenant-a"));
        services.AddDbContext<NotificationsDbContext>(options => options.UseNpgsql(
            connectionString,
            provider => provider.MigrationsAssembly(NotificationsMigrations.PostgreSqlAssembly)));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static ServiceProvider CreateSqlServerProvider(string connectionString)
    {
        ServiceCollection services = new();
        services.AddMetrics();
        services.AddSingleton<IScopeContext>(new TestScopeContext("tenant-a"));
        services.AddDbContext<NotificationsDbContext>(options => options.UseSqlServer(
            connectionString,
            provider => provider.MigrationsAssembly(NotificationsMigrations.SqlServerAssembly)));
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task MigrateAsync(ServiceProvider provider)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<NotificationsDbContext>()
            .Database
            .MigrateAsync();
    }

    private static async Task SeedDeliveriesAsync(ServiceProvider provider, int count)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        for (int index = 0; index < count; index++)
        {
            UserNotification notification = CreateNotification($"user-{index}", $"delivery-{index}");
            NotificationDelivery delivery = NotificationDelivery.CreatePending(
                Guid.CreateVersion7(),
                "tenant-a",
                notification.Id,
                NotificationTags.Email,
                TrackingDeliverySink.Provider,
                Now).Value;
            dbContext.UserNotifications.Add(notification);
            dbContext.NotificationDeliveries.Add(delivery);
        }

        await dbContext.SaveChangesAsync();
    }

    private static NotificationDeliveryService CreateWorker(
        ServiceProvider provider,
        string workerId,
        DateTimeOffset nowUtc,
        TrackingDeliverySink sink,
        int batchSize,
        int maxConcurrency)
    {
        NotificationDeliveryMetrics metrics = new(
            provider.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(),
            Options.Create(new ApplicationIdentityOptions { Namespace = "notifications-integration-tests" }));
        return new NotificationDeliveryService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new TestAdapterCatalog(sink),
            new FixedClock(nowUtc),
            new TestIdGenerator(),
            Options.Create(new NotificationDeliveryOptions
            {
                WorkerId = workerId,
                BatchSize = batchSize,
                MaxConcurrency = maxConcurrency,
                LeaseSeconds = 60,
            }),
            metrics,
            NullLogger<NotificationDeliveryService>.Instance);
    }

    private static UserNotification CreateNotification(string userId, string name) =>
        UserNotification.Create(
            Guid.CreateVersion7(),
            "tenant-a",
            userId,
            "notifications-tests",
            name,
            1,
            name,
            null,
            DomainSeverity.Info,
            Now,
            Now,
            "{}").Value;

    private static NotificationBroadcast CreateBroadcast(string name) =>
        NotificationBroadcast.Create(
            Guid.CreateVersion7(),
            "tenant-a",
            DomainAudience.TenantUsers,
            "notifications-tests",
            name,
            1,
            name,
            null,
            DomainSeverity.Info,
            Now,
            Now,
            "{}").Value;

    private sealed class TestAdapterCatalog(TrackingDeliverySink sink) : INotificationDeliveryAdapterCatalog
    {
        public IReadOnlyList<string> GetProviders(string deliveryTag) =>
            this.Supports(TrackingDeliverySink.Provider, deliveryTag) ? [TrackingDeliverySink.Provider] : [];

        public bool Supports(string provider, string deliveryTag) =>
            string.Equals(provider, TrackingDeliverySink.Provider, StringComparison.Ordinal) &&
            string.Equals(deliveryTag, NotificationTags.Email, StringComparison.Ordinal);

        public IUserNotificationSink? GetProvider(string provider) =>
            string.Equals(provider, TrackingDeliverySink.Provider, StringComparison.Ordinal) ? sink : null;
    }

    private sealed class TrackingDeliverySink : IUserNotificationSink
    {
        private int concurrency;
        private int maximumConcurrency;

        public const string Provider = "postgres-test-email";
        public string ProviderName => Provider;
        public IReadOnlyCollection<string> DeliveryTags => [NotificationTags.Email];
        public NotificationSinkDeliveryMode DeliveryModes => NotificationSinkDeliveryMode.Durable;
        public int MaximumConcurrency => this.maximumConcurrency;

        public async ValueTask<NotificationSinkDeliveryResult> DeliverAsync(
            NotificationSinkDeliveryRequest request,
            CancellationToken cancellationToken)
        {
            int current = Interlocked.Increment(ref this.concurrency);
            UpdateMaximum(ref this.maximumConcurrency, current);
            try
            {
                await Task.Delay(25, cancellationToken);
                return NotificationSinkDeliveryResult.Delivered(request.DeliveryId.ToString("N"));
            }
            finally
            {
                Interlocked.Decrement(ref this.concurrency);
            }
        }

        private static void UpdateMaximum(ref int maximum, int candidate)
        {
            int observed;
            do
            {
                observed = Volatile.Read(ref maximum);
                if (observed >= candidate)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref maximum, candidate, observed) != observed);
        }
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
