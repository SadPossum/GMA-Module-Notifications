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
        NotificationMaintenanceDbContextFactory dbContextFactory = scope.ServiceProvider
            .GetRequiredService<NotificationMaintenanceDbContextFactory>();
        await using NotificationsDbContext dbContext =
            dbContextFactory.CreateDbContext();
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
        int broadcastReadCount = await BoundedBatchProcessor.ExecuteAsync(
                settings.BatchSize,
                settings.MaxBatchesPerCategoryPerCycle,
                (batchSize, token) => DeleteExpiredBroadcastReadsBatchAsync(
                    dbContext,
                    broadcastsBefore,
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

        if (notificationCount > 0 ||
            broadcastReadCount > 0 ||
            broadcastCount > 0 ||
            attemptCount > 0)
        {
            logger.LogInformation(
                "Notification retention removed {NotificationCount} user notifications, {BroadcastReadCount} broadcast read receipts, {BroadcastCount} broadcasts, and {DeliveryAttemptCount} delivery attempts.",
                notificationCount,
                broadcastReadCount,
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
        await using IDbContextTransaction? transaction =
            await BeginSerializableTransactionAsync(dbContext, cancellationToken)
                .ConfigureAwait(false);
        var candidates = await ExpiredDeliveryAttempts(dbContext, completedBefore)
            .OrderBy(attempt => attempt.CompletedAtUtc)
            .ThenBy(attempt => attempt.Id)
            .Select(attempt => new
            {
                attempt.Id,
                attempt.ScopeId
            })
            .Take(batchSize)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (candidates.Length == 0)
        {
            return 0;
        }

        await RegisterMaintenanceScopesAsync(
                dbContext,
                candidates.Select(candidate => candidate.ScopeId),
                cancellationToken)
            .ConfigureAwait(false);
        Guid[] attemptIds = candidates
            .Select(candidate => candidate.Id)
            .ToArray();
        int removed = await dbContext.NotificationDeliveryAttempts
            .IgnoreQueryFilters()
            .Where(attempt => attemptIds.Contains(attempt.Id))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
        return removed;
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
        var candidates = await ExpiredUserNotifications(dbContext, readBefore, unreadBefore)
            .OrderBy(notification => notification.CreatedAtUtc)
            .ThenBy(notification => notification.Id)
            .Select(notification => new
            {
                notification.Id,
                notification.ScopeId
            })
            .Take(batchSize)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (candidates.Length == 0)
        {
            return 0;
        }

        Guid[] notificationIds = candidates
            .Select(candidate => candidate.Id)
            .ToArray();
        int removed;
        if (dbContext.Database.IsRelational())
        {
            await RegisterMaintenanceScopesAsync(
                    dbContext,
                    candidates.Select(candidate => candidate.ScopeId),
                    cancellationToken)
                .ConfigureAwait(false);
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

    internal static async Task<int> DeleteExpiredBroadcastReadsBatchAsync(
        NotificationsDbContext dbContext,
        DateTimeOffset createdBefore,
        int batchSize,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction? transaction =
            await BeginSerializableTransactionAsync(dbContext, cancellationToken)
                .ConfigureAwait(false);
        var candidates = await dbContext.NotificationBroadcastReads
            .IgnoreQueryFilters()
            .Where(read => dbContext.NotificationBroadcasts
                .IgnoreQueryFilters()
                .Any(broadcast =>
                    broadcast.Id == read.BroadcastId &&
                    broadcast.CreatedAtUtc < createdBefore))
            .Where(read => !dbContext.NotificationScopeStates
                .IgnoreQueryFilters()
                .Any(state =>
                    state.IsClosed &&
                    read.RecipientScope ==
                    NotificationBroadcastRead.TenantRecipientScopePrefix +
                    state.ScopeId))
            .OrderBy(read => read.ReadAtUtc)
            .ThenBy(read => read.Id)
            .Select(read => new
            {
                read.Id,
                read.RecipientScope
            })
            .Take(batchSize)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (candidates.Length == 0)
        {
            return 0;
        }

        string[] scopeIds = candidates
            .Select(candidate => candidate.RecipientScope)
            .Where(recipientScope => recipientScope.StartsWith(
                NotificationBroadcastRead.TenantRecipientScopePrefix,
                StringComparison.Ordinal))
            .Select(recipientScope => recipientScope[
                NotificationBroadcastRead.TenantRecipientScopePrefix.Length..])
            .ToArray();
        await RegisterMaintenanceScopesAsync(
                dbContext,
                scopeIds,
                cancellationToken)
            .ConfigureAwait(false);
        Guid[] readIds = candidates.Select(candidate => candidate.Id).ToArray();
        int removed = await dbContext.NotificationBroadcastReads
            .IgnoreQueryFilters()
            .Where(read => readIds.Contains(read.Id))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
        return removed;
    }

    internal static async Task<int> DeleteExpiredBroadcastsBatchAsync(
        NotificationsDbContext dbContext,
        DateTimeOffset createdBefore,
        int batchSize,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction? transaction =
            await BeginSerializableTransactionAsync(dbContext, cancellationToken)
                .ConfigureAwait(false);
        var candidates = await dbContext.NotificationBroadcasts
            .IgnoreQueryFilters()
            .Where(broadcast => broadcast.CreatedAtUtc < createdBefore)
            .Where(broadcast => !dbContext.NotificationBroadcastReads
                .IgnoreQueryFilters()
                .Any(read => read.BroadcastId == broadcast.Id))
            .Where(broadcast =>
                broadcast.ScopeId == null ||
                !dbContext.NotificationScopeStates
                    .IgnoreQueryFilters()
                    .Any(state =>
                        state.ScopeId == broadcast.ScopeId &&
                        state.IsClosed))
            .OrderBy(broadcast => broadcast.CreatedAtUtc)
            .ThenBy(broadcast => broadcast.Id)
            .Select(broadcast => new
            {
                broadcast.Id,
                broadcast.ScopeId
            })
            .Take(batchSize)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (candidates.Length == 0)
        {
            return 0;
        }

        await RegisterMaintenanceScopesAsync(
                dbContext,
                candidates
                    .Select(candidate => candidate.ScopeId)
                    .OfType<string>(),
                cancellationToken)
            .ConfigureAwait(false);
        Guid[] broadcastIds = candidates
            .Select(candidate => candidate.Id)
            .ToArray();
        int removed = await dbContext.NotificationBroadcasts
            .IgnoreQueryFilters()
            .Where(broadcast => broadcastIds.Contains(broadcast.Id))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        await CommitAsync(transaction, cancellationToken).ConfigureAwait(false);
        return removed;
    }

    internal static IQueryable<UserNotification> ExpiredUserNotifications(
        NotificationsDbContext dbContext,
        DateTimeOffset readBefore,
        DateTimeOffset unreadBefore)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        return dbContext.UserNotifications
            .IgnoreQueryFilters()
            .Where(notification => !dbContext.NotificationScopeStates
                .IgnoreQueryFilters()
                .Any(state =>
                    state.ScopeId == notification.ScopeId &&
                    state.IsClosed))
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
            .Where(attempt => !dbContext.NotificationScopeStates
                .IgnoreQueryFilters()
                .Any(state =>
                    state.ScopeId == attempt.ScopeId &&
                    state.IsClosed))
            .Where(attempt => attempt.CompletedAtUtc < completedBefore)
            .Where(attempt => !dbContext.NotificationDeliveries
                .IgnoreQueryFilters()
                .Any(delivery =>
                    delivery.Id == attempt.DeliveryId &&
                    (delivery.Status == NotificationDeliveryStatus.Pending ||
                     delivery.Status == NotificationDeliveryStatus.Processing ||
                     delivery.Status == NotificationDeliveryStatus.RetryScheduled)));
    }

    private static async Task RegisterMaintenanceScopesAsync(
        NotificationsDbContext dbContext,
        IEnumerable<string> scopeIds,
        CancellationToken cancellationToken)
    {
        string[] distinctScopeIds = scopeIds
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (distinctScopeIds.Length == 0)
        {
            return;
        }

        if (!await dbContext.TryRegisterMaintenanceScopeMutationsAsync(
                distinctScopeIds,
                cancellationToken).ConfigureAwait(false))
        {
            throw new NotificationScopeClosedException();
        }

        await dbContext.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<IDbContextTransaction?>
        BeginSerializableTransactionAsync(
            NotificationsDbContext dbContext,
            CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational() ||
            dbContext.Database.CurrentTransaction is not null)
        {
            return null;
        }

        return await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken) =>
        transaction is null
            ? Task.CompletedTask
            : transaction.CommitAsync(cancellationToken);
}
