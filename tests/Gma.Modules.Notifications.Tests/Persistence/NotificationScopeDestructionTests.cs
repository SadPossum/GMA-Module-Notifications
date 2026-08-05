namespace Gma.Modules.Notifications.Tests;

using Gma.Framework.Runtime.Time;
using Gma.Framework.Scoping;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Gma.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using DomainDeliveryStatus = Domain.ValueObjects.NotificationDeliveryStatus;

[Trait("Category", "Unit")]
public sealed class NotificationScopeDestructionTests
{
    private const string ScopeId = "tenant-a";
    private static readonly DateTimeOffset Now =
        new(2026, 8, 4, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Empty_scope_completes_and_exact_replay_returns_receipt()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        NotificationScopeLifecycleService service = CreateService(dbContext);
        NotificationScopeDestroyRequest request = new(
            Id(1),
            ScopeId,
            ExpectedRevision: 0,
            BatchSize: 2);

        NotificationScopeDestroyResult completed = await service
            .DestroyBatchAsync(request, CancellationToken.None);
        NotificationScopeDestroyResult replayed = await service
            .DestroyBatchAsync(request, CancellationToken.None);
        NotificationScopeDestroyResult conflict = await service
            .DestroyBatchAsync(
                request with { BatchSize = 3 },
                CancellationToken.None);

        Assert.Equal(NotificationScopeDestroyStatus.Completed, completed.Status);
        Assert.NotNull(completed.Receipt);
        Assert.Equal(0, completed.Receipt.RemovedRecordCount);
        Assert.Equal(NotificationScopeDestroyStatus.Replayed, replayed.Status);
        Assert.Equal(completed.Receipt, replayed.Receipt);
        Assert.Equal(NotificationScopeDestroyStatus.Conflict, conflict.Status);
        Assert.True((await dbContext.NotificationScopeStates.SingleAsync())
            .IsClosed);
        Assert.Empty(await dbContext.NotificationScopeDestroyOperations
            .ToArrayAsync());
        Assert.Single(await dbContext.NotificationScopeDestroyReceipts
            .ToArrayAsync());
    }

    [Fact]
    public async Task Destruction_resumes_across_bounded_dependency_stages()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        dbContext.NotificationPreferences.AddRange(
            CreatePreference(Id(10), "domain:first"),
            CreatePreference(Id(11), "domain:second"),
            CreatePreference(Id(12), "domain:third"));
        dbContext.NotificationDeliveryRoutes.Add(
            NotificationDeliveryRoute.Create(
                Id(13),
                ScopeId,
                "delivery:email",
                "email-primary",
                "operator-a",
                Now).Value);
        await dbContext.SaveChangesAsync();
        NotificationScopeLifecycleService service = CreateService(dbContext);
        long selectedRevision = (await service.GetSnapshotAsync(
            ScopeId,
            CancellationToken.None)).Revision;
        NotificationScopeDestroyRequest request = new(
            Id(2),
            ScopeId,
            selectedRevision,
            BatchSize: 2);

        NotificationScopeDestroyResult first = await service.DestroyBatchAsync(
            request,
            CancellationToken.None);
        Assert.Equal(NotificationScopeDestroyStatus.InProgress, first.Status);
        Assert.Equal(
            NotificationScopeDestructionStage.Preferences,
            first.Progress!.Stage);
        Assert.Equal(2, first.Progress.RemovedRecordCount);
        Assert.Single(await dbContext.NotificationPreferences.ToArrayAsync());

        NotificationScopeDestroyResult second = await service.DestroyBatchAsync(
            request,
            CancellationToken.None);
        Assert.Equal(NotificationScopeDestroyStatus.InProgress, second.Status);
        Assert.Equal(
            NotificationScopeDestructionStage.DeliveryRoutes,
            second.Progress!.Stage);
        Assert.Empty(await dbContext.NotificationPreferences.ToArrayAsync());

        NotificationScopeDestroyResult third = await service.DestroyBatchAsync(
            request,
            CancellationToken.None);
        Assert.Equal(NotificationScopeDestroyStatus.InProgress, third.Status);
        Assert.Equal(
            NotificationScopeDestructionStage.TagDefinitions,
            third.Progress!.Stage);
        Assert.Empty(await dbContext.NotificationDeliveryRoutes.ToArrayAsync());

        NotificationScopeDestroyResult completed = await service
            .DestroyBatchAsync(request, CancellationToken.None);
        Assert.Equal(NotificationScopeDestroyStatus.Completed, completed.Status);
        Assert.Equal(4, completed.Receipt!.RemovedRecordCount);
        Assert.Equal(3, completed.Receipt.CompletedBatchCount);
        Assert.Equal(first.Progress.ResultingRevision, completed.Receipt.ResultingRevision);
        Assert.Empty(await dbContext.NotificationScopeDestroyOperations
            .ToArrayAsync());
        Assert.Single(await dbContext.NotificationScopeDestroyReceipts
            .ToArrayAsync());
    }

    [Fact]
    public async Task Active_delivery_lease_pauses_before_scope_closure()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        UserNotification notification = UserNotification.Create(
            Id(20),
            ScopeId,
            "user-a",
            "operations",
            "reservation.changed",
            1,
            "Reservation changed",
            body: null,
            NotificationSeverity.Info,
            Now,
            Now,
            "{}").Value;
        NotificationDelivery delivery = NotificationDelivery.CreatePending(
            Id(21),
            ScopeId,
            notification.Id,
            "delivery:web",
            "web-primary",
            Now).Value;
        Assert.True(delivery.Claim(
            "worker-a",
            Now,
            TimeSpan.FromMinutes(5)).IsSuccess);
        dbContext.UserNotifications.Add(notification);
        dbContext.NotificationDeliveries.Add(delivery);
        await dbContext.SaveChangesAsync();
        NotificationScopeLifecycleService service = CreateService(dbContext);
        long selectedRevision = (await service.GetSnapshotAsync(
            ScopeId,
            CancellationToken.None)).Revision;

        NotificationScopeDestroyResult result = await service.DestroyBatchAsync(
            new NotificationScopeDestroyRequest(
                Id(3),
                ScopeId,
                selectedRevision,
                BatchSize: 2),
            CancellationToken.None);

        Assert.Equal(NotificationScopeDestroyStatus.Busy, result.Status);
        Assert.False((await dbContext.NotificationScopeStates.SingleAsync())
            .IsClosed);
        Assert.Empty(await dbContext.NotificationScopeDestroyOperations
            .ToArrayAsync());
        Assert.Equal(
            DomainDeliveryStatus.Processing,
            (await dbContext.NotificationDeliveries.SingleAsync()).Status);
    }

    [Fact]
    public async Task Exact_reference_batch_progress_pauses_before_scope_closure()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        NotificationHistoryReferenceKey reference =
            NotificationHistoryReferenceKey.Create(
                "operations",
                new string('a', 64)).Value;
        NotificationHistoryReferenceState state =
            NotificationHistoryReferenceState.Create(ScopeId, reference).Value;
        Assert.Equal(
            NotificationHistoryReferenceCloseTransition.Completed,
            state.Close(Id(30), new string('b', 64), Now));
        NotificationHistoryBatchCloseOperation progress =
            NotificationHistoryBatchCloseOperation.Create(
                Id(30),
                ScopeId,
                reference,
                new string('b', 64),
                expectedVersion: 0,
                state.Version,
                batchSize: 2,
                Now,
                NotificationHistoryLifecycleLimits.MaximumCloseBatchSize).Value;
        dbContext.NotificationHistoryReferenceStates.Add(state);
        dbContext.NotificationHistoryBatchCloseOperations.Add(progress);
        await dbContext.SaveChangesAsync();
        NotificationScopeLifecycleService service = CreateService(dbContext);
        long selectedRevision = (await service.GetSnapshotAsync(
            ScopeId,
            CancellationToken.None)).Revision;

        NotificationScopeDestroyResult result = await service.DestroyBatchAsync(
            new NotificationScopeDestroyRequest(
                Id(4),
                ScopeId,
                selectedRevision,
                BatchSize: 2),
            CancellationToken.None);

        Assert.Equal(NotificationScopeDestroyStatus.Busy, result.Status);
        Assert.False((await dbContext.NotificationScopeStates.SingleAsync())
            .IsClosed);
    }

    private static NotificationPreference CreatePreference(
        Guid id,
        string tagKey) =>
        NotificationPreference.Create(
            id,
            ScopeId,
            "user-a",
            tagKey,
            enabled: true,
            Now).Value;

    private static NotificationScopeLifecycleService CreateService(
        NotificationsDbContext dbContext) =>
        new(dbContext, new TestScopeContext(), new FixedClock());

    private static NotificationsDbContext CreateDbContext()
    {
        DbContextOptions<NotificationsDbContext> options =
            new DbContextOptionsBuilder<NotificationsDbContext>()
                .UseInMemoryDatabase(
                    $"notification-scope-destroy-{Guid.NewGuid():N}",
                    new InMemoryDatabaseRoot())
                .ConfigureWarnings(warnings => warnings.Ignore(
                    InMemoryEventId.TransactionIgnoredWarning))
                .Options;
        return new NotificationsDbContext(options, new TestScopeContext());
    }

    private static Guid Id(int value) =>
        Guid.Parse($"00000000-0000-0000-0000-{value:000000000000}");

    private sealed class TestScopeContext : IScopeContext
    {
        public bool IsEnabled => true;
        public string? ScopeId => NotificationScopeDestructionTests.ScopeId;
    }

    private sealed class FixedClock : ISystemClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
