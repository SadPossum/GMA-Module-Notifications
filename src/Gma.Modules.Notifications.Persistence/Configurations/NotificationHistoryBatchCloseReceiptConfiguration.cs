namespace Gma.Modules.Notifications.Persistence.Configurations;

using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class NotificationHistoryBatchCloseReceiptConfiguration
    : IEntityTypeConfiguration<NotificationHistoryBatchCloseReceipt>
{
    public void Configure(
        EntityTypeBuilder<NotificationHistoryBatchCloseReceipt> builder)
    {
        builder.ToTable(
            "notification_history_batch_close_receipts",
            table =>
            {
                table.HasTrigger(
                    "notification_history_batch_close_receipts_append_only");
                table.HasCheckConstraint(
                    "CK_notification_history_batch_close_receipts_version",
                    "\"ResultingVersion\" >= 1");
                table.HasCheckConstraint(
                    "CK_notification_history_batch_close_receipts_progress",
                    "((\"RemovedRecordCount\" = 0 AND " +
                    "\"CompletedBatchCount\" = 0) OR " +
                    "(\"RemovedRecordCount\" > 0 AND " +
                    "\"CompletedBatchCount\" > 0)) AND " +
                    "\"RemovalProofVersion\" = 1 AND " +
                    "\"CompletedAtUtc\" >= \"StartedAtUtc\"");
            });
        builder.HasKey(receipt => new
        {
            receipt.ScopeId,
            receipt.OperationId
        });
        builder.Property(receipt => receipt.Namespace)
            .HasMaxLength(NotificationHistoryReferenceKey.NamespaceMaxLength)
            .IsRequired();
        builder.Property(receipt => receipt.Digest)
            .HasMaxLength(NotificationHistoryReferenceKey.DigestLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(receipt => receipt.RequestSha256)
            .HasMaxLength(NotificationHistoryReferenceKey.DigestLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(receipt => receipt.RemovalProofSha256)
            .HasMaxLength(NotificationHistoryReferenceKey.DigestLength)
            .IsFixedLength()
            .IsRequired();
        builder.HasIndex(receipt => new
        {
            receipt.ScopeId,
            receipt.Namespace,
            receipt.Digest
        })
            .IsUnique();
        builder.HasOne<NotificationHistoryReferenceState>()
            .WithMany()
            .HasForeignKey(receipt => new
            {
                receipt.ScopeId,
                receipt.Namespace,
                receipt.Digest
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
