namespace Gma.Modules.Notifications.Persistence.Configurations;

using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("preferences");
        builder.HasKey(preference => preference.Id);
        builder.Property(preference => preference.UserId)
            .HasMaxLength(NotificationPreference.UserIdMaxLength)
            .IsRequired();
        builder.Property(preference => preference.TagKey)
            .HasConversion(key => key.Value, value => NotificationTagKey.Create(value).Value)
            .HasColumnName("TagKey")
            .HasMaxLength(NotificationTagKey.MaxLength)
            .IsRequired();
        builder.Property(preference => preference.Version).IsConcurrencyToken();
        builder.HasIndex(preference => new { preference.ScopeId, preference.UserId, preference.TagKey }).IsUnique();
        builder.HasIndex(preference => new { preference.ScopeId, preference.UserId, preference.Enabled });
    }
}
