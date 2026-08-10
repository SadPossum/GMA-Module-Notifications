namespace Gma.Modules.Notifications.Contracts;

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
