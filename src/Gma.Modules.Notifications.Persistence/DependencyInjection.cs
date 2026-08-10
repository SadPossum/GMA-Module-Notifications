namespace Gma.Modules.Notifications.Persistence;

using Gma.Framework.Cqrs.UnitOfWork;
using Gma.Framework.Messaging;
using Gma.Framework.Notifications;
using Gma.Framework.Persistence.EntityFrameworkCore;
using Gma.Modules.Notifications.Application;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddNotificationsPersistence(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddPersistenceOptions(builder.Configuration);
        NotificationRetentionOptions retentionOptions = builder.Configuration
            .GetSection(NotificationRetentionOptions.SectionName)
            .Get<NotificationRetentionOptions>() ?? new();
        ValidateOptionsResult retentionValidation = new NotificationRetentionOptionsValidator()
            .Validate(name: null, retentionOptions);
        if (retentionValidation.Failed)
        {
            throw new OptionsValidationException(
                NotificationRetentionOptions.SectionName,
                typeof(NotificationRetentionOptions),
                retentionValidation.Failures);
        }

        builder.Services
            .AddOptions<NotificationRetentionOptions>()
            .Bind(builder.Configuration.GetSection(NotificationRetentionOptions.SectionName))
            .ValidateOnStart();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<NotificationRetentionOptions>, NotificationRetentionOptionsValidator>());

        builder.Services.TryAddModuleDbContext<NotificationsDbContext>(options =>
            options.UseConfiguredProvider(
                builder.Configuration,
                NotificationsMigrations.SqlServerAssembly,
                NotificationsMigrations.PostgreSqlAssembly,
                NotificationsMigrations.Schema,
                NotificationsMigrations.HistoryTable));

        builder.Services.TryAddScoped<INotificationHistoryRepository, NotificationHistoryRepository>();
        builder.Services.TryAddScoped<
            INotificationHistoryLifecycleRepository,
            NotificationHistoryLifecycleRepository>();
        builder.Services.TryAddScoped<
            INotificationHistoryLifecycle,
            NotificationHistoryLifecycleService>();
        builder.Services.TryAddScoped<
            INotificationScopeLifecycle,
            NotificationScopeLifecycleService>();
        builder.Services.TryAddScoped<INotificationBroadcastRepository, NotificationBroadcastRepository>();
        builder.Services.TryAddScoped<INotificationRoutingRepository, NotificationRoutingRepository>();
        builder.Services.TryAddSingleton<NotificationDeliveryMetrics>();
        builder.Services.TryAddEnumerable([
            ServiceDescriptor.Scoped<IUnitOfWork, NotificationsUnitOfWork>(),
            ServiceDescriptor.Scoped<IInboxStore, NotificationsInboxStore>(),
            ServiceDescriptor.Scoped<IUserNotificationHistoryWriter, NotificationHistoryWriter>()
        ]);
        if (retentionOptions.Enabled)
        {
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, NotificationRetentionService>());
        }

        NotificationDeliveryOptions deliveryOptions = builder.Configuration
            .GetSection(NotificationDeliveryOptions.SectionName)
            .Get<NotificationDeliveryOptions>() ?? new();
        if (deliveryOptions.Enabled)
        {
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, NotificationDeliveryService>());
        }

        return builder;
    }

    public static IHostApplicationBuilder AddNotificationsDurableStreams(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(NotificationStreamMonitorMarker)))
        {
            return builder;
        }

        builder.Services.AddSingleton<NotificationStreamMonitorMarker>();
        builder.Services.TryAddSingleton<NotificationStreamPulse>();
        builder.Services.TryAddSingleton<INotificationStreamPulse>(provider =>
            provider.GetRequiredService<NotificationStreamPulse>());
        bool monitorEnabled = builder.Configuration
            .GetSection(NotificationStreamOptions.SectionName)
            .GetValue<bool?>(nameof(NotificationStreamOptions.MonitorEnabled)) ?? true;
        if (monitorEnabled)
        {
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, NotificationStreamMonitorService>());
        }

        return builder;
    }

    private sealed class NotificationStreamMonitorMarker;
}
