namespace Gma.Modules.Notifications.IntegrationTests;

using Gma.Framework.AccessControl;
using Gma.Modules.Notifications.Application;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.IntegrationTests.Support;
using Gma.Modules.Notifications.Persistence;
using Gma.Modules.Notifications.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

public sealed partial class NotificationsRelationalIntegrationTests
{
    [DockerFact]
    public async Task Read_transactions_respect_ownership_and_closed_scopes_on_postgresql()
    {
        await using PostgreSqlContainer database = await StartPostgreSqlAsync("notifications_read_transactions");
        await using ServiceProvider provider = CreateProvider(database.GetConnectionString());
        await MigrateAsync(provider);
        await AssertReadTransactionOwnershipAsync(provider);
    }

    [DockerFact]
    public async Task Read_transactions_respect_ownership_and_closed_scopes_on_sqlserver()
    {
        await using MsSqlContainer database = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await database.StartAsync();
        await using ServiceProvider provider = CreateSqlServerProvider(database.GetConnectionString());
        await MigrateAsync(provider);
        await AssertReadTransactionOwnershipAsync(provider);
    }

    [DockerFact]
    public async Task Bulk_read_admits_more_than_one_scope_batch_on_postgresql()
    {
        await using PostgreSqlContainer database = await StartPostgreSqlAsync("notifications_read_scope_batches");
        await using ServiceProvider provider = CreateProvider(database.GetConnectionString());
        await MigrateAsync(provider);
        await AssertBulkReadScopeBatchesAsync(provider);
    }

    [DockerFact]
    public async Task Bulk_read_admits_more_than_one_scope_batch_on_sqlserver()
    {
        await using MsSqlContainer database = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await database.StartAsync();
        await using ServiceProvider provider = CreateSqlServerProvider(database.GetConnectionString());
        await MigrateAsync(provider);
        await AssertBulkReadScopeBatchesAsync(provider);
    }

    private static async Task AssertReadTransactionOwnershipAsync(ServiceProvider provider)
    {
        const string userId = "owned-read-transaction-user";
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        NotificationsDbContext db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        UserNotification notification = CreateNotification(userId, "read-transaction-ownership");
        await AddNotificationAsync(db, notification);
        await db.SaveChangesAsync();
        NotificationsUnitOfWork unitOfWork = new(db);

        await unitOfWork.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.BeginTransactionAsync());
        db.NotificationPreferences.Add(MaintenancePreference("tenant-a"));
        await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
        Assert.Null(db.Database.CurrentTransaction);
        Assert.Empty(db.ChangeTracker.Entries());

        await AssertConcurrentReadTransactionAsync(provider);
        long initialVersion = await ScopeVersionAsync(provider);

        // Reuse after rollback, then leave an enclosing transaction entirely
        // under the caller's control, including its pending tracker entries.
        await using (IDbContextTransaction outer = await db.Database.BeginTransactionAsync())
        {
            await unitOfWork.BeginTransactionAsync();
            Assert.Same(outer, db.Database.CurrentTransaction);
            Assert.Equal(1, await new NotificationHistoryRepository(db).MarkAllReadAsync(
                AccessSubject.User(userId), "tenant-a", Now.AddSeconds(1), CancellationToken.None));
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            Assert.Same(outer, db.Database.CurrentTransaction);
            Assert.NotEmpty(db.ChangeTracker.Entries());
            await unitOfWork.CommitTransactionAsync();
            Assert.Same(outer, db.Database.CurrentTransaction);
            await outer.RollbackAsync();
        }

        await using (NotificationsDbContext assertion = MaintenanceTestContext(scope.ServiceProvider, "tenant-a"))
        {
            Assert.Null((await assertion.UserNotifications.SingleAsync()).ReadAtUtc);
            Assert.Empty(await assertion.NotificationPreferences.ToArrayAsync());
            Assert.Equal(initialVersion, await assertion.NotificationScopeStates.Select(state => state.Version).SingleAsync());
        }

        // A close that commits after admission must defeat the stale scope
        // version, and rollback must also undo the earlier set-based write.
        await using NotificationsDbContext racing = MaintenanceTestContext(scope.ServiceProvider, "tenant-a");
        NotificationsUnitOfWork racingUnitOfWork = new(racing);
        await racingUnitOfWork.BeginTransactionAsync();
        long closedVersion;
        try
        {
            Assert.Equal(1, await new NotificationHistoryRepository(racing).MarkAllReadAsync(
                AccessSubject.User(userId), "tenant-a", Now.AddSeconds(2), CancellationToken.None));
            await using (NotificationsDbContext closing = MaintenanceTestContext(scope.ServiceProvider, "tenant-a"))
            {
                NotificationScopeState state = await closing.NotificationScopeStates.SingleAsync();
                Assert.Equal(NotificationScopeCloseTransition.Completed,
                    state.Close(Guid.CreateVersion7(), new string('a', 64), Now.AddSeconds(2)));
                await closing.SaveChangesAsync();
                closedVersion = state.Version;
            }

            await Assert.ThrowsAsync<NotificationScopeClosedException>(
                () => racingUnitOfWork.SaveChangesAsync(CancellationToken.None));
        }
        finally
        {
            await racingUnitOfWork.RollbackTransactionAsync(CancellationToken.None);
        }

        await using (NotificationsDbContext assertion = MaintenanceTestContext(scope.ServiceProvider, "tenant-a"))
        {
            Assert.Null((await assertion.UserNotifications.SingleAsync()).ReadAtUtc);
            NotificationScopeState state = await assertion.NotificationScopeStates.SingleAsync();
            Assert.True(state.IsClosed);
            Assert.Equal(closedVersion, state.Version);
        }
    }

    private static async Task AssertConcurrentReadTransactionAsync(ServiceProvider provider)
    {
        NotificationBroadcast broadcast = CreateBroadcast("owned-concurrent-transaction");
        await using (AsyncServiceScope seedScope = provider.CreateAsyncScope())
        {
            NotificationsDbContext seed = seedScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            seed.NotificationBroadcasts.Add(broadcast);
            await seed.SaveChangesAsync();
        }

        long initialVersion = await ScopeVersionAsync(provider);
        NotificationBroadcastRecipientContext recipient = NotificationBroadcastRecipientContext.Create(
            "tenant-a", Contracts.NotificationBroadcastRecipientKind.User, "owned-concurrent-reader").Value;
        await using AsyncServiceScope firstScope = provider.CreateAsyncScope();
        await using AsyncServiceScope secondScope = provider.CreateAsyncScope();
        bool[] results = await Task.WhenAll(
            CommitConcurrentReadAsync(firstScope.ServiceProvider, broadcast.Id, recipient),
            CommitConcurrentReadAsync(secondScope.ServiceProvider, broadcast.Id, recipient));
        Assert.All(results, Assert.True);
        await using AsyncServiceScope assertionScope = provider.CreateAsyncScope();
        NotificationsDbContext assertion = assertionScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        Assert.Equal(1, await assertion.NotificationBroadcastReads.CountAsync(row => row.BroadcastId == broadcast.Id));
        Assert.Equal(initialVersion + 1, await assertion.NotificationScopeStates.Select(state => state.Version).SingleAsync());
    }

    private static async Task<bool> CommitConcurrentReadAsync(
        IServiceProvider provider, Guid broadcastId, NotificationBroadcastRecipientContext recipient)
    {
        NotificationsDbContext db = provider.GetRequiredService<NotificationsDbContext>();
        NotificationsUnitOfWork unitOfWork = new(db);
        await unitOfWork.BeginTransactionAsync();
        try
        {
            bool result = await new NotificationBroadcastRepository(db, new TestIdGenerator()).MarkReadAsync(
                broadcastId, recipient, Now, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            await unitOfWork.CommitTransactionAsync();
            return result;
        }
        finally
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
        }
    }

    private static async Task AssertBulkReadScopeBatchesAsync(ServiceProvider provider)
    {
        const int scopeCount = 501;
        const string userId = "owned-scope-batch-user";
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await using NotificationsDbContext db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>()
            .CreateMaintenanceContext();
        for (int index = 0; index < scopeCount; index++)
        {
            await AddNotificationAsync(db, UserNotification.Create(
                Guid.CreateVersion7(), $"batch-{index:D4}", userId, "notifications-tests", "scope-batch", 1,
                "Owned batch notification", null, Domain.ValueObjects.NotificationSeverity.Info,
                Now, Now, "{}").Value);
        }

        await db.SaveChangesAsync();
        Dictionary<string, long> versions = await db.NotificationScopeStates
            .ToDictionaryAsync(state => state.ScopeId, state => state.Version);
        Assert.Equal(scopeCount, versions.Count);
        db.ChangeTracker.Clear();
        NotificationsUnitOfWork unitOfWork = new(db);
        await unitOfWork.BeginTransactionAsync();
        try
        {
            Assert.Equal(scopeCount, await new NotificationHistoryRepository(db).MarkAllReadAsync(
                AccessSubject.User(userId), scopeId: null, Now.AddSeconds(1), CancellationToken.None));
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            await unitOfWork.CommitTransactionAsync();
        }
        finally
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
        }

        await using NotificationsDbContext assertion = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>()
            .CreateMaintenanceContext();
        Assert.Equal(scopeCount, await assertion.UserNotifications.CountAsync(row => row.ReadAtUtc != null));
        NotificationScopeState[] states = await assertion.NotificationScopeStates.ToArrayAsync();
        Assert.Equal(scopeCount, states.Length);
        Assert.All(states, state => Assert.Equal(versions[state.ScopeId] + 1, state.Version));
    }
}
