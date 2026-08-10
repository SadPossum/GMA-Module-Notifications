namespace Gma.Modules.Notifications.Application;

using Microsoft.Extensions.Options;

internal sealed class NotificationStreamOptionsValidator : IValidateOptions<NotificationStreamOptions>
{
    public ValidateOptionsResult Validate(string? name, NotificationStreamOptions options)
    {
        if (options.BatchSize is <= 0 or > NotificationStreamOptions.MaxBatchSize)
        {
            return ValidateOptionsResult.Fail(
                $"{NotificationStreamOptions.SectionName}:BatchSize must be between 1 and {NotificationStreamOptions.MaxBatchSize}.");
        }

        if (options.PollInterval < TimeSpan.FromMilliseconds(250) ||
            options.PollInterval > TimeSpan.FromMinutes(1))
        {
            return ValidateOptionsResult.Fail(
                $"{NotificationStreamOptions.SectionName}:PollInterval must be between 250 milliseconds and 1 minute.");
        }

        if (options.HeartbeatInterval < TimeSpan.FromSeconds(5) ||
            options.HeartbeatInterval > TimeSpan.FromMinutes(5) ||
            options.HeartbeatInterval < options.PollInterval)
        {
            return ValidateOptionsResult.Fail(
                $"{NotificationStreamOptions.SectionName}:HeartbeatInterval must be between 5 seconds and 5 minutes and cannot be shorter than PollInterval.");
        }

        if (options.AuthorizationRevalidationInterval < TimeSpan.FromSeconds(5) ||
            options.AuthorizationRevalidationInterval > TimeSpan.FromMinutes(5))
        {
            return ValidateOptionsResult.Fail(
                $"{NotificationStreamOptions.SectionName}:AuthorizationRevalidationInterval must be between 5 seconds and 5 minutes.");
        }

        if (options.MaximumConnectionLifetime < TimeSpan.FromMinutes(1) ||
            options.MaximumConnectionLifetime > TimeSpan.FromHours(24) ||
            options.MaximumConnectionLifetime < options.AuthorizationRevalidationInterval)
        {
            return ValidateOptionsResult.Fail(
                $"{NotificationStreamOptions.SectionName}:MaximumConnectionLifetime must be between 1 minute and 24 hours and cannot be shorter than AuthorizationRevalidationInterval.");
        }

        return ValidateOptionsResult.Success;
    }
}
