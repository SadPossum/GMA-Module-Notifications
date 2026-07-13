namespace Gma.Modules.Notifications.Persistence.Configurations;

using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class UserNotificationTagConfiguration : IEntityTypeConfiguration<UserNotificationTag>
{
    public void Configure(EntityTypeBuilder<UserNotificationTag> builder)
    {
        builder.ToTable("user_notification_tags");
        builder.HasKey(tag => new { tag.ScopeId, tag.NotificationId, tag.Key });
        builder.Property(tag => tag.Key)
            .HasConversion(key => key.Value, value => NotificationTagKey.Create(value).Value)
            .HasColumnName("TagKey")
            .HasMaxLength(NotificationTagKey.MaxLength)
            .IsRequired();
        builder.HasIndex(tag => new { tag.ScopeId, tag.Key, tag.NotificationId });
    }
}
