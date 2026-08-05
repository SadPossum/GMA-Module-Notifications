namespace Gma.Modules.Notifications.Persistence;

using System.Data;
using Gma.Framework.Messaging.Infrastructure;
using Gma.Framework.Messaging;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

internal sealed class NotificationsInboxStore(
    NotificationsDbContext dbContext,
    ISystemClock clock,
    IIdGenerator idGenerator)
    : EfInboxStore<NotificationsDbContext>(
        dbContext,
        clock,
        idGenerator,
        NotificationsMigrations.Schema)
{
    protected override ValueTask<bool> IsAdmittedAsync(
        InboxMessageRecord message,
        CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(message.ScopeId)
            ? ValueTask.FromResult(true)
            : this.DbContext.TryRegisterScopeMutationAsync(
                message.ScopeId,
                cancellationToken);

    public override async Task<int> DeleteProcessedBeforeAsync(
        DateTimeOffset processedBeforeUtc,
        int maxMessages,
        CancellationToken cancellationToken)
    {
        if (processedBeforeUtc == default)
        {
            throw new ArgumentException(
                $"{nameof(processedBeforeUtc)} must not be the default timestamp.",
                nameof(processedBeforeUtc));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maxMessages, 1);
        await using IDbContextTransaction? transaction =
            this.DbContext.Database.IsRelational() &&
            this.DbContext.Database.CurrentTransaction is null
                ? await this.DbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken)
                    .ConfigureAwait(false)
                : null;
        IQueryable<InboxMessage> candidates = this.DbContext.InboxMessages
            .Where(message =>
                message.Status == InboxMessageStatus.Processed &&
                message.ProcessedAtUtc != null &&
                message.ProcessedAtUtc < processedBeforeUtc)
            .Where(message =>
                message.ScopeId == null ||
                !this.DbContext.NotificationScopeStates
                    .IgnoreQueryFilters()
                    .Any(state =>
                        state.ScopeId == message.ScopeId &&
                        state.IsClosed))
            .OrderBy(message => message.ProcessedAtUtc)
            .ThenBy(message => message.Id)
            .ThenBy(message => message.Handler)
            .Take(maxMessages);

        string[] scopeIds = await candidates
            .Where(message => message.ScopeId != null)
            .Select(message => message.ScopeId!)
            .Distinct()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        if (scopeIds.Length > 0)
        {
            if (!await this.DbContext
                    .TryRegisterMaintenanceScopeMutationsAsync(
                        scopeIds,
                        cancellationToken).ConfigureAwait(false))
            {
                throw new NotificationScopeClosedException();
            }

            await this.DbContext.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        int removed = await candidates
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return removed;
    }
}
