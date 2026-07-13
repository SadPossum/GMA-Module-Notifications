namespace Gma.Modules.Notifications.Persistence.Configurations;

using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class NotificationTagDefinitionConfiguration : IEntityTypeConfiguration<NotificationTagDefinition>
{
    public void Configure(EntityTypeBuilder<NotificationTagDefinition> builder)
    {
        builder.ToTable("tag_definitions");
        builder.HasKey(definition => definition.Id);
        builder.Property(definition => definition.Key)
            .HasConversion(key => key.Value, value => NotificationTagKey.Create(value).Value)
            .HasColumnName("TagKey")
            .HasMaxLength(NotificationTagKey.MaxLength)
            .IsRequired();
        builder.Property(definition => definition.Kind)
            .HasConversion(
                value => NotificationRoutingSemanticNames.TagKind(value),
                value => NotificationRoutingSemanticNames.ParseTagKind(value))
            .HasMaxLength(NotificationRoutingSemanticNames.MaxLength)
            .IsRequired();
        builder.Property(definition => definition.Origin)
            .HasConversion(
                value => NotificationRoutingSemanticNames.TagOrigin(value),
                value => NotificationRoutingSemanticNames.ParseTagOrigin(value))
            .HasMaxLength(NotificationRoutingSemanticNames.MaxLength)
            .IsRequired();
        builder.Property(definition => definition.DisplayName)
            .HasMaxLength(NotificationTagDefinition.DisplayNameMaxLength)
            .IsRequired();
        builder.Property(definition => definition.Description)
            .HasMaxLength(NotificationTagDefinition.DescriptionMaxLength)
            .IsRequired();
        builder.Property(definition => definition.Owner)
            .HasMaxLength(NotificationTagDefinition.OwnerMaxLength)
            .IsRequired();
        builder.Property(definition => definition.CreatedBy)
            .HasMaxLength(NotificationTagDefinition.ActorIdMaxLength)
            .IsRequired();
        builder.Property(definition => definition.UpdatedBy)
            .HasMaxLength(NotificationTagDefinition.ActorIdMaxLength)
            .IsRequired();
        builder.Property(definition => definition.Version).IsConcurrencyToken();
        builder.HasIndex(definition => new { definition.ScopeId, definition.Key }).IsUnique();
        builder.HasIndex(definition => new { definition.ScopeId, definition.Kind, definition.IsActive, definition.Key });
    }
}
