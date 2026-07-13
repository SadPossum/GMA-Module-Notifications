namespace Gma.Modules.Notifications.Contracts;

using Gma.Framework.ModuleComposition;
using Gma.Framework.Modules;
using Gma.Framework.Permissions;

public static class NotificationsModuleMetadata
{
    public const string Name = "notifications";
    public const string Schema = "notifications";

    public static ModuleDescriptor Descriptor { get; } = ModuleDescriptor
        .Create(Name)
        .WithSchema(Schema)
        .WithPermissions([
            new ModulePermissionDescriptor(
                NotificationsAdminPermissionCodes.HistoryRead,
                "Read tenant notification history.",
                scopeRequirement: PermissionScopeRequirement.Scoped),
            new ModulePermissionDescriptor(
                NotificationsAdminPermissionCodes.BroadcastsRead,
                "Read notification broadcasts.",
                scopeRequirement: PermissionScopeRequirement.Scoped),
            new ModulePermissionDescriptor(
                NotificationsAdminPermissionCodes.BroadcastsCreate,
                "Create notification broadcasts.",
                scopeRequirement: PermissionScopeRequirement.Scoped),
            new ModulePermissionDescriptor(
                NotificationsAdminPermissionCodes.ConfigurationRead,
                "Read notification tags, preferences, and delivery routes.",
                scopeRequirement: PermissionScopeRequirement.Scoped),
            new ModulePermissionDescriptor(
                NotificationsAdminPermissionCodes.ConfigurationWrite,
                "Manage notification tags and delivery routes.",
                scopeRequirement: PermissionScopeRequirement.Scoped),
            new ModulePermissionDescriptor(
                NotificationsAdminPermissionCodes.DeliveriesRead,
                "Read notification delivery jobs and attempt history.",
                scopeRequirement: PermissionScopeRequirement.Scoped),
            new ModulePermissionDescriptor(
                NotificationsAdminPermissionCodes.DeliveriesRetry,
                "Retry terminal notification deliveries.",
                scopeRequirement: PermissionScopeRequirement.Scoped),
        ])
        .WithProfile(NotificationsProfiles.Default)
        .Build();
}
