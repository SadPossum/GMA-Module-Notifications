namespace Gma.Modules.Notifications.Persistence;

using Gma.Modules.Notifications.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

internal sealed class NotificationStreamMonitorService(
    IServiceScopeFactory scopeFactory,
    NotificationStreamPulse pulse,
    IOptions<NotificationStreamOptions> options,
    ILogger<NotificationStreamMonitorService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await this.RefreshAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "Notification stream monitor refresh failed with {ExceptionType}; connected streams will use heartbeat recovery.",
                    exception.GetType().Name);
            }

            try
            {
                await Task.Delay(options.Value.PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    internal async Task RefreshAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        long historyVersion = await dbContext.UserNotifications
            .IgnoreQueryFilters()
            .AsNoTracking()
            .MaxAsync(notification => (long?)notification.StreamSequence, cancellationToken)
            .ConfigureAwait(false) ?? 0;
        long broadcastVersion = await dbContext.NotificationBroadcasts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .MaxAsync(broadcast => (long?)broadcast.StreamSequence, cancellationToken)
            .ConfigureAwait(false) ?? 0;

        pulse.Advance(NotificationStreamKind.History, historyVersion);
        pulse.Advance(NotificationStreamKind.Broadcasts, broadcastVersion);
    }
}
