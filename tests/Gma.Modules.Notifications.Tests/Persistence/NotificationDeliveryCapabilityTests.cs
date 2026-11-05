namespace Gma.Modules.Notifications.Tests;

using Gma.Framework.ModuleComposition;
using Gma.Modules.Notifications.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

[Trait("Category", "Unit")]
public sealed class NotificationDeliveryCapabilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cross_scope_claim_capability_is_provided_only_with_the_enabled_delivery_runtime(
        bool enabled)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration["Persistence:Provider"] = "PostgreSql";
        builder.Configuration["ConnectionStrings:PostgreSql"] =
            "Host=127.0.0.1;Port=1;Database=capability;Username=unused";
        builder.Configuration["Notifications:Delivery:Enabled"] = enabled.ToString();

        builder.AddNotificationsPersistence();

        CompositionFeatureId capability = new("notifications.cross-scope-delivery-claims");
        bool provided = builder.Services
            .Where(descriptor => descriptor.ServiceType == typeof(ProvidedCompositionFeature))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ProvidedCompositionFeature>()
            .Any(feature => feature.Id == capability);
        bool deliveryRegistered = builder.Services.Any(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(NotificationDeliveryService));

        Assert.Equal(enabled, deliveryRegistered);
        Assert.Equal(enabled, provided);
    }
}
