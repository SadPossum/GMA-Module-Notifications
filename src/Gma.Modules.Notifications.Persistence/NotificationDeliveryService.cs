namespace Gma.Modules.Notifications.Persistence;

using System.Data;
using System.Text.Json;
using Gma.Framework.Notifications;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Framework.Runtime.Workers;
using Gma.Modules.Notifications.Application;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DomainDeliveryPolicy = Domain.ValueObjects.NotificationDeliveryPolicy;
using DomainSeverity = Domain.ValueObjects.NotificationSeverity;
using FrameworkDeliveryPolicy = Framework.Notifications.NotificationDeliveryPolicy;
using FrameworkSeverity = Framework.Notifications.NotificationSeverity;

internal sealed class NotificationDeliveryService(
    IServiceScopeFactory scopeFactory,
    INotificationDeliveryAdapterCatalog adapterCatalog,
    ISystemClock clock,
    IIdGenerator idGenerator,
    IOptions<NotificationDeliveryOptions> options,
    NotificationDeliveryMetrics metrics,
    ILogger<NotificationDeliveryService> logger)
    : BackgroundService
{
    private const string ExpiredLeaseCode = "worker-lease-expired";
    private readonly string workerId = CreateWorkerId(options.Value.WorkerId, idGenerator);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int processedCount = 0;
            try
            {
                processedCount = await this.ProcessAvailableBatchAsync(stoppingToken).ConfigureAwait(false);
                await this.RefreshBacklogAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Notification delivery worker iteration failed with {ExceptionType}; pending leases will expire safely.",
                    exception.GetType().Name);
            }

            if (processedCount == options.Value.BatchSize)
            {
                await Task.Yield();
                continue;
            }

            try
            {
                await Task.Delay(
                        TimeSpan.FromSeconds(options.Value.PollIntervalSeconds),
                        stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    internal async Task<int> ProcessAvailableBatchAsync(CancellationToken cancellationToken)
    {
        int processedCount = 0;
        while (processedCount < options.Value.BatchSize)
        {
            int waveSize = Math.Min(
                options.Value.MaxConcurrency,
                options.Value.BatchSize - processedCount);
            ClaimBatch claim = await this.ClaimBatchAsync(
                    waveSize,
                    cancellationToken)
                .ConfigureAwait(false);
            if (claim.DeliveryIds.Length == 0 && claim.TerminalRecoveryCount == 0)
            {
                break;
            }

            await Task.WhenAll(claim.DeliveryIds.Select(
                    deliveryId => this.DeliverAsync(
                        deliveryId,
                        cancellationToken)))
                .ConfigureAwait(false);
            processedCount += claim.DeliveryIds.Length +
                claim.TerminalRecoveryCount;
        }

        return processedCount;
    }

    internal async Task<Guid[]> ClaimAsync(int maximumCount, CancellationToken cancellationToken)
    {
        ClaimBatch claim = await this.ClaimBatchAsync(
                maximumCount,
                cancellationToken)
            .ConfigureAwait(false);
        return claim.DeliveryIds;
    }

    private async Task<ClaimBatch> ClaimBatchAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCount, 1);

        using IServiceScope scope = scopeFactory.CreateScope();
        NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        DateTimeOffset nowUtc = clock.UtcNow;
        DateTimeOffset lockedUntilUtc = nowUtc.AddSeconds(options.Value.LeaseSeconds);

        int claimLimit = Math.Min(maximumCount, options.Value.MaxConcurrency);
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        if (dbContext.Database.IsRelational())
        {
            transaction = await dbContext.Database
                .BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                .ConfigureAwait(false);
        }

        await using (transaction)
        {
            NotificationDelivery[] candidates = await LoadClaimCandidatesAsync(
                    dbContext,
                    nowUtc,
                    claimLimit,
                    cancellationToken)
                .ConfigureAwait(false);

            List<Guid> claimed = [];
            List<string> expiredLeaseProviders = [];
            int terminalRecoveryCount = 0;
            foreach (NotificationDelivery delivery in candidates)
            {
                if (delivery.Status == NotificationDeliveryStatus.Processing &&
                    delivery.LockedUntilUtc is not null &&
                    delivery.LockedUntilUtc <= nowUtc)
                {
                    await this.RecordExpiredLeaseAsync(
                            dbContext,
                            delivery,
                            nowUtc,
                            cancellationToken)
                        .ConfigureAwait(false);
                    expiredLeaseProviders.Add(delivery.Provider.Value);
                    if (delivery.Status == NotificationDeliveryStatus.Exhausted)
                    {
                        terminalRecoveryCount++;
                    }
                }

                if (delivery.Claim(this.workerId, nowUtc, lockedUntilUtc - nowUtc).IsSuccess)
                {
                    claimed.Add(delivery.Id);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            foreach (string provider in expiredLeaseProviders)
            {
                metrics.RecordAttempt(
                    provider,
                    NotificationRoutingSemanticNames.AttemptOutcome(
                        NotificationDeliveryAttemptOutcome.Exception),
                    TimeSpan.Zero);
            }

            return new ClaimBatch(
                claimed.ToArray(),
                terminalRecoveryCount);
        }
    }

    internal async Task DeliverAsync(Guid deliveryId, CancellationToken stoppingToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        NotificationDelivery? delivery = await dbContext.NotificationDeliveries
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                item =>
                    item.Id == deliveryId &&
                    item.LockedBy == this.workerId &&
                    !dbContext.NotificationScopeStates
                        .IgnoreQueryFilters()
                        .Any(state =>
                            state.ScopeId == item.ScopeId &&
                            state.IsClosed),
                stoppingToken)
            .ConfigureAwait(false);
        if (delivery is null)
        {
            return;
        }

        UserNotification? notification = await dbContext.UserNotifications
            .IgnoreQueryFilters()
            .Include(item => item.Tags)
            .SingleOrDefaultAsync(item => item.Id == delivery.NotificationId, stoppingToken)
            .ConfigureAwait(false);
        if (notification is null)
        {
            await this.CompleteWithoutAdapterAsync(
                    dbContext,
                    delivery,
                    NotificationDeliveryAttemptOutcome.Rejected,
                    "notification-missing",
                    stoppingToken)
                .ConfigureAwait(false);
            return;
        }

        IUserNotificationSink? adapter = adapterCatalog.GetProvider(delivery.Provider.Value);
        if (adapter is null || !adapterCatalog.Supports(delivery.Provider.Value, delivery.DeliveryTag.Value))
        {
            await this.CompleteRetryAsync(
                    dbContext,
                    delivery,
                    NotificationDeliveryAttemptOutcome.Retry,
                    "provider-unavailable",
                    retryAtUtc: null,
                    providerMessageId: null,
                    stoppingToken)
                .ConfigureAwait(false);
            return;
        }

        DateTimeOffset startedAtUtc = clock.UtcNow;
        NotificationSinkDeliveryResult result;
        NotificationDeliveryAttemptOutcome attemptOutcome;
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.LeaseSeconds - 1)));
            result = await adapter.DeliverAsync(
                    new NotificationSinkDeliveryRequest(
                        delivery.Id,
                        ToMessage(notification),
                        delivery.Attempts,
                        isDurable: true),
                    timeout.Token)
                .ConfigureAwait(false);
            attemptOutcome = result.Outcome switch
            {
                NotificationSinkDeliveryOutcome.Delivered => NotificationDeliveryAttemptOutcome.Delivered,
                NotificationSinkDeliveryOutcome.Retry => NotificationDeliveryAttemptOutcome.Retry,
                NotificationSinkDeliveryOutcome.Rejected or NotificationSinkDeliveryOutcome.Skipped =>
                    NotificationDeliveryAttemptOutcome.Rejected,
                _ => NotificationDeliveryAttemptOutcome.Exception
            };
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            result = NotificationSinkDeliveryResult.Retry("adapter-timeout");
            attemptOutcome = NotificationDeliveryAttemptOutcome.Exception;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Notification delivery through {Provider} failed with {ExceptionType}; no exception text was persisted.",
                delivery.Provider.Value,
                exception.GetType().Name);
            result = NotificationSinkDeliveryResult.Retry("adapter-exception");
            attemptOutcome = NotificationDeliveryAttemptOutcome.Exception;
        }

        switch (result.Outcome)
        {
            case NotificationSinkDeliveryOutcome.Delivered:
                await this.CompleteDeliveredAsync(
                        dbContext,
                        delivery,
                        attemptOutcome,
                        result.ProviderMessageId,
                        startedAtUtc,
                        stoppingToken)
                    .ConfigureAwait(false);
                break;
            case NotificationSinkDeliveryOutcome.Rejected:
            case NotificationSinkDeliveryOutcome.Skipped:
                await this.CompleteRejectedAsync(
                        dbContext,
                        delivery,
                        attemptOutcome,
                        result.Code ?? "adapter-rejected",
                        startedAtUtc,
                        stoppingToken)
                    .ConfigureAwait(false);
                break;
            case NotificationSinkDeliveryOutcome.Retry:
                await this.CompleteRetryAsync(
                        dbContext,
                        delivery,
                        attemptOutcome,
                        result.Code ?? "adapter-retry",
                        result.RetryAtUtc,
                        result.ProviderMessageId,
                        stoppingToken,
                        startedAtUtc)
                    .ConfigureAwait(false);
                break;
            case NotificationSinkDeliveryOutcome.Unknown:
            default:
                throw new InvalidOperationException("Notification adapter returned an unknown delivery outcome.");
        }
    }

    private async Task CompleteDeliveredAsync(
        NotificationsDbContext dbContext,
        NotificationDelivery delivery,
        NotificationDeliveryAttemptOutcome outcome,
        string? providerMessageId,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken)
    {
        DateTimeOffset completedAtUtc = clock.UtcNow;
        if (delivery.MarkDelivered(this.workerId, completedAtUtc, providerMessageId).IsFailure)
        {
            return;
        }

        await this.AddAttemptAsync(dbContext, delivery, outcome, startedAtUtc, completedAtUtc, code: null, providerMessageId, cancellationToken)
            .ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        metrics.RecordAttempt(
            delivery.Provider.Value,
            NotificationRoutingSemanticNames.AttemptOutcome(outcome),
            completedAtUtc - startedAtUtc);
    }

    private async Task CompleteRejectedAsync(
        NotificationsDbContext dbContext,
        NotificationDelivery delivery,
        NotificationDeliveryAttemptOutcome outcome,
        string code,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken)
    {
        DateTimeOffset completedAtUtc = clock.UtcNow;
        if (delivery.MarkRejected(this.workerId, completedAtUtc, code).IsFailure)
        {
            return;
        }

        await this.AddAttemptAsync(dbContext, delivery, outcome, startedAtUtc, completedAtUtc, code, providerMessageId: null, cancellationToken)
            .ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        metrics.RecordAttempt(
            delivery.Provider.Value,
            NotificationRoutingSemanticNames.AttemptOutcome(outcome),
            completedAtUtc - startedAtUtc);
    }

    private async Task CompleteRetryAsync(
        NotificationsDbContext dbContext,
        NotificationDelivery delivery,
        NotificationDeliveryAttemptOutcome outcome,
        string code,
        DateTimeOffset? retryAtUtc,
        string? providerMessageId,
        CancellationToken cancellationToken,
        DateTimeOffset? startedAtUtc = null)
    {
        DateTimeOffset completedAtUtc = clock.UtcNow;
        DateTimeOffset requestedRetryAt = retryAtUtc is not null && retryAtUtc > completedAtUtc
            ? retryAtUtc.Value
            : completedAtUtc.Add(RetryDelay(delivery.Attempts, options.Value));
        DateTimeOffset maximumRetryAt = completedAtUtc.AddMinutes(options.Value.RetryMaxMinutes);
        DateTimeOffset retryAt = requestedRetryAt <= maximumRetryAt
            ? requestedRetryAt
            : maximumRetryAt;
        if (delivery.MarkRetry(this.workerId, completedAtUtc, code, retryAt).IsFailure)
        {
            return;
        }

        await this.AddAttemptAsync(
                dbContext,
                delivery,
                outcome,
                startedAtUtc ?? completedAtUtc,
                completedAtUtc,
                code,
                providerMessageId,
                cancellationToken)
            .ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        metrics.RecordAttempt(
            delivery.Provider.Value,
            NotificationRoutingSemanticNames.AttemptOutcome(outcome),
            completedAtUtc - (startedAtUtc ?? completedAtUtc));
    }

    private async Task RefreshBacklogAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        NotificationsDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        DateTimeOffset nowUtc = clock.UtcNow;
        IQueryable<NotificationDelivery> pending = dbContext.NotificationDeliveries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(delivery =>
                delivery.Status == NotificationDeliveryStatus.Pending ||
                delivery.Status == NotificationDeliveryStatus.RetryScheduled ||
                delivery.Status == NotificationDeliveryStatus.Processing);
        long pendingCount = await pending.LongCountAsync(cancellationToken).ConfigureAwait(false);
        DateTimeOffset? oldest = await pending
            .MinAsync(delivery => (DateTimeOffset?)delivery.CreatedAtUtc, cancellationToken)
            .ConfigureAwait(false);
        long exhaustedCount = await dbContext.NotificationDeliveries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .LongCountAsync(
                delivery => delivery.Status == NotificationDeliveryStatus.Exhausted,
                cancellationToken)
            .ConfigureAwait(false);

        metrics.RecordBacklog(new NotificationDeliveryBacklogSnapshot(
            pendingCount,
            exhaustedCount,
            oldest is null ? TimeSpan.Zero : nowUtc - oldest.Value));
    }

    private Task CompleteWithoutAdapterAsync(
        NotificationsDbContext dbContext,
        NotificationDelivery delivery,
        NotificationDeliveryAttemptOutcome outcome,
        string code,
        CancellationToken cancellationToken) =>
        this.CompleteRejectedAsync(dbContext, delivery, outcome, code, clock.UtcNow, cancellationToken);

    private async Task RecordExpiredLeaseAsync(
        NotificationsDbContext dbContext,
        NotificationDelivery delivery,
        DateTimeOffset recoveredAtUtc,
        CancellationToken cancellationToken)
    {
        int attemptNumber = delivery.Attempts;
        Framework.Results.Result expired = delivery.RecordExpiredLease(
            recoveredAtUtc,
            ExpiredLeaseCode);
        if (expired.IsFailure)
        {
            throw new InvalidOperationException(
                $"An expired notification delivery lease could not be recorded: {expired.Error.Code}.");
        }

        await this.AddAttemptAsync(
                dbContext,
                delivery,
                attemptNumber,
                NotificationDeliveryAttemptOutcome.Exception,
                recoveredAtUtc,
                recoveredAtUtc,
                ExpiredLeaseCode,
                providerMessageId: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private Task AddAttemptAsync(
        NotificationsDbContext dbContext,
        NotificationDelivery delivery,
        NotificationDeliveryAttemptOutcome outcome,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        string? code,
        string? providerMessageId,
        CancellationToken cancellationToken) =>
        this.AddAttemptAsync(
            dbContext,
            delivery,
            delivery.Attempts,
            outcome,
            startedAtUtc,
            completedAtUtc,
            code,
            providerMessageId,
            cancellationToken);

    private async Task AddAttemptAsync(
        NotificationsDbContext dbContext,
        NotificationDelivery delivery,
        int attemptNumber,
        NotificationDeliveryAttemptOutcome outcome,
        DateTimeOffset startedAtUtc,
        DateTimeOffset completedAtUtc,
        string? code,
        string? providerMessageId,
        CancellationToken cancellationToken)
    {
        Framework.Results.Result<NotificationDeliveryAttempt> attempt = NotificationDeliveryAttempt.Create(
            idGenerator.NewId(),
            delivery.ScopeId,
            delivery.Id,
            attemptNumber,
            delivery.Provider.Value,
            outcome,
            startedAtUtc,
            completedAtUtc,
            code,
            providerMessageId);
        if (attempt.IsFailure)
        {
            throw new InvalidOperationException($"Notification delivery attempt is invalid: {attempt.Error.Code}.");
        }

        await dbContext.NotificationDeliveryAttempts.AddAsync(attempt.Value, cancellationToken).ConfigureAwait(false);
    }

    private static UserNotificationMessage ToMessage(UserNotification notification)
    {
        using JsonDocument payload = JsonDocument.Parse(notification.Payload.Json);
        return new UserNotificationMessage(
            notification.Id,
            notification.Source.Module,
            notification.Source.Name,
            notification.Source.Version,
            notification.ScopeId,
            notification.Recipient.UserId,
            notification.Content.Title,
            notification.Content.Body,
            ToFrameworkSeverity(notification.Severity),
            notification.OccurredAtUtc,
            payload.RootElement.Clone(),
            notification.Tags.Select(tag => tag.Key.Value).ToArray(),
            ToFrameworkPolicy(notification.DeliveryPolicy));
    }

    private static FrameworkSeverity ToFrameworkSeverity(DomainSeverity severity) => severity switch
    {
        DomainSeverity.Info => FrameworkSeverity.Info,
        DomainSeverity.Success => FrameworkSeverity.Success,
        DomainSeverity.Warning => FrameworkSeverity.Warning,
        DomainSeverity.Error => FrameworkSeverity.Error,
        _ => throw new InvalidOperationException("Stored notification severity is invalid.")
    };

    private static FrameworkDeliveryPolicy ToFrameworkPolicy(DomainDeliveryPolicy policy) => policy switch
    {
        DomainDeliveryPolicy.RespectPreferences => FrameworkDeliveryPolicy.RespectPreferences,
        DomainDeliveryPolicy.Mandatory => FrameworkDeliveryPolicy.Mandatory,
        _ => throw new InvalidOperationException("Stored notification delivery policy is invalid.")
    };

    private static TimeSpan RetryDelay(int attempts, NotificationDeliveryOptions settings)
    {
        double multiplier = Math.Pow(2, Math.Clamp(attempts - 1, 0, 10));
        TimeSpan delay = TimeSpan.FromSeconds(settings.RetryBaseSeconds * multiplier);
        TimeSpan maximum = TimeSpan.FromMinutes(settings.RetryMaxMinutes);
        return delay <= maximum ? delay : maximum;
    }

    private static string CreateWorkerId(string? configured, IIdGenerator idGenerator) =>
        string.IsNullOrWhiteSpace(configured)
            ? WorkerIds.Create(Environment.MachineName, idGenerator.NewId())
            : WorkerIds.Normalize(configured);

    private static Task<NotificationDelivery[]> LoadClaimCandidatesAsync(
        NotificationsDbContext dbContext,
        DateTimeOffset nowUtc,
        int claimLimit,
        CancellationToken cancellationToken)
    {
        string pending = NotificationRoutingSemanticNames.DeliveryStatus(NotificationDeliveryStatus.Pending);
        string retryScheduled = NotificationRoutingSemanticNames.DeliveryStatus(NotificationDeliveryStatus.RetryScheduled);
        string processing = NotificationRoutingSemanticNames.DeliveryStatus(NotificationDeliveryStatus.Processing);

        if (dbContext.Database.IsNpgsql())
        {
            return dbContext.NotificationDeliveries
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM "notifications"."deliveries" AS delivery
                    WHERE ("Attempts" < "MaxAttempts"
                       OR ("Status" = {processing}
                           AND "LockedUntilUtc" IS NOT NULL
                           AND "LockedUntilUtc" <= {nowUtc})
                      )
                      AND "Status" IN ({pending}, {retryScheduled}, {processing})
                      AND ("NextAttemptAtUtc" IS NULL OR "NextAttemptAtUtc" <= {nowUtc})
                      AND ("LockedUntilUtc" IS NULL OR "LockedUntilUtc" <= {nowUtc})
                      AND NOT EXISTS (
                          SELECT 1
                          FROM "notifications"."notification_scope_states" AS scope_state
                          WHERE scope_state."ScopeId" = delivery."ScopeId"
                            AND scope_state."IsClosed")
                    ORDER BY "NextAttemptAtUtc" NULLS FIRST, "CreatedAtUtc"
                    LIMIT {claimLimit}
                    FOR UPDATE SKIP LOCKED
                    """)
                .IgnoreQueryFilters()
                .ToArrayAsync(cancellationToken);
        }

        if (dbContext.Database.IsSqlServer())
        {
            return dbContext.NotificationDeliveries
                .FromSqlInterpolated($"""
                    SELECT TOP ({claimLimit}) *
                    FROM [notifications].[deliveries] AS [delivery] WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE ([Attempts] < [MaxAttempts]
                       OR ([Status] = {processing}
                           AND [LockedUntilUtc] IS NOT NULL
                           AND [LockedUntilUtc] <= {nowUtc}))
                      AND [Status] IN ({pending}, {retryScheduled}, {processing})
                      AND ([NextAttemptAtUtc] IS NULL OR [NextAttemptAtUtc] <= {nowUtc})
                      AND ([LockedUntilUtc] IS NULL OR [LockedUntilUtc] <= {nowUtc})
                      AND NOT EXISTS (
                          SELECT 1
                          FROM [notifications].[notification_scope_states] AS [scope_state]
                          WHERE [scope_state].[ScopeId] = [delivery].[ScopeId]
                            AND [scope_state].[IsClosed] = 1)
                    ORDER BY [Status], [NextAttemptAtUtc], [LockedUntilUtc], [CreatedAtUtc]
                    """)
                .IgnoreQueryFilters()
                .ToArrayAsync(cancellationToken);
        }

        return dbContext.NotificationDeliveries
            .IgnoreQueryFilters()
            .Where(delivery =>
                (delivery.Attempts < delivery.MaxAttempts ||
                 (delivery.Status == NotificationDeliveryStatus.Processing &&
                  delivery.LockedUntilUtc != null &&
                  delivery.LockedUntilUtc <= nowUtc)) &&
                (delivery.Status == NotificationDeliveryStatus.Pending ||
                 delivery.Status == NotificationDeliveryStatus.RetryScheduled ||
                 delivery.Status == NotificationDeliveryStatus.Processing) &&
                (delivery.NextAttemptAtUtc == null || delivery.NextAttemptAtUtc <= nowUtc) &&
                (delivery.LockedUntilUtc == null || delivery.LockedUntilUtc <= nowUtc) &&
                !dbContext.NotificationScopeStates
                    .IgnoreQueryFilters()
                    .Any(state =>
                        state.ScopeId == delivery.ScopeId &&
                        state.IsClosed))
            .OrderBy(delivery => delivery.NextAttemptAtUtc)
            .ThenBy(delivery => delivery.CreatedAtUtc)
            .Take(claimLimit)
            .ToArrayAsync(cancellationToken);
    }

    private sealed record ClaimBatch(
        Guid[] DeliveryIds,
        int TerminalRecoveryCount);
}
