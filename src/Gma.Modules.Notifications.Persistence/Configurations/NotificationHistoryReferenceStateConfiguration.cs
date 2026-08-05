namespace Gma.Modules.Notifications.Persistence.Configurations;

using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class NotificationHistoryReferenceStateConfiguration
    : IEntityTypeConfiguration<NotificationHistoryReferenceState>
{
    public void Configure(
        EntityTypeBuilder<NotificationHistoryReferenceState> builder)
    {
        builder.ToTable(
            "notification_history_reference_states",
            table =>
            {
                table.HasTrigger(
                    "notification_history_reference_states_closed_immutable");
                table.HasCheckConstraint(
                    "CK_notification_history_reference_states_version",
                    "\"Version\" >= 0");
                table.HasCheckConstraint(
                    "CK_notification_history_reference_states_closure",
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
        builder.HasKey(state => new
        {
            state.ScopeId,
            state.Namespace,
            state.Digest
        });
        builder.Property(state => state.Namespace)
            .HasMaxLength(
                NotificationHistoryReferenceKey.NamespaceMaxLength)
            .IsRequired();
        builder.Property(state => state.Digest)
            .HasMaxLength(NotificationHistoryReferenceKey.DigestLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(state => state.Version)
            .IsConcurrencyToken()
            .IsRequired();
        builder.Property(state => state.CloseRequestSha256)
            .HasMaxLength(NotificationHistoryReferenceKey.DigestLength)
            .IsFixedLength();
        builder.HasIndex(state => new
        {
            state.ScopeId,
            state.IsClosed,
            state.ClosedAtUtc
        });
    }
}
