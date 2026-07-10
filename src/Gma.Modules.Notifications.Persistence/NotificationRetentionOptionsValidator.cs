namespace Gma.Modules.Notifications.Persistence;

using Microsoft.Extensions.Options;

internal sealed class NotificationRetentionOptionsValidator : IValidateOptions<NotificationRetentionOptions>
{
    public ValidateOptionsResult Validate(string? name, NotificationRetentionOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        List<string> failures = [];
        if (options.ReadHistoryDays <= 0 || options.UnreadHistoryDays < options.ReadHistoryDays)
        {
            failures.Add("Notifications:Retention requires positive history periods and UnreadHistoryDays >= ReadHistoryDays.");
        }

        if (options.BroadcastDays <= 0)
        {
            failures.Add("Notifications:Retention:BroadcastDays must be positive.");
        }

        if (options.BatchSize is < 1 or > 10_000)
        {
            failures.Add("Notifications:Retention:BatchSize must be between 1 and 10000.");
        }

        if (options.IntervalMinutes is < 1 or > 10_080)
        {
            failures.Add("Notifications:Retention:IntervalMinutes must be between 1 and 10080.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
