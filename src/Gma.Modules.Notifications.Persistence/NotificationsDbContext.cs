namespace Gma.Modules.Notifications.Persistence;

using Gma.Framework.Messaging.Infrastructure;
using Gma.Framework.Persistence.EntityFrameworkCore;
using Gma.Framework.Scoping;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Microsoft.EntityFrameworkCore;

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options, IScopeContext scopeContext)
    : ScopeAwareDbContext<NotificationsDbContext>(options, scopeContext)
{
    public DbSet<UserNotification> UserNotifications => this.Set<UserNotification>();
    public DbSet<UserNotificationTag> UserNotificationTags => this.Set<UserNotificationTag>();
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
}
