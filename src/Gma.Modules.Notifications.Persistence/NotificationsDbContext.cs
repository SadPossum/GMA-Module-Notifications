namespace Gma.Modules.Notifications.Persistence;

using Microsoft.EntityFrameworkCore;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Framework.Messaging.Infrastructure;
using Gma.Framework.Scoping;
using Gma.Framework.Persistence.EntityFrameworkCore;

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options, IScopeContext scopeContext)
    : ScopeAwareDbContext<NotificationsDbContext>(options, scopeContext)
{
    public DbSet<UserNotification> UserNotifications => this.Set<UserNotification>();
    public DbSet<NotificationBroadcast> NotificationBroadcasts => this.Set<NotificationBroadcast>();
    public DbSet<NotificationBroadcastRead> NotificationBroadcastReads => this.Set<NotificationBroadcastRead>();
    public DbSet<InboxMessage> InboxMessages => this.Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(NotificationsMigrations.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsDbContext).Assembly);
        this.ApplyScopeConventions(modelBuilder);
    }
}
