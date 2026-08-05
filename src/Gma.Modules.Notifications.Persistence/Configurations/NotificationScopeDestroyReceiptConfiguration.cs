namespace Gma.Modules.Notifications.Persistence.Configurations;

using Gma.Modules.Notifications.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class NotificationScopeDestroyReceiptConfiguration
    : IEntityTypeConfiguration<NotificationScopeDestroyReceipt>
{
    public void Configure(
        EntityTypeBuilder<NotificationScopeDestroyReceipt> builder)
    {
        builder.ToTable(
            "notification_scope_destroy_receipts",
            table =>
            {
                table.HasTrigger(
                    "notification_scope_destroy_receipts_append_only");
                table.HasCheckConstraint(
                    "CK_notification_scope_destroy_receipts_revisions",
                    "\"ExpectedRevision\" >= 0 AND " +
                    "\"ResultingRevision\" > \"ExpectedRevision\"");
                table.HasCheckConstraint(
                    "CK_notification_scope_destroy_receipts_progress",
                    "\"BatchSize\" >= 1 AND " +
                    "\"BatchSize\" <= " +
                    Application.Ports.NotificationScopeLifecycleLimits
                        .MaximumDestroyBatchSize + " AND " +
                    "((\"RemovedRecordCount\" = 0 AND " +
                    "\"CompletedBatchCount\" = 0) OR " +
                    "(\"RemovedRecordCount\" > 0 AND " +
                    "\"CompletedBatchCount\" > 0)) AND " +
                    "\"RemovalProofVersion\" = 1 AND " +
                    "\"CompletedAtUtc\" >= \"StartedAtUtc\"");
            });
        builder.HasKey(receipt => receipt.ScopeId);
        builder.Property(receipt => receipt.RequestSha256)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(receipt => receipt.RemovalProofSha256)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.HasOne<NotificationScopeState>()
            .WithMany()
            .HasForeignKey(receipt => receipt.ScopeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
