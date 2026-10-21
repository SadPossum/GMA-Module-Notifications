namespace Gma.Modules.Notifications.Tests;

using Gma.Framework.Messaging;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Framework.Scoping;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Gma.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

[Trait("Category", "Unit")]
public sealed class NotificationScopeAdmissionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 4, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Tracked_scope_mutations_advance_one_version_per_save()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        NotificationPreference preference = CreatePreference(enabled: true);
        dbContext.NotificationPreferences.Add(preference);

        await dbContext.SaveChangesAsync();

        NotificationScopeState state =
            await dbContext.NotificationScopeStates.SingleAsync();
        Assert.Equal(1, state.Version);
        Assert.False(state.IsClosed);

        preference.SetEnabled(enabled: false, Now.AddMinutes(1));
        await dbContext.SaveChangesAsync();

        Assert.Equal(2, state.Version);
    }

    [Fact]
    public async Task Global_broadcast_does_not_create_scope_state()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        NotificationBroadcast broadcast = NotificationBroadcast.Create(
            Guid.CreateVersion7(),
            scopeId: null,
            NotificationBroadcastAudience.PlatformUsers,
            "notifications",
            "platform.maintenance",
            1,
            "Maintenance",
            body: null,
            NotificationSeverity.Info,
            Now,
            Now,
            "{}").Value;
        dbContext.NotificationBroadcasts.Add(broadcast);

        await dbContext.SaveChangesAsync();

        Assert.Empty(await dbContext.NotificationScopeStates.ToArrayAsync());
    }

    [Fact]
    public async Task Module_worker_versions_each_open_scope_in_one_save()
    {
        await using NotificationsDbContext dbContext =
            CreateMaintenanceDbContext(
                $"notification-scope-{Guid.NewGuid():N}",
                new InMemoryDatabaseRoot());
        dbContext.NotificationPreferences.AddRange(
            CreatePreference("tenant-a", enabled: true),
            CreatePreference("tenant-b", enabled: true));

        await dbContext.SaveChangesAsync();

        NotificationScopeState[] states = await dbContext
            .NotificationScopeStates
            .OrderBy(state => state.ScopeId)
            .ToArrayAsync();
        Assert.Collection(
            states,
            state =>
            {
                Assert.Equal("tenant-a", state.ScopeId);
                Assert.Equal(1, state.Version);
            },
            state =>
            {
                Assert.Equal("tenant-b", state.ScopeId);
                Assert.Equal(1, state.Version);
            });

        foreach (NotificationPreference preference in
                 dbContext.NotificationPreferences.Local)
        {
            preference.SetEnabled(enabled: false, Now.AddMinutes(1));
        }

        await dbContext.SaveChangesAsync();

        Assert.All(states, state => Assert.Equal(2, state.Version));
    }

    [Fact]
    public async Task Request_context_without_an_active_scope_rejects_scoped_mutation()
    {
        await using NotificationsDbContext dbContext = CreateDbContext(
            $"notification-scope-{Guid.NewGuid():N}",
            new InMemoryDatabaseRoot(),
            new TestScopeContext(scopeId: null));
        dbContext.NotificationPreferences.Add(CreatePreference(enabled: true));

        InvalidOperationException exception = await Assert.ThrowsAsync<
            InvalidOperationException>(() => dbContext.SaveChangesAsync());

        Assert.Equal(
            "A notification mutation requires a valid admitted scope.",
            exception.Message);
    }

    [Fact]
    public async Task Maintenance_context_rejects_closed_scope_mutation()
    {
        InMemoryDatabaseRoot root = new();
        string databaseName = $"notification-scope-{Guid.NewGuid():N}";
        await SeedClosedStateAsync(databaseName, root);
        await using NotificationsDbContext dbContext =
            CreateMaintenanceDbContext(databaseName, root);
        dbContext.NotificationPreferences.Add(CreatePreference(enabled: true));

        await Assert.ThrowsAsync<NotificationScopeClosedException>(
            () => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Scoped_inbox_message_creates_scope_state_and_is_processed()
    {
        await using NotificationsDbContext dbContext = CreateDbContext();
        NotificationsInboxStore store = CreateInboxStore(dbContext);
        int handlerCalls = 0;

        InboxProcessResult result = await store.ProcessAsync(
            CreateMessage(),
            _ =>
            {
                handlerCalls++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(InboxProcessStatus.Processed, result.Status);
        Assert.Equal(1, handlerCalls);
        Assert.Single(await dbContext.InboxMessages.ToArrayAsync());
        NotificationScopeState state =
            await dbContext.NotificationScopeStates.SingleAsync();
        Assert.Equal(1, state.Version);
        Assert.False(state.IsClosed);
    }

    [Fact]
    public async Task Closed_scope_suppresses_inbox_before_handler_or_row_creation()
    {
        InMemoryDatabaseRoot root = new();
        string databaseName = $"notification-scope-{Guid.NewGuid():N}";
        await SeedClosedStateAsync(databaseName, root);
        await using NotificationsDbContext dbContext =
            CreateDbContext(databaseName, root);
        NotificationsInboxStore store = CreateInboxStore(dbContext);
        int handlerCalls = 0;

        InboxProcessResult result = await store.ProcessAsync(
            CreateMessage(),
            _ =>
            {
                handlerCalls++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(InboxProcessStatus.Suppressed, result.Status);
        Assert.Equal(0, handlerCalls);
        Assert.Empty(await dbContext.InboxMessages.ToArrayAsync());
        NotificationScopeState state =
            await dbContext.NotificationScopeStates.SingleAsync();
        Assert.True(state.IsClosed);
        Assert.Equal(1, state.Version);
    }

    private static NotificationsInboxStore CreateInboxStore(
        NotificationsDbContext dbContext) =>
        new(dbContext, new FixedClock(), new TestIdGenerator());

    private static NotificationPreference CreatePreference(bool enabled) =>
        CreatePreference("tenant-a", enabled);

    private static NotificationPreference CreatePreference(
        string scopeId,
        bool enabled) =>
        NotificationPreference.Create(
            Guid.CreateVersion7(),
            scopeId,
            "user-a",
            "domain:operations",
            enabled,
            Now).Value;

    private static InboxMessageRecord CreateMessage() =>
        new(
            Guid.CreateVersion7(),
            "notifications-test-projection",
            "gma.notifications.test-event.v1",
            "test-event",
            1,
            "tenant-a",
            Now);

    private static async Task SeedClosedStateAsync(
        string databaseName,
        InMemoryDatabaseRoot root)
    {
        await using NotificationsDbContext dbContext =
            CreateDbContext(databaseName, root);
        NotificationScopeState state =
            NotificationScopeState.Create("tenant-a").Value;
        Assert.Equal(
            NotificationScopeCloseTransition.Completed,
            state.Close(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                new string('a', 64),
                Now));
        dbContext.NotificationScopeStates.Add(state);
        await dbContext.SaveChangesAsync();
    }

    private static NotificationsDbContext CreateDbContext() =>
        CreateDbContext(
            $"notification-scope-{Guid.NewGuid():N}",
            new InMemoryDatabaseRoot());

    private static NotificationsDbContext CreateDbContext(
        string databaseName,
        InMemoryDatabaseRoot root,
        IScopeContext? scopeContext = null)
    {
        DbContextOptions<NotificationsDbContext> options = CreateOptions(
            databaseName,
            root);
        return new NotificationsDbContext(
            options,
            scopeContext ?? new TestScopeContext());
    }

    private static NotificationsDbContext CreateMaintenanceDbContext(
        string databaseName,
        InMemoryDatabaseRoot root) =>
        new NotificationMaintenanceDbContextFactory(
                CreateOptions(databaseName, root))
            .CreateDbContext();

    private static DbContextOptions<NotificationsDbContext> CreateOptions(
        string databaseName,
        InMemoryDatabaseRoot root) =>
        new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(databaseName, root)
            .ConfigureWarnings(warnings => warnings.Ignore(
                InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private sealed class FixedClock : ISystemClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class TestIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.CreateVersion7();
    }

    private sealed class TestScopeContext(string? scopeId = "tenant-a") : IScopeContext
    {
        public bool IsEnabled => true;
        public string? ScopeId => scopeId;
    }
}
