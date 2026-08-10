namespace Gma.Modules.Notifications.Persistence.Configurations;

using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class NotificationScopeDestroyOperationConfiguration
    : IEntityTypeConfiguration<NotificationScopeDestroyOperation>
{
    public void Configure(
        EntityTypeBuilder<NotificationScopeDestroyOperation> builder)
    {
        builder.ToTable(
            "notification_scope_destroy_operations",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_notification_scope_destroy_operations_revisions",
                    "\"ExpectedRevision\" >= 0 AND " +
                    "\"ResultingRevision\" > \"ExpectedRevision\"");
                table.HasCheckConstraint(
                    "CK_notification_scope_destroy_operations_batch",
                    "\"BatchSize\" >= 1 AND \"BatchSize\" <= " +
                    NotificationScopeLifecycleLimits.MaximumDestroyBatchSize);
                table.HasCheckConstraint(
                    "CK_notification_scope_destroy_operations_progress",
                    "\"Stage\" >= 1 AND \"Stage\" <= 7 AND " +
                    "((\"RemovedRecordCount\" = 0 AND " +
                    "\"CompletedBatchCount\" = 0) OR " +
                    "(\"RemovedRecordCount\" > 0 AND " +
                    "\"CompletedBatchCount\" > 0)) AND " +
                    "\"ProofVersion\" = 1 AND " +
                    "\"UpdatedAtUtc\" >= \"StartedAtUtc\"");
            });
        builder.HasKey(operation => operation.ScopeId);
        builder.Property(operation => operation.RequestSha256)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(operation => operation.Stage)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(operation => operation.RemovalProofSha256)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(operation => operation.CompletedBatchCount)
            .IsConcurrencyToken();
        builder.HasOne<NotificationScopeState>()
            .WithMany()
            .HasForeignKey(operation => operation.ScopeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
