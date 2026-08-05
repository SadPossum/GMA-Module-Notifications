namespace Gma.Modules.Notifications.AdminCli;

using Gma.Framework.Administration.Cli;
using Gma.Framework.ModuleComposition;
using Gma.Modules.Notifications.Application;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Persistence;
using Microsoft.Extensions.Hosting;

public sealed class NotificationsAdminCliModule : IAdminCliModule
{
    public string Name => NotificationsModuleMetadata.Name;

    public void AddServices(IHostApplicationBuilder builder)
    {
        builder.SelectModuleProfile(
            NotificationsProfiles.Default,
            "Gma.Modules.Notifications.AdminCli");
        builder.Services.AddNotificationsApplication(builder.Configuration);
        builder.AddNotificationsPersistence();
        builder.AddNotificationsDurableStreams();
    }

    public void MapCommands(IAdminCliCommandRegistry commands) =>
        ArgumentNullException.ThrowIfNull(commands);
}
