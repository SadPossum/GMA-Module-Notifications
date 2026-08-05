namespace Gma.Modules.Notifications.Domain.Entities;

using System.Security.Cryptography;
using System.Text;
using Gma.Framework.Domain;
using Gma.Framework.Naming;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Domain.Errors;

public sealed class NotificationScopeDestroyOperation : IScopedEntity
{
    public const int RemovalProofVersion = 1;
    public static readonly string InitialRemovalProofSha256 = Sha256(
        "gma-notification-scope-destroy-proof/v1|empty");

    private NotificationScopeDestroyOperation() { }

    private NotificationScopeDestroyOperation(
        string scopeId,
        Guid operationId,
        string requestSha256,
        long expectedRevision,
        long resultingRevision,
        int batchSize,
        DateTimeOffset startedAtUtc)
    {
        this.ScopeId = scopeId;
        this.OperationId = operationId;
        this.RequestSha256 = requestSha256;
        this.ExpectedRevision = expectedRevision;
        this.ResultingRevision = resultingRevision;
        this.BatchSize = batchSize;
        this.Stage = NotificationScopeDestroyStage.InboxMessages;
        this.RemovalProofSha256 = InitialRemovalProofSha256;
        this.StartedAtUtc = startedAtUtc;
        this.UpdatedAtUtc = startedAtUtc;
    }

    public string ScopeId { get; private set; } = string.Empty;
    public Guid OperationId { get; private set; }
    public string RequestSha256 { get; private set; } = string.Empty;
    public long ExpectedRevision { get; private set; }
    public long ResultingRevision { get; private set; }
    public int BatchSize { get; private set; }
    public NotificationScopeDestroyStage Stage { get; private set; }
    public long RemovedRecordCount { get; private set; }
    public int CompletedBatchCount { get; private set; }
    public int ProofVersion { get; private set; } = RemovalProofVersion;
    public string RemovalProofSha256 { get; private set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public bool IsComplete => this.Stage == NotificationScopeDestroyStage.Completed;

    public static Result<NotificationScopeDestroyOperation> Create(
        string scopeId,
        Guid operationId,
        string requestSha256,
        long expectedRevision,
        long resultingRevision,
        int batchSize,
        int maximumBatchSize,
        DateTimeOffset startedAtUtc)
    {
        if (!ScopeIds.TryNormalize(scopeId, out string? normalizedScopeId) ||
            operationId == Guid.Empty ||
            !IsSha256(requestSha256) ||
            expectedRevision < 0 ||
            expectedRevision == long.MaxValue ||
            resultingRevision <= expectedRevision ||
            batchSize is < 1 ||
            batchSize > maximumBatchSize ||
            startedAtUtc == default)
        {
            return Result.Failure<NotificationScopeDestroyOperation>(
                NotificationsDomainErrors.ScopeDestroyOperationInvalid);
        }

        return Result.Success(new NotificationScopeDestroyOperation(
            normalizedScopeId!,
            operationId,
            requestSha256,
            expectedRevision,
            resultingRevision,
            batchSize,
            startedAtUtc));
    }

    public bool Matches(Guid operationId, string requestSha256) =>
        this.OperationId == operationId &&
        string.Equals(
            this.RequestSha256,
            requestSha256,
            StringComparison.Ordinal);

    public bool RecordBatch(
        NotificationScopeDestroyStage stage,
        int removedRecordCount,
        string removedRecordIdsSha256,
        bool stageCompleted,
        DateTimeOffset recordedAtUtc)
    {
        if (this.IsComplete ||
            stage != this.Stage ||
            removedRecordCount is < 1 ||
            removedRecordCount > this.BatchSize ||
            !IsSha256(removedRecordIdsSha256) ||
            recordedAtUtc < this.UpdatedAtUtc ||
            this.RemovedRecordCount > long.MaxValue - removedRecordCount ||
            this.CompletedBatchCount == int.MaxValue)
        {
            return false;
        }

        int nextBatch = this.CompletedBatchCount + 1;
        this.RemovalProofSha256 = Sha256(
            "gma-notification-scope-destroy-proof/v1|" +
            $"{this.RemovalProofSha256}|{nextBatch}|{(int)stage}|" +
            $"{removedRecordCount}|{removedRecordIdsSha256}");
        this.RemovedRecordCount += removedRecordCount;
        this.CompletedBatchCount = nextBatch;
        this.UpdatedAtUtc = recordedAtUtc;
        if (stageCompleted)
        {
            this.Stage = Next(stage);
        }

        return true;
    }

    public bool AdvanceEmptyStage(DateTimeOffset recordedAtUtc)
    {
        if (this.IsComplete || recordedAtUtc < this.UpdatedAtUtc)
        {
            return false;
        }

        this.Stage = Next(this.Stage);
        this.UpdatedAtUtc = recordedAtUtc;
        return true;
    }

    private static NotificationScopeDestroyStage Next(
        NotificationScopeDestroyStage stage) =>
        stage switch
        {
            NotificationScopeDestroyStage.InboxMessages =>
                NotificationScopeDestroyStage.TenantBroadcastReads,
            NotificationScopeDestroyStage.TenantBroadcastReads =>
                NotificationScopeDestroyStage.TenantBroadcasts,
            NotificationScopeDestroyStage.TenantBroadcasts =>
                NotificationScopeDestroyStage.Preferences,
            NotificationScopeDestroyStage.Preferences =>
                NotificationScopeDestroyStage.DeliveryRoutes,
            NotificationScopeDestroyStage.DeliveryRoutes =>
                NotificationScopeDestroyStage.TagDefinitions,
            NotificationScopeDestroyStage.TagDefinitions =>
                NotificationScopeDestroyStage.UserNotifications,
            NotificationScopeDestroyStage.UserNotifications =>
                NotificationScopeDestroyStage.Completed,
            _ => throw new InvalidOperationException(
                "The notification scope destruction stage is invalid.")
        };

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character =>
            character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public enum NotificationScopeDestroyStage
{
    Unknown = 0,
    InboxMessages = 1,
    TenantBroadcastReads = 2,
    TenantBroadcasts = 3,
    Preferences = 4,
    DeliveryRoutes = 5,
    TagDefinitions = 6,
    UserNotifications = 7,
    Completed = 8
}
