namespace Gma.Modules.Notifications.Persistence;

using System.Data;
using Gma.Framework.Runtime.Maintenance;
using Gma.Framework.Runtime.Time;
using Gma.Modules.Notifications.Application;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

        int attemptCount = await BoundedBatchProcessor.ExecuteAsync(
                settings.BatchSize,
                settings.MaxBatchesPerCategoryPerCycle,
                (batchSize, token) => DeleteExpiredAttemptsBatchAsync(
                    dbContext,
                    attemptsBefore,
                    batchSize,
                    token),
                cancellationToken)
            .ConfigureAwait(false);
        int notificationCount = await BoundedBatchProcessor.ExecuteAsync(
                settings.BatchSize,
                settings.MaxBatchesPerCategoryPerCycle,
                (batchSize, token) => DeleteExpiredNotificationsBatchAsync(
                    dbContext,
                    readBefore,
                    unreadBefore,
                    batchSize,
                    token),
                cancellationToken)
            .ConfigureAwait(false);
        int broadcastCount = await BoundedBatchProcessor.ExecuteAsync(
                settings.BatchSize,
                settings.MaxBatchesPerCategoryPerCycle,
                (batchSize, token) => DeleteExpiredBroadcastsBatchAsync(
                    dbContext,
                    broadcastsBefore,
                    batchSize,
                    token),
                cancellationToken)
            .ConfigureAwait(false);

        if (notificationCount > 0 || broadcastCount > 0 || attemptCount > 0)
        {
            logger.LogInformation(
                "Notification retention removed {NotificationCount} user notifications, {BroadcastCount} broadcasts, and {DeliveryAttemptCount} delivery attempts.",
                notificationCount,
                broadcastCount,
                attemptCount);
        }
    }

    private static async Task<int> DeleteExpiredAttemptsBatchAsync(
        NotificationsDbContext dbContext,
        DateTimeOffset completedBefore,
        int batchSize,
        CancellationToken cancellationToken)
    {
        Guid[] attemptIds = await ExpiredDeliveryAttempts(dbContext, completedBefore)
            .OrderBy(attempt => attempt.CompletedAtUtc)
            .Select(attempt => attempt.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (attemptIds.Length == 0)
        {
            return 0;
        }

        return await dbContext.NotificationDeliveryAttempts
            .IgnoreQueryFilters()
            .Where(attempt => attemptIds.Contains(attempt.Id))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<int> DeleteExpiredNotificationsBatchAsync(
        NotificationsDbContext dbContext,
        DateTimeOffset readBefore,
        DateTimeOffset unreadBefore,
        int batchSize,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction? transaction =
            dbContext.Database.IsRelational() &&
            dbContext.Database.CurrentTransaction is null
                ? await dbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken)
                    .ConfigureAwait(false)
                : null;
        Guid[] notificationIds = await ExpiredUserNotifications(dbContext, readBefore, unreadBefore)
            .OrderBy(notification => notification.CreatedAtUtc)
            .Select(notification => notification.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (notificationIds.Length == 0)
        {
            return 0;
        }

        int removed;
        if (dbContext.Database.IsRelational())
        {
            await dbContext.NotificationHistoryReferenceStates
                .IgnoreQueryFilters()
                .Where(state =>
                    !state.IsClosed &&
                    dbContext.UserNotificationReferences
                        .IgnoreQueryFilters()
                        .Any(assignment =>
                            notificationIds.Contains(
                                assignment.NotificationId) &&
                            assignment.ScopeId == state.ScopeId &&
                            assignment.Namespace == state.Namespace &&
                            assignment.Digest == state.Digest))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        state => state.Version,
                        state => state.Version + 1),
                    cancellationToken)
                .ConfigureAwait(false);
            removed = await dbContext.UserNotifications
                .IgnoreQueryFilters()
                .Where(notification =>
                    notificationIds.Contains(notification.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            UserNotificationReference[] assignments = await dbContext
                .UserNotificationReferences
                .IgnoreQueryFilters()
                .Where(assignment =>
                    notificationIds.Contains(
                        assignment.NotificationId))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (IGrouping<
                         (string ScopeId, string Namespace, string Digest),
                         UserNotificationReference> group in assignments
                         .GroupBy(assignment => (
                             assignment.ScopeId,
                             assignment.Namespace,
                             assignment.Digest)))
            {
                NotificationHistoryReferenceState? state = await dbContext
                    .NotificationHistoryReferenceStates
                    .IgnoreQueryFilters()
                    .SingleOrDefaultAsync(
                        candidate =>
                            candidate.ScopeId == group.Key.ScopeId &&
                            candidate.Namespace == group.Key.Namespace &&
                            candidate.Digest == group.Key.Digest,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (state is { IsClosed: false })
                {
                    state.RecordRemoval();
                }
            }

            UserNotification[] notifications = await dbContext
                .UserNotifications
                .IgnoreQueryFilters()
                .Where(notification =>
                    notificationIds.Contains(notification.Id))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            dbContext.UserNotifications.RemoveRange(notifications);
            await dbContext.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            removed = notifications.Length;
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return removed;
    }

    private static async Task<int> DeleteExpiredBroadcastsBatchAsync(
        NotificationsDbContext dbContext,
        DateTimeOffset createdBefore,
        int batchSize,
        CancellationToken cancellationToken)
    {
        Guid[] broadcastIds = await dbContext.NotificationBroadcasts
            .IgnoreQueryFilters()
            .Where(broadcast => broadcast.CreatedAtUtc < createdBefore)
            .OrderBy(broadcast => broadcast.CreatedAtUtc)
            .Select(broadcast => broadcast.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (broadcastIds.Length == 0)
        {
            return 0;
        }

        await dbContext.NotificationBroadcastReads
            .IgnoreQueryFilters()
            .Where(read => broadcastIds.Contains(read.BroadcastId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        return await dbContext.NotificationBroadcasts
            .IgnoreQueryFilters()
            .Where(broadcast => broadcastIds.Contains(broadcast.Id))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
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
