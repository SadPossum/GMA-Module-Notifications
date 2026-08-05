namespace Gma.Modules.Notifications.Domain.Entities;

using Gma.Framework.Domain;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Domain.Errors;

public sealed class NotificationScopeDestroyReceipt : IScopedEntity
{
    private NotificationScopeDestroyReceipt() { }

    private NotificationScopeDestroyReceipt(
        NotificationScopeDestroyOperation operation,
        DateTimeOffset completedAtUtc)
    {
        this.ScopeId = operation.ScopeId;
        this.OperationId = operation.OperationId;
        this.RequestSha256 = operation.RequestSha256;
        this.ExpectedRevision = operation.ExpectedRevision;
        this.ResultingRevision = operation.ResultingRevision;
        this.BatchSize = operation.BatchSize;
        this.RemovedRecordCount = operation.RemovedRecordCount;
        this.CompletedBatchCount = operation.CompletedBatchCount;
        this.RemovalProofVersion = operation.ProofVersion;
        this.RemovalProofSha256 = operation.RemovalProofSha256;
        this.StartedAtUtc = operation.StartedAtUtc;
        this.CompletedAtUtc = completedAtUtc;
    }

    public string ScopeId { get; private set; } = string.Empty;
    public Guid OperationId { get; private set; }
    public string RequestSha256 { get; private set; } = string.Empty;
    public long ExpectedRevision { get; private set; }
    public long ResultingRevision { get; private set; }
    public int BatchSize { get; private set; }
    public long RemovedRecordCount { get; private set; }
    public int CompletedBatchCount { get; private set; }
    public int RemovalProofVersion { get; private set; }
    public string RemovalProofSha256 { get; private set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset CompletedAtUtc { get; private set; }

    public static Result<NotificationScopeDestroyReceipt> Create(
        NotificationScopeDestroyOperation operation,
        DateTimeOffset completedAtUtc)
    {
        bool progressShapeValid = operation is not null &&
            ((operation.RemovedRecordCount == 0 &&
              operation.CompletedBatchCount == 0) ||
             (operation.RemovedRecordCount > 0 &&
              operation.CompletedBatchCount > 0));
        if (operation is null ||
            !operation.IsComplete ||
            !progressShapeValid ||
            operation.ResultingRevision < 1 ||
            operation.ProofVersion !=
                NotificationScopeDestroyOperation.RemovalProofVersion ||
            completedAtUtc < operation.UpdatedAtUtc)
        {
            return Result.Failure<NotificationScopeDestroyReceipt>(
                NotificationsDomainErrors.ScopeDestroyReceiptInvalid);
        }

        return Result.Success(new NotificationScopeDestroyReceipt(
            operation,
            completedAtUtc));
    }

    public bool Matches(Guid operationId, string requestSha256) =>
        this.OperationId == operationId &&
        string.Equals(
            this.RequestSha256,
            requestSha256,
            StringComparison.Ordinal);
}
