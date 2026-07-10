namespace Gma.Modules.Notifications.Persistence;

using Gma.Framework.Runtime.Time;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

internal sealed class NotificationRetentionService(
    IServiceScopeFactory scopeFactory,
    ISystemClock clock,
    IOptions<NotificationRetentionOptions> options,
    ILogger<NotificationRetentionService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(options.Value.IntervalMinutes));

        while (!stoppingToken.IsCancellationRequested)
        {
            await this.CleanupAsync(stoppingToken).ConfigureAwait(false);
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    internal async Task CleanupAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        NotificationRetentionOptions settings = options.Value;
        DateTimeOffset nowUtc = clock.UtcNow;
        DateTimeOffset readBefore = nowUtc.AddDays(-settings.ReadHistoryDays);
        DateTimeOffset unreadBefore = nowUtc.AddDays(-settings.UnreadHistoryDays);
        DateTimeOffset broadcastsBefore = nowUtc.AddDays(-settings.BroadcastDays);

        Guid[] notificationIds = await dbContext.UserNotifications
            .IgnoreQueryFilters()
            .Where(notification =>
                (notification.ReadAtUtc != null && notification.ReadAtUtc < readBefore) ||
                (notification.ReadAtUtc == null && notification.CreatedAtUtc < unreadBefore))
            .OrderBy(notification => notification.CreatedAtUtc)
            .Select(notification => notification.Id)
            .Take(settings.BatchSize)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (notificationIds.Length > 0)
        {
            await dbContext.UserNotifications
                .IgnoreQueryFilters()
                .Where(notification => notificationIds.Contains(notification.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        Guid[] broadcastIds = await dbContext.NotificationBroadcasts
            .IgnoreQueryFilters()
            .Where(broadcast => broadcast.CreatedAtUtc < broadcastsBefore)
            .OrderBy(broadcast => broadcast.CreatedAtUtc)
            .Select(broadcast => broadcast.Id)
            .Take(settings.BatchSize)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (broadcastIds.Length > 0)
        {
            await dbContext.NotificationBroadcastReads
                .IgnoreQueryFilters()
                .Where(read => broadcastIds.Contains(read.BroadcastId))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            await dbContext.NotificationBroadcasts
                .IgnoreQueryFilters()
                .Where(broadcast => broadcastIds.Contains(broadcast.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (notificationIds.Length > 0 || broadcastIds.Length > 0)
        {
            logger.LogInformation(
                "Notification retention removed {NotificationCount} user notifications and {BroadcastCount} broadcasts.",
                notificationIds.Length,
                broadcastIds.Length);
        }
    }
}
