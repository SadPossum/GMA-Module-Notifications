namespace Gma.Modules.Notifications.Admin.Contracts;

using Gma.Framework.Administration;
using Gma.Modules.Notifications.Contracts;

public static class NotificationsAdminPermissions
{
    public static readonly AdminPermission HistoryRead = AdminPermission.Create(NotificationsAdminPermissionCodes.HistoryRead);
    public static readonly AdminPermission BroadcastsRead = AdminPermission.Create(NotificationsAdminPermissionCodes.BroadcastsRead);
    public static readonly AdminPermission BroadcastsCreate = AdminPermission.Create(NotificationsAdminPermissionCodes.BroadcastsCreate);
    public static readonly AdminPermission ConfigurationRead = AdminPermission.Create(NotificationsAdminPermissionCodes.ConfigurationRead);
    public static readonly AdminPermission ConfigurationWrite = AdminPermission.Create(NotificationsAdminPermissionCodes.ConfigurationWrite);
    public static readonly AdminPermission DeliveriesRead = AdminPermission.Create(NotificationsAdminPermissionCodes.DeliveriesRead);
    public static readonly AdminPermission DeliveriesRetry = AdminPermission.Create(NotificationsAdminPermissionCodes.DeliveriesRetry);
}
