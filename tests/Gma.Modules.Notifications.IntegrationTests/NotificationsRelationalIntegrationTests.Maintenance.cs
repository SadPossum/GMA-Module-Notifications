namespace Gma.Modules.Notifications.IntegrationTests;

using Gma.Framework.Messaging;
using Gma.Framework.Messaging.Infrastructure;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.IntegrationTests.Support;
using Gma.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Gma.Modules.Notifications.Application;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

public sealed partial class NotificationsRelationalIntegrationTests
{
    [DockerFact]
    public async Task Journal_maintenance_preserves_tenant_isolation_and_closed_scopes_on_postgresql()
    {
        await using PostgreSqlContainer database =
            await StartPostgreSqlAsync("notifications_journal_maintenance");
        await using ServiceProvider provider =
            CreateProvider(database.GetConnectionString(), "default");
        await MigrateAsync(provider);
        await AssertJournalMaintenanceAsync(provider);
    }

    [DockerFact]
    public async Task Journal_maintenance_preserves_tenant_isolation_and_closed_scopes_on_sqlserver()
    {
        await using MsSqlContainer database =
            new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await database.StartAsync();
        await using ServiceProvider provider =
            CreateSqlServerProvider(database.GetConnectionString(), "default");
        await MigrateAsync(provider);
        await AssertJournalMaintenanceAsync(provider);
    }

    [DockerFact]
    public async Task History_maintenance_uses_the_owner_context_on_postgresql()
    {
        await using PostgreSqlContainer database =
            await StartPostgreSqlAsync("notifications_history_maintenance");
        await using ServiceProvider provider =
            CreateProvider(database.GetConnectionString(), "default");
        await MigrateAsync(provider);
        await AssertHistoryMaintenanceAsync(provider);
    }

    [DockerFact]
    public async Task History_maintenance_uses_the_owner_context_on_sqlserver()
    {
        await using MsSqlContainer database =
            new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await database.StartAsync();
        await using ServiceProvider provider =
            CreateSqlServerProvider(database.GetConnectionString(), "default");
        await MigrateAsync(provider);
        await AssertHistoryMaintenanceAsync(provider);
    }

    [DockerFact]
    public async Task Sqlserver_recipient_scope_upgrade_preserves_existing_receipts()
    {
        await using MsSqlContainer database =
            new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await database.StartAsync();
        await using ServiceProvider root = CreateSqlServerProvider(database.GetConnectionString());
        await using AsyncServiceScope dataScope = root.CreateAsyncScope();
        IServiceProvider provider = dataScope.ServiceProvider;
        await using (NotificationsDbContext before = MaintenanceTestContext(provider, "tenant-a"))
        {
            string[] migrations = before.Database.GetMigrations().ToArray();
            Assert.EndsWith("MakeBroadcastRecipientScopesOrdinal", migrations[^1]);
            await before.GetService<IMigrator>().MigrateAsync(migrations[^2]);
            before.NotificationBroadcasts.Add(NotificationBroadcast.Create(
                StableId(350), null,
                Gma.Modules.Notifications.Domain.ValueObjects.NotificationBroadcastAudience.PlatformUsers,
                "notifications-tests", "upgrade-broadcast", 1, "Synthetic upgrade receipt", null,
                Gma.Modules.Notifications.Domain.ValueObjects.NotificationSeverity.Info,
                Now, Now, "{}").Value);
            before.NotificationBroadcastReads.Add(NotificationBroadcastRead.Create(
                StableId(351), StableId(350), "tenant-a",
                Gma.Modules.Notifications.Domain.ValueObjects.NotificationBroadcastRecipientKind.User,
                "maintenance-test-user", Now).Value);
            await before.SaveChangesAsync();
            await before.GetService<IMigrator>().MigrateAsync();
        }

        await using (NotificationsDbContext after = MaintenanceTestContext(provider, "Tenant-A"))
        {
            Assert.Equal(StableId(351), (await after.NotificationBroadcastReads.SingleAsync()).Id);
            after.NotificationBroadcastReads.Add(NotificationBroadcastRead.Create(
                StableId(352), StableId(350), "Tenant-A",
                Gma.Modules.Notifications.Domain.ValueObjects.NotificationBroadcastRecipientKind.User,
                "maintenance-test-user", Now).Value);
            await after.SaveChangesAsync();
            Assert.Equal(2, await after.NotificationBroadcastReads.CountAsync());
        }
    }

    private static async Task AssertHistoryMaintenanceAsync(ServiceProvider rootProvider)
    {
        await using AsyncServiceScope dataScope = rootProvider.CreateAsyncScope();
        IServiceProvider provider = dataScope.ServiceProvider;
        await SeedMaintenanceHistoryAsync(provider, 201, "tenant-a", Now.AddDays(-3), read: true);
        await SeedMaintenanceHistoryAsync(provider, 202, "tenant-b", Now.AddDays(-3), read: true);
        await SeedMaintenanceHistoryAsync(provider, 203, "tenant-a", Now.AddDays(-3), read: false);
        await SeedMaintenanceHistoryAsync(provider, 204, "tenant-b", Now.AddHours(-12), read: true);
        await SeedMaintenanceHistoryAsync(provider, 205, "tenant-closed", Now.AddDays(-30), read: true);
        await SeedMaintenanceHistoryAsync(provider, 206, "tenant-a", Now.AddDays(-20), read: false);
        await using (NotificationsDbContext broadcasts = MaintenanceTestContext(provider, "default"))
        {
            broadcasts.NotificationBroadcasts.Add(NotificationBroadcast.Create(
                StableId(250), null,
                Gma.Modules.Notifications.Domain.ValueObjects.NotificationBroadcastAudience.PlatformUsers,
                "notifications-tests", "maintenance-broadcast", 1, "Synthetic broadcast", null,
                Gma.Modules.Notifications.Domain.ValueObjects.NotificationSeverity.Info,
                Now.AddDays(-10), Now.AddDays(-10), "{}").Value);
            await broadcasts.SaveChangesAsync();
        }

        string?[] recipientScopes = ["tenant-a", "tenant-b", "tenant-closed", "Tenant-Closed", null];
        for (int index = 0; index < recipientScopes.Length; index++)
        {
            await using NotificationsDbContext receipts = MaintenanceTestContext(provider, recipientScopes[index] ?? "default");
            receipts.NotificationBroadcastReads.Add(NotificationBroadcastRead.Create(
                StableId(260 + index), StableId(250), recipientScopes[index],
                Gma.Modules.Notifications.Domain.ValueObjects.NotificationBroadcastRecipientKind.User,
                "maintenance-test-user", Now.AddDays(-8)).Value);
            await receipts.SaveChangesAsync();
        }

        await using (NotificationsDbContext closing = MaintenanceTestContext(provider, "tenant-closed"))
        {
            NotificationScopeState state = await closing.NotificationScopeStates.SingleAsync();
            state.Close(StableId(220), new string('b', 64), Now.AddDays(-1));
            await closing.SaveChangesAsync();
        }

        NotificationRetentionService job = new(
            rootProvider.GetRequiredService<IServiceScopeFactory>(),
            new FixedClock(Now),
            Options.Create(new NotificationRetentionOptions
            {
                Enabled = true,
                ReadHistoryDays = 1,
                UnreadHistoryDays = 7,
                BroadcastDays = 7,
                BatchSize = 2,
                MaxBatchesPerCategoryPerCycle = 3
            }),
            Options.Create(new NotificationDeliveryOptions()),
            NullLogger<NotificationRetentionService>.Instance);
        await job.CleanupAsync(CancellationToken.None);
        await job.CleanupAsync(CancellationToken.None);

        await using NotificationsDbContext read = MaintenanceTestContext(provider, "default");
        Guid[] retained = await read.UserNotifications.IgnoreQueryFilters().Select(row => row.Id).ToArrayAsync();
        Assert.Equal(new[] { StableId(203), StableId(204), StableId(205) }.Order(), retained.Order());
        Assert.True(read.ScopeFilterEnabled);
        Assert.Equal("default", read.CurrentScopeId);
        Assert.Empty(await read.UserNotifications.ToArrayAsync());
        Assert.False(await read.NotificationScopeStates.AnyAsync());
        NotificationBroadcastRead retainedRead = await read.NotificationBroadcastReads.SingleAsync();
        Assert.Equal(StableId(262), retainedRead.Id);
        Assert.Equal("tenant:tenant-closed", retainedRead.RecipientScope);
        Assert.Equal(StableId(250), (await read.NotificationBroadcasts.SingleAsync()).Id);
    }

    private static async Task SeedMaintenanceHistoryAsync(
        IServiceProvider provider,
        int id,
        string scopeId,
        DateTimeOffset timestamp,
        bool read)
    {
        await using NotificationsDbContext context = MaintenanceTestContext(provider, scopeId);
        UserNotification notification = UserNotification.Create(
            StableId(id), scopeId, "maintenance-test-user", "notifications-tests", "maintenance-history", 1,
            "Synthetic maintenance history", null,
            Gma.Modules.Notifications.Domain.ValueObjects.NotificationSeverity.Info,
            timestamp, timestamp, "{}").Value;
        if (read)
        {
            notification.MarkRead(timestamp.AddHours(1));
        }

        await AddNotificationAsync(context, notification);
        await context.SaveChangesAsync();
    }

    private static async Task AssertJournalMaintenanceAsync(ServiceProvider rootProvider)
    {
        await using AsyncServiceScope dataScope = rootProvider.CreateAsyncScope();
        IServiceProvider provider = dataScope.ServiceProvider;
        DateTimeOffset cutoff = Now.AddDays(-14);
        await SeedMaintenanceInboxAsync(provider, 101, "tenant-a", Now.AddDays(-31));
        await SeedMaintenanceInboxAsync(provider, 102, "tenant-b", Now.AddDays(-30));
        await SeedMaintenanceInboxAsync(provider, 103, null, Now.AddDays(-29));
        await SeedMaintenanceInboxAsync(provider, 104, "tenant-a", Now.AddDays(-28));
        await SeedMaintenanceInboxAsync(provider, 105, "tenant-b", Now.AddDays(-27));
        await SeedMaintenanceInboxAsync(provider, 106, "tenant-a", cutoff);
        await SeedMaintenanceInboxAsync(provider, 107, "tenant-b", Now.AddDays(-1));
        await SeedMaintenanceInboxAsync(provider, 108, "tenant-a", Now.AddDays(-26), InboxMessageStatus.Pending);
        await SeedMaintenanceInboxAsync(provider, 109, "tenant-b", Now.AddDays(-26), InboxMessageStatus.Failed);
        await SeedMaintenanceInboxAsync(provider, 110, "tenant-a", Now.AddDays(-26), InboxMessageStatus.Processing);
        await SeedMaintenanceInboxAsync(provider, 111, "tenant-closed", Now.AddDays(-26));

        await using (NotificationsDbContext closing = MaintenanceTestContext(provider, "tenant-closed"))
        {
            NotificationScopeState state = await closing.NotificationScopeStates.SingleAsync();
            state.Close(StableId(120), new string('a', 64), Now.AddDays(-1));
            await closing.SaveChangesAsync();
        }

        await using AsyncServiceScope workerScope = provider.CreateAsyncScope();
        NotificationsDbContext ambient = workerScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        NotificationPreference pending = MaintenancePreference("default");
        ambient.NotificationPreferences.Add(pending);
        NotificationsInboxStore inbox = new(ambient, new FixedClock(Now), new TestIdGenerator());
        Dictionary<string, long> before;
        await using (NotificationsDbContext read = MaintenanceTestContext(provider, "default"))
        {
            before = await read.NotificationScopeStates.IgnoreQueryFilters()
                .ToDictionaryAsync(state => state.ScopeId, state => state.Version);
        }

        using (CancellationTokenSource canceled = new())
        {
            await canceled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                inbox.DeleteProcessedBeforeAsync(cutoff, 2, canceled.Token));
        }

        Assert.Equal(2, await inbox.DeleteProcessedBeforeAsync(cutoff, 2, CancellationToken.None));
        Assert.True(ambient.ScopeFilterEnabled);
        Assert.Equal("default", ambient.CurrentScopeId);
        Assert.Equal(EntityState.Added, ambient.Entry(pending).State);
        await using (NotificationsDbContext read = MaintenanceTestContext(provider, "default"))
        {
            Assert.False(await read.InboxMessages.AnyAsync(row => row.Id == StableId(101) || row.Id == StableId(102)));
            Assert.Empty(await read.NotificationPreferences.ToArrayAsync());
            Assert.False(await read.NotificationScopeStates.AnyAsync());
            Dictionary<string, long> after = await read.NotificationScopeStates.IgnoreQueryFilters()
                .ToDictionaryAsync(state => state.ScopeId, state => state.Version);
            Assert.Equal(before["tenant-a"] + 1, after["tenant-a"]);
            Assert.Equal(before["tenant-b"] + 1, after["tenant-b"]);
            Assert.Equal(before["tenant-closed"], after["tenant-closed"]);
        }

        Assert.Equal(2, await inbox.DeleteProcessedBeforeAsync(cutoff, 2, CancellationToken.None));
        Assert.Equal(1, await inbox.DeleteProcessedBeforeAsync(cutoff, 2, CancellationToken.None));
        Assert.Equal(0, await inbox.DeleteProcessedBeforeAsync(cutoff, 2, CancellationToken.None));
        await using (NotificationsDbContext read = MaintenanceTestContext(provider, "default"))
        {
            Guid[] retained = await read.InboxMessages.Select(row => row.Id).ToArrayAsync();
            Assert.Equal(Enumerable.Range(106, 6).Select(StableId).Order(), retained.Order());
            Assert.Empty(await read.NotificationPreferences.IgnoreQueryFilters().ToArrayAsync());
        }

        // The module job must neither alter its caller's scope nor turn its
        // foreground context into a cross-tenant writer.
        ambient.NotificationPreferences.Add(MaintenancePreference("tenant-b"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ambient.SaveChangesAsync());
        ambient.ChangeTracker.Clear();
        ambient.NotificationPreferences.Add(pending);
        await ambient.SaveChangesAsync();
        await using NotificationsDbContext finalRead = MaintenanceTestContext(provider, "default");
        Assert.Equal(pending.Id, (await finalRead.NotificationPreferences.SingleAsync()).Id);
    }

    private static NotificationsDbContext MaintenanceTestContext(IServiceProvider provider, string scopeId) =>
        new(provider.GetRequiredService<DbContextOptions<NotificationsDbContext>>(), new TestScopeContext(scopeId));

    private static NotificationPreference MaintenancePreference(string scopeId) =>
        NotificationPreference.Create(Guid.CreateVersion7(), scopeId, "maintenance-test-user", "domain:operations", true, Now).Value;

    private static async Task SeedMaintenanceInboxAsync(
        IServiceProvider provider,
        int id,
        string? scopeId,
        DateTimeOffset timestamp,
        InboxMessageStatus status = InboxMessageStatus.Processed)
    {
        await using NotificationsDbContext context = MaintenanceTestContext(provider, scopeId ?? "default");
        if (status == InboxMessageStatus.Processed)
        {
            NotificationsInboxStore inbox = new(context, new FixedClock(timestamp), new TestIdGenerator());
            Assert.Equal(InboxProcessStatus.Processed, (await inbox.ProcessAsync(
                new InboxMessageRecord(StableId(id), "maintenance-regression", "gma.notifications.maintenance.v1", "maintenance", 1, scopeId, timestamp),
                _ => Task.CompletedTask, CancellationToken.None)).Status);
            return;
        }

        InboxMessage row = InboxMessage.Create(StableId(id), "maintenance-regression", "gma.notifications.maintenance.v1", "maintenance", 1, scopeId, timestamp, timestamp);
        if (status is InboxMessageStatus.Processing or InboxMessageStatus.Failed)
        {
            row.MarkProcessing("maintenance-seed", timestamp);
        }

        if (status == InboxMessageStatus.Failed)
        {
            row.MarkFailed("synthetic failure retained for recovery", timestamp);
        }

        context.InboxMessages.Add(row);
        await context.SaveChangesAsync();
    }
}
