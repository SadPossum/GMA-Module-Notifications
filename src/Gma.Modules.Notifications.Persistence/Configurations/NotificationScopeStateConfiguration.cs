namespace Gma.Modules.Notifications.Persistence.Configurations;

using Gma.Framework.Naming;
using Gma.Modules.Notifications.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class NotificationScopeStateConfiguration
    : IEntityTypeConfiguration<NotificationScopeState>
{
    public void Configure(EntityTypeBuilder<NotificationScopeState> builder)
    {
        builder.ToTable(
            "notification_scope_states",
            table =>
            {
                table.HasTrigger(
                    "notification_scope_states_closed_immutable");
                table.HasCheckConstraint(
                    "CK_notification_scope_states_version",
                    "\"Version\" >= 0");
                table.HasCheckConstraint(
                    "CK_notification_scope_states_closure",
                    "(CAST(\"IsClosed\" AS integer) = 0 AND " +
                    "\"CloseOperationId\" IS NULL AND " +
                    "\"CloseRequestSha256\" IS NULL AND " +
                    "\"ClosedAtUtc\" IS NULL) OR " +
                    "(CAST(\"IsClosed\" AS integer) = 1 AND " +
                    "\"Version\" >= 1 AND " +
                    "\"CloseOperationId\" IS NOT NULL AND " +
                    "\"CloseRequestSha256\" IS NOT NULL AND " +
                    "\"ClosedAtUtc\" IS NOT NULL)");
            });
        builder.HasKey(state => state.ScopeId);
        builder.Property(state => state.ScopeId)
            .HasMaxLength(ScopeIds.MaxLength)
            .IsRequired();
        builder.Property(state => state.Version)
            .IsConcurrencyToken()
            .IsRequired();
        builder.Property(state => state.CloseRequestSha256)
            .HasMaxLength(64)
            .IsFixedLength();
        builder.HasIndex(state => new
        {
            state.IsClosed,
            state.ClosedAtUtc,
            state.ScopeId
        });
    }
}
