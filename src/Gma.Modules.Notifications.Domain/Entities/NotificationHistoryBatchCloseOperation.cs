namespace Gma.Modules.Notifications.Domain.Entities;

using System.Security.Cryptography;
using System.Text;
using Gma.Framework.Domain;
using Gma.Framework.Naming;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Domain.Errors;
using Gma.Modules.Notifications.Domain.ValueObjects;

public sealed class NotificationHistoryBatchCloseOperation : IScopedEntity
{
    public const int RemovalProofVersion = 1;
    public static readonly string InitialRemovalProofSha256 = Sha256(
        "gma-notification-history-batch-close-proof/v1|empty");

    private NotificationHistoryBatchCloseOperation() { }

    private NotificationHistoryBatchCloseOperation(
        Guid operationId,
        string scopeId,
        NotificationHistoryReferenceKey reference,
        string requestSha256,
        long expectedVersion,
        long resultingVersion,
        int batchSize,
        DateTimeOffset startedAtUtc)
    {
        this.OperationId = operationId;
        this.ScopeId = scopeId;
        this.Namespace = reference.Namespace;
        this.Digest = reference.Digest;
        this.RequestSha256 = requestSha256;
        this.ExpectedVersion = expectedVersion;
        this.ResultingVersion = resultingVersion;
        this.BatchSize = batchSize;
        this.RemovalProofSha256 = InitialRemovalProofSha256;
        this.StartedAtUtc = startedAtUtc;
        this.UpdatedAtUtc = startedAtUtc;
    }

    public Guid OperationId { get; private set; }
    public string ScopeId { get; private set; } = string.Empty;
    public string Namespace { get; private set; } = string.Empty;
    public string Digest { get; private set; } = string.Empty;
    public string RequestSha256 { get; private set; } = string.Empty;
    public long ExpectedVersion { get; private set; }
    public long ResultingVersion { get; private set; }
    public int BatchSize { get; private set; }
    public long RemovedRecordCount { get; private set; }
    public int CompletedBatchCount { get; private set; }
    public int ProofVersion { get; private set; } = RemovalProofVersion;
    public string RemovalProofSha256 { get; private set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Result<NotificationHistoryBatchCloseOperation> Create(
        Guid operationId,
        string scopeId,
        NotificationHistoryReferenceKey reference,
        string requestSha256,
        long expectedVersion,
        long resultingVersion,
        int batchSize,
        DateTimeOffset startedAtUtc,
        int maximumBatchSize)
    {
        if (operationId == Guid.Empty ||
            !ScopeIds.TryNormalize(scopeId, out string? normalizedScopeId) ||
            reference is null ||
            !IsSha256(requestSha256) ||
            expectedVersion < 0 ||
            expectedVersion == long.MaxValue ||
            resultingVersion <= expectedVersion ||
            batchSize is < 1 ||
            batchSize > maximumBatchSize ||
            startedAtUtc == default)
        {
            return Result.Failure<NotificationHistoryBatchCloseOperation>(
                NotificationsDomainErrors.HistoryBatchCloseOperationInvalid);
        }

        return Result.Success(
            new NotificationHistoryBatchCloseOperation(
                operationId,
                normalizedScopeId!,
                reference,
                requestSha256,
                expectedVersion,
                resultingVersion,
                batchSize,
                startedAtUtc));
    }

    public bool RecordBatch(
        int removedRecordCount,
        string removedRecordIdsSha256,
        DateTimeOffset recordedAtUtc)
    {
        if (removedRecordCount is < 1 ||
            removedRecordCount > this.BatchSize ||
            !IsSha256(removedRecordIdsSha256) ||
            recordedAtUtc < this.UpdatedAtUtc ||
            this.RemovedRecordCount >
                long.MaxValue - removedRecordCount ||
            this.CompletedBatchCount == int.MaxValue)
        {
            return false;
        }

        int nextBatch = this.CompletedBatchCount + 1;
        this.RemovalProofSha256 = Sha256(
            $"gma-notification-history-batch-close-proof/v1|" +
            $"{this.RemovalProofSha256}|{nextBatch}|" +
            $"{removedRecordCount}|{removedRecordIdsSha256}");
        this.RemovedRecordCount += removedRecordCount;
        this.CompletedBatchCount = nextBatch;
        this.UpdatedAtUtc = recordedAtUtc;
        return true;
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

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
