namespace Gma.Modules.Notifications.Tests;

using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Xunit;

[Trait("Category", "Unit")]
public sealed class NotificationHistoryBatchCloseOperationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Batches_advance_one_deterministic_bounded_progress_proof()
    {
        NotificationHistoryBatchCloseOperation operation = Create();
        string initialProof = operation.RemovalProofSha256;

        bool first = operation.RecordBatch(
            2,
            new string('a', 64),
            Now.AddMinutes(1));
        string firstProof = operation.RemovalProofSha256;
        bool second = operation.RecordBatch(
            1,
            new string('b', 64),
            Now.AddMinutes(2));

        Assert.True(first);
        Assert.True(second);
        Assert.Equal(3, operation.RemovedRecordCount);
        Assert.Equal(2, operation.CompletedBatchCount);
        Assert.NotEqual(initialProof, firstProof);
        Assert.NotEqual(firstProof, operation.RemovalProofSha256);
        Assert.Equal(64, operation.RemovalProofSha256.Length);

        NotificationHistoryBatchCloseReceipt receipt =
            NotificationHistoryBatchCloseReceipt.Create(
                operation,
                Now.AddMinutes(3)).Value;
        Assert.Equal(operation.OperationId, receipt.OperationId);
        Assert.Equal(operation.RemovedRecordCount, receipt.RemovedRecordCount);
        Assert.Equal(operation.RemovalProofSha256, receipt.RemovalProofSha256);
    }

    [Fact]
    public void Invalid_batch_does_not_change_progress()
    {
        NotificationHistoryBatchCloseOperation operation = Create();

        bool recorded = operation.RecordBatch(
            3,
            new string('a', 64),
            Now.AddMinutes(1));

        Assert.False(recorded);
        Assert.Equal(0, operation.RemovedRecordCount);
        Assert.Equal(0, operation.CompletedBatchCount);
        Assert.Equal(
            NotificationHistoryBatchCloseOperation.InitialRemovalProofSha256,
            operation.RemovalProofSha256);
    }

    private static NotificationHistoryBatchCloseOperation Create() =>
        NotificationHistoryBatchCloseOperation.Create(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "tenant-a",
            NotificationHistoryReferenceKey.Create(
                "product-subject",
                new string('c', 64)).Value,
            new string('d', 64),
            expectedVersion: 3,
            resultingVersion: 4,
            batchSize: 2,
            Now,
            maximumBatchSize: 1000).Value;
}
