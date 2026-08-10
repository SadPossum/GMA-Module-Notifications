namespace Gma.Modules.Notifications.IntegrationTests;

using System.Data;
using Gma.Framework.AccessControl;
using Gma.Framework.Messaging;
using Gma.Framework.Messaging.Infrastructure;
using Gma.Framework.Notifications;
using Gma.Framework.Runtime;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Framework.Scoping;
using Gma.Modules.Notifications.Application;
using Gma.Modules.Notifications.Application.Handlers;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Contracts;
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
using ContractHistoryReference = Contracts.NotificationHistoryReference;
using ContractRecipientKind = Contracts.NotificationBroadcastRecipientKind;
using ContractSeverity = Contracts.NotificationSeverity;
using DomainAttemptOutcome = Domain.ValueObjects.NotificationDeliveryAttemptOutcome;
using DomainAudience = Domain.ValueObjects.NotificationBroadcastAudience;
using DomainDeliveryPolicy = Domain.ValueObjects.NotificationDeliveryPolicy;
using DomainDeliveryStatus = Domain.ValueObjects.NotificationDeliveryStatus;
using DomainHistoryReferenceKey = Domain.ValueObjects.NotificationHistoryReferenceKey;
using DomainRecipientKind = Domain.ValueObjects.NotificationBroadcastRecipientKind;
using DomainSeverity = Domain.ValueObjects.NotificationSeverity;
using DomainTagKind = Domain.ValueObjects.NotificationTagKind;
using DomainTagOrigin = Domain.ValueObjects.NotificationTagOrigin;

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
    public async Task Lifecycle_receipts_insert_on_sql_server_trigger_tables()
    {
        await using MsSqlContainer sqlServer = new MsSqlBuilder(
            "mcr.microsoft.com/mssql/server:2022-latest").Build();
        await sqlServer.StartAsync();
        await using ServiceProvider provider = CreateSqlServerProvider(
            sqlServer.GetConnectionString());
        await MigrateAsync(provider);

        ContractHistoryReference atomicReference =
            ContractHistoryReference.FromCanonicalCoordinate(
                "sql-atomic-proof",
                "notifications-sql-lifecycle/v1|tenant-a|atomic");
        NotificationHistoryReferenceSnapshot atomicSnapshot;
        await using (AsyncServiceScope prepareAtomic =
                     provider.CreateAsyncScope())
        {
            atomicSnapshot = await CreateHistoryLifecycle(prepareAtomic, Now)
                .EnsureOpenAsync(
                    "tenant-a",
                    atomicReference,
                    CancellationToken.None);
        }

        await using (AsyncServiceScope closeAtomic = provider.CreateAsyncScope())
        {
            NotificationHistoryReferenceCloseResult result =
                await CreateHistoryLifecycle(closeAtomic, Now.AddMinutes(1))
                    .CloseAsync(
                        new NotificationHistoryReferenceCloseRequest(
                            StableId(200),
                            "tenant-a",
                            atomicReference,
                            atomicSnapshot.Version,
                            MaximumRecords: 1),
                        CancellationToken.None);
            Assert.Equal(
                NotificationHistoryReferenceCloseStatus.Completed,
                result.Status);
        }

        ContractHistoryReference batchReference =
            ContractHistoryReference.FromCanonicalCoordinate(
                "sql-batch-proof",
                "notifications-sql-lifecycle/v1|tenant-a|batch");
        NotificationHistoryReferenceSnapshot batchSnapshot;
        await using (AsyncServiceScope prepareBatch =
                     provider.CreateAsyncScope())
        {
            batchSnapshot = await CreateHistoryLifecycle(prepareBatch, Now)
                .EnsureOpenAsync(
                    "tenant-a",
                    batchReference,
                    CancellationToken.None);
        }

        await using (AsyncServiceScope closeBatch = provider.CreateAsyncScope())
        {
            NotificationHistoryReferenceCloseBatchResult result =
                await CreateHistoryLifecycle(closeBatch, Now.AddMinutes(2))
                    .CloseBatchAsync(
                        new NotificationHistoryReferenceCloseBatchRequest(
                            StableId(201),
                            "tenant-a",
                            batchReference,
                            batchSnapshot.Version,
                            BatchSize: 1),
                        CancellationToken.None);
            Assert.Equal(
                NotificationHistoryReferenceCloseBatchStatus.Completed,
                result.Status);
        }

        long selectedRevision;
        await using (AsyncServiceScope selectScope =
                     provider.CreateAsyncScope())
        {
            NotificationScopeLifecycleService lifecycle = new(
                selectScope.ServiceProvider
                    .GetRequiredService<NotificationsDbContext>(),
                selectScope.ServiceProvider.GetRequiredService<IScopeContext>(),
                new FixedClock(Now.AddMinutes(3)));
            selectedRevision = (await lifecycle.GetSnapshotAsync(
                "tenant-a",
                CancellationToken.None)).Revision;
        }

        await using (AsyncServiceScope destroyScope =
                     provider.CreateAsyncScope())
        {
            NotificationScopeLifecycleService lifecycle = new(
                destroyScope.ServiceProvider
                    .GetRequiredService<NotificationsDbContext>(),
                destroyScope.ServiceProvider.GetRequiredService<IScopeContext>(),
                new FixedClock(Now.AddMinutes(4)));
            NotificationScopeDestroyResult result = await lifecycle
                .DestroyBatchAsync(
                    new NotificationScopeDestroyRequest(
                        StableId(202),
                        "tenant-a",
                        selectedRevision,
                        BatchSize: 1),
                    CancellationToken.None);
            Assert.Equal(NotificationScopeDestroyStatus.Completed, result.Status);
        }

        await using AsyncServiceScope assertionScope =
            provider.CreateAsyncScope();
        NotificationsDbContext assertionDb = assertionScope.ServiceProvider
            .GetRequiredService<NotificationsDbContext>();
        Assert.Single(await assertionDb.NotificationHistoryCloseReceipts
            .ToArrayAsync());
        Assert.Single(await assertionDb.NotificationHistoryBatchCloseReceipts
            .ToArrayAsync());
        Assert.Single(await assertionDb.NotificationScopeDestroyReceipts
            .ToArrayAsync());
    }

    [DockerFact]
    public async Task Scope_state_fences_bulk_cleanup_and_delivery_work_on_postgresql()
    {
        await using PostgreSqlContainer postgreSql =
            await StartPostgreSqlAsync("notifications_scope_state_tests");
        await using ServiceProvider provider =
            CreateProvider(postgreSql.GetConnectionString());
        await MigrateAsync(provider);
        DateTimeOffset old = Now.AddDays(-400);
        UserNotification expired = CreateNotification(
            "scope-state-user",
            "scope-state-expired",
            old);
        UserNotification pending = CreateNotification(
            "scope-state-delivery-user",
            "scope-state-pending-delivery");
        NotificationDelivery pendingDelivery =
            NotificationDelivery.CreatePending(
                Guid.CreateVersion7(),
                "tenant-a",
                pending.Id,
                NotificationTags.Email,
                TrackingDeliverySink.Provider,
                Now).Value;
        NotificationBroadcast expiredBroadcast =
            NotificationBroadcast.Create(
                Guid.CreateVersion7(),
                "tenant-a",
                DomainAudience.TenantUsers,
                "notifications-tests",
                "scope-state-broadcast",
                1,
                "Scope state broadcast",
                null,
                DomainSeverity.Info,
                old,
                old,
                "{}").Value;
        await using (AsyncServiceScope seedScope =
                     provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = seedScope.ServiceProvider
                .GetRequiredService<NotificationsDbContext>();
            await AddNotificationAsync(dbContext, expired);
            await AddNotificationAsync(dbContext, pending);
            dbContext.NotificationDeliveries.Add(pendingDelivery);
            dbContext.NotificationBroadcasts.Add(expiredBroadcast);
            await dbContext.SaveChangesAsync();
        }

        long version = await ScopeVersionAsync(provider);
        await using (AsyncServiceScope historyScope =
                     provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = historyScope.ServiceProvider
                .GetRequiredService<NotificationsDbContext>();
            await using var transaction = await dbContext.Database
                .BeginTransactionAsync(IsolationLevel.Serializable);
            int updated = await new NotificationHistoryRepository(dbContext)
                .MarkAllReadAsync(
                    AccessSubject.User("scope-state-user"),
                    "tenant-a",
                    old.AddMinutes(1),
                    CancellationToken.None);
            await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
            Assert.Equal(1, updated);
        }

        Assert.True(await ScopeVersionAsync(provider) > version);
        version = await ScopeVersionAsync(provider);
        NotificationBroadcastRecipientContext recipient =
            NotificationBroadcastRecipientContext.Create(
                "tenant-a",
                ContractRecipientKind.User,
                "scope-state-user").Value;
        await using (AsyncServiceScope broadcastScope =
                     provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = broadcastScope.ServiceProvider
                .GetRequiredService<NotificationsDbContext>();
            await using var transaction = await dbContext.Database
                .BeginTransactionAsync(IsolationLevel.Serializable);
            Assert.True(await new NotificationBroadcastRepository(
                    dbContext,
                    new TestIdGenerator())
                .MarkReadAsync(
                    expiredBroadcast.Id,
                    recipient,
                    old.AddMinutes(1),
                    CancellationToken.None));
            await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        Assert.True(await ScopeVersionAsync(provider) > version);
        version = await ScopeVersionAsync(provider);
        Guid inboxEventId = Guid.CreateVersion7();
        await using (AsyncServiceScope inboxScope =
                     provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = inboxScope.ServiceProvider
                .GetRequiredService<NotificationsDbContext>();
            NotificationsInboxStore inbox = new(
                dbContext,
                new FixedClock(Now),
                new TestIdGenerator());
            InboxProcessResult processed = await inbox.ProcessAsync(
                new InboxMessageRecord(
                    inboxEventId,
                    "scope-state-projection",
                    "gma.notifications.scope-state.v1",
                    "scope-state",
                    1,
                    "tenant-a",
                    Now),
                _ => Task.CompletedTask,
                CancellationToken.None);
            Assert.Equal(InboxProcessStatus.Processed, processed.Status);
            Assert.Equal(
                1,
                await inbox.DeleteProcessedBeforeAsync(
                    Now.AddMinutes(1),
                    100,
                    CancellationToken.None));
        }

        Assert.True(await ScopeVersionAsync(provider) > version);
        version = await ScopeVersionAsync(provider);
        await using (AsyncServiceScope retentionScope =
                     provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = retentionScope.ServiceProvider
                .GetRequiredService<NotificationsDbContext>();
            Assert.Equal(
                1,
                await NotificationRetentionService
                    .DeleteExpiredNotificationsBatchAsync(
                        dbContext,
                        Now.AddDays(-90),
                        Now.AddDays(-365),
                        100,
                        CancellationToken.None));
            Assert.Equal(
                1,
                await NotificationRetentionService
                    .DeleteExpiredBroadcastReadsBatchAsync(
                        dbContext,
                        Now.AddDays(-90),
                        100,
                        CancellationToken.None));
            Assert.Equal(
                1,
                await NotificationRetentionService
                    .DeleteExpiredBroadcastsBatchAsync(
                        dbContext,
                        Now.AddDays(-90),
                        100,
                        CancellationToken.None));
        }

        Assert.True(await ScopeVersionAsync(provider) > version);
        Guid retainedInboxEventId = Guid.CreateVersion7();
        await using (AsyncServiceScope retainedInboxScope =
                     provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = retainedInboxScope.ServiceProvider
                .GetRequiredService<NotificationsDbContext>();
            NotificationsInboxStore inbox = new(
                dbContext,
                new FixedClock(Now.AddMinutes(1)),
                new TestIdGenerator());
            Assert.Equal(
                InboxProcessStatus.Processed,
                (await inbox.ProcessAsync(
                    new InboxMessageRecord(
                        retainedInboxEventId,
                        "scope-state-projection",
                        "gma.notifications.scope-state.v1",
                        "scope-state",
                        1,
                        "tenant-a",
                        Now.AddMinutes(1)),
                    _ => Task.CompletedTask,
                    CancellationToken.None)).Status);
        }

        await using (AsyncServiceScope closeScope =
                     provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = closeScope.ServiceProvider
                .GetRequiredService<NotificationsDbContext>();
            NotificationScopeState state =
                await dbContext.NotificationScopeStates.SingleAsync();
            Assert.Equal(
                NotificationScopeCloseTransition.Completed,
                state.Close(
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    new string('a', 64),
                    Now.AddMinutes(2)));
            await dbContext.SaveChangesAsync();
        }

        await using (AsyncServiceScope terminalScope =
                     provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = terminalScope.ServiceProvider
                .GetRequiredService<NotificationsDbContext>();
            NotificationsInboxStore inbox = new(
                dbContext,
                new FixedClock(Now.AddMinutes(3)),
                new TestIdGenerator());
            Assert.Equal(
                0,
                await inbox.DeleteProcessedBeforeAsync(
                    Now.AddMinutes(4),
                    100,
                    CancellationToken.None));
            InboxProcessResult suppressed = await inbox.ProcessAsync(
                new InboxMessageRecord(
                    Guid.CreateVersion7(),
                    "scope-state-projection",
                    "gma.notifications.scope-state.v1",
                    "scope-state",
                    1,
                    "tenant-a",
                    Now.AddMinutes(3)),
                _ => throw new InvalidOperationException(
                    "Closed-scope handler must not run."),
                CancellationToken.None);
            Assert.Equal(InboxProcessStatus.Suppressed, suppressed.Status);

            await using var transaction = await dbContext.Database
                .BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.ThrowsAsync<NotificationScopeClosedException>(
                () => new NotificationHistoryRepository(dbContext)
                    .MarkAllReadAsync(
                        AccessSubject.User("scope-state-delivery-user"),
                        "tenant-a",
                        Now.AddMinutes(3),
                        CancellationToken.None));
            await transaction.RollbackAsync();
        }

        NotificationDeliveryService worker = CreateWorker(
            provider,
            "scope-state-worker",
            Now.AddMinutes(3),
            new TrackingDeliverySink(),
            batchSize: 1,
            maxConcurrency: 1);
        Assert.Empty(await worker.ClaimAsync(1, CancellationToken.None));
        await using AsyncServiceScope assertionScope =
            provider.CreateAsyncScope();
        NotificationsDbContext assertionDb = assertionScope.ServiceProvider
            .GetRequiredService<NotificationsDbContext>();
        Assert.True((await assertionDb.NotificationScopeStates.SingleAsync())
            .IsClosed);
        InboxMessage retainedInbox =
            await assertionDb.InboxMessages.SingleAsync();
        Assert.Equal(retainedInboxEventId, retainedInbox.Id);
        Assert.Equal(
            DomainDeliveryStatus.Pending,
            (await assertionDb.NotificationDeliveries.SingleAsync()).Status);
        Assert.Null((await assertionDb.UserNotifications
            .SingleAsync(notification => notification.Id == pending.Id))
            .ReadAtUtc);
    }

    [DockerFact]
    public async Task Scope_export_uses_stable_guid_and_reference_cursors_on_postgresql()
    {
        await using PostgreSqlContainer postgreSql =
            await StartPostgreSqlAsync("notifications_scope_export_tests");
        await using ServiceProvider provider =
            CreateProvider(postgreSql.GetConnectionString());
        await MigrateAsync(provider);

        await using (AsyncServiceScope seedScope = provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = seedScope.ServiceProvider
                .GetRequiredService<NotificationsDbContext>();
            dbContext.NotificationPreferences.AddRange(
                NotificationPreference.Create(
                    StableId(1),
                    "tenant-a",
                    "user-a",
                    "domain:first",
                    enabled: true,
                    Now).Value,
                NotificationPreference.Create(
                    StableId(2),
                    "tenant-a",
                    "user-a",
                    "domain:second",
                    enabled: true,
                    Now).Value,
                NotificationPreference.Create(
                    StableId(3),
                    "tenant-a",
                    "user-a",
                    "domain:third",
                    enabled: true,
                    Now).Value);
            foreach ((string referenceNamespace, char digest) in new[]
                     {
                         ("first", '1'),
                         ("second", '2'),
                         ("third", '3')
                     })
            {
                NotificationHistoryReferenceState state =
                    NotificationHistoryReferenceState.Create(
                        "tenant-a",
                        DomainHistoryReferenceKey.Create(
                            referenceNamespace,
                            new string(digest, 64)).Value).Value;
                Assert.True(state.EnsureOpen());
                dbContext.NotificationHistoryReferenceStates.Add(state);
            }

            await dbContext.SaveChangesAsync();
        }

        await using AsyncServiceScope exportScope = provider.CreateAsyncScope();
        NotificationsDbContext exportDb = exportScope.ServiceProvider
            .GetRequiredService<NotificationsDbContext>();
        NotificationScopeLifecycleService lifecycle = new(
            exportDb,
            exportScope.ServiceProvider.GetRequiredService<IScopeContext>(),
            new FixedClock(Now));
        NotificationScopeSnapshot selected = await lifecycle.GetSnapshotAsync(
            "tenant-a",
            CancellationToken.None);
        NotificationScopeExportPage firstPreferences = await lifecycle
            .ExportAsync(
                new NotificationScopeExportRequest(
                    "tenant-a",
                    selected.Revision,
                    NotificationScopeExportStore.Preferences,
                    AfterCursor: null,
                    PageSize: 2),
                CancellationToken.None);
        NotificationScopeExportPage remainingPreferences = await lifecycle
            .ExportAsync(
                new NotificationScopeExportRequest(
                    "tenant-a",
                    selected.Revision,
                    NotificationScopeExportStore.Preferences,
                    firstPreferences.NextCursor,
                    PageSize: 2),
                CancellationToken.None);
        NotificationScopeExportPage firstReferences = await lifecycle
            .ExportAsync(
                new NotificationScopeExportRequest(
                    "tenant-a",
                    selected.Revision,
                    NotificationScopeExportStore.HistoryReferenceStates,
                    AfterCursor: null,
                    PageSize: 2),
                CancellationToken.None);
        NotificationScopeExportPage remainingReferences = await lifecycle
            .ExportAsync(
                new NotificationScopeExportRequest(
                    "tenant-a",
                    selected.Revision,
                    NotificationScopeExportStore.HistoryReferenceStates,
                    firstReferences.NextCursor,
                    PageSize: 2),
                CancellationToken.None);

        Assert.Equal(NotificationScopeStatus.Open, selected.Status);
        Assert.True(firstPreferences.HasMore);
        Assert.Equal(
            [StableId(1), StableId(2)],
            firstPreferences.Records
                .Cast<NotificationScopePreferenceExportRecord>()
                .Select(record => record.PreferenceId));
        Assert.Equal(
            StableId(3),
            Assert.Single(remainingPreferences.Records
                .Cast<NotificationScopePreferenceExportRecord>())
                .PreferenceId);
        Assert.True(firstReferences.HasMore);
        Assert.Equal(
            ["first", "second"],
            firstReferences.Records
                .Cast<NotificationScopeHistoryReferenceStateExportRecord>()
                .Select(record => record.Reference.Namespace));
        Assert.Equal(
            "third",
            Assert.Single(remainingReferences.Records
                .Cast<NotificationScopeHistoryReferenceStateExportRecord>())
                .Reference.Namespace);

        await using (AsyncServiceScope mutationScope =
                     provider.CreateAsyncScope())
        {
            NotificationsDbContext mutationDb = mutationScope.ServiceProvider
                .GetRequiredService<NotificationsDbContext>();
            NotificationPreference preference = await mutationDb
                .NotificationPreferences
                .SingleAsync(candidate => candidate.Id == StableId(1));
            preference.SetEnabled(enabled: false, Now.AddMinutes(1));
            await mutationDb.SaveChangesAsync();
        }

        NotificationScopeExportPage stale = await lifecycle.ExportAsync(
            new NotificationScopeExportRequest(
                "tenant-a",
                selected.Revision,
                NotificationScopeExportStore.Preferences,
                AfterCursor: null,
                PageSize: 2),
            CancellationToken.None);
        Assert.Equal(NotificationScopeExportStatus.Stale, stale.Status);
        Assert.Empty(stale.Records);
    }

    [DockerFact]
    public async Task Scope_destruction_resumes_and_retains_only_terminal_proof_on_postgresql()
    {
        await using PostgreSqlContainer postgreSql =
            await StartPostgreSqlAsync("notifications_scope_destroy_tests");
        await using ServiceProvider provider =
            CreateProvider(postgreSql.GetConnectionString());
        await MigrateAsync(provider);
        Guid inboxId = StableId(50);
        NotificationBroadcast tenantBroadcast = NotificationBroadcast.Create(
            StableId(51),
            "tenant-a",
            DomainAudience.TenantUsers,
            "notifications-tests",
            "tenant-notice",
            1,
            "Tenant notice",
            body: null,
            DomainSeverity.Info,
            Now,
            Now,
            "{}").Value;
        NotificationBroadcast globalBroadcast = NotificationBroadcast.Create(
            StableId(52),
            scopeId: null,
            DomainAudience.PlatformUsers,
            "notifications-tests",
            "platform-notice",
            1,
            "Platform notice",
            body: null,
            DomainSeverity.Info,
            Now,
            Now,
            "{}").Value;
        UserNotification notification = UserNotification.Create(
            StableId(53),
            "tenant-a",
            "scope-destroy-user",
            "notifications-tests",
            "scope-destroy-notice",
            1,
            "Scope destruction notice",
            body: null,
            DomainSeverity.Info,
            Now,
            Now,
            "{}",
            [NotificationTags.Web],
            DomainDeliveryPolicy.RespectPreferences,
            isInboxVisible: true).Value;
        NotificationDelivery delivery = NotificationDelivery.CreateDelivered(
            StableId(54),
            "tenant-a",
            notification.Id,
            NotificationTags.Web,
            "web-primary",
            Now).Value;
        NotificationDeliveryAttempt attempt =
            NotificationDeliveryAttempt.Create(
                StableId(55),
                "tenant-a",
                delivery.Id,
                1,
                "web-primary",
                DomainAttemptOutcome.Delivered,
                Now,
                Now.AddSeconds(1),
                code: null,
                providerMessageId: "provider-proof").Value;

        await using (AsyncServiceScope seedScope = provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = seedScope.ServiceProvider
                .GetRequiredService<NotificationsDbContext>();
            NotificationsInboxStore inbox = new(
                dbContext,
                new FixedClock(Now),
                new TestIdGenerator());
            Assert.Equal(
                InboxProcessStatus.Processed,
                (await inbox.ProcessAsync(
                    new InboxMessageRecord(
                        inboxId,
                        "scope-destroy-projection",
                        "gma.notifications.scope-destroy.v1",
                        "scope-destroy",
                        1,
                        "tenant-a",
                        Now),
                    _ => Task.CompletedTask,
                    CancellationToken.None)).Status);

            await AddNotificationAsync(dbContext, notification);
            dbContext.NotificationDeliveries.Add(delivery);
            dbContext.NotificationDeliveryAttempts.Add(attempt);
            dbContext.NotificationPreferences.Add(
                NotificationPreference.Create(
                    StableId(56),
                    "tenant-a",
                    "scope-destroy-user",
                    "domain:operations",
                    enabled: true,
                    Now).Value);
            dbContext.NotificationDeliveryRoutes.Add(
                NotificationDeliveryRoute.Create(
                    StableId(57),
                    "tenant-a",
                    NotificationTags.Web,
                    "web-primary",
                    "operator-a",
                    Now).Value);
            dbContext.NotificationTagDefinitions.Add(
                NotificationTagDefinition.Create(
                    StableId(58),
                    "tenant-a",
                    "domain:operations",
                    DomainTagKind.Domain,
                    "Operations",
                    "Operational notifications.",
                    DomainTagOrigin.Operator,
                    "notifications-tests",
                    "operator-a",
                    Now).Value);
            dbContext.NotificationBroadcasts.AddRange(
                tenantBroadcast,
                globalBroadcast);
            dbContext.NotificationBroadcastReads.AddRange(
                NotificationBroadcastRead.Create(
                    StableId(59),
                    tenantBroadcast.Id,
                    "tenant-a",
                    DomainRecipientKind.User,
                    "scope-destroy-user",
                    Now).Value,
                NotificationBroadcastRead.Create(
                    StableId(60),
                    globalBroadcast.Id,
                    scopeId: null,
                    DomainRecipientKind.User,
                    "scope-destroy-user",
                    Now).Value);

            DomainHistoryReferenceKey atomicReference =
                DomainHistoryReferenceKey.Create(
                    "atomic-proof",
                    new string('a', 64)).Value;
            NotificationHistoryReferenceState atomicState =
                NotificationHistoryReferenceState.Create(
                    "tenant-a",
                    atomicReference).Value;
            Assert.Equal(
                NotificationHistoryReferenceCloseTransition.Completed,
                atomicState.Close(
                    StableId(61),
                    new string('b', 64),
                    Now));
            dbContext.NotificationHistoryReferenceStates.Add(atomicState);
            dbContext.NotificationHistoryCloseReceipts.Add(
                NotificationHistoryCloseReceipt.Create(
                    StableId(61),
                    "tenant-a",
                    atomicReference,
                    new string('b', 64),
                    atomicState.Version,
                    removedRecordCount: 0,
                    new string('c', 64),
                    Now).Value);

            DomainHistoryReferenceKey batchReference =
                DomainHistoryReferenceKey.Create(
                    "batch-proof",
                    new string('d', 64)).Value;
            NotificationHistoryReferenceState batchState =
                NotificationHistoryReferenceState.Create(
                    "tenant-a",
                    batchReference).Value;
            Assert.Equal(
                NotificationHistoryReferenceCloseTransition.Completed,
                batchState.Close(
                    StableId(62),
                    new string('e', 64),
                    Now));
            NotificationHistoryBatchCloseOperation completedBatch =
                NotificationHistoryBatchCloseOperation.Create(
                    StableId(62),
                    "tenant-a",
                    batchReference,
                    new string('e', 64),
                    expectedVersion: 0,
                    batchState.Version,
                    batchSize: 1,
                    Now,
                    NotificationHistoryLifecycleLimits.MaximumCloseBatchSize)
                .Value;
            dbContext.NotificationHistoryReferenceStates.Add(batchState);
            dbContext.NotificationHistoryBatchCloseReceipts.Add(
                NotificationHistoryBatchCloseReceipt.Create(
                    completedBatch,
                    Now).Value);
            await dbContext.SaveChangesAsync();
        }

        long selectedRevision;
        await using (AsyncServiceScope selectScope =
                     provider.CreateAsyncScope())
        {
            NotificationScopeLifecycleService lifecycle = new(
                selectScope.ServiceProvider
                    .GetRequiredService<NotificationsDbContext>(),
                selectScope.ServiceProvider.GetRequiredService<IScopeContext>(),
                new FixedClock(Now.AddMinutes(1)));
            selectedRevision = (await lifecycle.GetSnapshotAsync(
                "tenant-a",
                CancellationToken.None)).Revision;
        }

        NotificationScopeDestroyRequest request = new(
            StableId(63),
            "tenant-a",
            selectedRevision,
            BatchSize: 1);
        NotificationScopeDestroyResult? completed = null;
        for (int call = 0; call < 20; call++)
        {
            await using AsyncServiceScope destroyScope =
                provider.CreateAsyncScope();
            NotificationScopeLifecycleService lifecycle = new(
                destroyScope.ServiceProvider
                    .GetRequiredService<NotificationsDbContext>(),
                destroyScope.ServiceProvider
                    .GetRequiredService<IScopeContext>(),
                new FixedClock(Now.AddMinutes(1 + call)));
            NotificationScopeDestroyResult result = await lifecycle
                .DestroyBatchAsync(request, CancellationToken.None);
            if (result.Status == NotificationScopeDestroyStatus.Completed)
            {
                completed = result;
                break;
            }

            Assert.Equal(NotificationScopeDestroyStatus.InProgress, result.Status);
            Assert.NotNull(result.Progress);
        }

        Assert.NotNull(completed);
        Assert.Equal(7, completed.Receipt!.RemovedRecordCount);
        Assert.Equal(7, completed.Receipt.CompletedBatchCount);

        await using AsyncServiceScope assertionScope =
            provider.CreateAsyncScope();
        NotificationsDbContext assertionDb = assertionScope.ServiceProvider
            .GetRequiredService<NotificationsDbContext>();
        Assert.Empty(await assertionDb.InboxMessages.ToArrayAsync());
        Assert.Empty(await assertionDb.UserNotifications.ToArrayAsync());
        Assert.Empty(await assertionDb.UserNotificationTags.ToArrayAsync());
        Assert.Empty(await assertionDb.UserNotificationReferences.ToArrayAsync());
        Assert.Empty(await assertionDb.NotificationDeliveries.ToArrayAsync());
        Assert.Empty(await assertionDb.NotificationDeliveryAttempts.ToArrayAsync());
        Assert.Empty(await assertionDb.NotificationPreferences.ToArrayAsync());
        Assert.Empty(await assertionDb.NotificationDeliveryRoutes.ToArrayAsync());
        Assert.Empty(await assertionDb.NotificationTagDefinitions.ToArrayAsync());
        Assert.Empty(await assertionDb.NotificationBroadcasts
            .Where(broadcast => broadcast.ScopeId == "tenant-a")
            .ToArrayAsync());
        Assert.Empty(await assertionDb.NotificationBroadcastReads
            .Where(read => read.RecipientScope == "tenant:tenant-a")
            .ToArrayAsync());
        Assert.Equal(
            globalBroadcast.Id,
            (await assertionDb.NotificationBroadcasts.SingleAsync()).Id);
        Assert.Equal(
            StableId(60),
            (await assertionDb.NotificationBroadcastReads.SingleAsync()).Id);
        Assert.True((await assertionDb.NotificationScopeStates.SingleAsync())
            .IsClosed);
        Assert.Empty(await assertionDb.NotificationScopeDestroyOperations
            .ToArrayAsync());
        Assert.Single(await assertionDb.NotificationScopeDestroyReceipts
            .ToArrayAsync());
        Assert.Equal(
            3,
            await assertionDb.NotificationHistoryReferenceStates.CountAsync());
        Assert.Single(await assertionDb.NotificationHistoryCloseReceipts
            .ToArrayAsync());
        Assert.Single(await assertionDb.NotificationHistoryBatchCloseReceipts
            .ToArrayAsync());

        NotificationScopeLifecycleService replayLifecycle = new(
            assertionDb,
            assertionScope.ServiceProvider.GetRequiredService<IScopeContext>(),
            new FixedClock(Now.AddHours(1)));
        NotificationScopeDestroyResult replay = await replayLifecycle
            .DestroyBatchAsync(request, CancellationToken.None);
        Assert.Equal(NotificationScopeDestroyStatus.Replayed, replay.Status);
        Assert.Equal(completed.Receipt, replay.Receipt);

        PostgresException scopeReceiptMutation =
            await Assert.ThrowsAsync<PostgresException>(
                () => assertionDb.Database.ExecuteSqlRawAsync(
                    """
                    UPDATE notifications.notification_scope_destroy_receipts
                    SET "RemovedRecordCount" = "RemovedRecordCount"
                    """));
        Assert.Contains("append-only", scopeReceiptMutation.MessageText);

        PostgresException atomicReceiptMutation =
            await Assert.ThrowsAsync<PostgresException>(
                () => assertionDb.Database.ExecuteSqlRawAsync(
                    """
                    UPDATE notifications.notification_history_close_receipts
                    SET "RemovedRecordCount" = "RemovedRecordCount"
                    """));
        Assert.Contains("append-only", atomicReceiptMutation.MessageText);

        PostgresException batchReceiptMutation =
            await Assert.ThrowsAsync<PostgresException>(
                () => assertionDb.Database.ExecuteSqlRawAsync(
                    """
                    UPDATE notifications.notification_history_batch_close_receipts
                    SET "RemovedRecordCount" = "RemovedRecordCount"
                    """));
        Assert.Contains("append-only", batchReceiptMutation.MessageText);

        PostgresException scopeReopen =
            await Assert.ThrowsAsync<PostgresException>(
                () => assertionDb.Database.ExecuteSqlRawAsync(
                    """
                    UPDATE notifications.notification_scope_states
                    SET "IsClosed" = FALSE
                    WHERE "ScopeId" = 'tenant-a'
                    """));
        Assert.Contains("immutable", scopeReopen.MessageText);

        PostgresException referenceReopen =
            await Assert.ThrowsAsync<PostgresException>(
                () => assertionDb.Database.ExecuteSqlRawAsync(
                    """
                    UPDATE notifications.notification_history_reference_states
                    SET "IsClosed" = FALSE
                    WHERE "ScopeId" = 'tenant-a'
                      AND "Namespace" = 'atomic-proof'
                    """));
        Assert.Contains("immutable", referenceReopen.MessageText);
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
                new Contracts
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

    [DockerFact]
    public async Task Notification_history_batch_close_resumes_and_seals_its_receipt()
    {
        await using PostgreSqlContainer postgreSql =
            await StartPostgreSqlAsync("notifications_batch_close_tests");
        await using ServiceProvider provider =
            CreateProvider(postgreSql.GetConnectionString());
        await MigrateAsync(provider);

        ContractHistoryReference reference =
            ContractHistoryReference.FromCanonicalCoordinate(
                "account-history-test",
                "notifications-history-test/v1|tenant-a|account|00000000000000000000000000000001");
        NotificationHistoryReferenceSnapshot prepared;
        await using (AsyncServiceScope prepareScope = provider.CreateAsyncScope())
        {
            prepared = await CreateHistoryLifecycle(prepareScope, Now)
                .EnsureOpenAsync("tenant-a", reference, CancellationToken.None);
        }

        Assert.Equal(1, prepared.Version);
        await using (AsyncServiceScope seedScope = provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = seedScope.ServiceProvider
                .GetRequiredService<NotificationsDbContext>();
            foreach (int sequence in Enumerable.Range(1, 3))
            {
                UserNotification notification = CreateNotification(
                    "batch-history-user",
                    $"batch-history-{sequence}",
                    Now.AddMinutes(sequence),
                    reference);
                await AddNotificationAsync(dbContext, notification);
            }

            await dbContext.SaveChangesAsync();
        }

        NotificationHistoryReferenceSnapshot projected;
        await using (AsyncServiceScope snapshotScope = provider.CreateAsyncScope())
        {
            projected = await CreateHistoryLifecycle(snapshotScope, Now)
                .GetSnapshotAsync("tenant-a", reference, CancellationToken.None);
        }

        Assert.Equal(NotificationHistoryReferenceStatus.Open, projected.Status);
        Assert.Equal(4, projected.Version);
        Assert.Equal(3, projected.RecordCount);

        NotificationHistoryReferenceCloseBatchRequest request = new(
            Guid.CreateVersion7(),
            "tenant-a",
            reference,
            projected.Version,
            BatchSize: 2);
        NotificationHistoryReferenceCloseBatchProgress progress;
        await using (AsyncServiceScope firstBatchScope = provider.CreateAsyncScope())
        {
            NotificationHistoryReferenceCloseBatchResult first =
                await CreateHistoryLifecycle(firstBatchScope, Now.AddMinutes(5))
                    .CloseBatchAsync(request, CancellationToken.None);
            Assert.Equal(
                NotificationHistoryReferenceCloseBatchStatus.InProgress,
                first.Status);
            Assert.Null(first.Receipt);
            progress = Assert.IsType<NotificationHistoryReferenceCloseBatchProgress>(
                first.Progress);
            Assert.Equal(5, progress.ResultingVersion);
            Assert.Equal(2, progress.RemovedRecordCount);
            Assert.Equal(1, progress.CompletedBatchCount);
        }

        await using (AsyncServiceScope progressScope = provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = progressScope.ServiceProvider
                .GetRequiredService<NotificationsDbContext>();
            Assert.Equal(1, await dbContext.UserNotifications.CountAsync());
            Assert.Equal(
                1,
                await dbContext.NotificationHistoryBatchCloseOperations.CountAsync());
            Assert.Equal(
                0,
                await dbContext.NotificationHistoryBatchCloseReceipts.CountAsync());

            NotificationHistoryReferenceSnapshot closed =
                await CreateHistoryLifecycle(progressScope, Now.AddMinutes(6))
                    .GetSnapshotAsync(
                        "tenant-a",
                        reference,
                        CancellationToken.None);
            Assert.Equal(NotificationHistoryReferenceStatus.Closed, closed.Status);
            Assert.Equal(progress.ResultingVersion, closed.Version);
            Assert.Equal(1, closed.RecordCount);

            UserNotification late = CreateNotification(
                "batch-history-user",
                "late-batch-history",
                Now.AddMinutes(6),
                reference);
            NotificationHistoryLifecycleRepository repository = new(dbContext);
            Assert.False(
                await repository.RegisterAsync(late, CancellationToken.None));
        }

        NotificationHistoryReferenceCloseBatchReceipt receipt;
        await using (AsyncServiceScope secondBatchScope = provider.CreateAsyncScope())
        {
            NotificationHistoryReferenceCloseBatchResult completed =
                await CreateHistoryLifecycle(secondBatchScope, Now.AddMinutes(7))
                    .CloseBatchAsync(request, CancellationToken.None);
            Assert.Equal(
                NotificationHistoryReferenceCloseBatchStatus.Completed,
                completed.Status);
            Assert.Null(completed.Progress);
            receipt = Assert.IsType<NotificationHistoryReferenceCloseBatchReceipt>(
                completed.Receipt);
            Assert.Equal(5, receipt.ResultingVersion);
            Assert.Equal(3, receipt.RemovedRecordCount);
            Assert.Equal(2, receipt.CompletedBatchCount);
            Assert.Equal(64, receipt.RemovalProofSha256.Length);
        }

        await using (AsyncServiceScope completionScope = provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = completionScope.ServiceProvider
                .GetRequiredService<NotificationsDbContext>();
            Assert.Equal(0, await dbContext.UserNotifications.CountAsync());
            Assert.Equal(
                0,
                await dbContext.NotificationHistoryBatchCloseOperations.CountAsync());
            Assert.Equal(
                1,
                await dbContext.NotificationHistoryBatchCloseReceipts.CountAsync());

            NotificationHistoryReferenceCloseBatchResult replayed =
                await CreateHistoryLifecycle(completionScope, Now.AddMinutes(8))
                    .CloseBatchAsync(request, CancellationToken.None);
            Assert.Equal(
                NotificationHistoryReferenceCloseBatchStatus.Replayed,
                replayed.Status);
            Assert.Equal(receipt, replayed.Receipt);

            NotificationHistoryReferenceCloseBatchResult conflict =
                await CreateHistoryLifecycle(completionScope, Now.AddMinutes(8))
                    .CloseBatchAsync(
                        request with { BatchSize = 3 },
                        CancellationToken.None);
            Assert.Equal(
                NotificationHistoryReferenceCloseBatchStatus.Conflict,
                conflict.Status);
        }

        await using (AsyncServiceScope mutationScope = provider.CreateAsyncScope())
        {
            NotificationsDbContext dbContext = mutationScope.ServiceProvider
                .GetRequiredService<NotificationsDbContext>();
            PostgresException mutation = await Assert.ThrowsAsync<PostgresException>(
                () => dbContext.Database.ExecuteSqlRawAsync(
                    """
                    UPDATE notifications.notification_history_batch_close_receipts
                    SET "RemovedRecordCount" = "RemovedRecordCount"
                    """));
            Assert.Contains("append-only", mutation.MessageText);
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

    private static Guid StableId(int value) =>
        Guid.Parse($"00000000-0000-0000-0000-{value:000000000000}");

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

    private static async Task<long> ScopeVersionAsync(
        ServiceProvider provider)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<NotificationsDbContext>()
            .NotificationScopeStates
            .Select(state => state.Version)
            .SingleAsync();
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
        int lifecycleMigrationIndex = Array.FindIndex(
            migrations,
            migration => migration.EndsWith(
                "AddNotificationHistoryLifecycle",
                StringComparison.Ordinal));
        Assert.True(lifecycleMigrationIndex > 0);
        IMigrator migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[lifecycleMigrationIndex - 1]);

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
            Domain.ValueObjects.NotificationDeliveryPolicy
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
