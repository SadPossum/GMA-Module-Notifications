namespace Gma.Modules.Notifications.Persistence.Configurations;

using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class NotificationHistoryCloseReceiptConfiguration
    : IEntityTypeConfiguration<NotificationHistoryCloseReceipt>
{
    public void Configure(
        EntityTypeBuilder<NotificationHistoryCloseReceipt> builder)
    {
        builder.ToTable(
            "notification_history_close_receipts",
            table =>
            {
                table.HasTrigger(
                    "notification_history_close_receipts_append_only");
                table.HasCheckConstraint(
                    "CK_notification_history_close_receipts_version",
                    "\"ResultingVersion\" >= 1");
                table.HasCheckConstraint(
                    "CK_notification_history_close_receipts_count",
                    "\"RemovedRecordCount\" >= 0");
            });
        builder.HasKey(receipt => new
        {
            receipt.ScopeId,
            receipt.OperationId
        });
        builder.Property(receipt => receipt.Namespace)
            .HasMaxLength(
                NotificationHistoryReferenceKey.NamespaceMaxLength)
            .IsRequired();
        builder.Property(receipt => receipt.Digest)
            .HasMaxLength(NotificationHistoryReferenceKey.DigestLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(receipt => receipt.RequestSha256)
            .HasMaxLength(NotificationHistoryReferenceKey.DigestLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(receipt => receipt.RemovedRecordIdsSha256)
            .HasMaxLength(NotificationHistoryReferenceKey.DigestLength)
            .IsFixedLength()
            .IsRequired();
        builder.HasIndex(receipt => new
        {
            receipt.ScopeId,
            receipt.Namespace,
            receipt.Digest,
            receipt.CompletedAtUtc
        });
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
