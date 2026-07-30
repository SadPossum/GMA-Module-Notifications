namespace Gma.Modules.Notifications.Persistence.Configurations;

using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class UserNotificationReferenceConfiguration
    : IEntityTypeConfiguration<UserNotificationReference>
{
    public void Configure(
        EntityTypeBuilder<UserNotificationReference> builder)
    {
        builder.ToTable("user_notification_references");
        builder.HasKey(reference => new
        {
            reference.ScopeId,
            reference.NotificationId,
            reference.Namespace,
            reference.Digest
        });
        builder.Property(reference => reference.Namespace)
            .HasMaxLength(
                NotificationHistoryReferenceKey.NamespaceMaxLength)
            .IsRequired();
        builder.Property(reference => reference.Digest)
            .HasMaxLength(NotificationHistoryReferenceKey.DigestLength)
            .IsFixedLength()
            .IsRequired();
        builder.HasOne<NotificationHistoryReferenceState>()
            .WithMany()
            .HasForeignKey(reference => new
            {
                reference.ScopeId,
                reference.Namespace,
                reference.Digest
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(reference => new
        {
            reference.ScopeId,
            reference.Namespace,
            reference.Digest,
            reference.NotificationId
        });
    }
}
