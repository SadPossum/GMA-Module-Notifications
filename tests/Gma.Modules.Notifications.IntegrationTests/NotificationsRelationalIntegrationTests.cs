namespace Gma.Modules.Notifications.IntegrationTests;

using System.Data;
using Gma.Framework.Notifications;
using Gma.Framework.Runtime;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Framework.Scoping;
using Gma.Modules.Notifications.Application;
using Gma.Modules.Notifications.Application.Handlers;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.IntegrationTests.Support;
using Gma.Modules.Notifications.Persistence;
using Gma.Modules.Notifications.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;
using ContractRecipientKind = Gma.Modules.Notifications.Contracts.NotificationBroadcastRecipientKind;
using ContractHistoryReference = Gma.Modules.Notifications.Contracts.NotificationHistoryReference;
using ContractSeverity = Gma.Modules.Notifications.Contracts.NotificationSeverity;
using DomainHistoryReferenceKey = Gma.Modules.Notifications.Domain.ValueObjects.NotificationHistoryReferenceKey;
using DomainAudience = Gma.Modules.Notifications.Domain.ValueObjects.NotificationBroadcastAudience;
using DomainAttemptOutcome = Gma.Modules.Notifications.Domain.ValueObjects.NotificationDeliveryAttemptOutcome;
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
        await MigrateWithLegacyRecipientBackfillAsync(provider);

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            await AddNotificationAsync(
                dbContext,
                CreateNotification("user-a", "first-user-notification"));
            await AddNotificationAsync(
                dbContext,
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
    public async Task Retention_deletes_only_completed_history_and_advances_reference_versions_on_postgresql()
    {
        await using PostgreSqlContainer postgreSql =
            await StartPostgreSqlAsync("notifications_retention_tests");
        await using ServiceProvider provider =
            CreateProvider(postgreSql.GetConnectionString());
        await MigrateAsync(provider);

        await AssertRetentionLifecycleAsync(provider);
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
        await MigrateWithLegacyRecipientBackfillAsync(provider);
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
        await AssertRetentionLifecycleAsync(provider);
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

    [DockerFact]
    public async Task Notification_history_lifecycle_closes_exact_reference_and_suppresses_replay()
    {
        await using PostgreSqlContainer postgreSql =
            await StartPostgreSqlAsync("notifications_history_lifecycle_tests");
        await using ServiceProvider provider =
            CreateProvider(postgreSql.GetConnectionString());
        await MigrateAsync(provider);

        ContractHistoryReference reference =
            ContractHistoryReference.FromCanonicalCoordinate(
                "reservation-history-test",
                "notifications-history-test/v1|tenant-a|reservation|00000000000000000000000000000001");
        NotificationHistoryReferenceSnapshot prepared;
        await using (AsyncServiceScope prepareScope =
                     provider.CreateAsyncScope())
        {
            NotificationHistoryLifecycleService lifecycle =
                CreateHistoryLifecycle(prepareScope, Now);
            prepared = await lifecycle.EnsureOpenAsync(
                "tenant-a",
                reference,
                CancellationToken.None);
        }

        Assert.Equal(NotificationHistoryReferenceStatus.Open, prepared.Status);
        Assert.Equal(1, prepared.Version);
        Assert.Equal(0, prepared.RecordCount);

        UserNotification notification = CreateNotification(
            "history-user",
            "reservation-changed",
            Now,
            reference);
        NotificationDelivery delivery = NotificationDelivery.CreatePending(
            Guid.CreateVersion7(),
            "tenant-a",
            notification.Id,
            NotificationTags.Email,
            TrackingDeliverySink.Provider,
            Now).Value;
        Assert.True(
            delivery
                .Claim(
                    "history-worker",
                    Now,
                    TimeSpan.FromMinutes(5))
                .IsSuccess);

        await using (AsyncServiceScope seedScope =
                     provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext =
                seedScope.ServiceProvider
                    .GetRequiredService<NotificationsDbContext>();
            NotificationHistoryLifecycleRepository repository = new(
                dbContext);
            Assert.True(
                await repository.RegisterAsync(
                    notification,
                    CancellationToken.None));
            dbContext.UserNotifications.Add(notification);
            dbContext.NotificationDeliveries.Add(delivery);
            await dbContext.SaveChangesAsync();
        }

        NotificationHistoryReferenceSnapshot projected;
        await using (AsyncServiceScope readScope =
                     provider.CreateAsyncScope())
        {
            NotificationHistoryLifecycleService lifecycle =
                CreateHistoryLifecycle(readScope, Now);
            projected = await lifecycle.GetSnapshotAsync(
                "tenant-a",
                reference,
                CancellationToken.None);
            NotificationHistoryReferencePage page =
                await lifecycle.ListAsync(
                    "tenant-a",
                    reference,
                    afterStreamSequence: 0,
                    pageSize: 10,
                    CancellationToken.None);

            Assert.Equal(
                NotificationHistoryReferenceStatus.Open,
                projected.Status);
            Assert.Equal(2, projected.Version);
            Assert.Equal(1, projected.RecordCount);
            Assert.True(projected.LatestStreamSequence > 0);
            Assert.Equal(projected.Version, page.ReferenceVersion);
            NotificationHistoryReferenceRecord record =
                Assert.Single(page.Records);
            Assert.Equal(notification.Id, record.NotificationId);
            Assert.Equal("history-user", record.RecipientId);
            Assert.False(page.HasMore);
        }

        Guid operationId = Guid.CreateVersion7();
        NotificationHistoryReferenceCloseRequest closeRequest = new(
            operationId,
            "tenant-a",
            reference,
            projected.Version,
            MaximumRecords: 10);
        await using (AsyncServiceScope busyScope =
                     provider.CreateAsyncScope())
        {
            NotificationHistoryReferenceCloseResult busy =
                await CreateHistoryLifecycle(busyScope, Now)
                    .CloseAsync(closeRequest, CancellationToken.None);
            Assert.Equal(
                NotificationHistoryReferenceCloseStatus.Busy,
                busy.Status);
            Assert.Null(busy.Receipt);
        }

        await using (AsyncServiceScope releaseScope =
                     provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext =
                releaseScope.ServiceProvider
                    .GetRequiredService<NotificationsDbContext>();
            NotificationDelivery claimed = await dbContext
                .NotificationDeliveries
                .SingleAsync(candidate => candidate.Id == delivery.Id);
            Assert.True(
                claimed
                    .MarkRetry(
                        "history-worker",
                        Now.AddSeconds(1),
                        "retry-before-close",
                        Now.AddMinutes(1))
                    .IsSuccess);
            await dbContext.SaveChangesAsync();
        }

        NotificationHistoryReferenceCloseReceipt completedReceipt;
        await using (AsyncServiceScope closeScope =
                     provider.CreateAsyncScope())
        {
            NotificationHistoryReferenceCloseResult completed =
                await CreateHistoryLifecycle(
                        closeScope,
                        Now.AddSeconds(2))
                    .CloseAsync(closeRequest, CancellationToken.None);
            Assert.Equal(
                NotificationHistoryReferenceCloseStatus.Completed,
                completed.Status);
            completedReceipt = Assert.IsType<
                NotificationHistoryReferenceCloseReceipt>(
                completed.Receipt);
            Assert.Equal(3, completedReceipt.ResultingVersion);
            Assert.Equal(1, completedReceipt.RemovedRecordCount);
            Assert.Equal(64, completedReceipt.RemovedRecordIdsSha256.Length);
        }

        await using (AsyncServiceScope assertionScope =
                     provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext =
                assertionScope.ServiceProvider
                    .GetRequiredService<NotificationsDbContext>();
            Assert.Equal(0, await dbContext.UserNotifications.CountAsync());
            Assert.Equal(
                0,
                await dbContext.NotificationDeliveries.CountAsync());
            Assert.Equal(
                0,
                await dbContext.UserNotificationReferences.CountAsync());
            Assert.Equal(
                1,
                await dbContext.NotificationHistoryCloseReceipts
                    .CountAsync());

            ContractHistoryReference recipientReference =
                ContractHistoryReference.ForRecipient(
                    "tenant-a",
                    "history-user");
            NotificationHistoryReferenceState recipientState =
                await dbContext.NotificationHistoryReferenceStates
                    .SingleAsync(state =>
                        state.ScopeId == "tenant-a" &&
                        state.Namespace ==
                        recipientReference.Namespace &&
                        state.Digest == recipientReference.Digest);
            Assert.False(recipientState.IsClosed);
            Assert.Equal(2, recipientState.Version);
        }

        await using (AsyncServiceScope replayScope =
                     provider.CreateAsyncScope())
        {
            NotificationHistoryLifecycleService lifecycle =
                CreateHistoryLifecycle(
                    replayScope,
                    Now.AddSeconds(3));
            NotificationHistoryReferenceCloseResult replayed =
                await lifecycle.CloseAsync(
                    closeRequest,
                    CancellationToken.None);
            Assert.Equal(
                NotificationHistoryReferenceCloseStatus.Replayed,
                replayed.Status);
            Assert.Equal(completedReceipt, replayed.Receipt);

            NotificationHistoryReferenceCloseResult conflictingReplay =
                await lifecycle.CloseAsync(
                    closeRequest with { MaximumRecords = 11 },
                    CancellationToken.None);
            Assert.Equal(
                NotificationHistoryReferenceCloseStatus.Conflict,
                conflictingReplay.Status);
        }

        UserNotification late = CreateNotification(
            "history-user",
            "late-reservation-change",
            Now.AddMinutes(1),
            reference);
        await using (AsyncServiceScope lateScope =
                     provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext =
                lateScope.ServiceProvider
                    .GetRequiredService<NotificationsDbContext>();
            NotificationHistoryLifecycleRepository repository = new(
                dbContext);
            Assert.False(
                await repository.RegisterAsync(
                    late,
                    CancellationToken.None));

            NotificationHistoryReferenceSnapshot closed =
                await CreateHistoryLifecycle(
                        lateScope,
                        Now.AddMinutes(1))
                    .GetSnapshotAsync(
                        "tenant-a",
                        reference,
                        CancellationToken.None);
            Assert.Equal(
                NotificationHistoryReferenceStatus.Closed,
                closed.Status);
            Assert.Equal(
                completedReceipt.ResultingVersion,
                closed.Version);
            Assert.Equal(0, closed.RecordCount);
        }

        ContractHistoryReference racingReference =
            ContractHistoryReference.FromCanonicalCoordinate(
                "reservation-history-test",
                "notifications-history-test/v1|tenant-a|reservation|00000000000000000000000000000002");
        NotificationHistoryReferenceSnapshot racingPrepared;
        await using (AsyncServiceScope prepareRaceScope =
                     provider.CreateAsyncScope())
        {
            racingPrepared =
                await CreateHistoryLifecycle(
                        prepareRaceScope,
                        Now.AddMinutes(2))
                    .EnsureOpenAsync(
                        "tenant-a",
                        racingReference,
                        CancellationToken.None);
        }

        await using (AsyncServiceScope projectionScope =
                     provider.CreateAsyncScope())
        {
            NotificationsDbContext projectionDb =
                projectionScope.ServiceProvider
                    .GetRequiredService<NotificationsDbContext>();
            await using var projectionTransaction =
                await projectionDb.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable);
            UserNotification racingNotification = CreateNotification(
                "history-user",
                "racing-reservation-change",
                Now.AddMinutes(2),
                racingReference);
            NotificationHistoryLifecycleRepository repository = new(
                projectionDb);
            Assert.True(
                await repository.RegisterAsync(
                    racingNotification,
                    CancellationToken.None));
            projectionDb.UserNotifications.Add(racingNotification);

            await using (AsyncServiceScope closeRaceScope =
                         provider.CreateAsyncScope())
            {
                NotificationHistoryReferenceCloseResult closeWon =
                    await CreateHistoryLifecycle(
                            closeRaceScope,
                            Now.AddMinutes(2).AddSeconds(1))
                        .CloseAsync(
                            new NotificationHistoryReferenceCloseRequest(
                                Guid.CreateVersion7(),
                                "tenant-a",
                                racingReference,
                                racingPrepared.Version,
                                MaximumRecords: 10),
                            CancellationToken.None);
                Assert.Equal(
                    NotificationHistoryReferenceCloseStatus.Completed,
                    closeWon.Status);
                Assert.Equal(
                    0,
                    closeWon.Receipt!.RemovedRecordCount);
            }

            Exception projectionFailure =
                await Assert.ThrowsAnyAsync<Exception>(
                    () => projectionDb.SaveChangesAsync());
            Assert.True(
                IsSafeProjectionConcurrencyAbort(projectionFailure),
                $"Unexpected projection race failure: {projectionFailure.GetType().FullName}");
            await projectionTransaction.RollbackAsync();
        }

        await using (AsyncServiceScope raceAssertionScope =
                     provider.CreateAsyncScope())
        {
            NotificationHistoryReferenceSnapshot raceClosed =
                await CreateHistoryLifecycle(
                        raceAssertionScope,
                        Now.AddMinutes(3))
                    .GetSnapshotAsync(
                        "tenant-a",
                        racingReference,
                        CancellationToken.None);
            Assert.Equal(
                NotificationHistoryReferenceStatus.Closed,
                raceClosed.Status);
            Assert.Equal(0, raceClosed.RecordCount);
            Assert.False(
                await raceAssertionScope.ServiceProvider
                    .GetRequiredService<NotificationsDbContext>()
                    .UserNotifications
                    .AnyAsync(notification =>
                        notification.Source.Name ==
                        "racing-reservation-change"));
        }

        Guid legacyNotificationId = Guid.CreateVersion7();
        await using (AsyncServiceScope legacyScope =
                     provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext =
                legacyScope.ServiceProvider
                    .GetRequiredService<NotificationsDbContext>();
            UserNotificationRequestedIntegrationEventHandler handler = new(
                new NotificationHistoryRepository(dbContext),
                new NotificationHistoryLifecycleRepository(dbContext),
                new NotificationRoutingRepository(dbContext),
                new AllowAllNotificationPreferenceEvaluator(),
                new FixedClock(Now.AddMinutes(4)),
                new TestIdGenerator());
            await handler.HandleAsync(
                new Gma.Modules.Notifications.Contracts
                    .UserNotificationRequestedIntegrationEvent(
                        legacyNotificationId,
                        "tenant-a",
                        Now.AddMinutes(4),
                        "legacy-history-user",
                        "legacy-producer",
                        "legacy-notification",
                        1,
                        "Legacy notification",
                        "Legacy notification body",
                        ContractSeverity.Info,
                        "{}"),
                CancellationToken.None);
            await dbContext.SaveChangesAsync();

            Assert.True(
                await dbContext.UserNotifications.AnyAsync(
                    notification =>
                        notification.Id == legacyNotificationId));
            Assert.Equal(
                1,
                await dbContext.UserNotificationReferences.CountAsync(
                    assignment =>
                        assignment.NotificationId ==
                        legacyNotificationId));
        }
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

    private static async Task MigrateWithLegacyRecipientBackfillAsync(
        ServiceProvider provider)
    {
        const string legacyScope = "tenant-backfill";
        const string emptyPayload = "{}";
        string[] legacyRecipients =
        [
            "CaseSensitiveUser",
            "casesensitiveuser"
        ];

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        NotificationsDbContext dbContext = scope.ServiceProvider
            .GetRequiredService<NotificationsDbContext>();
        string[] migrations = dbContext.Database.GetMigrations().ToArray();
        Assert.EndsWith(
            "AddNotificationHistoryLifecycle",
            migrations[^1],
            StringComparison.Ordinal);
        IMigrator migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[^2]);

        foreach (string recipient in legacyRecipients)
        {
            Guid id = Guid.CreateVersion7();
            if (dbContext.Database.ProviderName?.Contains(
                    "Npgsql",
                    StringComparison.Ordinal) == true)
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     INSERT INTO notifications.user_notifications
                         ("Id", "ScopeId", "UserId", "Module", "Name",
                          "Version", "Title", "Body", "Severity",
                          "OccurredAtUtc", "CreatedAtUtc", "ReadAtUtc",
                          "PayloadJson", "DeliveryPolicy", "IsInboxVisible")
                     VALUES
                         ({id}, {legacyScope}, {recipient},
                          'legacy-producer', 'legacy-notification', 1,
                          'Legacy notification', NULL, 'info',
                          {Now}, {Now}, NULL, {emptyPayload},
                          'respect-preferences', TRUE);
                     """);
            }
            else
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     INSERT INTO [notifications].[user_notifications]
                         ([Id], [ScopeId], [UserId], [Module], [Name],
                          [Version], [Title], [Body], [Severity],
                          [OccurredAtUtc], [CreatedAtUtc], [ReadAtUtc],
                          [PayloadJson], [DeliveryPolicy], [IsInboxVisible])
                     VALUES
                         ({id}, {legacyScope}, {recipient},
                          N'legacy-producer', N'legacy-notification', 1,
                          N'Legacy notification', NULL, N'info',
                          {Now}, {Now}, NULL, {emptyPayload},
                          N'respect-preferences', CAST(1 AS bit));
                     """);
            }
        }

        await migrator.MigrateAsync();

        UserNotificationReference[] assignments = await dbContext
            .UserNotificationReferences
            .IgnoreQueryFilters()
            .Where(reference => reference.ScopeId == legacyScope)
            .OrderBy(reference => reference.Digest)
            .ToArrayAsync();
        Assert.Equal(legacyRecipients.Length, assignments.Length);
        Assert.Equal(
            legacyRecipients
                .Select(recipient =>
                    ContractHistoryReference.ForRecipient(
                        legacyScope,
                        recipient).Digest)
                .Order(StringComparer.Ordinal),
            assignments
                .Select(reference => reference.Digest)
                .Order(StringComparer.Ordinal));

        NotificationHistoryReferenceState[] states = await dbContext
            .NotificationHistoryReferenceStates
            .IgnoreQueryFilters()
            .Where(state =>
                state.ScopeId == legacyScope &&
                state.Namespace ==
                ContractHistoryReference.RecipientNamespace)
            .ToArrayAsync();
        Assert.Equal(legacyRecipients.Length, states.Length);
        Assert.All(states, state =>
        {
            Assert.Equal(1, state.Version);
            Assert.False(state.IsClosed);
        });
    }

    private static bool IsSafeProjectionConcurrencyAbort(
        Exception exception) =>
        exception is DbUpdateConcurrencyException ||
        exception is PostgresException
        {
            SqlState: PostgresErrorCodes.SerializationFailure
        } ||
        (exception.InnerException is not null &&
         IsSafeProjectionConcurrencyAbort(exception.InnerException));

    private static NotificationHistoryLifecycleService CreateHistoryLifecycle(
        AsyncServiceScope scope,
        DateTimeOffset nowUtc) =>
        new(
            scope.ServiceProvider
                .GetRequiredService<NotificationsDbContext>(),
            scope.ServiceProvider.GetRequiredService<IScopeContext>(),
            new FixedClock(nowUtc));

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
            await AddNotificationAsync(dbContext, notification);
            dbContext.NotificationDeliveries.Add(delivery);
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task AssertRetentionLifecycleAsync(
        ServiceProvider provider)
    {
        DateTimeOffset old = Now.AddDays(-400);
        UserNotification activeNotification = CreateNotification(
            "active-retention-user",
            "active-retention",
            old);
        UserNotification completedNotification = CreateNotification(
            "completed-retention-user",
            "completed-retention",
            old);
        NotificationDelivery activeDelivery = NotificationDelivery.CreatePending(
            Guid.CreateVersion7(),
            "tenant-a",
            activeNotification.Id,
            NotificationTags.Email,
            TrackingDeliverySink.Provider,
            old).Value;
        NotificationDelivery completedDelivery = NotificationDelivery.CreateDelivered(
            Guid.CreateVersion7(),
            "tenant-a",
            completedNotification.Id,
            NotificationTags.Email,
            TrackingDeliverySink.Provider,
            old).Value;
        NotificationDeliveryAttempt activeAttempt = NotificationDeliveryAttempt.Create(
            Guid.CreateVersion7(),
            "tenant-a",
            activeDelivery.Id,
            1,
            TrackingDeliverySink.Provider,
            DomainAttemptOutcome.Retry,
            old,
            old.AddSeconds(1),
            "rate-limited",
            providerMessageId: null).Value;
        NotificationDeliveryAttempt completedAttempt = NotificationDeliveryAttempt.Create(
            Guid.CreateVersion7(),
            "tenant-a",
            completedDelivery.Id,
            1,
            TrackingDeliverySink.Provider,
            DomainAttemptOutcome.Delivered,
            old,
            old.AddSeconds(1),
            code: null,
            providerMessageId: "provider-message-1").Value;

        await using (AsyncServiceScope seedScope = provider.CreateAsyncScope())
        {
            NotificationsDbContext seed = seedScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
            await AddNotificationAsync(seed, activeNotification);
            await AddNotificationAsync(seed, completedNotification);
            seed.NotificationDeliveries.AddRange(activeDelivery, completedDelivery);
            seed.NotificationDeliveryAttempts.AddRange(activeAttempt, completedAttempt);
            await seed.SaveChangesAsync();
        }

        await using AsyncServiceScope assertionScope = provider.CreateAsyncScope();
        NotificationsDbContext dbContext = assertionScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        UserNotification[] expiredNotifications = await NotificationRetentionService
            .ExpiredUserNotifications(dbContext, Now.AddDays(-90), Now.AddDays(-365))
            .ToArrayAsync();
        NotificationDeliveryAttempt[] expiredAttempts = await NotificationRetentionService
            .ExpiredDeliveryAttempts(dbContext, Now.AddDays(-90))
            .ToArrayAsync();

        Assert.Equal(completedNotification.Id, Assert.Single(expiredNotifications).Id);
        Assert.Equal(completedAttempt.Id, Assert.Single(expiredAttempts).Id);

        ContractHistoryReference activeReference =
            ContractHistoryReference.ForRecipient(
                "tenant-a",
                "active-retention-user");
        ContractHistoryReference completedReference =
            ContractHistoryReference.ForRecipient(
                "tenant-a",
                "completed-retention-user");
        long activeReferenceVersion = await ReferenceVersionAsync(
            dbContext,
            activeReference);
        long completedReferenceVersion = await ReferenceVersionAsync(
            dbContext,
            completedReference);

        int removed = await NotificationRetentionService
            .DeleteExpiredNotificationsBatchAsync(
                dbContext,
                Now.AddDays(-90),
                Now.AddDays(-365),
                batchSize: 100,
                CancellationToken.None);

        Assert.Equal(1, removed);
        dbContext.ChangeTracker.Clear();
        Assert.True(await dbContext.UserNotifications
            .AnyAsync(notification =>
                notification.Id == activeNotification.Id));
        Assert.False(await dbContext.UserNotifications
            .AnyAsync(notification =>
                notification.Id == completedNotification.Id));
        Assert.True(await dbContext.NotificationDeliveries
            .AnyAsync(delivery => delivery.Id == activeDelivery.Id));
        Assert.False(await dbContext.NotificationDeliveries
            .AnyAsync(delivery => delivery.Id == completedDelivery.Id));
        Assert.True(await dbContext.NotificationDeliveryAttempts
            .AnyAsync(attempt => attempt.Id == activeAttempt.Id));
        Assert.False(await dbContext.NotificationDeliveryAttempts
            .AnyAsync(attempt => attempt.Id == completedAttempt.Id));
        Assert.Equal(
            activeReferenceVersion,
            await ReferenceVersionAsync(dbContext, activeReference));
        Assert.Equal(
            completedReferenceVersion + 1,
            await ReferenceVersionAsync(dbContext, completedReference));
    }

    private static Task<long> ReferenceVersionAsync(
        NotificationsDbContext dbContext,
        ContractHistoryReference reference) =>
        dbContext.NotificationHistoryReferenceStates
            .AsNoTracking()
            .Where(state =>
                state.ScopeId == "tenant-a" &&
                state.Namespace == reference.Namespace &&
                state.Digest == reference.Digest)
            .Select(state => state.Version)
            .SingleAsync();

    private static async Task AddNotificationAsync(
        NotificationsDbContext dbContext,
        UserNotification notification)
    {
        NotificationHistoryLifecycleRepository repository = new(dbContext);
        Assert.True(
            await repository.RegisterAsync(
                notification,
                CancellationToken.None));
        dbContext.UserNotifications.Add(notification);
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
        CreateNotification(userId, name, Now);

    private static UserNotification CreateNotification(string userId, string name, DateTimeOffset createdAtUtc) =>
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
            createdAtUtc,
            createdAtUtc,
            "{}").Value;

    private static UserNotification CreateNotification(
        string userId,
        string name,
        DateTimeOffset createdAtUtc,
        ContractHistoryReference reference) =>
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
            createdAtUtc,
            createdAtUtc,
            "{}",
            ["delivery:web", NotificationTags.Email],
            Gma.Modules.Notifications.Domain.ValueObjects.NotificationDeliveryPolicy
                .RespectPreferences,
            isInboxVisible: true,
            [
                DomainHistoryReferenceKey.Create(
                    reference.Namespace,
                    reference.Digest).Value
            ]).Value;

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
