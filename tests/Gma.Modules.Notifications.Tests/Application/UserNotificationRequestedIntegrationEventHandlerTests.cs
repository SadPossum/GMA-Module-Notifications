namespace Gma.Modules.Notifications.Tests;

using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Framework.Scoping;
using Gma.Modules.Notifications.Application.Handlers;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Persistence;
using Gma.Modules.Notifications.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

[Trait("Category", "Unit")]
public sealed class UserNotificationRequestedIntegrationEventHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task V1_projection_registers_its_recipient_reference()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        UserNotificationRequestedIntegrationEventHandler handler =
            CreateHandler(dbContext);

        await handler.HandleAsync(
            Request(Guid.Parse(
                "11111111-1111-1111-1111-111111111111")),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Single(
            await dbContext.UserNotifications.ToArrayAsync());
        Assert.Single(
            await dbContext.UserNotificationReferences.ToArrayAsync());
        NotificationHistoryReferenceState state =
            Assert.Single(
                await dbContext.NotificationHistoryReferenceStates
                    .ToArrayAsync());
        Assert.Equal(
            NotificationHistoryReference.RecipientNamespace,
            state.Namespace);
        Assert.Equal(1, state.Version);
    }

    [Fact]
    public async Task V1_projection_is_suppressed_after_recipient_closure()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        NotificationHistoryReference recipient =
            NotificationHistoryReference.ForRecipient(
                "tenant-a",
                "user-a");
        NotificationHistoryLifecycleService lifecycle = new(
            dbContext,
            new TestScopeContext(),
            new FixedClock());
        NotificationHistoryReferenceCloseResult closed =
            await lifecycle.CloseAsync(
                new NotificationHistoryReferenceCloseRequest(
                    Guid.Parse(
                        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    "tenant-a",
                    recipient,
                    0,
                    100),
                CancellationToken.None);

        await CreateHandler(dbContext).HandleAsync(
            Request(Guid.Parse(
                "22222222-2222-2222-2222-222222222222")),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.Equal(
            NotificationHistoryReferenceCloseStatus.Completed,
            closed.Status);
        Assert.Empty(
            await dbContext.UserNotifications.ToArrayAsync());
        Assert.Empty(
            await dbContext.NotificationDeliveries.ToArrayAsync());
    }

    private static UserNotificationRequestedIntegrationEventHandler
        CreateHandler(NotificationsDbContext dbContext) =>
        new(
            new NotificationHistoryRepository(dbContext),
            new NotificationHistoryLifecycleRepository(dbContext),
            new NotificationRoutingRepository(dbContext),
            new AllowAllNotificationPreferenceEvaluator(),
            new FixedClock(),
            new TestIdGenerator());

    private static UserNotificationRequestedIntegrationEvent Request(
        Guid eventId) =>
        new(
            eventId,
            "tenant-a",
            Now,
            "user-a",
            "legacy-producer",
            "legacy-notification",
            1,
            "Legacy notification",
            "Legacy body",
            NotificationSeverity.Info,
            "{}");

    private static NotificationsDbContext CreateDbContext()
    {
        DbContextOptions<NotificationsDbContext> options =
            new DbContextOptionsBuilder<NotificationsDbContext>()
                .UseInMemoryDatabase(
                    $"notification-v1-lifecycle-{Guid.NewGuid():N}")
                .Options;
        return new NotificationsDbContext(
            options,
            new TestScopeContext());
    }

    private sealed class FixedClock : ISystemClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class TestIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.CreateVersion7();
    }

    private sealed class TestScopeContext : IScopeContext
    {
        public bool IsEnabled => true;
        public string? ScopeId => "tenant-a";
    }
}
