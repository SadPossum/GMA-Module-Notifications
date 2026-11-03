namespace Gma.Modules.Notifications.IntegrationTests;

using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.IntegrationTests.Support;
using Gma.Modules.Notifications.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;

public sealed partial class NotificationsRelationalIntegrationTests
{
    [DockerFact]
    public async Task Scope_state_storage_guards_reject_invalid_transitions_on_postgresql()
    {
        await using PostgreSqlContainer database = await StartPostgreSqlAsync("notifications_storage_guards");
        await using ServiceProvider provider = CreateProvider(database.GetConnectionString());
        await MigrateAsync(provider);
        await AssertScopeStateStorageGuardsAsync(provider);
    }

    [DockerFact]
    public async Task Scope_state_storage_guards_reject_invalid_transitions_on_sqlserver()
    {
        await using MsSqlContainer database = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await database.StartAsync();
        await using ServiceProvider provider = CreateSqlServerProvider(database.GetConnectionString());
        await MigrateAsync(provider);
        await AssertScopeStateStorageGuardsAsync(provider);
    }

    private static async Task AssertScopeStateStorageGuardsAsync(
        ServiceProvider provider)
    {
        const string versionScope = "storage-guard-version";
        const string peerScope = "storage-guard-peer";
        bool usePostgreSql;
        string connectionString;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = scope.ServiceProvider
                .GetRequiredService<NotificationsDbContext>();
            usePostgreSql = dbContext.Database.ProviderName?.Contains(
                "Npgsql",
                StringComparison.Ordinal) == true;
            connectionString = dbContext.Database.GetConnectionString() ??
                throw new InvalidOperationException(
                    "Relational guard tests require a connection string.");

            await AssertScopeStateGuardRejectedAsync(
                dbContext,
                usePostgreSql
                    ? """
                      INSERT INTO notifications.notification_scope_states
                          ("ScopeId", "Version", "IsClosed")
                      VALUES ('storage-guard-version', 0, FALSE)
                      """
                    : """
                      INSERT INTO [notifications].[notification_scope_states]
                          ([ScopeId], [Version], [IsClosed])
                      VALUES (N'storage-guard-version', 0, CAST(0 AS bit))
                      """,
                "notification scope state version must begin at one");
            Assert.False(await dbContext.NotificationScopeStates
                .IgnoreQueryFilters()
                .AnyAsync(state => state.ScopeId == versionScope));

            Assert.Equal(
                2,
                await dbContext.Database.ExecuteSqlRawAsync(
                    usePostgreSql
                        ? """
                          INSERT INTO notifications.notification_scope_states
                              ("ScopeId", "Version", "IsClosed")
                          VALUES
                              ('storage-guard-version', 1, FALSE),
                              ('storage-guard-peer', 1, FALSE)
                          """
                        : """
                          INSERT INTO [notifications].[notification_scope_states]
                              ([ScopeId], [Version], [IsClosed])
                          VALUES
                              (N'storage-guard-version', 1, CAST(0 AS bit)),
                              (N'storage-guard-peer', 1, CAST(0 AS bit))
                          """));

            await AssertScopeStateGuardRejectedAsync(
                dbContext,
                usePostgreSql
                    ? """
                      UPDATE notifications.notification_scope_states
                      SET "Version" = CASE
                          WHEN "ScopeId" = 'storage-guard-version'
                              THEN "Version" + 1
                          ELSE "Version"
                      END
                      WHERE "ScopeId" IN
                          ('storage-guard-version', 'storage-guard-peer')
                      """
                    : """
                      UPDATE [notifications].[notification_scope_states]
                      SET [Version] = CASE
                          WHEN [ScopeId] = N'storage-guard-version'
                              THEN [Version] + 1
                          ELSE [Version]
                      END
                      WHERE [ScopeId] IN
                          (N'storage-guard-version', N'storage-guard-peer')
                      """,
                "notification scope state version must advance by exactly one");
            long[] versions = await dbContext.NotificationScopeStates
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(state =>
                    state.ScopeId == versionScope ||
                    state.ScopeId == peerScope)
                .OrderBy(state => state.ScopeId)
                .Select(state => state.Version)
                .ToArrayAsync();
            Assert.Equal([1L, 1L], versions);

            await AssertScopeStateGuardRejectedAsync(
                dbContext,
                usePostgreSql
                    ? """
                      UPDATE notifications.notification_scope_states
                      SET "Version" = "Version" + 2
                      WHERE "ScopeId" = 'storage-guard-version'
                      """
                    : """
                      UPDATE [notifications].[notification_scope_states]
                      SET [Version] = [Version] + 2
                      WHERE [ScopeId] = N'storage-guard-version'
                      """,
                "notification scope state version must advance by exactly one");

            await AssertScopeStateGuardRejectedAsync(
                dbContext,
                usePostgreSql
                    ? """
                      UPDATE notifications.notification_scope_states
                      SET "Version" = "Version" - 1
                      WHERE "ScopeId" = 'storage-guard-version'
                      """
                    : """
                      UPDATE [notifications].[notification_scope_states]
                      SET [Version] = [Version] - 1
                      WHERE [ScopeId] = N'storage-guard-version'
                      """,
                "notification scope state version must advance by exactly one");

            await AssertScopeStateGuardRejectedAsync(
                dbContext,
                usePostgreSql
                    ? """
                      UPDATE notifications.notification_scope_states
                      SET "ScopeId" = 'storage-guard-renamed',
                          "Version" = "Version" + 1
                      WHERE "ScopeId" = 'storage-guard-version'
                      """
                    : """
                      UPDATE [notifications].[notification_scope_states]
                      SET [ScopeId] = N'storage-guard-renamed',
                          [Version] = [Version] + 1
                      WHERE [ScopeId] = N'storage-guard-version'
                      """,
                "notification scope state identity is immutable");
            Assert.False(await dbContext.NotificationScopeStates
                .IgnoreQueryFilters()
                .AnyAsync(state => state.ScopeId == "storage-guard-renamed"));

            await AssertScopeStateGuardRejectedAsync(
                dbContext,
                usePostgreSql
                    ? """
                      DELETE FROM notifications.notification_scope_states
                      WHERE "ScopeId" = 'storage-guard-peer'
                      """
                    : """
                      DELETE FROM [notifications].[notification_scope_states]
                      WHERE [ScopeId] = N'storage-guard-peer'
                      """,
                "notification scope state cannot be deleted");
            Assert.True(await dbContext.NotificationScopeStates
                .IgnoreQueryFilters()
                .AnyAsync(state => state.ScopeId == peerScope));

            Assert.Equal(
                1,
                await dbContext.Database.ExecuteSqlRawAsync(
                    usePostgreSql
                        ? """
                          UPDATE notifications.notification_scope_states
                          SET "Version" = "Version" + 1
                          WHERE "ScopeId" = 'storage-guard-version'
                          """
                        : """
                          UPDATE [notifications].[notification_scope_states]
                          SET [Version] = [Version] + 1
                          WHERE [ScopeId] = N'storage-guard-version'
                          """));
            Assert.Equal(
                2,
                await dbContext.NotificationScopeStates
                    .IgnoreQueryFilters()
                    .Where(state => state.ScopeId == versionScope)
                    .Select(state => state.Version)
                    .SingleAsync());

            if (!usePostgreSql)
            {
                await AssertScopeStateGuardRejectedAsync(
                    dbContext,
                    """
                    UPDATE [notifications].[notification_scope_states]
                    SET [ScopeId] = CASE
                            WHEN [ScopeId] = N'storage-guard-version'
                                THEN N'storage-guard-peer'
                            ELSE N'storage-guard-version'
                        END,
                        [Version] = CASE
                            WHEN [ScopeId] = N'storage-guard-version'
                                THEN 2
                            ELSE 3
                        END
                    WHERE [ScopeId] IN
                        (N'storage-guard-version', N'storage-guard-peer')
                    """,
                    "notification scope state identity is immutable");
                Assert.Equal(
                    2,
                    await dbContext.Database.ExecuteSqlRawAsync(
                        """
                        MERGE [notifications].[notification_scope_states]
                            AS target_state
                        USING (VALUES
                            (N'storage-guard-version', CAST(3 AS bigint)),
                            (N'storage-guard-merge', CAST(1 AS bigint)))
                            AS source_state ([ScopeId], [Version])
                        ON target_state.[ScopeId] = source_state.[ScopeId]
                        WHEN MATCHED THEN
                            UPDATE SET [Version] = source_state.[Version]
                        WHEN NOT MATCHED THEN
                            INSERT ([ScopeId], [Version], [IsClosed])
                            VALUES (
                                source_state.[ScopeId],
                                source_state.[Version],
                                CAST(0 AS bit));
                        """));
                long[] mergeVersions = await dbContext
                    .NotificationScopeStates
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(state =>
                        state.ScopeId == versionScope ||
                        state.ScopeId == "storage-guard-merge")
                    .OrderBy(state => state.ScopeId)
                    .Select(state => state.Version)
                    .ToArrayAsync();
                Assert.Equal([1L, 3L], mergeVersions);
            }
        }

        await using ServiceProvider guardProvider = usePostgreSql
            ? CreateProvider(connectionString, versionScope)
            : CreateSqlServerProvider(connectionString, versionScope);
        long rebasedVersion;
        await using (AsyncServiceScope scope = guardProvider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = scope.ServiceProvider
                .GetRequiredService<NotificationsDbContext>();
            NotificationScopeState state = await dbContext
                .NotificationScopeStates
                .SingleAsync(candidate => candidate.ScopeId == versionScope);
            long selectedVersion = state.Version;
            Assert.Equal(
                1,
                await dbContext.Database.ExecuteSqlRawAsync(
                    usePostgreSql
                        ? """
                          UPDATE notifications.notification_scope_states
                          SET "Version" = "Version" + 1
                          WHERE "ScopeId" = 'storage-guard-version'
                          """
                        : """
                          UPDATE [notifications].[notification_scope_states]
                          SET [Version] = [Version] + 1
                          WHERE [ScopeId] = N'storage-guard-version'
                          """));
            Assert.True(state.RegisterMutation());
            await dbContext.SaveChangesAsync();
            rebasedVersion = selectedVersion + 2;
            Assert.Equal(rebasedVersion, state.Version);
        }

        await using (AsyncServiceScope scope = guardProvider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = scope.ServiceProvider
                .GetRequiredService<NotificationsDbContext>();
            NotificationScopeState state = await dbContext
                .NotificationScopeStates
                .SingleAsync(candidate => candidate.ScopeId == versionScope);
            Assert.Equal(rebasedVersion, state.Version);
            Assert.Equal(
                NotificationScopeCloseTransition.Completed,
                state.Close(
                    StableId(211),
                    new string('f', 64),
                    Now.AddDays(1)));
            await dbContext.SaveChangesAsync();
            Assert.Equal(rebasedVersion + 1, state.Version);
            Assert.True(state.IsClosed);
            Assert.Equal(StableId(211), state.CloseOperationId);
        }

        await using (AsyncServiceScope scope = guardProvider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = scope.ServiceProvider
                .GetRequiredService<NotificationsDbContext>();
            await AssertScopeStateGuardRejectedAsync(
                dbContext,
                usePostgreSql
                    ? """
                      UPDATE notifications.notification_scope_states
                      SET "Version" = "Version" + 1
                      WHERE "ScopeId" = 'storage-guard-version'
                      """
                    : """
                      UPDATE [notifications].[notification_scope_states]
                      SET [Version] = [Version] + 1
                      WHERE [ScopeId] = N'storage-guard-version'
                      """,
                "closed notification scope state is immutable");
            await AssertScopeStateGuardRejectedAsync(
                dbContext,
                usePostgreSql
                    ? """
                      DELETE FROM notifications.notification_scope_states
                      WHERE "ScopeId" = 'storage-guard-version'
                      """
                    : """
                      DELETE FROM [notifications].[notification_scope_states]
                      WHERE [ScopeId] = N'storage-guard-version'
                      """,
                "notification scope state cannot be deleted");

            NotificationScopeState closedState = await dbContext
                .NotificationScopeStates
                .AsNoTracking()
                .SingleAsync(candidate => candidate.ScopeId == versionScope);
            Assert.True(closedState.IsClosed);
            Assert.Equal(rebasedVersion + 1, closedState.Version);
        }
    }

    private static async Task AssertScopeStateGuardRejectedAsync(
        NotificationsDbContext dbContext,
        string sql,
        string expectedMessage)
    {
        Exception exception = await Assert.ThrowsAnyAsync<Exception>(
            () => dbContext.Database.ExecuteSqlRawAsync(sql));
        if (dbContext.Database.ProviderName?.Contains(
                "Npgsql",
                StringComparison.Ordinal) == true)
        {
            PostgresException? postgresException =
                FindException<PostgresException>(exception);
            Assert.NotNull(postgresException);
            Assert.Equal(
                PostgresErrorCodes.RaiseException,
                postgresException.SqlState);
            Assert.Equal(expectedMessage, postgresException.MessageText);
            return;
        }

        SqlException? sqlException = FindException<SqlException>(exception);
        Assert.NotNull(sqlException);
        Assert.Contains(
            sqlException.Errors.Cast<SqlError>(),
            error => error.Number == 51000 &&
                string.Equals(
                    error.Message,
                    expectedMessage,
                    StringComparison.Ordinal));
    }

    private static TException? FindException<TException>(Exception exception)
        where TException : Exception
    {
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current is TException match)
            {
                return match;
            }
        }

        return null;
    }
}
