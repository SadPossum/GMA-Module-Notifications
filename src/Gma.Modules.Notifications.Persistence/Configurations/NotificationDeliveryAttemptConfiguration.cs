namespace Gma.Modules.Notifications.Persistence.Configurations;

using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class NotificationDeliveryAttemptConfiguration : IEntityTypeConfiguration<NotificationDeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<NotificationDeliveryAttempt> builder)
    {
        builder.ToTable("delivery_attempts");
        builder.HasKey(attempt => attempt.Id);
        builder.Property(attempt => attempt.Provider)
            .HasConversion(provider => provider.Value, value => NotificationDeliveryProvider.Create(value).Value)
            .HasMaxLength(NotificationDeliveryAttempt.ProviderMaxLength)
            .IsRequired();
        builder.Property(attempt => attempt.Outcome)
            .HasConversion(
                value => NotificationRoutingSemanticNames.AttemptOutcome(value),
                value => NotificationRoutingSemanticNames.ParseAttemptOutcome(value))
            .HasMaxLength(NotificationRoutingSemanticNames.MaxLength)
            .IsRequired();
        builder.Property(attempt => attempt.Code)
            .HasMaxLength(NotificationDeliveryAttempt.CodeMaxLength);
        builder.Property(attempt => attempt.ProviderMessageId)
            .HasMaxLength(NotificationDeliveryAttempt.ProviderMessageIdMaxLength);
        builder.HasOne<NotificationDelivery>()
            .WithMany()
            .HasForeignKey(attempt => attempt.DeliveryId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(attempt => new { attempt.ScopeId, attempt.DeliveryId, attempt.AttemptNumber }).IsUnique();
        builder.HasIndex(attempt => new { attempt.ScopeId, attempt.CompletedAtUtc });
    }
}
