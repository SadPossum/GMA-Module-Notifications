namespace Gma.Modules.Notifications.Tests;

using Gma.Framework.Runtime.Time;
using Gma.Framework.Scoping;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Gma.Modules.Notifications.Persistence;
using Gma.Modules.Notifications.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using DomainDeliveryPolicy =
    Domain.ValueObjects.NotificationDeliveryPolicy;
using DomainSeverity = Domain.ValueObjects.NotificationSeverity;

[Trait("Category", "Unit")]
public sealed class NotificationHistoryLifecycleTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Ensure_open_versions_empty_reference_and_detects_later_registration()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        NotificationHistoryReference reference = Reference(
            "product-subject",
            "product-subject/v1|tenant-a|record-42");
        NotificationHistoryLifecycleService lifecycle =
            new(dbContext, new TestScopeContext(), new FixedClock());

        NotificationHistoryReferenceSnapshot first =
            await lifecycle.EnsureOpenAsync(
                "tenant-a",
                reference,
                CancellationToken.None);
        NotificationHistoryReferenceSnapshot replay =
            await lifecycle.EnsureOpenAsync(
                "tenant-a",
                reference,
                CancellationToken.None);

        Assert.Equal(NotificationHistoryReferenceStatus.Open, first.Status);
        Assert.Equal(1, first.Version);
        Assert.Equal(first, replay);

        UserNotification notification = CreateNotification(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "user-a",
            [ToDomain(reference)],
            1);
        NotificationHistoryLifecycleRepository repository = new(dbContext);
        Assert.True(await repository.RegisterAsync(
            notification,
            CancellationToken.None));
        dbContext.UserNotifications.Add(notification);
        await dbContext.SaveChangesAsync();

        NotificationHistoryReferenceSnapshot changed =
            await lifecycle.GetSnapshotAsync(
                "tenant-a",
                reference,
                CancellationToken.None);
        NotificationHistoryReferenceCloseResult stale =
            await lifecycle.CloseAsync(
                new NotificationHistoryReferenceCloseRequest(
                    Guid.Parse(
                        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    "tenant-a",
                    reference,
                    first.Version,
                    100),
                CancellationToken.None);

        Assert.Equal(2, changed.Version);
        Assert.Equal(1, changed.RecordCount);
        Assert.Equal(
            NotificationHistoryReferenceCloseStatus.Stale,
            stale.Status);
    }

    [Fact]
    public async Task Lifecycle_lists_closes_and_replays_one_exact_reference()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        NotificationHistoryReference subject = Reference(
            "product-subject",
            "product-subject/v1|tenant-a|record-42");
        NotificationHistoryReference companion = Reference(
            "related-resource",
            "related-resource/v1|tenant-a|record-7");
        NotificationHistoryLifecycleRepository repository = new(dbContext);
        UserNotification first = CreateNotification(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "user-a",
            [ToDomain(subject), ToDomain(companion)],
            1);
        UserNotification second = CreateNotification(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "user-a",
            [ToDomain(subject)],
            2);

        Assert.True(await repository.RegisterAsync(
            first,
            CancellationToken.None));
        Assert.True(await repository.RegisterAsync(
            second,
            CancellationToken.None));
        dbContext.UserNotifications.AddRange(first, second);
        await dbContext.SaveChangesAsync();

        NotificationHistoryLifecycleService lifecycle =
            new(dbContext, new TestScopeContext(), new FixedClock());
        NotificationHistoryReferenceSnapshot snapshot =
            await lifecycle.GetSnapshotAsync(
                "tenant-a",
                subject,
                CancellationToken.None);
        NotificationHistoryReferencePage firstPage =
            await lifecycle.ListAsync(
                "tenant-a",
                subject,
                0,
                1,
                CancellationToken.None);

        Assert.Equal(NotificationHistoryReferenceStatus.Open, snapshot.Status);
        Assert.Equal(2, snapshot.Version);
        Assert.Equal(2, snapshot.RecordCount);
        Assert.Single(firstPage.Records);
        Assert.True(firstPage.HasMore);

        NotificationHistoryReferencePage secondPage =
            await lifecycle.ListAsync(
                "tenant-a",
                subject,
                firstPage.NextStreamSequence,
                1,
                CancellationToken.None);
        Assert.Single(secondPage.Records);
        Assert.False(secondPage.HasMore);

        Guid operationId =
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        NotificationHistoryReferenceCloseRequest request = new(
            operationId,
            "tenant-a",
            subject,
            snapshot.Version,
            100);
        NotificationHistoryReferenceCloseResult completed =
            await lifecycle.CloseAsync(
                request,
                CancellationToken.None);
        NotificationHistoryReferenceCloseResult replayed =
            await lifecycle.CloseAsync(
                request,
                CancellationToken.None);

        Assert.Equal(
            NotificationHistoryReferenceCloseStatus.Completed,
            completed.Status);
        Assert.Equal(2, completed.Receipt!.RemovedRecordCount);
        Assert.Equal(
            NotificationHistoryReferenceCloseStatus.Replayed,
            replayed.Status);
        Assert.Equal(completed.Receipt, replayed.Receipt);
        Assert.Empty(await dbContext.UserNotifications.ToArrayAsync());

        NotificationHistoryReferenceSnapshot closed =
            await lifecycle.GetSnapshotAsync(
                "tenant-a",
                subject,
                CancellationToken.None);
        NotificationHistoryReferenceSnapshot companionAfterClose =
            await lifecycle.GetSnapshotAsync(
                "tenant-a",
                companion,
                CancellationToken.None);
        Assert.Equal(
            NotificationHistoryReferenceStatus.Closed,
            closed.Status);
        Assert.Equal(3, closed.Version);
        Assert.Equal(0, closed.RecordCount);
        Assert.Equal(
            NotificationHistoryReferenceStatus.Open,
            companionAfterClose.Status);
        Assert.Equal(2, companionAfterClose.Version);
        Assert.Equal(0, companionAfterClose.RecordCount);

        NotificationHistoryReferenceCloseResult changedReplay =
            await lifecycle.CloseAsync(
                request with { MaximumRecords = 101 },
                CancellationToken.None);
        Assert.Equal(
            NotificationHistoryReferenceCloseStatus.Conflict,
            changedReplay.Status);
    }

    [Fact]
    public async Task Closed_reference_suppresses_the_entire_registration()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        NotificationHistoryReference closedReference = Reference(
            "closed-resource",
            "closed-resource/v1|tenant-a|record-42");
        NotificationHistoryLifecycleService lifecycle =
            new(dbContext, new TestScopeContext(), new FixedClock());
        NotificationHistoryReferenceCloseResult closed =
            await lifecycle.CloseAsync(
                new NotificationHistoryReferenceCloseRequest(
                    Guid.Parse(
                        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    "tenant-a",
                    closedReference,
                    0,
                    100),
                CancellationToken.None);
        Assert.Equal(
            NotificationHistoryReferenceCloseStatus.Completed,
            closed.Status);

        NotificationHistoryReference newReference = Reference(
            "new-resource",
            "new-resource/v1|tenant-a|record-7");
        UserNotification notification = CreateNotification(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "user-a",
            [ToDomain(closedReference), ToDomain(newReference)]);
        NotificationHistoryLifecycleRepository repository = new(dbContext);

        bool registered = await repository.RegisterAsync(
            notification,
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.False(registered);
        Assert.Single(
            await dbContext.NotificationHistoryReferenceStates
                .ToArrayAsync());
        Assert.Equal(
            NotificationHistoryReferenceStatus.Missing,
            (await lifecycle.GetSnapshotAsync(
                "tenant-a",
                newReference,
                CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Close_hides_scope_mismatch_without_mutating_state()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        NotificationHistoryLifecycleService lifecycle =
            new(dbContext, new TestScopeContext(), new FixedClock());

        NotificationHistoryReferenceCloseResult result =
            await lifecycle.CloseAsync(
                new NotificationHistoryReferenceCloseRequest(
                    Guid.Parse(
                        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    "tenant-b",
                    Reference(
                        "product-subject",
                        "product-subject/v1|tenant-b|record-42"),
                    0,
                    100),
                CancellationToken.None);

        Assert.Equal(
            NotificationHistoryReferenceCloseStatus.ScopeUnavailable,
            result.Status);
        Assert.Empty(
            await dbContext.NotificationHistoryReferenceStates
                .ToArrayAsync());
        Assert.Empty(
            await dbContext.NotificationHistoryCloseReceipts
                .ToArrayAsync());
    }

    [Fact]
    public async Task Close_rejects_a_version_that_cannot_advance()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        NotificationHistoryLifecycleService lifecycle =
            new(dbContext, new TestScopeContext(), new FixedClock());

        NotificationHistoryReferenceCloseResult result =
            await lifecycle.CloseAsync(
                new NotificationHistoryReferenceCloseRequest(
                    Guid.Parse(
                        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    "tenant-a",
                    Reference(
                        "product-subject",
                        "product-subject/v1|tenant-a|record-42"),
                    long.MaxValue,
                    100),
                CancellationToken.None);

        Assert.Equal(
            NotificationHistoryReferenceCloseStatus.Invalid,
            result.Status);
        Assert.Empty(
            await dbContext.NotificationHistoryReferenceStates
                .ToArrayAsync());
    }

    [Fact]
    public async Task Batch_close_resumes_with_bounded_progress_and_exact_replay()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        NotificationHistoryReference subject = Reference(
            "product-subject",
            "product-subject/v1|tenant-a|record-42");
        NotificationHistoryReference companion = Reference(
            "related-resource",
            "related-resource/v1|tenant-a|record-7");
        NotificationHistoryLifecycleRepository repository = new(dbContext);
        UserNotification first = CreateNotification(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "user-a",
            [ToDomain(subject), ToDomain(companion)],
            1);
        UserNotification second = CreateNotification(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "user-a",
            [ToDomain(subject)],
            2);
        UserNotification third = CreateNotification(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "user-a",
            [ToDomain(subject)],
            3);
        foreach (UserNotification notification in
                 new[] { first, second, third })
        {
            Assert.True(await repository.RegisterAsync(
                notification,
                CancellationToken.None));
        }

        dbContext.UserNotifications.AddRange(first, second, third);
        await dbContext.SaveChangesAsync();

        NotificationHistoryLifecycleService lifecycle =
            new(dbContext, new TestScopeContext(), new FixedClock());
        NotificationHistoryReferenceSnapshot selected =
            await lifecycle.GetSnapshotAsync(
                "tenant-a",
                subject,
                CancellationToken.None);
        NotificationHistoryReferenceCloseBatchRequest request = new(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "tenant-a",
            subject,
            selected.Version,
            BatchSize: 2);

        NotificationHistoryReferenceCloseBatchResult firstBatch =
            await lifecycle.CloseBatchAsync(
                request,
                CancellationToken.None);

        Assert.Equal(
            NotificationHistoryReferenceCloseBatchStatus.InProgress,
            firstBatch.Status);
        Assert.Equal(2, firstBatch.Progress!.RemovedRecordCount);
        Assert.Equal(1, firstBatch.Progress.CompletedBatchCount);
        Assert.Single(await dbContext.UserNotifications.ToArrayAsync());
        Assert.Single(
            await dbContext.NotificationHistoryBatchCloseOperations
                .ToArrayAsync());
        Assert.Empty(
            await dbContext.NotificationHistoryBatchCloseReceipts
                .ToArrayAsync());

        UserNotification late = CreateNotification(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            "user-a",
            [ToDomain(subject)],
            4);
        Assert.False(await repository.RegisterAsync(
            late,
            CancellationToken.None));

        NotificationHistoryReferenceCloseBatchResult completed =
            await lifecycle.CloseBatchAsync(
                request,
                CancellationToken.None);
        NotificationHistoryReferenceCloseBatchResult replayed =
            await lifecycle.CloseBatchAsync(
                request,
                CancellationToken.None);
        NotificationHistoryReferenceCloseBatchResult changedReplay =
            await lifecycle.CloseBatchAsync(
                request with { BatchSize = 1 },
                CancellationToken.None);

        Assert.Equal(
            NotificationHistoryReferenceCloseBatchStatus.Completed,
            completed.Status);
        Assert.Equal(3, completed.Receipt!.RemovedRecordCount);
        Assert.Equal(2, completed.Receipt.CompletedBatchCount);
        Assert.Equal(
            NotificationHistoryReferenceCloseBatchStatus.Replayed,
            replayed.Status);
        Assert.Equal(completed.Receipt, replayed.Receipt);
        Assert.Equal(
            NotificationHistoryReferenceCloseBatchStatus.Conflict,
            changedReplay.Status);
        Assert.Empty(await dbContext.UserNotifications.ToArrayAsync());
        Assert.Empty(
            await dbContext.NotificationHistoryBatchCloseOperations
                .ToArrayAsync());
        Assert.Single(
            await dbContext.NotificationHistoryBatchCloseReceipts
                .ToArrayAsync());

        NotificationHistoryReferenceSnapshot closed =
            await lifecycle.GetSnapshotAsync(
                "tenant-a",
                subject,
                CancellationToken.None);
        NotificationHistoryReferenceSnapshot companionAfterClose =
            await lifecycle.GetSnapshotAsync(
                "tenant-a",
                companion,
                CancellationToken.None);
        Assert.Equal(NotificationHistoryReferenceStatus.Closed, closed.Status);
        Assert.Equal(0, closed.RecordCount);
        Assert.Equal(2, companionAfterClose.Version);
        Assert.Equal(0, companionAfterClose.RecordCount);
    }

    [Fact]
    public async Task Batch_close_of_missing_history_installs_terminal_tombstone()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        NotificationHistoryReference reference = Reference(
            "product-subject",
            "product-subject/v1|tenant-a|missing");
        NotificationHistoryLifecycleService lifecycle =
            new(dbContext, new TestScopeContext(), new FixedClock());

        NotificationHistoryReferenceCloseBatchResult result =
            await lifecycle.CloseBatchAsync(
                new NotificationHistoryReferenceCloseBatchRequest(
                    Guid.Parse(
                        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    "tenant-a",
                    reference,
                    ExpectedVersion: 0,
                    BatchSize: 100),
                CancellationToken.None);

        Assert.Equal(
            NotificationHistoryReferenceCloseBatchStatus.Completed,
            result.Status);
        Assert.Equal(0, result.Receipt!.RemovedRecordCount);
        Assert.Equal(0, result.Receipt.CompletedBatchCount);
        Assert.Equal(
            NotificationHistoryBatchCloseOperation.InitialRemovalProofSha256,
            result.Receipt.RemovalProofSha256);
        Assert.Equal(
            NotificationHistoryReferenceStatus.Closed,
            (await lifecycle.GetSnapshotAsync(
                "tenant-a",
                reference,
                CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Reads_hide_a_missing_reference_without_querying_state()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        NotificationHistoryLifecycleService lifecycle =
            new(dbContext, new TestScopeContext(), new FixedClock());

        NotificationHistoryReferenceSnapshot snapshot =
            await lifecycle.GetSnapshotAsync(
                "tenant-a",
                null!,
                CancellationToken.None);
        NotificationHistoryReferencePage page =
            await lifecycle.ListAsync(
                "tenant-a",
                null!,
                0,
                10,
                CancellationToken.None);

        Assert.Equal(
            NotificationHistoryReferenceStatus.Missing,
            snapshot.Status);
        Assert.Equal(
            NotificationHistoryReferenceStatus.Missing,
            page.Status);
        Assert.Empty(
            await dbContext.NotificationHistoryReferenceStates
                .ToArrayAsync());
    }

    [Fact]
    public async Task Retention_advances_the_reference_version_atomically()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        NotificationHistoryReference reference = Reference(
            "product-subject",
            "product-subject/v1|tenant-a|record-42");
        UserNotification notification = CreateNotification(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "user-a",
            [ToDomain(reference)],
            1);
        notification.MarkRead(Now.AddDays(-10));
        NotificationHistoryLifecycleRepository repository = new(dbContext);
        Assert.True(await repository.RegisterAsync(
            notification,
            CancellationToken.None));
        dbContext.UserNotifications.Add(notification);
        await dbContext.SaveChangesAsync();

        int removed = await NotificationRetentionService
            .DeleteExpiredNotificationsBatchAsync(
                dbContext,
                Now.AddDays(-1),
                Now.AddDays(-1),
                100,
                CancellationToken.None);

        Assert.Equal(1, removed);
        NotificationHistoryReferenceState state =
            await dbContext.NotificationHistoryReferenceStates
                .SingleAsync(candidate =>
                    candidate.Namespace == reference.Namespace &&
                    candidate.Digest == reference.Digest);
        Assert.Equal(2, state.Version);
        Assert.Empty(await dbContext.UserNotifications.ToArrayAsync());
    }

    private static NotificationsDbContext CreateDbContext()
    {
        DbContextOptions<NotificationsDbContext> options =
            new DbContextOptionsBuilder<NotificationsDbContext>()
                .UseInMemoryDatabase(
                    $"notification-lifecycle-{Guid.NewGuid():N}")
                .Options;
        return new NotificationsDbContext(
            options,
            new TestScopeContext());
    }

    private static UserNotification CreateNotification(
        Guid id,
        string userId,
        IReadOnlyCollection<NotificationHistoryReferenceKey> references,
        long? streamSequence = null)
    {
        UserNotification notification = UserNotification.Create(
            id,
            "tenant-a",
            userId,
            "operations",
            "record.changed",
            1,
            "Record changed",
            "A referenced record changed.",
            DomainSeverity.Info,
            Now,
            Now,
            "{}",
            ["delivery:web"],
            DomainDeliveryPolicy.RespectPreferences,
            isInboxVisible: true,
            references).Value;
        if (streamSequence is not null)
        {
            typeof(UserNotification)
                .GetProperty(nameof(UserNotification.StreamSequence))!
                .SetValue(notification, streamSequence.Value);
        }

        return notification;
    }

    private static NotificationHistoryReference Reference(
        string referenceNamespace,
        string coordinate) =>
        NotificationHistoryReference.FromCanonicalCoordinate(
            referenceNamespace,
            coordinate);

    private static NotificationHistoryReferenceKey ToDomain(
        NotificationHistoryReference reference) =>
        NotificationHistoryReferenceKey.Create(
            reference.Namespace,
            reference.Digest).Value;

    private sealed class FixedClock : ISystemClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class TestScopeContext : IScopeContext
    {
        public bool IsEnabled => true;
        public string? ScopeId => "tenant-a";
    }
}
