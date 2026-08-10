namespace Gma.Modules.Notifications.Contracts;

public static class NotificationHistoryLifecycleLimits
{
    public const int MaximumPageSize = 200;
    public const int MaximumCloseRecords = 10_000;
    public const int MaximumCloseBatchSize = 1_000;
}
