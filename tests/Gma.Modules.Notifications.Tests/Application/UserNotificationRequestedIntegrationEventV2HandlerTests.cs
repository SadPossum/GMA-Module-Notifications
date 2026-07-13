namespace Gma.Modules.Notifications.Tests;

using Gma.Framework.Notifications;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Framework.Scoping;
using Gma.Modules.Notifications.Application;
using Gma.Modules.Notifications.Application.Handlers;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Gma.Modules.Notifications.Persistence;
using Gma.Modules.Notifications.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;
using ContractDeliveryPolicy = Notifications.Contracts.NotificationDeliveryPolicy;
using ContractSeverity = Notifications.Contracts.NotificationSeverity;
using ContractTagKind = Notifications.Contracts.NotificationTagKind;
using DomainDeliveryStatus = Domain.ValueObjects.NotificationDeliveryStatus;

[Trait("Category", "Unit")]
public sealed class UserNotificationRequestedIntegrationEventV2HandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handler_persists_catalog_intent_and_auditable_delivery_plan()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        UserNotificationRequestedIntegrationEventV2Handler handler = CreateHandler(dbContext);
        UserNotificationRequestedIntegrationEventV2 request = Event(Guid.CreateVersion7(), ContractDeliveryPolicy.RespectPreferences);

        await handler.HandleAsync(request, CancellationToken.None);
        await dbContext.SaveChangesAsync();
        await handler.HandleAsync(request, CancellationToken.None);
        await dbContext.SaveChangesAsync();

        UserNotification notification = Assert.Single(await dbContext.UserNotifications.Include(item => item.Tags).ToArrayAsync());
        Assert.True(notification.IsInboxVisible);
        Assert.Equal(3, notification.Tags.Count);
        Assert.Equal(3, await dbContext.NotificationTagDefinitions.CountAsync());
        NotificationDelivery[] deliveries = (await dbContext.NotificationDeliveries.ToArrayAsync())
            .OrderBy(item => item.DeliveryTag.Value, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(2, deliveries.Length);
        Assert.Contains(deliveries, delivery =>
            delivery.DeliveryTag.Value == NotificationTags.Web &&
            delivery.Status == DomainDeliveryStatus.Delivered);
        Assert.Contains(deliveries, delivery =>
            delivery.DeliveryTag.Value == NotificationTags.Email &&
            delivery.Provider.Value == "email-primary" &&
            delivery.Status == DomainDeliveryStatus.Pending);
    }

    [Fact]
    public async Task Domain_preference_suppresses_all_routes_but_mandatory_policy_bypasses_preferences()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        UserNotificationRequestedIntegrationEventV2Handler handler = CreateHandler(dbContext);
        await handler.HandleAsync(Event(Guid.CreateVersion7(), ContractDeliveryPolicy.RespectPreferences), CancellationToken.None);
        await dbContext.SaveChangesAsync();
        NotificationPreference preference = NotificationPreference.Create(
            Guid.CreateVersion7(),
            "tenant-a",
            "user-a",
            "domain:security",
            enabled: false,
            Now).Value;
        dbContext.NotificationPreferences.Add(preference);
        await dbContext.SaveChangesAsync();

        Guid suppressedId = Guid.CreateVersion7();
        Guid mandatoryId = Guid.CreateVersion7();
        await handler.HandleAsync(Event(suppressedId, ContractDeliveryPolicy.RespectPreferences), CancellationToken.None);
        await dbContext.SaveChangesAsync();
        await handler.HandleAsync(Event(mandatoryId, ContractDeliveryPolicy.Mandatory), CancellationToken.None);
        await dbContext.SaveChangesAsync();

        UserNotification suppressed = await dbContext.UserNotifications.SingleAsync(item => item.Id == suppressedId);
        UserNotification mandatory = await dbContext.UserNotifications.SingleAsync(item => item.Id == mandatoryId);
        Assert.False(suppressed.IsInboxVisible);
        Assert.True(mandatory.IsInboxVisible);
        Assert.All(
            await dbContext.NotificationDeliveries.Where(item => item.NotificationId == suppressedId).ToArrayAsync(),
            delivery => Assert.Equal(DomainDeliveryStatus.Suppressed, delivery.Status));
        Assert.Contains(
            await dbContext.NotificationDeliveries.Where(item => item.NotificationId == mandatoryId).ToArrayAsync(),
            delivery => delivery.Status == DomainDeliveryStatus.Pending);
    }

    private static UserNotificationRequestedIntegrationEventV2Handler CreateHandler(NotificationsDbContext dbContext)
    {
        NotificationRoutingRepository routing = new(dbContext);
        NotificationHistoryRepository history = new(dbContext);
        NotificationDeliveryAdapterCatalog catalog = new([new DurableEmailSink()]);
        return new UserNotificationRequestedIntegrationEventV2Handler(
            history,
            routing,
            catalog,
            new AllowAllNotificationPreferenceEvaluator(),
            Options.Create(new NotificationDeliveryOptions()),
            new FixedClock(),
            new TestIdGenerator());
    }

    private static NotificationsDbContext CreateDbContext()
    {
        DbContextOptions<NotificationsDbContext> options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase($"notifications-v2-{Guid.NewGuid():N}")
            .Options;
        return new NotificationsDbContext(options, new TestScopeContext());
    }

    private static UserNotificationRequestedIntegrationEventV2 Event(
        Guid id,
        ContractDeliveryPolicy policy) =>
        new(
            id,
            "tenant-a",
            Now,
            "user-a",
            "auth",
            "account.signed-in",
            1,
            "Account signed in",
            "A new session was created.",
            ContractSeverity.Warning,
            "{}",
            [
                new NotificationTag(NotificationTags.Web, ContractTagKind.Delivery),
                new NotificationTag(NotificationTags.Email, ContractTagKind.Delivery),
                new NotificationTag("domain:security", ContractTagKind.Domain)
            ],
            policy);

    private sealed class DurableEmailSink : IUserNotificationSink
    {
        public string ProviderName => "email-primary";
        public IReadOnlyCollection<string> DeliveryTags { get; } = [NotificationTags.Email];
        public NotificationSinkDeliveryMode DeliveryModes => NotificationSinkDeliveryMode.Durable;

        public ValueTask<NotificationSinkDeliveryResult> DeliverAsync(
            NotificationSinkDeliveryRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(NotificationSinkDeliveryResult.Delivered());
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
