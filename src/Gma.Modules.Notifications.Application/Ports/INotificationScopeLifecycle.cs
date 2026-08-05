namespace Gma.Modules.Notifications.Application.Ports;

using System.Text.Json;
using Gma.Modules.Notifications.Contracts;

public interface INotificationScopeLifecycle
{
    Task<NotificationScopeSnapshot> GetSnapshotAsync(
        string scopeId,
        CancellationToken cancellationToken);

    Task<NotificationScopeExportPage> ExportAsync(
        NotificationScopeExportRequest request,
        CancellationToken cancellationToken);

    Task<NotificationScopeDestroyResult> DestroyBatchAsync(
        NotificationScopeDestroyRequest request,
        CancellationToken cancellationToken);
}

public sealed record NotificationScopeSnapshot(
    NotificationScopeStatus Status,
    long Revision);

public enum NotificationScopeStatus
{
    Missing = 0,
    Open = 1,
    Closed = 2,
    ScopeUnavailable = 3
}

public sealed record NotificationScopeExportRequest(
    string ScopeId,
    long ExpectedRevision,
    NotificationScopeExportStore Store,
    string? AfterCursor,
    int PageSize);

public sealed record NotificationScopeExportPage(
    NotificationScopeExportStatus Status,
    long ScopeRevision,
    NotificationScopeExportStore Store,
    IReadOnlyList<NotificationScopeExportRecord> Records,
    string? NextCursor,
    bool HasMore);

public enum NotificationScopeExportStatus
{
    Invalid = 0,
    Completed = 1,
    Missing = 2,
    Closed = 3,
    Stale = 4,
    ScopeUnavailable = 5
}

public enum NotificationScopeExportStore
{
    Unknown = 0,
    UserNotifications = 1,
    Preferences = 2,
    DeliveryRoutes = 3,
    TagDefinitions = 4,
    Deliveries = 5,
    DeliveryAttempts = 6,
    TenantBroadcasts = 7,
    TenantBroadcastReads = 8,
    HistoryReferenceStates = 9,
    HistoryCloseReceipts = 10,
    HistoryBatchCloseOperations = 11,
    HistoryBatchCloseReceipts = 12
}

public static class NotificationScopeLifecycleLimits
{
    public const int MaximumPageSize = 200;
    public const int MaximumCursorLength = 160;
    public const int MaximumDestroyBatchSize = 1_000;
}

public sealed record NotificationScopeDestroyRequest(
    Guid OperationId,
    string ScopeId,
    long ExpectedRevision,
    int BatchSize);

public sealed record NotificationScopeDestroyResult(
    NotificationScopeDestroyStatus Status,
    NotificationScopeDestroyProgress? Progress,
    NotificationScopeDestroyReceipt? Receipt);

public enum NotificationScopeDestroyStatus
{
    Invalid = 0,
    InProgress = 1,
    Completed = 2,
    Replayed = 3,
    Stale = 4,
    Busy = 5,
    Conflict = 6,
    ScopeUnavailable = 7
}

public enum NotificationScopeDestructionStage
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

public sealed record NotificationScopeDestroyProgress(
    Guid OperationId,
    long ResultingRevision,
    int BatchSize,
    NotificationScopeDestructionStage Stage,
    long RemovedRecordCount,
    int CompletedBatchCount,
    int RemovalProofVersion,
    string RemovalProofSha256,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record NotificationScopeDestroyReceipt(
    Guid OperationId,
    long ResultingRevision,
    int BatchSize,
    long RemovedRecordCount,
    int CompletedBatchCount,
    int RemovalProofVersion,
    string RemovalProofSha256,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);

public abstract record NotificationScopeExportRecord;

public sealed record NotificationScopeUserNotificationExportRecord(
    Guid NotificationId,
    string RecipientId,
    string SourceModule,
    string NotificationName,
    int NotificationVersion,
    string Title,
    string? Body,
    NotificationSeverity Severity,
    long StreamSequence,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReadAtUtc,
    JsonElement Payload,
    NotificationDeliveryPolicy DeliveryPolicy,
    bool IsInboxVisible,
    IReadOnlyList<string> Tags,
    IReadOnlyList<NotificationHistoryReference> References)
    : NotificationScopeExportRecord;

public sealed record NotificationScopePreferenceExportRecord(
    Guid PreferenceId,
    string UserId,
    string TagKey,
    bool Enabled,
    int Version,
    DateTimeOffset UpdatedAtUtc)
    : NotificationScopeExportRecord;

public sealed record NotificationScopeDeliveryRouteExportRecord(
    Guid RouteId,
    string DeliveryTag,
    NotificationDeliveryProviderCode Provider,
    bool IsActive,
    int Version,
    DateTimeOffset UpdatedAtUtc,
    string UpdatedBy)
    : NotificationScopeExportRecord;

public sealed record NotificationScopeTagDefinitionExportRecord(
    Guid DefinitionId,
    string TagKey,
    NotificationTagKind Kind,
    string DisplayName,
    string Description,
    NotificationTagOrigin Origin,
    string Owner,
    bool IsActive,
    int Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string CreatedBy,
    string UpdatedBy)
    : NotificationScopeExportRecord;

public sealed record NotificationScopeDeliveryExportRecord(
    Guid DeliveryId,
    Guid NotificationId,
    string DeliveryTag,
    NotificationDeliveryProviderCode Provider,
    NotificationDeliveryStatus Status,
    int Attempts,
    int MaxAttempts,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? NextAttemptAtUtc,
    string? LockedBy,
    DateTimeOffset? LockedUntilUtc,
    DateTimeOffset? DeliveredAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? LastCode,
    string? ProviderMessageId,
    Guid ConcurrencyStamp)
    : NotificationScopeExportRecord;

public sealed record NotificationScopeDeliveryAttemptExportRecord(
    Guid AttemptId,
    Guid DeliveryId,
    int AttemptNumber,
    NotificationDeliveryProviderCode Provider,
    NotificationDeliveryAttemptOutcome Outcome,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string? Code,
    string? ProviderMessageId)
    : NotificationScopeExportRecord;

public sealed record NotificationScopeBroadcastExportRecord(
    Guid BroadcastId,
    NotificationBroadcastAudience Audience,
    string SourceModule,
    string NotificationName,
    int NotificationVersion,
    string Title,
    string? Body,
    NotificationSeverity Severity,
    long StreamSequence,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset CreatedAtUtc,
    JsonElement Payload)
    : NotificationScopeExportRecord;

public sealed record NotificationScopeBroadcastReadExportRecord(
    Guid ReadId,
    Guid BroadcastId,
    NotificationBroadcastRecipientKind RecipientKind,
    string RecipientId,
    DateTimeOffset ReadAtUtc)
    : NotificationScopeExportRecord;

public sealed record NotificationScopeHistoryReferenceStateExportRecord(
    NotificationHistoryReference Reference,
    long Version,
    bool IsClosed,
    DateTimeOffset? ClosedAtUtc,
    Guid? CloseOperationId,
    string? CloseRequestSha256)
    : NotificationScopeExportRecord;

public sealed record NotificationScopeHistoryCloseReceiptExportRecord(
    Guid OperationId,
    NotificationHistoryReference Reference,
    string RequestSha256,
    long ResultingVersion,
    int RemovedRecordCount,
    string RemovedRecordIdsSha256,
    DateTimeOffset CompletedAtUtc)
    : NotificationScopeExportRecord;

public sealed record NotificationScopeHistoryBatchCloseOperationExportRecord(
    Guid OperationId,
    NotificationHistoryReference Reference,
    string RequestSha256,
    long ExpectedVersion,
    long ResultingVersion,
    int BatchSize,
    long RemovedRecordCount,
    int CompletedBatchCount,
    int RemovalProofVersion,
    string RemovalProofSha256,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc)
    : NotificationScopeExportRecord;

public sealed record NotificationScopeHistoryBatchCloseReceiptExportRecord(
    Guid OperationId,
    NotificationHistoryReference Reference,
    string RequestSha256,
    long ResultingVersion,
    long RemovedRecordCount,
    int CompletedBatchCount,
    int RemovalProofVersion,
    string RemovalProofSha256,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc)
    : NotificationScopeExportRecord;
