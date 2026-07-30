namespace Gma.Modules.Notifications.Domain.Entities;

using Gma.Framework.Domain;
using Gma.Framework.Naming;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Domain.Errors;
using Gma.Modules.Notifications.Domain.ValueObjects;

public sealed class NotificationHistoryCloseReceipt : IScopedEntity
{
    private NotificationHistoryCloseReceipt() { }

    private NotificationHistoryCloseReceipt(
        Guid operationId,
        string scopeId,
        NotificationHistoryReferenceKey reference,
        string requestSha256,
        long resultingVersion,
        int removedRecordCount,
        string removedRecordIdsSha256,
        DateTimeOffset completedAtUtc)
    {
        this.OperationId = operationId;
        this.ScopeId = scopeId;
        this.Namespace = reference.Namespace;
        this.Digest = reference.Digest;
        this.RequestSha256 = requestSha256;
        this.ResultingVersion = resultingVersion;
        this.RemovedRecordCount = removedRecordCount;
        this.RemovedRecordIdsSha256 = removedRecordIdsSha256;
        this.CompletedAtUtc = completedAtUtc;
    }

    public Guid OperationId { get; private set; }
    public string ScopeId { get; private set; } = string.Empty;
    public string Namespace { get; private set; } = string.Empty;
    public string Digest { get; private set; } = string.Empty;
    public string RequestSha256 { get; private set; } = string.Empty;
    public long ResultingVersion { get; private set; }
    public int RemovedRecordCount { get; private set; }
    public string RemovedRecordIdsSha256 { get; private set; } = string.Empty;
    public DateTimeOffset CompletedAtUtc { get; private set; }

    public static Result<NotificationHistoryCloseReceipt> Create(
        Guid operationId,
        string scopeId,
        NotificationHistoryReferenceKey reference,
        string requestSha256,
        long resultingVersion,
        int removedRecordCount,
        string removedRecordIdsSha256,
        DateTimeOffset completedAtUtc)
    {
        if (operationId == Guid.Empty ||
            !ScopeIds.TryNormalize(
                scopeId,
                out string? normalizedScopeId) ||
            reference is null ||
            !IsSha256(requestSha256) ||
            resultingVersion < 1 ||
            removedRecordCount < 0 ||
            !IsSha256(removedRecordIdsSha256) ||
            completedAtUtc == default)
        {
            return Result.Failure<NotificationHistoryCloseReceipt>(
                NotificationsDomainErrors.HistoryCloseReceiptInvalid);
        }

        return Result.Success(
            new NotificationHistoryCloseReceipt(
                operationId,
                normalizedScopeId!,
                reference,
                requestSha256,
                resultingVersion,
                removedRecordCount,
                removedRecordIdsSha256,
                completedAtUtc));
    }

    private static bool IsSha256(string? value) =>
        value?.Length == NotificationHistoryReferenceKey.DigestLength &&
        value.All(character =>
            character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
