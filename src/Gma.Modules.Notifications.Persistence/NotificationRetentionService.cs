namespace Gma.Modules.Notifications.Persistence;

using Gma.Framework.Runtime.Time;
using Gma.Modules.Notifications.Application;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

internal sealed class NotificationRetentionService(
    IServiceScopeFactory scopeFactory,
    ISystemClock clock,
    IOptions<NotificationRetentionOptions> options,
    IOptions<NotificationDeliveryOptions> deliveryOptions,
    ILogger<NotificationRetentionService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(options.Value.IntervalMinutes));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await this.CleanupAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Notification retention iteration failed with {ExceptionType}; cleanup will retry on the next interval.",
                    exception.GetType().Name);
            }

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
        DateTimeOffset attemptsBefore = nowUtc.AddDays(-deliveryOptions.Value.AttemptRetentionDays);

        Guid[] attemptIds = await ExpiredDeliveryAttempts(dbContext, attemptsBefore)
            .OrderBy(attempt => attempt.CompletedAtUtc)
            .Select(attempt => attempt.Id)
            .Take(settings.BatchSize)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (attemptIds.Length > 0)
        {
            await dbContext.NotificationDeliveryAttempts
                .IgnoreQueryFilters()
                .Where(attempt => attemptIds.Contains(attempt.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        Guid[] notificationIds = await ExpiredUserNotifications(dbContext, readBefore, unreadBefore)
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

        if (notificationIds.Length > 0 || broadcastIds.Length > 0 || attemptIds.Length > 0)
        {
            logger.LogInformation(
                "Notification retention removed {NotificationCount} user notifications, {BroadcastCount} broadcasts, and {DeliveryAttemptCount} delivery attempts.",
                notificationIds.Length,
                broadcastIds.Length,
                attemptIds.Length);
        }
    }

    internal static IQueryable<UserNotification> ExpiredUserNotifications(
        NotificationsDbContext dbContext,
        DateTimeOffset readBefore,
        DateTimeOffset unreadBefore)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        return dbContext.UserNotifications
            .IgnoreQueryFilters()
            .Where(notification =>
                (notification.ReadAtUtc != null && notification.ReadAtUtc < readBefore) ||
                (notification.ReadAtUtc == null && notification.CreatedAtUtc < unreadBefore))
            .Where(notification => !dbContext.NotificationDeliveries
                .IgnoreQueryFilters()
                .Any(delivery =>
                    delivery.NotificationId == notification.Id &&
                    (delivery.Status == NotificationDeliveryStatus.Pending ||
                     delivery.Status == NotificationDeliveryStatus.Processing ||
                     delivery.Status == NotificationDeliveryStatus.RetryScheduled)));
    }

    internal static IQueryable<NotificationDeliveryAttempt> ExpiredDeliveryAttempts(
        NotificationsDbContext dbContext,
        DateTimeOffset completedBefore)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        return dbContext.NotificationDeliveryAttempts
            .IgnoreQueryFilters()
            .Where(attempt => attempt.CompletedAtUtc < completedBefore)
            .Where(attempt => !dbContext.NotificationDeliveries
                .IgnoreQueryFilters()
                .Any(delivery =>
                    delivery.Id == attempt.DeliveryId &&
                    (delivery.Status == NotificationDeliveryStatus.Pending ||
                     delivery.Status == NotificationDeliveryStatus.Processing ||
                     delivery.Status == NotificationDeliveryStatus.RetryScheduled)));
    }
}
