namespace Gma.Modules.Notifications.Persistence;

using Gma.Framework.Messaging.Infrastructure;
using Gma.Framework.Domain;
using Gma.Framework.Naming;
using Gma.Framework.Persistence.EntityFrameworkCore;
using Gma.Framework.Scoping;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Microsoft.EntityFrameworkCore;

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options, IScopeContext scopeContext)
    : ScopeAwareDbContext<NotificationsDbContext>(options, scopeContext)
{
    private const int ScopeStateQueryBatchSize = 500;
    private const int ScopeStateConcurrencyAttemptLimit = 8;

    public DbSet<UserNotification> UserNotifications => this.Set<UserNotification>();
    public DbSet<UserNotificationTag> UserNotificationTags => this.Set<UserNotificationTag>();
    public DbSet<UserNotificationReference> UserNotificationReferences =>
        this.Set<UserNotificationReference>();
    public DbSet<NotificationHistoryReferenceState>
        NotificationHistoryReferenceStates =>
        this.Set<NotificationHistoryReferenceState>();
    public DbSet<NotificationHistoryCloseReceipt>
        NotificationHistoryCloseReceipts =>
        this.Set<NotificationHistoryCloseReceipt>();
    public DbSet<NotificationHistoryBatchCloseOperation>
        NotificationHistoryBatchCloseOperations =>
        this.Set<NotificationHistoryBatchCloseOperation>();
    public DbSet<NotificationHistoryBatchCloseReceipt>
        NotificationHistoryBatchCloseReceipts =>
        this.Set<NotificationHistoryBatchCloseReceipt>();
    public DbSet<NotificationScopeState> NotificationScopeStates =>
        this.Set<NotificationScopeState>();
    public DbSet<NotificationScopeDestroyOperation>
        NotificationScopeDestroyOperations =>
        this.Set<NotificationScopeDestroyOperation>();
    public DbSet<NotificationScopeDestroyReceipt>
        NotificationScopeDestroyReceipts =>
        this.Set<NotificationScopeDestroyReceipt>();
    public DbSet<NotificationTagDefinition> NotificationTagDefinitions => this.Set<NotificationTagDefinition>();
    public DbSet<NotificationPreference> NotificationPreferences => this.Set<NotificationPreference>();
    public DbSet<NotificationDeliveryRoute> NotificationDeliveryRoutes => this.Set<NotificationDeliveryRoute>();
    public DbSet<NotificationDelivery> NotificationDeliveries => this.Set<NotificationDelivery>();
    public DbSet<NotificationDeliveryAttempt> NotificationDeliveryAttempts => this.Set<NotificationDeliveryAttempt>();
    public DbSet<NotificationBroadcast> NotificationBroadcasts => this.Set<NotificationBroadcast>();
    public DbSet<NotificationBroadcastRead> NotificationBroadcastReads => this.Set<NotificationBroadcastRead>();
    public DbSet<InboxMessage> InboxMessages => this.Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(NotificationsMigrations.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsDbContext).Assembly);
        this.ApplyScopeConventions(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.RegisterTrackedScopeMutations();
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return base.SaveChanges(acceptAllChangesOnSuccess);
            }
            catch (DbUpdateConcurrencyException exception)
                when (attempt < ScopeStateConcurrencyAttemptLimit)
            {
                if (!TryRebaseOpenScopeStates(exception))
                {
                    throw;
                }
            }
        }
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        await this.RegisterTrackedScopeMutationsAsync(cancellationToken)
            .ConfigureAwait(false);
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await base.SaveChangesAsync(
                        acceptAllChangesOnSuccess,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException exception)
                when (attempt < ScopeStateConcurrencyAttemptLimit)
            {
                if (!await TryRebaseOpenScopeStatesAsync(
                        exception,
                        cancellationToken).ConfigureAwait(false))
                {
                    throw;
                }
            }
        }
    }

    private static bool TryRebaseOpenScopeStates(
        DbUpdateConcurrencyException exception)
    {
        if (!HasOnlyOpenScopeStateEntries(exception))
        {
            return false;
        }

        foreach (var entry in exception.Entries)
        {
            var databaseValues = entry.GetDatabaseValues();
            if (databaseValues is null)
            {
                return false;
            }

            entry.CurrentValues.SetValues(databaseValues);
            entry.OriginalValues.SetValues(databaseValues);
            entry.State = EntityState.Unchanged;
            if (!((NotificationScopeState)entry.Entity).RegisterMutation())
            {
                throw new NotificationScopeClosedException();
            }
        }

        return true;
    }

    private static async Task<bool> TryRebaseOpenScopeStatesAsync(
        DbUpdateConcurrencyException exception,
        CancellationToken cancellationToken)
    {
        if (!HasOnlyOpenScopeStateEntries(exception))
        {
            return false;
        }

        foreach (var entry in exception.Entries)
        {
            var databaseValues = await entry
                .GetDatabaseValuesAsync(cancellationToken)
                .ConfigureAwait(false);
            if (databaseValues is null)
            {
                return false;
            }

            entry.CurrentValues.SetValues(databaseValues);
            entry.OriginalValues.SetValues(databaseValues);
            entry.State = EntityState.Unchanged;
            if (!((NotificationScopeState)entry.Entity).RegisterMutation())
            {
                throw new NotificationScopeClosedException();
            }
        }

        return true;
    }

    private static bool HasOnlyOpenScopeStateEntries(
        DbUpdateConcurrencyException exception) =>
        exception.Entries.Count > 0 &&
        exception.Entries.All(entry =>
            entry.Entity is NotificationScopeState { IsClosed: false });

    internal async ValueTask<bool> TryRegisterScopeMutationAsync(
        string scopeId,
        CancellationToken cancellationToken) =>
        await this.TryRegisterScopeMutationsAsync(
                [scopeId],
                requireActiveScope: true,
                cancellationToken)
            .ConfigureAwait(false);

    internal Task<bool> TryRegisterMaintenanceScopeMutationsAsync(
        IEnumerable<string> scopeIds,
        CancellationToken cancellationToken) =>
        this.TryRegisterScopeMutationsAsync(
            scopeIds,
            requireActiveScope: false,
            cancellationToken);

    internal async Task<int> SaveScopeDestructionChangesAsync(
        string scopeId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        string normalizedScope = this.RequireMutationScope(
            scopeId,
            requireActiveScope: true);
        this.ChangeTracker.DetectChanges();
        NotificationScopeState? state = this.NotificationScopeStates.Local
            .SingleOrDefault(candidate =>
                candidate.ScopeId == normalizedScope);
        if (operationId == Guid.Empty ||
            state is null ||
            !state.IsClosed ||
            state.CloseOperationId != operationId)
        {
            throw new InvalidOperationException(
                "Notification scope destruction state is unavailable.");
        }

        foreach (var entry in this.ChangeTracker.Entries().Where(entry =>
                     entry.State is EntityState.Added or
                         EntityState.Modified or EntityState.Deleted))
        {
            bool allowed = entry.Entity switch
            {
                NotificationScopeState candidate =>
                    entry.State is EntityState.Added or EntityState.Modified &&
                    candidate.ScopeId == normalizedScope &&
                    candidate.IsClosed &&
                    candidate.CloseOperationId == operationId,
                NotificationScopeDestroyOperation operation =>
                    operation.ScopeId == normalizedScope &&
                    operation.OperationId == operationId,
                NotificationScopeDestroyReceipt receipt =>
                    entry.State == EntityState.Added &&
                    receipt.ScopeId == normalizedScope &&
                    receipt.OperationId == operationId,
                _ => entry.State == EntityState.Deleted &&
                    string.Equals(
                        DestructionScopeId(entry.Entity),
                        normalizedScope,
                        StringComparison.Ordinal)
            };
            if (!allowed)
            {
                throw new InvalidOperationException(
                    "Notification scope destruction attempted an invalid write.");
            }
        }

        return await base.SaveChangesAsync(
                acceptAllChangesOnSuccess: true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private void RegisterTrackedScopeMutations()
    {
        string[] scopeIds = this.ChangedScopeIds();
        Dictionary<string, NotificationScopeState> states =
            this.LoadScopeStates(scopeIds);
        foreach (string scopeId in scopeIds)
        {
            NotificationScopeState state = states[scopeId];
            if (!state.RegisterMutation())
            {
                throw new NotificationScopeClosedException();
            }
        }
    }

    private async Task RegisterTrackedScopeMutationsAsync(
        CancellationToken cancellationToken)
    {
        string[] scopeIds = this.ChangedScopeIds();
        if (!await this.TryRegisterScopeMutationsAsync(
                scopeIds,
                requireActiveScope: false,
                cancellationToken).ConfigureAwait(false))
        {
            throw new NotificationScopeClosedException();
        }
    }

    private string[] ChangedScopeIds() =>
        this.ChangeTracker.Entries()
            .Where(entry => entry.State is
                EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(entry => ScopeId(entry.Entity))
            .Where(scopeId => scopeId is not null)
            .Select(scopeId => this.RequireMutationScope(
                scopeId!,
                requireActiveScope: false))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string? ScopeId(object entity) =>
        entity switch
        {
            NotificationScopeState or
            NotificationScopeDestroyOperation or
            NotificationScopeDestroyReceipt => null,
            InboxMessage => null,
            IScopedEntity scoped => scoped.ScopeId,
            NotificationBroadcast { ScopeId: not null } broadcast =>
                broadcast.ScopeId,
            NotificationBroadcastRead read
                when read.TryGetTenantScopeId(out string? scopeId) => scopeId,
            _ => null
        };

    private static string? DestructionScopeId(object entity) =>
        entity switch
        {
            InboxMessage message => message.ScopeId,
            NotificationBroadcastRead read
                when read.TryGetTenantScopeId(out string? scopeId) => scopeId,
            NotificationBroadcast broadcast => broadcast.ScopeId,
            NotificationPreference preference => preference.ScopeId,
            NotificationDeliveryRoute route => route.ScopeId,
            NotificationTagDefinition definition => definition.ScopeId,
            UserNotification notification => notification.ScopeId,
            UserNotificationTag tag => tag.ScopeId,
            UserNotificationReference reference => reference.ScopeId,
            NotificationDelivery delivery => delivery.ScopeId,
            NotificationDeliveryAttempt attempt => attempt.ScopeId,
            _ => null
        };

    private Dictionary<string, NotificationScopeState> LoadScopeStates(
        IReadOnlyCollection<string> scopeIds)
    {
        Dictionary<string, NotificationScopeState> states =
            this.NotificationScopeStates.Local
                .Where(state => scopeIds.Contains(state.ScopeId))
                .ToDictionary(state => state.ScopeId, StringComparer.Ordinal);
        string[] missingScopeIds = scopeIds
            .Where(scopeId => !states.ContainsKey(scopeId))
            .ToArray();
        foreach (string[] batch in missingScopeIds.Chunk(ScopeStateQueryBatchSize))
        {
            foreach (NotificationScopeState state in this.NotificationScopeStates
                         .Where(candidate => batch.Contains(candidate.ScopeId)))
            {
                states.Add(state.ScopeId, state);
            }
        }

        foreach (string scopeId in scopeIds.Where(scopeId =>
                     !states.ContainsKey(scopeId)))
        {
            NotificationScopeState state =
                NotificationScopeState.Create(scopeId).Value;
            this.NotificationScopeStates.Add(state);
            states.Add(scopeId, state);
        }

        return states;
    }

    private async Task<bool> TryRegisterScopeMutationsAsync(
        IEnumerable<string> scopeIds,
        bool requireActiveScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeIds);
        string[] normalizedScopeIds = scopeIds
            .Select(scopeId => this.RequireMutationScope(
                scopeId,
                requireActiveScope))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (normalizedScopeIds.Length == 0)
        {
            return true;
        }

        Dictionary<string, NotificationScopeState> states =
            this.NotificationScopeStates.Local
                .Where(state => normalizedScopeIds.Contains(state.ScopeId))
                .ToDictionary(state => state.ScopeId, StringComparer.Ordinal);
        string[] missingScopeIds = normalizedScopeIds
            .Where(scopeId => !states.ContainsKey(scopeId))
            .ToArray();
        foreach (string[] batch in missingScopeIds.Chunk(ScopeStateQueryBatchSize))
        {
            NotificationScopeState[] loaded = await this.NotificationScopeStates
                .Where(state => batch.Contains(state.ScopeId))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (NotificationScopeState state in loaded)
            {
                states.Add(state.ScopeId, state);
            }
        }

        foreach (string scopeId in normalizedScopeIds.Where(scopeId =>
                     !states.ContainsKey(scopeId)))
        {
            NotificationScopeState state =
                NotificationScopeState.Create(scopeId).Value;
            await this.NotificationScopeStates.AddAsync(state, cancellationToken)
                .ConfigureAwait(false);
            states.Add(scopeId, state);
        }

        if (states.Values.Any(state =>
                state.IsClosed || state.Version == long.MaxValue))
        {
            return false;
        }

        foreach (NotificationScopeState state in states.Values)
        {
            if (!state.RegisterMutation())
            {
                throw new InvalidOperationException(
                    "Notification scope mutation state changed unexpectedly.");
            }
        }

        return true;
    }

    private string RequireMutationScope(
        string scopeId,
        bool requireActiveScope)
    {
        if (!ScopeIds.TryNormalize(scopeId, out string? normalizedScopeId) ||
            ((requireActiveScope || this.ScopeFilterEnabled) &&
             (!this.ScopeFilterEnabled ||
              !string.Equals(
                  this.CurrentScopeId,
                  normalizedScopeId,
                  StringComparison.Ordinal))))
        {
            throw new InvalidOperationException(
                "A notification mutation requires a valid admitted scope.");
        }

        return normalizedScopeId;
    }
}
