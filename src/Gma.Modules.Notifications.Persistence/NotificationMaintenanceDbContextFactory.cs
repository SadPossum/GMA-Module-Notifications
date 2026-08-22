namespace Gma.Modules.Notifications.Persistence;

using Gma.Framework.Scoping;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

internal sealed class NotificationMaintenanceDbContextFactory(
    DbContextOptions<NotificationsDbContext> options)
{
    public NotificationsDbContext CreateDbContext() =>
        new(options, NotificationMaintenanceScopeContext.Instance);

    public static NotificationMaintenanceDbContextFactory FromConfiguredContext(
        NotificationsDbContext configuredContext)
    {
        ArgumentNullException.ThrowIfNull(configuredContext);

        return configuredContext.GetService<IDbContextOptions>() is
            DbContextOptions<NotificationsDbContext> configuredOptions
                ? new NotificationMaintenanceDbContextFactory(
                    configuredOptions)
                : throw new InvalidOperationException(
                    "The configured notifications context does not expose compatible options.");
    }

    private sealed class NotificationMaintenanceScopeContext : IScopeContext
    {
        public static NotificationMaintenanceScopeContext Instance { get; } = new();

        public bool IsEnabled => false;
        public string? ScopeId => null;
    }
}
