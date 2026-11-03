namespace Gma.Modules.Notifications.IntegrationTests;

using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.IntegrationTests.Support;
using Gma.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

public sealed partial class NotificationsRelationalIntegrationTests
{
    [DockerFact]
    public async Task Scope_storage_guard_upgrade_rollback_and_reupgrade_preserve_existing_data_on_postgresql()
    {
        await using PostgreSqlContainer database = await StartPostgreSqlAsync("notifications_storage_upgrade");
        await using ServiceProvider provider = CreateProvider(database.GetConnectionString());
        await AssertScopeStorageUpgradeAsync(provider, usePostgreSql: true);
    }

    [DockerFact]
    public async Task Scope_storage_guard_upgrade_rollback_and_reupgrade_preserve_existing_data_on_sqlserver()
    {
        await using MsSqlContainer database = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await database.StartAsync();
        await using ServiceProvider provider = CreateSqlServerProvider(database.GetConnectionString());
        await AssertScopeStorageUpgradeAsync(provider, usePostgreSql: false);
    }

    private static async Task AssertScopeStorageUpgradeAsync(ServiceProvider provider, bool usePostgreSql)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        NotificationsDbContext db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        string[] migrations = db.Database.GetMigrations().ToArray();
        int guardIndex = Array.FindIndex(migrations,
            migration => migration.EndsWith("EnforceNotificationScopeStateStorageGuards", StringComparison.Ordinal));
        Assert.True(guardIndex > 0);
        string predecessor = migrations[guardIndex - 1];
        Assert.EndsWith(usePostgreSql ? "HardenNotificationLifecycles" : "MakeBroadcastRecipientScopesOrdinal", predecessor);
        IMigrator migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(predecessor);
        Assert.Equal(migrations.Take(guardIndex), await db.Database.GetAppliedMigrationsAsync());

        UserNotification notification = CreateNotification("owned-upgrade-user", "owned-upgrade-history");
        NotificationPreference preference = MaintenancePreference("tenant-a");
        await AddNotificationAsync(db, notification);
        await db.SaveChangesAsync();
        db.NotificationPreferences.Add(preference);
        await db.SaveChangesAsync();
        Guid closedOperationId = Guid.CreateVersion7();
        UserNotification closedNotification = UserNotification.Create(
            Guid.CreateVersion7(), "tenant-b", "owned-upgrade-user", "notifications-tests", "closed-upgrade-history", 1,
            "Owned closed history", null, Domain.ValueObjects.NotificationSeverity.Info, Now, Now, "{}").Value;
        await using (NotificationsDbContext closed = MaintenanceTestContext(scope.ServiceProvider, "tenant-b"))
        {
            await AddNotificationAsync(closed, closedNotification);
            await closed.SaveChangesAsync();
            NotificationScopeState state = await closed.NotificationScopeStates.SingleAsync();
            Assert.Equal(NotificationScopeCloseTransition.Completed,
                state.Close(closedOperationId, new string('c', 64), Now.AddDays(1)));
            await closed.SaveChangesAsync();
            Assert.Equal(2, state.Version);
        }

        string openNoop = usePostgreSql
            ? "UPDATE notifications.notification_scope_states SET \"Version\" = \"Version\" WHERE \"ScopeId\" = 'tenant-a'"
            : "UPDATE [notifications].[notification_scope_states] SET [Version] = [Version] WHERE [ScopeId] = N'tenant-a'";
        string closedNoop = usePostgreSql
            ? "UPDATE notifications.notification_scope_states SET \"Version\" = \"Version\" WHERE \"ScopeId\" = 'tenant-b'"
            : "UPDATE [notifications].[notification_scope_states] SET [Version] = [Version] WHERE [ScopeId] = N'tenant-b'";
        Assert.Equal(1, await db.Database.ExecuteSqlRawAsync(openNoop));
        await AssertScopeStateGuardRejectedAsync(db, closedNoop, "closed notification scope state is immutable");
        await AssertLegacyScopeWritesAsync(db, usePostgreSql);

        await migrator.MigrateAsync(migrations[guardIndex]);
        Assert.Equal(migrations.Take(guardIndex + 1), await db.Database.GetAppliedMigrationsAsync());
        await AssertUpgradedScopeDataAsync(scope.ServiceProvider, notification.Id, closedNotification.Id, preference.Id, closedOperationId);
        await AssertScopeStateGuardRejectedAsync(db, openNoop, "notification scope state version must advance by exactly one");
        await AssertScopeStateGuardRejectedAsync(db, closedNoop, "closed notification scope state is immutable");

        await migrator.MigrateAsync(predecessor);
        Assert.Equal(migrations.Take(guardIndex), await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, await db.Database.ExecuteSqlRawAsync(openNoop));
        await AssertLegacyScopeWritesAsync(db, usePostgreSql);
        await AssertScopeStateGuardRejectedAsync(db, closedNoop, "closed notification scope state is immutable");
        await AssertUpgradedScopeDataAsync(scope.ServiceProvider, notification.Id, closedNotification.Id, preference.Id, closedOperationId);

        await migrator.MigrateAsync(migrations[guardIndex]);
        Assert.Equal(migrations.Take(guardIndex + 1), await db.Database.GetAppliedMigrationsAsync());
        await AssertScopeStateGuardRejectedAsync(db, openNoop, "notification scope state version must advance by exactly one");
        await AssertScopeStateGuardRejectedAsync(db,
            usePostgreSql
                ? "INSERT INTO notifications.notification_scope_states (\"ScopeId\", \"Version\", \"IsClosed\") VALUES ('rollback-probe', 0, FALSE)"
                : "INSERT INTO [notifications].[notification_scope_states] ([ScopeId], [Version], [IsClosed]) VALUES (N'rollback-probe', 0, CAST(0 AS bit))",
            "notification scope state version must begin at one");
        await AssertUpgradedScopeDataAsync(scope.ServiceProvider, notification.Id, closedNotification.Id, preference.Id, closedOperationId);
    }

    private static async Task AssertLegacyScopeWritesAsync(NotificationsDbContext db, bool usePostgreSql)
    {
        string[] statements = usePostgreSql
            ? [
                "INSERT INTO notifications.notification_scope_states (\"ScopeId\", \"Version\", \"IsClosed\") VALUES ('rollback-probe', 0, FALSE)",
                "UPDATE notifications.notification_scope_states SET \"Version\" = 5 WHERE \"ScopeId\" = 'rollback-probe'",
                "UPDATE notifications.notification_scope_states SET \"ScopeId\" = 'rollback-renamed' WHERE \"ScopeId\" = 'rollback-probe'",
                "DELETE FROM notifications.notification_scope_states WHERE \"ScopeId\" = 'rollback-renamed'"
            ]
            : [
                "INSERT INTO [notifications].[notification_scope_states] ([ScopeId], [Version], [IsClosed]) VALUES (N'rollback-probe', 0, CAST(0 AS bit))",
                "UPDATE [notifications].[notification_scope_states] SET [Version] = 5 WHERE [ScopeId] = N'rollback-probe'",
                "UPDATE [notifications].[notification_scope_states] SET [ScopeId] = N'rollback-renamed' WHERE [ScopeId] = N'rollback-probe'",
                "DELETE FROM [notifications].[notification_scope_states] WHERE [ScopeId] = N'rollback-renamed'"
            ];
        foreach (string statement in statements)
        {
            Assert.Equal(1, await db.Database.ExecuteSqlRawAsync(statement));
        }
    }

    private static async Task AssertUpgradedScopeDataAsync(
        IServiceProvider provider, Guid openNotificationId, Guid closedNotificationId, Guid preferenceId, Guid closedOperationId)
    {
        await using (NotificationsDbContext open = MaintenanceTestContext(provider, "tenant-a"))
        {
            UserNotification notification = Assert.Single(await open.UserNotifications.ToArrayAsync());
            Assert.Equal(openNotificationId, notification.Id);
            Assert.Null(notification.ReadAtUtc);
            Assert.Equal(preferenceId, Assert.Single(await open.NotificationPreferences.ToArrayAsync()).Id);
            NotificationScopeState state = await open.NotificationScopeStates.SingleAsync();
            Assert.Equal(2, state.Version);
            Assert.False(state.IsClosed);
        }

        await using (NotificationsDbContext closed = MaintenanceTestContext(provider, "tenant-b"))
        {
            UserNotification notification = Assert.Single(await closed.UserNotifications.ToArrayAsync());
            Assert.Equal(closedNotificationId, notification.Id);
            Assert.Null(notification.ReadAtUtc);
            NotificationScopeState state = await closed.NotificationScopeStates.SingleAsync();
            Assert.Equal(2, state.Version);
            Assert.True(state.IsClosed);
            Assert.Equal(closedOperationId, state.CloseOperationId);
            Assert.Equal(new string('c', 64), state.CloseRequestSha256);
            Assert.Equal(Now.AddDays(1), state.ClosedAtUtc);
        }
    }
}
