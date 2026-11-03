namespace Gma.Modules.Notifications.IntegrationTests;

using Gma.Framework.Notifications;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.IntegrationTests.Support;
using Gma.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Xunit;
using DeliveryStatus = Domain.ValueObjects.NotificationDeliveryStatus;
using Severity = Domain.ValueObjects.NotificationSeverity;

public sealed partial class NotificationsRelationalIntegrationTests
{
    private static readonly string[] OpenMaintenanceDeliveryScopes = ["tenant-a", "tenant-b"];

    [DockerFact]
    public async Task Delivery_maintenance_without_an_active_scope_delivers_open_scopes_on_postgresql()
    {
        await using PostgreSqlContainer database =
            await StartPostgreSqlAsync("notifications_delivery_maintenance");
        await using ServiceProvider provider =
            CreateProvider(database.GetConnectionString(), string.Empty);
        await MigrateAsync(provider);
        await AssertDeliveryMaintenanceWithoutScopeAsync(provider);
    }

    [DockerFact]
    public async Task Delivery_maintenance_without_an_active_scope_delivers_open_scopes_on_sqlserver()
    {
        await using MsSqlContainer database =
            new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await database.StartAsync();
        await using ServiceProvider provider =
            CreateSqlServerProvider(database.GetConnectionString(), string.Empty);
        await MigrateAsync(provider);
        await AssertDeliveryMaintenanceWithoutScopeAsync(provider);
    }

    private static async Task AssertDeliveryMaintenanceWithoutScopeAsync(ServiceProvider provider)
    {
        Guid closedDeliveryId = Guid.Empty;
        foreach (string scopeId in new[] { "tenant-a", "tenant-b", "tenant-closed" })
        {
            await using AsyncServiceScope dataScope = provider.CreateAsyncScope();
            await using NotificationsDbContext context =
                MaintenanceTestContext(dataScope.ServiceProvider, scopeId);
            UserNotification notification = UserNotification.Create(
                Guid.CreateVersion7(), scopeId, "owned-delivery-user",
                "notifications-tests", "owned-unscoped-delivery", 1,
                "Owned delivery fixture", null, Severity.Info, Now, Now, "{}").Value;
            NotificationDelivery delivery = NotificationDelivery.CreatePending(
                Guid.CreateVersion7(), scopeId, notification.Id,
                NotificationTags.Email, TrackingDeliverySink.Provider, Now).Value;
            await AddNotificationAsync(context, notification);
            context.NotificationDeliveries.Add(delivery);
            await context.SaveChangesAsync();
            if (scopeId == "tenant-closed")
            {
                closedDeliveryId = delivery.Id;
                NotificationScopeState state = await context.NotificationScopeStates.SingleAsync();
                state.Close(Guid.CreateVersion7(), new string('a', 64), Now.AddSeconds(1));
                await context.SaveChangesAsync();
            }
        }

        await using AsyncServiceScope foregroundScope = provider.CreateAsyncScope();
        NotificationsDbContext foreground = foregroundScope.ServiceProvider
            .GetRequiredService<NotificationsDbContext>();
        Assert.True(foreground.ScopeFilterEnabled);
        Assert.Equal(string.Empty, foreground.CurrentScopeId);
        NotificationPreference pending = MaintenancePreference("tenant-a");
        foreground.NotificationPreferences.Add(pending);
        Dictionary<string, long> before;
        await using (NotificationsDbContext read =
                     MaintenanceTestContext(foregroundScope.ServiceProvider, "tenant-a"))
        {
            before = await read.NotificationScopeStates.IgnoreQueryFilters()
                .ToDictionaryAsync(state => state.ScopeId, state => state.Version);
        }

        TrackingDeliverySink sink = new();
        NotificationDeliveryService worker = CreateWorker(
            provider, "owned-no-active-scope-worker", Now.AddSeconds(2), sink, 4, 2);
        Assert.Equal(2, await worker.ProcessAvailableBatchAsync(CancellationToken.None));
        Assert.Equal(0, await worker.ProcessAvailableBatchAsync(CancellationToken.None));
        Assert.InRange(sink.MaximumConcurrency, 1, 2);
        Assert.True(foreground.ScopeFilterEnabled);
        Assert.Equal(string.Empty, foreground.CurrentScopeId);
        Assert.Equal(EntityState.Added, foreground.Entry(pending).State);

        await using (NotificationsDbContext read =
                     MaintenanceTestContext(foregroundScope.ServiceProvider, "tenant-a"))
        {
            NotificationDelivery[] delivered = await read.NotificationDeliveries.IgnoreQueryFilters()
                .Where(delivery => delivery.Status == DeliveryStatus.Delivered).ToArrayAsync();
            Assert.Equal(2, delivered.Length);
            Assert.Equal(OpenMaintenanceDeliveryScopes,
                delivered.Select(delivery => delivery.ScopeId).Order(StringComparer.Ordinal));
            Assert.Equal(2, await read.NotificationDeliveryAttempts.IgnoreQueryFilters().CountAsync());
            NotificationDelivery closed = await read.NotificationDeliveries.IgnoreQueryFilters()
                .SingleAsync(delivery => delivery.Id == closedDeliveryId);
            Assert.Equal(DeliveryStatus.Pending, closed.Status);
            Assert.Empty(await read.NotificationDeliveryAttempts.IgnoreQueryFilters()
                .Where(attempt => attempt.DeliveryId == closedDeliveryId).ToArrayAsync());
            Dictionary<string, long> after = await read.NotificationScopeStates.IgnoreQueryFilters()
                .ToDictionaryAsync(state => state.ScopeId, state => state.Version);
            Assert.True(after["tenant-a"] > before["tenant-a"]);
            Assert.True(after["tenant-b"] > before["tenant-b"]);
            Assert.Equal(before["tenant-closed"], after["tenant-closed"]);
            Assert.Empty(await read.NotificationPreferences.IgnoreQueryFilters().ToArrayAsync());
        }

        // A successful owner-maintenance operation must not turn its caller
        // into an unrestricted request writer or flush the caller's tracker.
        await Assert.ThrowsAsync<InvalidOperationException>(() => foreground.SaveChangesAsync());
    }
}
