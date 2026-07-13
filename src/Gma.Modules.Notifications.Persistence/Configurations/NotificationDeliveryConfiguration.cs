namespace Gma.Modules.Notifications.Persistence.Configurations;

using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("deliveries");
        builder.HasKey(delivery => delivery.Id);
        builder.Property(delivery => delivery.DeliveryTag)
            .HasConversion(key => key.Value, value => NotificationTagKey.Create(value).Value)
            .HasColumnName("DeliveryTag")
            .HasMaxLength(NotificationTagKey.MaxLength)
            .IsRequired();
        builder.Property(delivery => delivery.Provider)
            .HasConversion(provider => provider.Value, value => NotificationDeliveryProvider.Create(value).Value)
            .HasMaxLength(NotificationDelivery.ProviderMaxLength)
            .IsRequired();
        builder.Property(delivery => delivery.Status)
            .HasConversion(
                value => NotificationRoutingSemanticNames.DeliveryStatus(value),
                value => NotificationRoutingSemanticNames.ParseDeliveryStatus(value))
            .HasMaxLength(NotificationRoutingSemanticNames.MaxLength)
            .IsRequired();
        builder.Property(delivery => delivery.LockedBy)
            .HasMaxLength(NotificationDelivery.WorkerIdMaxLength);
        builder.Property(delivery => delivery.LastCode)
            .HasMaxLength(NotificationDelivery.CodeMaxLength);
        builder.Property(delivery => delivery.ProviderMessageId)
            .HasMaxLength(NotificationDelivery.ProviderMessageIdMaxLength);
        builder.Property(delivery => delivery.ConcurrencyStamp)
            .IsConcurrencyToken()
            .IsRequired();
        builder.HasOne<UserNotification>()
            .WithMany()
            .HasForeignKey(delivery => delivery.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(delivery => new { delivery.ScopeId, delivery.NotificationId, delivery.DeliveryTag, delivery.Provider })
            .IsUnique();
        builder.HasIndex(delivery => new { delivery.Status, delivery.NextAttemptAtUtc, delivery.LockedUntilUtc, delivery.CreatedAtUtc });
        builder.HasIndex(delivery => new { delivery.ScopeId, delivery.Status, delivery.CreatedAtUtc });
    }
}
