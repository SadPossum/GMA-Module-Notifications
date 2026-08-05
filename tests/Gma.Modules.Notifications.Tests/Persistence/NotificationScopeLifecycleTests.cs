namespace Gma.Modules.Notifications.Tests;

using Gma.Framework.Scoping;
using Gma.Framework.Runtime.Time;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Gma.Modules.Notifications.Persistence;
using Gma.Modules.Notifications.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using DomainAttemptOutcome = Domain.ValueObjects.NotificationDeliveryAttemptOutcome;
using DomainAudience = Domain.ValueObjects.NotificationBroadcastAudience;
using DomainDeliveryPolicy = Domain.ValueObjects.NotificationDeliveryPolicy;
using DomainSeverity = Domain.ValueObjects.NotificationSeverity;
using DomainTagKind = Domain.ValueObjects.NotificationTagKind;
using DomainTagOrigin = Domain.ValueObjects.NotificationTagOrigin;

[Trait("Category", "Unit")]
public sealed class NotificationScopeLifecycleTests
{
    private const string ScopeId = "tenant-a";
    private static readonly DateTimeOffset Now =
        new(2026, 8, 4, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Snapshot_distinguishes_missing_and_unavailable_scope()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        NotificationScopeLifecycleService service = new(
            dbContext,
            new TestScopeContext(ScopeId),
            new FixedClock());

        NotificationScopeSnapshot missing = await service.GetSnapshotAsync(
            ScopeId,
            CancellationToken.None);
        NotificationScopeSnapshot unavailable = await service.GetSnapshotAsync(
            "tenant-b",
            CancellationToken.None);

        Assert.Equal(NotificationScopeStatus.Missing, missing.Status);
        Assert.Equal(0, missing.Revision);
        Assert.Equal(
            NotificationScopeStatus.ScopeUnavailable,
            unavailable.Status);
    }

    [Fact]
    public async Task Export_uses_stable_bounded_pages_at_one_scope_revision()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        NotificationHistoryLifecycleRepository references = new(dbContext);
        foreach (Guid id in new[] { Id(1), Id(2), Id(3) })
        {
            UserNotification notification = CreateNotification(id);
            Assert.True(await references.RegisterAsync(
                notification,
                CancellationToken.None));
            dbContext.UserNotifications.Add(notification);
        }

        await dbContext.SaveChangesAsync();
        NotificationScopeLifecycleService service = new(
            dbContext,
            new TestScopeContext(ScopeId),
            new FixedClock());
        NotificationScopeSnapshot selected = await service.GetSnapshotAsync(
            ScopeId,
            CancellationToken.None);

        NotificationScopeExportPage first = await service.ExportAsync(
            Request(
                selected.Revision,
                NotificationScopeExportStore.UserNotifications,
                pageSize: 2),
            CancellationToken.None);
        NotificationScopeExportPage second = await service.ExportAsync(
            Request(
                selected.Revision,
                NotificationScopeExportStore.UserNotifications,
                pageSize: 2,
                first.NextCursor),
            CancellationToken.None);

        Assert.Equal(NotificationScopeStatus.Open, selected.Status);
        Assert.Equal(NotificationScopeExportStatus.Completed, first.Status);
        Assert.True(first.HasMore);
        Assert.NotNull(first.NextCursor);
        Assert.Equal(
            [Id(1), Id(2)],
            first.Records
                .Cast<NotificationScopeUserNotificationExportRecord>()
                .Select(record => record.NotificationId));
        Assert.Equal(NotificationScopeExportStatus.Completed, second.Status);
        Assert.False(second.HasMore);
        Assert.NotNull(second.NextCursor);
        Assert.NotEqual(first.NextCursor, second.NextCursor);
        Assert.Equal(
            Id(3),
            Assert.Single(second.Records
                .Cast<NotificationScopeUserNotificationExportRecord>())
                .NotificationId);

        NotificationScopeExportPage stale = await service.ExportAsync(
            Request(
                selected.Revision - 1,
                NotificationScopeExportStore.UserNotifications,
                pageSize: 2),
            CancellationToken.None);
        NotificationScopeExportPage malformed = await service.ExportAsync(
            Request(
                selected.Revision,
                NotificationScopeExportStore.UserNotifications,
                pageSize: 2,
                "not-a-cursor"),
            CancellationToken.None);

        Assert.Equal(NotificationScopeExportStatus.Stale, stale.Status);
        Assert.Empty(stale.Records);
        Assert.Equal(NotificationScopeExportStatus.Invalid, malformed.Status);
        Assert.Empty(malformed.Records);
    }

    [Fact]
    public async Task Export_maps_every_portable_store_to_its_typed_record()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        await SeedEveryStoreAsync(dbContext);
        NotificationScopeLifecycleService service = new(
            dbContext,
            new TestScopeContext(ScopeId),
            new FixedClock());
        NotificationScopeSnapshot selected = await service.GetSnapshotAsync(
            ScopeId,
            CancellationToken.None);
        Dictionary<NotificationScopeExportStore, Type> expectedTypes = new()
        {
            [NotificationScopeExportStore.UserNotifications] =
                typeof(NotificationScopeUserNotificationExportRecord),
            [NotificationScopeExportStore.Preferences] =
                typeof(NotificationScopePreferenceExportRecord),
            [NotificationScopeExportStore.DeliveryRoutes] =
                typeof(NotificationScopeDeliveryRouteExportRecord),
            [NotificationScopeExportStore.TagDefinitions] =
                typeof(NotificationScopeTagDefinitionExportRecord),
            [NotificationScopeExportStore.Deliveries] =
                typeof(NotificationScopeDeliveryExportRecord),
            [NotificationScopeExportStore.DeliveryAttempts] =
                typeof(NotificationScopeDeliveryAttemptExportRecord),
            [NotificationScopeExportStore.TenantBroadcasts] =
                typeof(NotificationScopeBroadcastExportRecord),
            [NotificationScopeExportStore.TenantBroadcastReads] =
                typeof(NotificationScopeBroadcastReadExportRecord),
            [NotificationScopeExportStore.HistoryReferenceStates] =
                typeof(NotificationScopeHistoryReferenceStateExportRecord),
            [NotificationScopeExportStore.HistoryCloseReceipts] =
                typeof(NotificationScopeHistoryCloseReceiptExportRecord),
            [NotificationScopeExportStore.HistoryBatchCloseOperations] =
                typeof(NotificationScopeHistoryBatchCloseOperationExportRecord),
            [NotificationScopeExportStore.HistoryBatchCloseReceipts] =
                typeof(NotificationScopeHistoryBatchCloseReceiptExportRecord)
        };

        foreach ((NotificationScopeExportStore store, Type expectedType) in
                 expectedTypes)
        {
            NotificationScopeExportPage page = await service.ExportAsync(
                Request(selected.Revision, store),
                CancellationToken.None);

            Assert.Equal(NotificationScopeExportStatus.Completed, page.Status);
            Assert.Equal(selected.Revision, page.ScopeRevision);
            Assert.Equal(store, page.Store);
            Assert.NotEmpty(page.Records);
            Assert.All(page.Records, record =>
                Assert.Equal(expectedType, record.GetType()));
        }

        NotificationScopeUserNotificationExportRecord notification =
            Assert.IsType<NotificationScopeUserNotificationExportRecord>(
                Assert.Single((await service.ExportAsync(
                    Request(
                        selected.Revision,
                        NotificationScopeExportStore.UserNotifications),
                    CancellationToken.None)).Records));
        Assert.Equal("opaque", notification.Payload.GetProperty("kind").GetString());
        Assert.Equal("delivery:web", Assert.Single(notification.Tags));
        Assert.NotEmpty(notification.References);
    }

    [Fact]
    public async Task Tenant_export_excludes_global_broadcasts_and_reads()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        NotificationBroadcast tenant = CreateBroadcast(
            Id(30),
            ScopeId,
            DomainAudience.TenantUsers);
        NotificationBroadcast global = CreateBroadcast(
            Id(31),
            scopeId: null,
            DomainAudience.PlatformUsers);
        NotificationBroadcastRead tenantRead = NotificationBroadcastRead.Create(
            Id(32),
            tenant.Id,
            ScopeId,
            Domain.ValueObjects.NotificationBroadcastRecipientKind.User,
            "user-a",
            Now).Value;
        NotificationBroadcastRead globalRead = NotificationBroadcastRead.Create(
            Id(33),
            global.Id,
            scopeId: null,
            Domain.ValueObjects.NotificationBroadcastRecipientKind.User,
            "user-a",
            Now).Value;
        dbContext.NotificationBroadcasts.AddRange(tenant, global);
        dbContext.NotificationBroadcastReads.AddRange(tenantRead, globalRead);
        await dbContext.SaveChangesAsync();

        NotificationScopeLifecycleService service = new(
            dbContext,
            new TestScopeContext(ScopeId),
            new FixedClock());
        NotificationScopeSnapshot selected = await service.GetSnapshotAsync(
            ScopeId,
            CancellationToken.None);
        NotificationScopeExportPage broadcasts = await service.ExportAsync(
            Request(
                selected.Revision,
                NotificationScopeExportStore.TenantBroadcasts),
            CancellationToken.None);
        NotificationScopeExportPage reads = await service.ExportAsync(
            Request(
                selected.Revision,
                NotificationScopeExportStore.TenantBroadcastReads),
            CancellationToken.None);

        Assert.Equal(
            tenant.Id,
            Assert.IsType<NotificationScopeBroadcastExportRecord>(
                Assert.Single(broadcasts.Records)).BroadcastId);
        Assert.Equal(
            tenantRead.Id,
            Assert.IsType<NotificationScopeBroadcastReadExportRecord>(
                Assert.Single(reads.Records)).ReadId);
    }

    [Fact]
    public async Task Closed_scope_cannot_be_exported()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        dbContext.NotificationPreferences.Add(NotificationPreference.Create(
            Id(40),
            ScopeId,
            "user-a",
            "domain:operations",
            enabled: true,
            Now).Value);
        await dbContext.SaveChangesAsync();
        NotificationScopeState state = await dbContext.NotificationScopeStates
            .SingleAsync();
        Assert.Equal(
            NotificationScopeCloseTransition.Completed,
            state.Close(Id(41), new string('a', 64), Now.AddMinutes(1)));
        await dbContext.SaveChangesAsync();

        NotificationScopeLifecycleService service = new(
            dbContext,
            new TestScopeContext(ScopeId),
            new FixedClock());
        NotificationScopeSnapshot snapshot = await service.GetSnapshotAsync(
            ScopeId,
            CancellationToken.None);
        NotificationScopeExportPage page = await service.ExportAsync(
            Request(
                snapshot.Revision,
                NotificationScopeExportStore.Preferences),
            CancellationToken.None);

        Assert.Equal(NotificationScopeStatus.Closed, snapshot.Status);
        Assert.Equal(NotificationScopeExportStatus.Closed, page.Status);
        Assert.Empty(page.Records);
    }

    private static async Task SeedEveryStoreAsync(
        NotificationsDbContext dbContext)
    {
        NotificationHistoryReferenceKey producerReference = Reference(
            "operations",
            '1');
        UserNotification notification = CreateNotification(
            Id(10),
            [producerReference]);
        NotificationHistoryLifecycleRepository references = new(dbContext);
        Assert.True(await references.RegisterAsync(
            notification,
            CancellationToken.None));

        NotificationPreference preference = NotificationPreference.Create(
            Id(11),
            ScopeId,
            "user-a",
            "domain:operations",
            enabled: true,
            Now).Value;
        NotificationDeliveryRoute route = NotificationDeliveryRoute.Create(
            Id(12),
            ScopeId,
            "delivery:email",
            "email-primary",
            "operator-a",
            Now).Value;
        NotificationTagDefinition definition = NotificationTagDefinition.Create(
            Id(13),
            ScopeId,
            "domain:operations",
            DomainTagKind.Domain,
            "Operations",
            "Operational notifications.",
            DomainTagOrigin.Operator,
            "operations",
            "operator-a",
            Now).Value;
        NotificationDelivery delivery = NotificationDelivery.CreatePending(
            Id(14),
            ScopeId,
            notification.Id,
            "delivery:web",
            "web-primary",
            Now).Value;
        NotificationDeliveryAttempt attempt = NotificationDeliveryAttempt.Create(
            Id(15),
            ScopeId,
            delivery.Id,
            1,
            "web-primary",
            DomainAttemptOutcome.Retry,
            Now,
            Now.AddSeconds(1),
            "temporary",
            providerMessageId: null).Value;
        NotificationBroadcast broadcast = CreateBroadcast(
            Id(16),
            ScopeId,
            DomainAudience.TenantUsers);
        NotificationBroadcastRead read = NotificationBroadcastRead.Create(
            Id(17),
            broadcast.Id,
            ScopeId,
            Domain.ValueObjects.NotificationBroadcastRecipientKind.User,
            "user-a",
            Now).Value;

        NotificationHistoryReferenceKey atomicReference = Reference(
            "atomic-proof",
            '2');
        NotificationHistoryReferenceState atomicState =
            NotificationHistoryReferenceState.Create(
                ScopeId,
                atomicReference).Value;
        Assert.Equal(
            NotificationHistoryReferenceCloseTransition.Completed,
            atomicState.Close(Id(18), new string('2', 64), Now));
        NotificationHistoryCloseReceipt atomicReceipt =
            NotificationHistoryCloseReceipt.Create(
                Id(18),
                ScopeId,
                atomicReference,
                new string('2', 64),
                atomicState.Version,
                1,
                new string('3', 64),
                Now).Value;

        NotificationHistoryReferenceKey progressReference = Reference(
            "batch-progress",
            '4');
        NotificationHistoryReferenceState progressState =
            NotificationHistoryReferenceState.Create(
                ScopeId,
                progressReference).Value;
        Assert.Equal(
            NotificationHistoryReferenceCloseTransition.Completed,
            progressState.Close(Id(19), new string('4', 64), Now));
        NotificationHistoryBatchCloseOperation progress =
            NotificationHistoryBatchCloseOperation.Create(
                Id(19),
                ScopeId,
                progressReference,
                new string('4', 64),
                expectedVersion: 0,
                progressState.Version,
                batchSize: 100,
                Now,
                NotificationHistoryLifecycleLimits.MaximumCloseBatchSize).Value;

        NotificationHistoryReferenceKey receiptReference = Reference(
            "batch-receipt",
            '5');
        NotificationHistoryReferenceState receiptState =
            NotificationHistoryReferenceState.Create(
                ScopeId,
                receiptReference).Value;
        Assert.Equal(
            NotificationHistoryReferenceCloseTransition.Completed,
            receiptState.Close(Id(20), new string('5', 64), Now));
        NotificationHistoryBatchCloseOperation completedOperation =
            NotificationHistoryBatchCloseOperation.Create(
                Id(20),
                ScopeId,
                receiptReference,
                new string('5', 64),
                expectedVersion: 0,
                receiptState.Version,
                batchSize: 100,
                Now,
                NotificationHistoryLifecycleLimits.MaximumCloseBatchSize).Value;
        Assert.True(completedOperation.RecordBatch(
            1,
            new string('6', 64),
            Now.AddSeconds(1)));
        NotificationHistoryBatchCloseReceipt batchReceipt =
            NotificationHistoryBatchCloseReceipt.Create(
                completedOperation,
                Now.AddSeconds(2)).Value;

        dbContext.AddRange(
            notification,
            preference,
            route,
            definition,
            delivery,
            attempt,
            broadcast,
            read,
            atomicState,
            atomicReceipt,
            progressState,
            progress,
            receiptState,
            batchReceipt);
        await dbContext.SaveChangesAsync();
    }

    private static NotificationScopeExportRequest Request(
        long revision,
        NotificationScopeExportStore store,
        int pageSize = NotificationScopeLifecycleLimits.MaximumPageSize,
        string? afterCursor = null) =>
        new(ScopeId, revision, store, afterCursor, pageSize);

    private static UserNotification CreateNotification(
        Guid id,
        IReadOnlyCollection<NotificationHistoryReferenceKey>? references = null)
    {
        UserNotification notification = UserNotification.Create(
            id,
            ScopeId,
            "user-a",
            "operations",
            "reservation.changed",
            1,
            "Reservation changed",
            "The reservation changed.",
            DomainSeverity.Info,
            Now,
            Now,
            "{\"kind\":\"opaque\"}",
            ["delivery:web"],
            DomainDeliveryPolicy.RespectPreferences,
            isInboxVisible: true,
            references ?? []).Value;
        typeof(UserNotification)
            .GetProperty(nameof(UserNotification.StreamSequence))!
            .SetValue(notification, id.ToByteArray()[15]);
        return notification;
    }

    private static NotificationBroadcast CreateBroadcast(
        Guid id,
        string? scopeId,
        DomainAudience audience) =>
        NotificationBroadcast.Create(
            id,
            scopeId,
            audience,
            "operations",
            "workspace.notice",
            1,
            "Workspace notice",
            body: null,
            DomainSeverity.Info,
            Now,
            Now,
            "{\"kind\":\"opaque\"}").Value;

    private static NotificationHistoryReferenceKey Reference(
        string referenceNamespace,
        char digestCharacter) =>
        NotificationHistoryReferenceKey.Create(
            referenceNamespace,
            new string(digestCharacter, 64)).Value;

    private static NotificationsDbContext CreateDbContext()
    {
        DbContextOptions<NotificationsDbContext> options =
            new DbContextOptionsBuilder<NotificationsDbContext>()
                .UseInMemoryDatabase(
                    $"notification-scope-export-{Guid.NewGuid():N}",
                    new InMemoryDatabaseRoot())
                .ConfigureWarnings(warnings => warnings.Ignore(
                    InMemoryEventId.TransactionIgnoredWarning))
                .Options;
        return new NotificationsDbContext(
            options,
            new TestScopeContext(ScopeId));
    }

    private static Guid Id(int value) =>
        Guid.Parse($"00000000-0000-0000-0000-{value:000000000000}");

    private sealed class TestScopeContext(string scopeId) : IScopeContext
    {
        public bool IsEnabled => true;
        public string? ScopeId => scopeId;
    }

    private sealed class FixedClock : ISystemClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
