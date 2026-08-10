namespace Gma.Modules.Notifications.Tests;

using Gma.Framework.Scoping;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Gma.Modules.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

[Trait("Category", "Unit")]
public sealed class NotificationPersistenceModelTests
{
    [Fact]
    public void Delivery_policy_default_declares_unknown_as_its_sentinel()
    {
        DbContextOptions<NotificationsDbContext> options =
            new DbContextOptionsBuilder<NotificationsDbContext>()
                .UseInMemoryDatabase($"notifications-model-{Guid.NewGuid():N}")
                .Options;
        using NotificationsDbContext dbContext =
            new(options, new TestScopeContext());

        IModel model = dbContext.GetService<IDesignTimeModel>().Model;
        IProperty property = model
            .FindEntityType(typeof(UserNotification))!
            .FindProperty(nameof(UserNotification.DeliveryPolicy))!;
        IConventionProperty conventionProperty =
            Assert.IsType<IConventionProperty>(property, exactMatch: false);

        Assert.NotNull(conventionProperty.GetSentinelConfigurationSource());
        Assert.Equal(NotificationDeliveryPolicy.Unknown, property.Sentinel);
        Assert.Equal(
            NotificationDeliveryPolicy.RespectPreferences,
            property.GetDefaultValue());
    }

    private sealed class TestScopeContext : IScopeContext
    {
        public bool IsEnabled => true;
        public string? ScopeId => "tenant-a";
    }
}
