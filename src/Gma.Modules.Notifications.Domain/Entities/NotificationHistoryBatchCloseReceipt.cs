namespace Gma.Modules.Notifications.Domain.Entities;

using Gma.Framework.Domain;
using Gma.Framework.Naming;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Domain.Errors;
using Gma.Modules.Notifications.Domain.ValueObjects;

public sealed class NotificationHistoryBatchCloseReceipt : IScopedEntity
{
    private NotificationHistoryBatchCloseReceipt() { }

    private NotificationHistoryBatchCloseReceipt(
        NotificationHistoryBatchCloseOperation operation,
        DateTimeOffset completedAtUtc)
    {
        this.OperationId = operation.OperationId;
        this.ScopeId = operation.ScopeId;
        this.Namespace = operation.Namespace;
        this.Digest = operation.Digest;
        this.RequestSha256 = operation.RequestSha256;
        this.ResultingVersion = operation.ResultingVersion;
        this.RemovedRecordCount = operation.RemovedRecordCount;
        this.CompletedBatchCount = operation.CompletedBatchCount;
        this.RemovalProofVersion = operation.ProofVersion;
        this.RemovalProofSha256 = operation.RemovalProofSha256;
        this.StartedAtUtc = operation.StartedAtUtc;
        this.CompletedAtUtc = completedAtUtc;
    }

    public Guid OperationId { get; private set; }
    public string ScopeId { get; private set; } = string.Empty;
    public string Namespace { get; private set; } = string.Empty;
    public string Digest { get; private set; } = string.Empty;
    public string RequestSha256 { get; private set; } = string.Empty;
    public long ResultingVersion { get; private set; }
    public long RemovedRecordCount { get; private set; }
    public int CompletedBatchCount { get; private set; }
    public int RemovalProofVersion { get; private set; }
    public string RemovalProofSha256 { get; private set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset CompletedAtUtc { get; private set; }

    public static Result<NotificationHistoryBatchCloseReceipt> Create(
        NotificationHistoryBatchCloseOperation operation,
        DateTimeOffset completedAtUtc)
    {
        bool hasValidProgressShape = operation is not null &&
            ((operation.RemovedRecordCount == 0 &&
              operation.CompletedBatchCount == 0) ||
             (operation.RemovedRecordCount > 0 &&
              operation.CompletedBatchCount > 0));
        if (operation is null ||
            completedAtUtc < operation.UpdatedAtUtc ||
            operation.ResultingVersion < 1 ||
            operation.RemovedRecordCount < 0 ||
            operation.CompletedBatchCount < 0 ||
            !hasValidProgressShape ||
            operation.ProofVersion !=
                NotificationHistoryBatchCloseOperation.RemovalProofVersion ||
            !IsSha256(operation.RemovalProofSha256))
        {
            return Result.Failure<NotificationHistoryBatchCloseReceipt>(
                NotificationsDomainErrors.HistoryBatchCloseReceiptInvalid);
        }

        return Result.Success(
            new NotificationHistoryBatchCloseReceipt(
                operation,
                completedAtUtc));
    }

    public bool Matches(
        NotificationHistoryReferenceKey reference,
        string requestSha256) =>
        reference is not null &&
        string.Equals(
            this.Namespace,
            reference.Namespace,
            StringComparison.Ordinal) &&
        string.Equals(
            this.Digest,
            reference.Digest,
            StringComparison.Ordinal) &&
        string.Equals(
            this.RequestSha256,
            requestSha256,
            StringComparison.Ordinal);

    public NotificationHistoryReferenceKey ToKey() =>
        NotificationHistoryReferenceKey.Create(
            this.Namespace,
            this.Digest).Value;

    private static bool IsSha256(string? value) =>
        value?.Length == NotificationHistoryReferenceKey.DigestLength &&
        value.All(character =>
            character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
