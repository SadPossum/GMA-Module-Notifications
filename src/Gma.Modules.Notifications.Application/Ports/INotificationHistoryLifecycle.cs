namespace Gma.Modules.Notifications.Application.Ports;

using System.Text.Json;
using Gma.Modules.Notifications.Contracts;

public interface INotificationHistoryLifecycle
{
    Task<NotificationHistoryReferenceSnapshot> EnsureOpenAsync(
        string scopeId,
        NotificationHistoryReference reference,
        CancellationToken cancellationToken);

    Task<NotificationHistoryReferenceSnapshot> GetSnapshotAsync(
        string scopeId,
        NotificationHistoryReference reference,
        CancellationToken cancellationToken);

    Task<NotificationHistoryReferencePage> ListAsync(
        string scopeId,
        NotificationHistoryReference reference,
        long afterStreamSequence,
        int pageSize,
        CancellationToken cancellationToken);

    Task<NotificationHistoryReferenceCloseResult> CloseAsync(
        NotificationHistoryReferenceCloseRequest request,
        CancellationToken cancellationToken);
}

public sealed record NotificationHistoryReferenceSnapshot(
    NotificationHistoryReferenceStatus Status,
    long Version,
    int RecordCount,
    long LatestStreamSequence);

public enum NotificationHistoryReferenceStatus
{
    Missing = 0,
    Open = 1,
    Closed = 2
}

public sealed record NotificationHistoryReferencePage(
    NotificationHistoryReferenceStatus Status,
    long ReferenceVersion,
    IReadOnlyList<NotificationHistoryReferenceRecord> Records,
    long NextStreamSequence,
    bool HasMore);

public sealed record NotificationHistoryReferenceRecord(
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
    JsonElement Payload,
    IReadOnlyList<string> Tags,
    NotificationDeliveryPolicy DeliveryPolicy);

public sealed record NotificationHistoryReferenceCloseRequest(
    Guid OperationId,
    string ScopeId,
    NotificationHistoryReference Reference,
    long ExpectedVersion,
    int MaximumRecords);

public sealed record NotificationHistoryReferenceCloseResult(
    NotificationHistoryReferenceCloseStatus Status,
    NotificationHistoryReferenceCloseReceipt? Receipt);

public enum NotificationHistoryReferenceCloseStatus
{
    Invalid = 0,
    Completed = 1,
    Replayed = 2,
    Stale = 3,
    Busy = 4,
    Conflict = 5,
    Overflow = 6,
    ScopeUnavailable = 7
}

public sealed record NotificationHistoryReferenceCloseReceipt(
    Guid OperationId,
    NotificationHistoryReference Reference,
    long ResultingVersion,
    int RemovedRecordCount,
    string RemovedRecordIdsSha256,
    DateTimeOffset CompletedAtUtc);

public static class NotificationHistoryLifecycleLimits
{
    public const int MaximumPageSize = 200;
    public const int MaximumCloseRecords = 10_000;
}
