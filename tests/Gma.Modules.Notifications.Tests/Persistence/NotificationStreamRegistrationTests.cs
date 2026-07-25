namespace Gma.Modules.Notifications.Tests;

using Gma.Modules.Notifications.Application;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

[Trait("Category", "Unit")]
public sealed class NotificationStreamRegistrationTests
{
    [Fact]
    public void Durable_streams_register_the_database_monitor_by_default()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        builder.Services.AddNotificationsApplication(builder.Configuration);
        builder.AddNotificationsDurableStreams();

        Assert.Contains(
            builder.Services,
            descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType == typeof(NotificationStreamMonitorService));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(INotificationStreamPulse));
    }

    [Fact]
    public void Durable_streams_can_omit_the_database_monitor_without_removing_the_pulse()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Notifications:DurableStreams:MonitorEnabled"] = "false";

        builder.Services.AddNotificationsApplication(builder.Configuration);
        builder.AddNotificationsDurableStreams();

        Assert.DoesNotContain(
            builder.Services,
            descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType == typeof(NotificationStreamMonitorService));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(INotificationStreamPulse));
    }
}
