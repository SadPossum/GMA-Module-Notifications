namespace Gma.Modules.Notifications.Persistence.Configurations;

using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class NotificationHistoryBatchCloseOperationConfiguration
    : IEntityTypeConfiguration<NotificationHistoryBatchCloseOperation>
{
    public void Configure(
        EntityTypeBuilder<NotificationHistoryBatchCloseOperation> builder)
    {
        builder.ToTable(
            "notification_history_batch_close_operations",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_notification_history_batch_close_operations_versions",
                    "\"ExpectedVersion\" >= 0 AND " +
                    "\"ResultingVersion\" > \"ExpectedVersion\"");
                table.HasCheckConstraint(
                    "CK_notification_history_batch_close_operations_batch",
                    "\"BatchSize\" >= 1 AND " +
                    $"\"BatchSize\" <= " +
                    Application.Ports.NotificationHistoryLifecycleLimits
                        .MaximumCloseBatchSize);
                table.HasCheckConstraint(
                    "CK_notification_history_batch_close_operations_progress",
                    "((\"RemovedRecordCount\" = 0 AND " +
                    "\"CompletedBatchCount\" = 0) OR " +
                    "(\"RemovedRecordCount\" > 0 AND " +
                    "\"CompletedBatchCount\" > 0)) AND " +
                    "\"ProofVersion\" = 1 AND " +
                    "\"UpdatedAtUtc\" >= \"StartedAtUtc\"");
            });
        builder.HasKey(operation => new
        {
            operation.ScopeId,
            operation.OperationId
        });
        builder.Property(operation => operation.Namespace)
            .HasMaxLength(NotificationHistoryReferenceKey.NamespaceMaxLength)
            .IsRequired();
        builder.Property(operation => operation.Digest)
            .HasMaxLength(NotificationHistoryReferenceKey.DigestLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(operation => operation.RequestSha256)
            .HasMaxLength(NotificationHistoryReferenceKey.DigestLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(operation => operation.RemovalProofSha256)
            .HasMaxLength(NotificationHistoryReferenceKey.DigestLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(operation => operation.CompletedBatchCount)
            .IsConcurrencyToken();
        builder.HasIndex(operation => new
        {
            operation.ScopeId,
            operation.Namespace,
            operation.Digest
        })
            .IsUnique();
        builder.HasOne<NotificationHistoryReferenceState>()
            .WithMany()
            .HasForeignKey(operation => new
            {
                operation.ScopeId,
                operation.Namespace,
                operation.Digest
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
