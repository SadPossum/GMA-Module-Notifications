namespace Gma.Modules.Notifications.IntegrationTests;

using System.Data.Common;
using Gma.Framework.AccessControl;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.IntegrationTests.Support;
using Gma.Modules.Notifications.Persistence;
using Gma.Modules.Notifications.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

public sealed partial class NotificationsRelationalIntegrationTests
{
    [DockerFact]
    public async Task Bulk_read_only_mutates_the_admitted_scope_snapshot_on_postgresql()
    {
        await using PostgreSqlContainer database = await StartPostgreSqlAsync("notifications_read_admission");
        await using ServiceProvider provider = CreateProvider(database.GetConnectionString());
        await MigrateAsync(provider);
        await AssertBulkReadAdmissionSnapshotAsync(provider);
    }

    [DockerFact]
    public async Task Bulk_read_only_mutates_the_admitted_scope_snapshot_on_sqlserver()
    {
        await using MsSqlContainer database = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await database.StartAsync();
        await using ServiceProvider provider = CreateSqlServerProvider(database.GetConnectionString());
        await MigrateAsync(provider);
        await AssertBulkReadAdmissionSnapshotAsync(provider);
    }

    private static async Task AssertBulkReadAdmissionSnapshotAsync(ServiceProvider provider)
    {
        const string userId = "owned-read-admission-user";
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await using (NotificationsDbContext seed = MaintenanceTestContext(scope.ServiceProvider, "tenant-a"))
        {
            await AddNotificationAsync(seed, CreateNotification(userId, "admitted-scope"));
            await seed.SaveChangesAsync();
        }

        long admittedVersion = await ScopeVersionAsync(provider);
        long lateVersion = 0;
        LateReadScopeInterceptor interceptor = new(async () =>
        {
            // Commit another tenant's notification after scope admission but
            // immediately before the actual set-based UPDATE is executed.
            await using NotificationsDbContext late = MaintenanceTestContext(scope.ServiceProvider, "tenant-b");
            UserNotification notification = UserNotification.Create(
                Guid.CreateVersion7(), "tenant-b", userId, "notifications-tests", "late-scope", 1,
                "Owned late notification", null, Domain.ValueObjects.NotificationSeverity.Info,
                Now, Now, "{}").Value;
            await AddNotificationAsync(late, notification);
            await late.SaveChangesAsync();
            lateVersion = await late.NotificationScopeStates.Select(state => state.Version).SingleAsync();
        });
        DbContextOptions<NotificationsDbContext> options = new DbContextOptionsBuilder<NotificationsDbContext>(
                scope.ServiceProvider.GetRequiredService<DbContextOptions<NotificationsDbContext>>())
            .AddInterceptors(interceptor)
            .Options;
        await using NotificationsDbContext foreground = new(options, new TestScopeContext("tenant-a"));
        await using NotificationsDbContext writer = foreground.CreateMaintenanceContext();
        NotificationsUnitOfWork unitOfWork = new(writer);
        await unitOfWork.BeginTransactionAsync();
        try
        {
            Assert.Equal(1, await new NotificationHistoryRepository(writer).MarkAllReadAsync(
                AccessSubject.User(userId), scopeId: null, Now.AddSeconds(1), CancellationToken.None));
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            await unitOfWork.CommitTransactionAsync();
        }
        finally
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
        }

        Assert.Equal(1, interceptor.Injections);
        await using (NotificationsDbContext admitted = MaintenanceTestContext(scope.ServiceProvider, "tenant-a"))
        {
            Assert.NotNull((await admitted.UserNotifications.SingleAsync()).ReadAtUtc);
            Assert.Equal(admittedVersion + 1, await admitted.NotificationScopeStates.Select(state => state.Version).SingleAsync());
        }

        await using (NotificationsDbContext late = MaintenanceTestContext(scope.ServiceProvider, "tenant-b"))
        {
            Assert.Null((await late.UserNotifications.SingleAsync()).ReadAtUtc);
            Assert.Equal(lateVersion, await late.NotificationScopeStates.Select(state => state.Version).SingleAsync());
        }
    }

    private sealed class LateReadScopeInterceptor(Func<Task> insertLateScope) : DbCommandInterceptor
    {
        public int Injections { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (this.Injections == 0 &&
                command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("user_notifications", StringComparison.Ordinal))
            {
                this.Injections++;
                await insertLateScope();
            }

            return result;
        }
    }
}
