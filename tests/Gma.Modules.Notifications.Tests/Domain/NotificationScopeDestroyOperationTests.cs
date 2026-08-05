namespace Gma.Modules.Notifications.Tests;

using Gma.Modules.Notifications.Domain.Entities;
using Xunit;

[Trait("Category", "Unit")]
public sealed class NotificationScopeDestroyOperationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 4, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Progress_is_bounded_monotonic_and_proof_carrying()
    {
        NotificationScopeDestroyOperation operation = CreateOperation();
        string initialProof = operation.RemovalProofSha256;

        Assert.True(operation.RecordBatch(
            NotificationScopeDestroyStage.InboxMessages,
            2,
            new string('1', 64),
            stageCompleted: false,
            Now.AddSeconds(1)));
        Assert.True(operation.RecordBatch(
            NotificationScopeDestroyStage.InboxMessages,
            1,
            new string('2', 64),
            stageCompleted: true,
            Now.AddSeconds(2)));

        Assert.Equal(
            NotificationScopeDestroyStage.TenantBroadcastReads,
            operation.Stage);
        Assert.Equal(3, operation.RemovedRecordCount);
        Assert.Equal(2, operation.CompletedBatchCount);
        Assert.NotEqual(initialProof, operation.RemovalProofSha256);
        Assert.False(operation.RecordBatch(
            NotificationScopeDestroyStage.InboxMessages,
            1,
            new string('3', 64),
            stageCompleted: true,
            Now.AddSeconds(3)));
    }

    [Fact]
    public void Empty_stages_can_complete_with_an_immutable_receipt()
    {
        NotificationScopeDestroyOperation operation = CreateOperation();
        while (!operation.IsComplete)
        {
            Assert.True(operation.AdvanceEmptyStage(Now.AddMinutes(1)));
        }

        NotificationScopeDestroyReceipt receipt =
            NotificationScopeDestroyReceipt.Create(
                operation,
                Now.AddMinutes(2)).Value;

        Assert.Equal(operation.OperationId, receipt.OperationId);
        Assert.Equal(0, receipt.RemovedRecordCount);
        Assert.Equal(0, receipt.CompletedBatchCount);
        Assert.Equal(
            NotificationScopeDestroyOperation.InitialRemovalProofSha256,
            receipt.RemovalProofSha256);
        Assert.True(receipt.Matches(
            operation.OperationId,
            operation.RequestSha256));
    }

    private static NotificationScopeDestroyOperation CreateOperation() =>
        NotificationScopeDestroyOperation.Create(
            "tenant-a",
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            new string('a', 64),
            expectedRevision: 4,
            resultingRevision: 5,
            batchSize: 2,
            maximumBatchSize: 1000,
            Now).Value;
}
