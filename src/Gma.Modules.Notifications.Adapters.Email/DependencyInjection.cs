namespace Gma.Modules.Notifications.Adapters.Email;

using Gma.Framework.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationEmailAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        NotificationEmailAdapterOptions settings = configuration
            .GetSection(NotificationEmailAdapterOptions.SectionName)
            .Get<NotificationEmailAdapterOptions>() ?? new();
        ValidateOptionsResult validation = new NotificationEmailAdapterOptionsValidator().Validate(null, settings);
        if (validation.Failed)
        {
            throw new OptionsValidationException(
                NotificationEmailAdapterOptions.SectionName,
                typeof(NotificationEmailAdapterOptions),
                validation.Failures);
        }

        services.AddOptions<NotificationEmailAdapterOptions>()
            .Bind(configuration.GetSection(NotificationEmailAdapterOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<NotificationEmailAdapterOptions>,
            NotificationEmailAdapterOptionsValidator>());

        if (settings.Enabled)
        {
            services.TryAddSingleton<IUserNotificationEmailRenderer, PlainTextUserNotificationEmailRenderer>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IUserNotificationSink, EmailUserNotificationSink>());
        }

        return services;
    }
}
