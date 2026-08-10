namespace Gma.Modules.Notifications.Contracts;

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

    Task<NotificationHistoryReferenceCloseBatchResult> CloseBatchAsync(
        NotificationHistoryReferenceCloseBatchRequest request,
        CancellationToken cancellationToken);
}
