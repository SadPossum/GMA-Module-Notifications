namespace Gma.Modules.Notifications.Persistence.Configurations;

using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class NotificationDeliveryRouteConfiguration : IEntityTypeConfiguration<NotificationDeliveryRoute>
{
    public void Configure(EntityTypeBuilder<NotificationDeliveryRoute> builder)
    {
        builder.ToTable("delivery_routes");
        builder.HasKey(route => route.Id);
        builder.Property(route => route.DeliveryTag)
            .HasConversion(key => key.Value, value => NotificationTagKey.Create(value).Value)
            .HasColumnName("DeliveryTag")
            .HasMaxLength(NotificationTagKey.MaxLength)
            .IsRequired();
        builder.Property(route => route.Provider)
            .HasConversion(provider => provider.Value, value => NotificationDeliveryProvider.Create(value).Value)
            .HasMaxLength(NotificationDeliveryRoute.ProviderMaxLength)
            .IsRequired();
        builder.Property(route => route.UpdatedBy)
            .HasMaxLength(NotificationDeliveryRoute.ActorIdMaxLength)
            .IsRequired();
        builder.Property(route => route.Version).IsConcurrencyToken();
        builder.HasIndex(route => new { route.ScopeId, route.DeliveryTag }).IsUnique();
        builder.HasIndex(route => new { route.ScopeId, route.Provider, route.IsActive });
    }
}
