namespace Gma.Modules.Notifications.Tests;

using Gma.Framework.Administration.Cli;
using Gma.Modules.Notifications.AdminCli;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.CommandLine;
using Xunit;

[Trait("Category", "Unit")]
public sealed class NotificationsAdminCliModuleTests
{
    [Fact]
    public void Module_composes_notifications_without_claiming_operator_commands()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Notifications:DurableStreams:MonitorEnabled"] = "false",
            ["Persistence:Provider"] = "SqlServer",
            ["ConnectionStrings:SqlServer"] =
                "Server=localhost;Database=notifications-admin-cli-tests;" +
                "Trusted_Connection=True;TrustServerCertificate=True"
        });
        NotificationsAdminCliModule module = new();

        module.AddServices(builder);

        Assert.Equal(NotificationsModuleMetadata.Name, module.Name);
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(INotificationHistoryLifecycle));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(INotificationStreamPulse));

        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        RootCommand root = new("notifications-admin");
        module.MapCommands(new AdminCliCommandRegistry(root, provider));

        Assert.Empty(root.Subcommands);
    }
}
