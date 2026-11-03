namespace Gma.Modules.Notifications.IntegrationTests;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs.UnitOfWork;
using Gma.Modules.Notifications.Application;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.IntegrationTests.Support;
using Gma.Modules.Notifications.Persistence;
using Gma.Modules.Notifications.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

public sealed partial class NotificationsRelationalIntegrationTests
{
    [DockerFact]
    public async Task Read_write_cancellation_rolls_back_history_on_postgresql()
    {
        await using PostgreSqlContainer database = await StartPostgreSqlAsync("notifications_history_atomicity");
        await using ServiceProvider provider = CreateProvider(database.GetConnectionString());
        await MigrateAsync(provider);
        await AssertReadWriteCancellationAsync(provider, broadcast: false);
    }

    [DockerFact]
    public async Task Read_write_cancellation_rolls_back_broadcast_on_postgresql()
    {
        await using PostgreSqlContainer database = await StartPostgreSqlAsync("notifications_broadcast_atomicity");
        await using ServiceProvider provider = CreateProvider(database.GetConnectionString());
        await MigrateAsync(provider);
        await AssertReadWriteCancellationAsync(provider, broadcast: true);
    }

    [DockerFact]
    public async Task Read_write_cancellation_rolls_back_history_on_sqlserver()
    {
        await using MsSqlContainer database = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await database.StartAsync();
        await using ServiceProvider provider = CreateSqlServerProvider(database.GetConnectionString());
        await MigrateAsync(provider);
        await AssertReadWriteCancellationAsync(provider, broadcast: false);
    }

    [DockerFact]
    public async Task Read_write_cancellation_rolls_back_broadcast_on_sqlserver()
    {
        await using MsSqlContainer database = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await database.StartAsync();
        await using ServiceProvider provider = CreateSqlServerProvider(database.GetConnectionString());
        await MigrateAsync(provider);
        await AssertReadWriteCancellationAsync(provider, broadcast: true);
    }

    private static async Task AssertReadWriteCancellationAsync(ServiceProvider provider, bool broadcast)
    {
        UserNotification notification = CreateNotification("owned-atomic-user", "owned-read-cancellation");
        NotificationBroadcast announcement = NotificationBroadcast.Create(
            Guid.CreateVersion7(), null, Domain.ValueObjects.NotificationBroadcastAudience.PlatformUsers,
            "notifications-tests", "owned-read-cancellation", 1, "Owned announcement", null,
            Domain.ValueObjects.NotificationSeverity.Info, Now, Now, "{}").Value;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            NotificationsDbContext db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            await AddNotificationAsync(db, notification);
            db.NotificationBroadcasts.Add(announcement);
            await db.SaveChangesAsync();
        }

        long version = await ScopeVersionAsync(provider);
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            NotificationsDbContext db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            ITransactionalUnitOfWork? transaction = unitOfWork as ITransactionalUnitOfWork;
            using CancellationTokenSource canceled = new();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                if (transaction is not null)
                {
                    await transaction.BeginTransactionAsync();
                }
                try
                {
                    await ApplyNativeReadAsync(db, announcement.Id, broadcast);
                    await canceled.CancelAsync();
                    await unitOfWork.SaveChangesAsync(canceled.Token);
                    if (transaction is not null)
                    {
                        await transaction.CommitTransactionAsync();
                    }
                }
                catch
                {
                    if (transaction is not null)
                    {
                        await transaction.RollbackTransactionAsync(CancellationToken.None);
                    }
                    throw;
                }
            });
        }

        // Read through another context: both the raw write and its scope
        // revision must have rolled back, regardless of the old tracker's state.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            NotificationsDbContext db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            Assert.Null((await db.UserNotifications.SingleAsync(row => row.Id == notification.Id)).ReadAtUtc);
            Assert.Empty(await db.NotificationBroadcastReads.ToArrayAsync());
            Assert.Equal(version, await db.NotificationScopeStates.Select(row => row.Version).SingleAsync());
        }

        // Keep the successful operation as a control; atomicity cannot be
        // obtained by discarding or refusing the original read operation.
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            NotificationsDbContext db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            ITransactionalUnitOfWork? transaction = unitOfWork as ITransactionalUnitOfWork;
            if (transaction is not null)
            {
                await transaction.BeginTransactionAsync();
            }

            await ApplyNativeReadAsync(db, announcement.Id, broadcast);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            if (transaction is not null)
            {
                await transaction.CommitTransactionAsync();
            }
        }

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            NotificationsDbContext db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            if (broadcast)
            {
                Assert.Single(await db.NotificationBroadcastReads.ToArrayAsync());
            }
            else
            {
                Assert.NotNull((await db.UserNotifications.SingleAsync(row => row.Id == notification.Id)).ReadAtUtc);
            }
            Assert.True(await db.NotificationScopeStates.Select(row => row.Version).SingleAsync() > version);
        }
    }

    private static async Task ApplyNativeReadAsync(NotificationsDbContext db, Guid broadcastId, bool broadcast)
    {
        if (broadcast)
        {
            NotificationBroadcastRecipientContext recipient = NotificationBroadcastRecipientContext.Create(
                "tenant-a", Contracts.NotificationBroadcastRecipientKind.User, "owned-atomic-user").Value;
            Assert.True(await new NotificationBroadcastRepository(db, new TestIdGenerator())
                .MarkReadAsync(broadcastId, recipient, Now.AddSeconds(1), CancellationToken.None));
        }
        else
        {
            Assert.Equal(1, await new NotificationHistoryRepository(db).MarkAllReadAsync(
                AccessSubject.User("owned-atomic-user"), "tenant-a", Now.AddSeconds(1), CancellationToken.None));
        }
    }
}
