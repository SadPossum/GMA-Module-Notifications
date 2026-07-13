namespace Gma.Modules.Notifications.Contracts;

public static class NotificationsAdminPermissionCodes
{
    public const string HistoryRead = NotificationsModuleMetadata.Name + ".history.read";
    public const string BroadcastsRead = NotificationsModuleMetadata.Name + ".broadcasts.read";
    public const string BroadcastsCreate = NotificationsModuleMetadata.Name + ".broadcasts.create";
    public const string ConfigurationRead = NotificationsModuleMetadata.Name + ".configuration.read";
    public const string ConfigurationWrite = NotificationsModuleMetadata.Name + ".configuration.write";
    public const string DeliveriesRead = NotificationsModuleMetadata.Name + ".deliveries.read";
    public const string DeliveriesRetry = NotificationsModuleMetadata.Name + ".deliveries.retry";
}
