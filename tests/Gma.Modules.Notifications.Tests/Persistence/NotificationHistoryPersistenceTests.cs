namespace Gma.Modules.Notifications.Tests;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Framework.Cqrs.Infrastructure;
using Gma.Framework.Notifications;
using Gma.Framework.Notifications.Infrastructure;
using Gma.Framework.Results;
using Gma.Framework.Realtime.Notifications;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Framework.Scoping;
using Gma.Modules.Notifications.Application;
using Gma.Modules.Notifications.Application.Commands;
using Gma.Modules.Notifications.Application.Queries;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;
using ContractNotificationSeverity = Notifications.Contracts.NotificationSeverity;
using DomainBroadcastAudience = Domain.ValueObjects.NotificationBroadcastAudience;
using DomainDeliveryAttemptOutcome = Domain.ValueObjects.NotificationDeliveryAttemptOutcome;
using DomainDeliveryPolicy = Domain.ValueObjects.NotificationDeliveryPolicy;
using DomainDeliveryStatus = Domain.ValueObjects.NotificationDeliveryStatus;
using DomainNotificationSeverity = Domain.ValueObjects.NotificationSeverity;
using DomainTagKind = Domain.ValueObjects.NotificationTagKind;
using DomainTagOrigin = Domain.ValueObjects.NotificationTagOrigin;
using FrameworkNotificationSeverity = Framework.Notifications.NotificationSeverity;

[Trait("Category", "Unit")]
public sealed class NotificationHistoryPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Publisher_persists_history_when_live_notifications_are_disabled()
    {
        using IHost host = BuildHost(enabled: false, scopeId: "tenant-a");
        using IServiceScope scope = host.Services.CreateScope();
        IUserNotificationPublisher publisher = scope.ServiceProvider.GetRequiredService<IUserNotificationPublisher>();
        NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        await publisher.PublishAsync(
            "catalog",
            UserNotificationTarget.User("tenant-a", "user-a"),
            new SampleNotificationPayload("SKU-1"),
            new NotificationPublishOptions(
                "Item updated",
                "Catalog item SKU-1 changed.",
                FrameworkNotificationSeverity.Success,
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Now));
        await publisher.PublishAsync(
            "catalog",
            UserNotificationTarget.User("tenant-a", "user-a"),
            new SampleNotificationPayload("SKU-1"),
            new NotificationPublishOptions(
                "Item updated",
                "Catalog item SKU-1 changed.",
                FrameworkNotificationSeverity.Success,
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Now));

        UserNotification notification = Assert.Single(await dbContext.UserNotifications.ToArrayAsync());
        Assert.Equal("tenant-a", notification.ScopeId);
        Assert.Equal("user-a", notification.Recipient.UserId);
        Assert.Equal("catalog.item-updated", notification.Source.Name);
        Assert.Equal(DomainNotificationSeverity.Success, notification.Severity);
        Assert.Equal(/*lang=json,strict*/ "{\"sku\":\"SKU-1\"}", notification.Payload.Json);
    }

    [Fact]
    public async Task Projector_reuses_pending_tag_definitions_across_a_fan_out_batch()
    {
        using IHost host = BuildHost(enabled: false, scopeId: "tenant-a");
        using IServiceScope scope = host.Services.CreateScope();
        IUserNotificationRequestProjector projector =
            scope.ServiceProvider.GetRequiredService<IUserNotificationRequestProjector>();
        NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        NotificationTag[] tags =
        [
            new(NotificationTags.Web, NotificationTagKind.Delivery),
            new("domain:reservations", NotificationTagKind.Domain),
        ];

        foreach (string userId in new[] { "user-a", "user-b" })
        {
            await projector.ProjectAsync(
                new UserNotificationRequestedIntegrationEventV2(
                    Guid.CreateVersion7(),
                    "tenant-a",
                    Now,
                    userId,
                    "reservations",
                    "reservation-confirmed",
                    1,
                    "Reservation confirmed",
                    null,
                    ContractNotificationSeverity.Success,
                    "{}",
                    tags),
                CancellationToken.None);
        }

        await dbContext.SaveChangesAsync();

        Assert.Equal(2, await dbContext.UserNotifications.CountAsync());
        Assert.Equal(2, await dbContext.NotificationTagDefinitions.CountAsync());
    }

    [Fact]
    public async Task Publisher_respects_persisted_web_preference_before_best_effort_delivery()
    {
        using IHost host = BuildHost(enabled: true, scopeId: "tenant-a");
        using IServiceScope scope = host.Services.CreateScope();
        NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        NotificationTagDefinition webDefinition = NotificationTagDefinition.Create(
            Guid.CreateVersion7(),
            "tenant-a",
            NotificationTags.Web,
            DomainTagKind.Delivery,
            "Web inbox",
            "Stores the notification in the durable web inbox.",
            DomainTagOrigin.System,
            "notifications",
            "test",
            Now).Value;
        NotificationPreference preference = NotificationPreference.Create(
            Guid.CreateVersion7(),
            "tenant-a",
            "user-a",
            NotificationTags.Web,
            enabled: false,
            nowUtc: Now).Value;
        dbContext.NotificationTagDefinitions.Add(webDefinition);
        dbContext.NotificationPreferences.Add(preference);
        await dbContext.SaveChangesAsync();
        IUserNotificationFeed feed = scope.ServiceProvider.GetRequiredService<IUserNotificationFeed>();
        UserNotificationTarget target = UserNotificationTarget.User("tenant-a", "user-a");
        await using IUserNotificationSubscription subscription = feed.Subscribe(target);
        IUserNotificationPublisher publisher = scope.ServiceProvider.GetRequiredService<IUserNotificationPublisher>();

        await publisher.PublishAsync(
            "catalog",
            target,
            new SampleNotificationPayload("SKU-1"),
            new NotificationPublishOptions("Item updated"));

        using CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(150));
        await using IAsyncEnumerator<UserNotificationMessage> messages =
            subscription.ReadAllAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await messages.MoveNextAsync().AsTask());
        UserNotification notification = Assert.Single(await dbContext.UserNotifications.ToArrayAsync());
        NotificationDelivery delivery = Assert.Single(await dbContext.NotificationDeliveries.ToArrayAsync());
        Assert.False(notification.IsInboxVisible);
        Assert.Equal(DomainDeliveryStatus.Suppressed, delivery.Status);
    }

    [Fact]
    public async Task Tenant_filter_isolates_notification_history()
    {
        InMemoryDatabaseRoot databaseRoot = new();
        await using (NotificationsDbContext tenantA = CreateDbContext(databaseRoot, "tenant-a"))
        {
            tenantA.UserNotifications.Add(CreateNotification("tenant-a", "user-a"));
            await tenantA.SaveChangesAsync();
        }

        await using (NotificationsDbContext tenantB = CreateDbContext(databaseRoot, "tenant-b"))
        {
            tenantB.UserNotifications.Add(CreateNotification("tenant-b", "user-a"));
            await tenantB.SaveChangesAsync();
        }

        await using NotificationsDbContext readTenantA = CreateDbContext(databaseRoot, "tenant-a");
        UserNotification[] visible = await readTenantA.UserNotifications
            .OrderBy(notification => notification.ScopeId)
            .ToArrayAsync();

        UserNotification notification = Assert.Single(visible);
        Assert.Equal("tenant-a", notification.ScopeId);
    }

    [Fact]
    public async Task Mark_read_commands_update_current_user_notifications_through_unit_of_work()
    {
        using IHost host = BuildHost(enabled: false, scopeId: "tenant-a");
        using IServiceScope scope = host.Services.CreateScope();
        NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        IRequestDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        Guid notificationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        dbContext.UserNotifications.Add(CreateNotification("tenant-a", "user-a", notificationId));
        dbContext.UserNotifications.Add(CreateNotification("tenant-a", "user-a", Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")));
        await dbContext.SaveChangesAsync();

        Result<Unit> markOne = await dispatcher.SendAsync(
            new MarkNotificationReadCommand(notificationId, UserSubject("user-a"), "tenant-a"),
            CancellationToken.None);
        Result<NotificationHistoryListResponse> unreadAfterOne = await dispatcher.QueryAsync(
            new ListNotificationHistoryQuery(UserSubject("user-a"), "tenant-a", UnreadOnly: true),
            CancellationToken.None);
        Result<MarkAllNotificationsReadResponse> markAll = await dispatcher.SendAsync(
            new MarkAllNotificationsReadCommand(UserSubject("user-a"), "tenant-a"),
            CancellationToken.None);

        Assert.True(markOne.IsSuccess);
        Assert.True(unreadAfterOne.IsSuccess);
        Assert.Single(unreadAfterOne.Value.Items);
        Assert.True(markAll.IsSuccess);
        Assert.Equal(1, markAll.Value.UpdatedCount);
        Assert.Equal(2, await dbContext.UserNotifications.CountAsync(notification => notification.ReadAtUtc != null));
    }

    [Fact]
    public async Task Stream_queries_use_monotonic_sequence_cursor()
    {
        using IHost host = BuildHost(enabled: false, scopeId: "tenant-a");
        using IServiceScope scope = host.Services.CreateScope();
        NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        IRequestDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        UserNotification first = CreateNotification("tenant-a", "user-a", Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), 10);
        UserNotification second = CreateNotification("tenant-a", "user-a", Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), 11);
        UserNotification otherUser = CreateNotification("tenant-a", "user-b", Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), 12);
        dbContext.UserNotifications.AddRange(first, second, otherUser);
        await dbContext.SaveChangesAsync();

        Result<long> userCursor = await dispatcher.QueryAsync(
            new GetNotificationStreamCursorQuery(UserSubject("user-a"), "tenant-a"),
            CancellationToken.None);
        Result<IReadOnlyList<NotificationHistoryItem>> userItems = await dispatcher.QueryAsync(
            new StreamNotificationHistoryQuery(UserSubject("user-a"), "tenant-a", AfterStreamSequence: 10, BatchSize: 10),
            CancellationToken.None);
        Result<long> tenantCursor = await dispatcher.QueryAsync(
            new GetTenantNotificationStreamCursorQuery(null),
            CancellationToken.None);
        Result<IReadOnlyList<AdminNotificationHistoryItem>> tenantItems = await dispatcher.QueryAsync(
            new StreamTenantNotificationHistoryQuery(null, AfterStreamSequence: 10, BatchSize: 10),
            CancellationToken.None);

        Assert.True(userCursor.IsSuccess);
        Assert.True(userItems.IsSuccess);
        Assert.Equal(11, userCursor.Value);
        NotificationHistoryItem userItem = Assert.Single(userItems.Value);
        Assert.Equal(second.Id, userItem.Id);
        Assert.Equal(11, userItem.StreamSequence);
        Assert.True(tenantCursor.IsSuccess);
        Assert.True(tenantItems.IsSuccess);
        Assert.Equal(12, tenantCursor.Value);
        long[] expectedSequences = [11, 12];
        Assert.Equal(expectedSequences, tenantItems.Value.Select(item => item.StreamSequence).ToArray());
    }

    [Fact]
    public async Task User_history_policy_denies_wrong_user_and_wrong_tenant_without_leaking_item()
    {
        using IHost host = BuildHost(enabled: false, scopeId: "tenant-a");
        using IServiceScope scope = host.Services.CreateScope();
        NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        IRequestDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        Guid notificationId = Guid.Parse("abababab-abab-abab-abab-abababababab");
        dbContext.UserNotifications.Add(CreateNotification("tenant-a", "user-a", notificationId));
        await dbContext.SaveChangesAsync();

        Result<NotificationHistoryItem> wrongUser = await dispatcher.QueryAsync(
            new GetNotificationHistoryItemQuery(notificationId, UserSubject("user-b"), "tenant-a"),
            CancellationToken.None);
        Result<Unit> wrongTenant = await dispatcher.SendAsync(
            new MarkNotificationReadCommand(notificationId, UserSubject("user-a"), "tenant-b"),
            CancellationToken.None);
        Result<NotificationHistoryListResponse> wrongTenantList = await dispatcher.QueryAsync(
            new ListNotificationHistoryQuery(UserSubject("user-a"), "tenant-b"),
            CancellationToken.None);
        Result<NotificationHistoryListResponse> userList = await dispatcher.QueryAsync(
            new ListNotificationHistoryQuery(UserSubject("user-a"), "tenant-a"),
            CancellationToken.None);

        Assert.True(wrongUser.IsFailure);
        Assert.Equal(NotificationsApplicationErrors.NotificationNotFound, wrongUser.Error);
        Assert.True(wrongTenant.IsFailure);
        Assert.Equal(NotificationsApplicationErrors.NotificationNotFound, wrongTenant.Error);
        Assert.True(wrongTenantList.IsSuccess);
        Assert.Empty(wrongTenantList.Value.Items);
        Assert.True(userList.IsSuccess);
        Assert.Single(userList.Value.Items);
    }

    [Fact]
    public async Task Broadcast_queries_include_matching_tenant_and_platform_user_audiences()
    {
        using IHost host = BuildHost(enabled: false, scopeId: "tenant-a");
        using IServiceScope scope = host.Services.CreateScope();
        NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        IRequestDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        NotificationBroadcast tenantBroadcast = CreateBroadcast(
            "tenant-a",
            NotificationBroadcastAudience.TenantUsers,
            Guid.Parse("11111111-1111-1111-1111-111111111111"));
        NotificationBroadcast otherTenantBroadcast = CreateBroadcast(
            "tenant-b",
            NotificationBroadcastAudience.TenantUsers,
            Guid.Parse("22222222-2222-2222-2222-222222222222"));
        NotificationBroadcast platformBroadcast = CreateBroadcast(
            null,
            NotificationBroadcastAudience.PlatformUsers,
            Guid.Parse("33333333-3333-3333-3333-333333333333"));
        NotificationBroadcast adminOnlyBroadcast = CreateBroadcast(
            "tenant-a",
            NotificationBroadcastAudience.TenantAdmins,
            Guid.Parse("44444444-4444-4444-4444-444444444444"));
        dbContext.NotificationBroadcasts.AddRange(
            tenantBroadcast,
            otherTenantBroadcast,
            platformBroadcast,
            adminOnlyBroadcast);
        await dbContext.SaveChangesAsync();

        Result<NotificationBroadcastListResponse> result = await dispatcher.QueryAsync(
            new ListNotificationBroadcastsQuery(
                "tenant-a",
                NotificationBroadcastRecipientKind.User,
                "user-a"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Equal(2, result.Value.UnreadCount);
        Guid[] broadcastIds = result.Value.Items.Select(item => item.BroadcastId).Order().ToArray();
        Guid[] expectedBroadcastIds = new[] { platformBroadcast.Id, tenantBroadcast.Id }.Order().ToArray();
        Assert.Equal(expectedBroadcastIds, broadcastIds);
    }

    [Fact]
    public async Task Broadcast_read_receipts_are_per_recipient_and_idempotent()
    {
        using IHost host = BuildHost(enabled: false, scopeId: "tenant-a");
        using IServiceScope scope = host.Services.CreateScope();
        NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        IRequestDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        NotificationBroadcast broadcast = CreateBroadcast(
            "tenant-a",
            NotificationBroadcastAudience.TenantUsers,
            Guid.Parse("55555555-5555-5555-5555-555555555555"));
        dbContext.NotificationBroadcasts.Add(broadcast);
        await dbContext.SaveChangesAsync();

        Result<Unit> firstRead = await dispatcher.SendAsync(
            new MarkNotificationBroadcastReadCommand(
                broadcast.Id,
                "tenant-a",
                NotificationBroadcastRecipientKind.User,
                "user-a"),
            CancellationToken.None);
        Result<Unit> secondRead = await dispatcher.SendAsync(
            new MarkNotificationBroadcastReadCommand(
                broadcast.Id,
                "tenant-a",
                NotificationBroadcastRecipientKind.User,
                "user-a"),
            CancellationToken.None);
        Result<NotificationBroadcastListResponse> userA = await dispatcher.QueryAsync(
            new ListNotificationBroadcastsQuery(
                "tenant-a",
                NotificationBroadcastRecipientKind.User,
                "user-a"),
            CancellationToken.None);
        Result<NotificationBroadcastListResponse> userB = await dispatcher.QueryAsync(
            new ListNotificationBroadcastsQuery(
                "tenant-a",
                NotificationBroadcastRecipientKind.User,
                "user-b"),
            CancellationToken.None);

        Assert.True(firstRead.IsSuccess);
        Assert.True(secondRead.IsSuccess);
        Assert.True(userA.IsSuccess);
        Assert.True(userB.IsSuccess);
        Assert.Equal(0, userA.Value.UnreadCount);
        Assert.Equal(1, userB.Value.UnreadCount);
        Assert.Equal(1, await dbContext.NotificationBroadcastReads.CountAsync());
    }

    [Fact]
    public async Task Platform_broadcast_read_receipts_are_scoped_by_tenant_context()
    {
        using IHost host = BuildHost(enabled: false, scopeId: "tenant-a");
        using IServiceScope scope = host.Services.CreateScope();
        NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        IRequestDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        NotificationBroadcast broadcast = CreateBroadcast(
            null,
            NotificationBroadcastAudience.PlatformUsers,
            Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"));
        dbContext.NotificationBroadcasts.Add(broadcast);
        await dbContext.SaveChangesAsync();

        Result<Unit> tenantARead = await dispatcher.SendAsync(
            new MarkNotificationBroadcastReadCommand(
                broadcast.Id,
                "tenant-a",
                NotificationBroadcastRecipientKind.User,
                "shared-user-id"),
            CancellationToken.None);
        Result<NotificationBroadcastListResponse> tenantA = await dispatcher.QueryAsync(
            new ListNotificationBroadcastsQuery(
                "tenant-a",
                NotificationBroadcastRecipientKind.User,
                "shared-user-id"),
            CancellationToken.None);
        Result<NotificationBroadcastListResponse> tenantB = await dispatcher.QueryAsync(
            new ListNotificationBroadcastsQuery(
                "tenant-b",
                NotificationBroadcastRecipientKind.User,
                "shared-user-id"),
            CancellationToken.None);

        Assert.True(tenantARead.IsSuccess);
        Assert.True(tenantA.IsSuccess);
        Assert.True(tenantB.IsSuccess);
        Assert.Equal(0, tenantA.Value.UnreadCount);
        Assert.Equal(1, tenantB.Value.UnreadCount);
        Assert.Equal(1, await dbContext.NotificationBroadcastReads.CountAsync());
    }

    [Fact]
    public async Task Broadcast_mark_all_read_processes_visible_items_in_batches_and_is_idempotent()
    {
        using IHost host = BuildHost(enabled: false, scopeId: "tenant-a");
        using IServiceScope scope = host.Services.CreateScope();
        NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        IRequestDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        NotificationBroadcast[] broadcasts = Enumerable
            .Range(1, 505)
            .Select(index => CreateBroadcast(
                "tenant-a",
                NotificationBroadcastAudience.TenantUsers,
                Guid.Parse($"99999999-9999-9999-9999-{index:000000000000}"),
                streamSequence: index))
            .ToArray();
        dbContext.NotificationBroadcasts.AddRange(broadcasts);
        await dbContext.SaveChangesAsync();

        Result<MarkAllNotificationBroadcastsReadResponse> first = await dispatcher.SendAsync(
            new MarkAllNotificationBroadcastsReadCommand(
                "tenant-a",
                NotificationBroadcastRecipientKind.User,
                "user-a"),
            CancellationToken.None);
        Result<MarkAllNotificationBroadcastsReadResponse> second = await dispatcher.SendAsync(
            new MarkAllNotificationBroadcastsReadCommand(
                "tenant-a",
                NotificationBroadcastRecipientKind.User,
                "user-a"),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(505, first.Value.UpdatedCount);
        Assert.Equal(0, second.Value.UpdatedCount);
        Assert.Equal(505, await dbContext.NotificationBroadcastReads.CountAsync());
    }

    [Fact]
    public async Task Broadcast_stream_uses_admin_audience_visibility()
    {
        using IHost host = BuildHost(enabled: false, scopeId: "tenant-a");
        using IServiceScope scope = host.Services.CreateScope();
        NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        IRequestDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        NotificationBroadcast tenantAdmin = CreateBroadcast(
            "tenant-a",
            NotificationBroadcastAudience.TenantAdmins,
            Guid.Parse("66666666-6666-6666-6666-666666666666"),
            streamSequence: 10);
        NotificationBroadcast platformAdmin = CreateBroadcast(
            null,
            NotificationBroadcastAudience.PlatformAdmins,
            Guid.Parse("77777777-7777-7777-7777-777777777777"),
            streamSequence: 11);
        NotificationBroadcast tenantUser = CreateBroadcast(
            "tenant-a",
            NotificationBroadcastAudience.TenantUsers,
            Guid.Parse("88888888-8888-8888-8888-888888888888"),
            streamSequence: 12);
        dbContext.NotificationBroadcasts.AddRange(tenantAdmin, platformAdmin, tenantUser);
        await dbContext.SaveChangesAsync();

        Result<IReadOnlyList<NotificationBroadcastItem>> result = await dispatcher.QueryAsync(
            new StreamNotificationBroadcastsQuery(
                "tenant-a",
                NotificationBroadcastRecipientKind.Admin,
                "admin-a",
                AfterStreamSequence: 10,
                BatchSize: 10),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        NotificationBroadcastItem item = Assert.Single(result.Value);
        Assert.Equal(platformAdmin.Id, item.BroadcastId);
        Assert.Equal(NotificationBroadcastAudience.PlatformAdmins, item.Audience);
        Assert.Equal(11, item.StreamSequence);
    }

    [Fact]
    public async Task Create_broadcast_command_persists_through_unit_of_work()
    {
        using IHost host = BuildHost(enabled: false, scopeId: "tenant-a");
        using IServiceScope scope = host.Services.CreateScope();
        NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        IRequestDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();

        Result<AdminCreateNotificationBroadcastResponse> result = await dispatcher.SendAsync(
            new CreateNotificationBroadcastCommand(
                NotificationBroadcastAudience.TenantUsers,
                "tenant-a",
                "notifications",
                "system.maintenance",
                1,
                "Maintenance",
                "Scheduled maintenance.",
                ContractNotificationSeverity.Warning,
                Now,
                "{}"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        NotificationBroadcast broadcast = Assert.Single(await dbContext.NotificationBroadcasts.ToArrayAsync());
        Assert.Equal(result.Value.BroadcastId, broadcast.Id);
        Assert.Equal("tenant-a", broadcast.ScopeId);
        Assert.Equal(DomainBroadcastAudience.TenantUsers, broadcast.Audience);
    }

    [Fact]
    public async Task Email_only_intents_stay_out_of_inbox_while_visible_history_exposes_tags_and_policy()
    {
        using IHost host = BuildHost(enabled: false, scopeId: "tenant-a");
        using IServiceScope scope = host.Services.CreateScope();
        NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        IRequestDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IRequestDispatcher>();
        UserNotification hidden = UserNotification.Create(
            Guid.CreateVersion7(),
            "tenant-a",
            "user-a",
            "auth",
            "account.signed-in",
            1,
            "Account signed in",
            null,
            DomainNotificationSeverity.Warning,
            Now,
            Now,
            "{}",
            ["delivery:email", "domain:security"],
            DomainDeliveryPolicy.Mandatory,
            isInboxVisible: false).Value;
        UserNotification visible = UserNotification.Create(
            Guid.CreateVersion7(),
            "tenant-a",
            "user-a",
            "catalog",
            "catalog.item-updated",
            1,
            "Item updated",
            null,
            DomainNotificationSeverity.Info,
            Now,
            Now,
            "{}",
            ["delivery:web", "domain:catalog"],
            DomainDeliveryPolicy.RespectPreferences,
            isInboxVisible: true).Value;
        dbContext.UserNotifications.AddRange(hidden, visible);
        await dbContext.SaveChangesAsync();

        Result<NotificationHistoryListResponse> userHistory = await dispatcher.QueryAsync(
            new ListNotificationHistoryQuery(UserSubject("user-a"), "tenant-a"),
            CancellationToken.None);
        Result<AdminNotificationHistoryListResponse> adminHistory = await dispatcher.QueryAsync(
            new ListTenantNotificationHistoryQuery(),
            CancellationToken.None);

        Assert.Equal(2, await dbContext.UserNotifications.CountAsync());
        NotificationHistoryItem userItem = Assert.Single(userHistory.Value.Items);
        Assert.Equal(visible.Id, userItem.Id);
        Assert.Equal(["delivery:web", "domain:catalog"], userItem.Tags);
        Assert.Equal(Notifications.Contracts.NotificationDeliveryPolicy.RespectPreferences, userItem.DeliveryPolicy);
        Assert.Equal(visible.Id, Assert.Single(adminHistory.Value.Items).NotificationId);
    }

    [Fact]
    public async Task Retention_does_not_delete_notification_content_needed_by_active_deliveries()
    {
        using IHost host = BuildHost(enabled: false, scopeId: "tenant-a");
        using IServiceScope scope = host.Services.CreateScope();
        NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        DateTimeOffset old = Now.AddDays(-400);
        UserNotification active = UserNotification.Create(
            Guid.CreateVersion7(),
            "tenant-a",
            "user-a",
            "auth",
            "account.signed-in",
            1,
            "Account signed in",
            null,
            DomainNotificationSeverity.Warning,
            old,
            old,
            "{}",
            ["delivery:email"],
            DomainDeliveryPolicy.Mandatory,
            isInboxVisible: false).Value;
        UserNotification completed = UserNotification.Create(
            Guid.CreateVersion7(),
            "tenant-a",
            "user-a",
            "catalog",
            "catalog.item-updated",
            1,
            "Item updated",
            null,
            DomainNotificationSeverity.Info,
            old,
            old,
            "{}",
            ["delivery:email"],
            DomainDeliveryPolicy.RespectPreferences,
            isInboxVisible: false).Value;
        NotificationDelivery pending = NotificationDelivery.CreatePending(
            Guid.CreateVersion7(),
            "tenant-a",
            active.Id,
            "delivery:email",
            "email-primary",
            old).Value;
        NotificationDelivery delivered = NotificationDelivery.CreateDelivered(
            Guid.CreateVersion7(),
            "tenant-a",
            completed.Id,
            "delivery:email",
            "email-primary",
            old).Value;
        NotificationDeliveryAttempt pendingAttempt = NotificationDeliveryAttempt.Create(
            Guid.CreateVersion7(),
            "tenant-a",
            pending.Id,
            1,
            "email-primary",
            DomainDeliveryAttemptOutcome.Retry,
            old,
            old.AddSeconds(1),
            "rate-limited",
            providerMessageId: null).Value;
        NotificationDeliveryAttempt deliveredAttempt = NotificationDeliveryAttempt.Create(
            Guid.CreateVersion7(),
            "tenant-a",
            delivered.Id,
            1,
            "email-primary",
            DomainDeliveryAttemptOutcome.Delivered,
            old,
            old.AddSeconds(1),
            code: null,
            providerMessageId: "provider-message-1").Value;
        dbContext.UserNotifications.AddRange(active, completed);
        dbContext.NotificationDeliveries.AddRange(pending, delivered);
        dbContext.NotificationDeliveryAttempts.AddRange(pendingAttempt, deliveredAttempt);
        await dbContext.SaveChangesAsync();

        UserNotification[] expired = await NotificationRetentionService
            .ExpiredUserNotifications(dbContext, Now.AddDays(-90), Now.AddDays(-365))
            .ToArrayAsync();
        NotificationDeliveryAttempt[] expiredAttempts = await NotificationRetentionService
            .ExpiredDeliveryAttempts(dbContext, Now.AddDays(-90))
            .ToArrayAsync();

        Assert.Equal(completed.Id, Assert.Single(expired).Id);
        Assert.Equal(deliveredAttempt.Id, Assert.Single(expiredAttempts).Id);
    }

    private static IHost BuildHost(bool enabled, string scopeId)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        TestTenantContext tenantContext = new(scopeId);
        InMemoryDatabaseRoot databaseRoot = new();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApplicationIdentity:Namespace"] = "test-app",
            ["Notifications:Enabled"] = enabled.ToString(),
            ["Persistence:Provider"] = "SqlServer",
            ["ConnectionStrings:SqlServer"] = "Server=localhost;Database=notifications-tests;Trusted_Connection=True;TrustServerCertificate=True",
            ["Tenancy:Enabled"] = "true"
        });
        builder.Services.TryAddSingleton<ISystemClock>(new FixedClock());
        builder.Services.TryAddSingleton<IIdGenerator, TestIdGenerator>();
        builder.Services.TryAddSingleton<IScopeContext>(tenantContext);
        builder.Services.TryAddSingleton<IScopeContextAccessor>(tenantContext);
        builder.Services.AddDbContext<NotificationsDbContext>(options =>
            options.UseInMemoryDatabase($"notifications-{Guid.NewGuid():N}", databaseRoot));

        builder.AddUserNotificationsInfrastructure();
        builder.AddUserNotificationsRealtime();
        builder.AddCqrsInfrastructure();
        builder.Services.AddNotificationsApplication();
        builder.AddNotificationsPersistence();

        return builder.Build();
    }

    private static NotificationsDbContext CreateDbContext(InMemoryDatabaseRoot databaseRoot, string scopeId)
    {
        DbContextOptions<NotificationsDbContext> options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase("notifications-tenant-filter", databaseRoot)
            .Options;

        return new NotificationsDbContext(options, new TestTenantContext(scopeId));
    }

    private static AccessSubject UserSubject(string userId) =>
        AccessSubject.User(userId);

    private static UserNotification CreateNotification(
        string scopeId,
        string userId,
        Guid? notificationId = null,
        long? streamSequence = null)
    {
        UserNotification notification = UserNotification.Create(
            notificationId ?? Guid.NewGuid(),
            scopeId,
            userId,
            "catalog",
            "catalog.item-updated",
            1,
            "Item updated",
            null,
            DomainNotificationSeverity.Info,
            Now,
            Now,
            "{}").Value;

        if (streamSequence is not null)
        {
            typeof(UserNotification)
                .GetProperty(nameof(UserNotification.StreamSequence))!
                .SetValue(notification, streamSequence.Value);
        }

        return notification;
    }

    private static NotificationBroadcast CreateBroadcast(
        string? scopeId,
        NotificationBroadcastAudience audience,
        Guid broadcastId,
        long? streamSequence = null)
    {
        NotificationBroadcast broadcast = NotificationBroadcast.Create(
            broadcastId,
            scopeId,
            ToDomainAudience(audience),
            "notifications",
            "system.maintenance",
            1,
            "Maintenance",
            null,
            DomainNotificationSeverity.Info,
            Now,
            Now,
            "{}").Value;

        if (streamSequence is not null)
        {
            typeof(NotificationBroadcast)
                .GetProperty(nameof(NotificationBroadcast.StreamSequence))!
                .SetValue(broadcast, streamSequence.Value);
        }

        return broadcast;
    }

    private static DomainBroadcastAudience ToDomainAudience(NotificationBroadcastAudience audience) =>
        audience switch
        {
            NotificationBroadcastAudience.TenantUsers => DomainBroadcastAudience.TenantUsers,
            NotificationBroadcastAudience.TenantAdmins => DomainBroadcastAudience.TenantAdmins,
            NotificationBroadcastAudience.PlatformUsers => DomainBroadcastAudience.PlatformUsers,
            NotificationBroadcastAudience.PlatformAdmins => DomainBroadcastAudience.PlatformAdmins,
            _ => DomainBroadcastAudience.Unknown
        };

    private sealed class FixedClock : ISystemClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class TestIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.NewGuid();
    }

    private sealed class TestTenantContext(string scopeId) : IScopeContextAccessor
    {
        public bool IsEnabled => true;
        public string? ScopeId { get; private set; } = scopeId;

        public void SetScope(string scopeId) => this.ScopeId = scopeId;

        public void ClearScope() => this.ScopeId = null;
    }

    [NotificationName("catalog.item-updated")]
    [NotificationVersion(1)]
    [NotificationDescription("Catalog item update test notification.")]
    private sealed record SampleNotificationPayload(string Sku) : IUserNotificationPayload;
}
