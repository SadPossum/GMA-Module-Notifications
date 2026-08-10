namespace Gma.Modules.Notifications.Contracts;

public static class NotificationScopeLifecycleLimits
{
    public const int MaximumPageSize = 200;
    public const int MaximumCursorLength = 160;
    public const int MaximumDestroyBatchSize = 1_000;
}
