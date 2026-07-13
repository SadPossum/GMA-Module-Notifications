namespace Gma.Modules.Notifications.Application;

using Gma.Framework.Runtime.Workers;
using Microsoft.Extensions.Options;

internal sealed class NotificationDeliveryOptionsValidator : IValidateOptions<NotificationDeliveryOptions>
{
    public ValidateOptionsResult Validate(string? name, NotificationDeliveryOptions options)
    {
        List<string> failures = [];
        if (options.BatchSize is < 1 or > 1000)
        {
            failures.Add("Notifications:Delivery:BatchSize must be between 1 and 1000.");
        }

        if (options.MaxConcurrency is < 1 or > 256)
        {
            failures.Add("Notifications:Delivery:MaxConcurrency must be between 1 and 256.");
        }

        if (options.PollIntervalSeconds is < 1 or > 3600)
        {
            failures.Add("Notifications:Delivery:PollIntervalSeconds must be between 1 and 3600.");
        }

        if (options.LeaseSeconds is < 10 or > 3600)
        {
            failures.Add("Notifications:Delivery:LeaseSeconds must be between 10 and 3600.");
        }

        if (options.MaxAttempts is < 1 or > 100)
        {
            failures.Add("Notifications:Delivery:MaxAttempts must be between 1 and 100.");
        }

        if (options.RetryBaseSeconds is < 1 or > 3600)
        {
            failures.Add("Notifications:Delivery:RetryBaseSeconds must be between 1 and 3600.");
        }

        if (options.RetryMaxMinutes is < 1 or > 10080)
        {
            failures.Add("Notifications:Delivery:RetryMaxMinutes must be between 1 and 10080.");
        }

        if (options.AttemptRetentionDays is < 1 or > 3650)
        {
            failures.Add("Notifications:Delivery:AttemptRetentionDays must be between 1 and 3650.");
        }

        if (options.RetryBaseSeconds > options.RetryMaxMinutes * 60)
        {
            failures.Add("Notifications delivery retry base delay cannot exceed the retry maximum.");
        }
        if (!string.IsNullOrWhiteSpace(options.WorkerId))
        {
            try
            {
                _ = WorkerIds.Normalize(options.WorkerId);
            }
            catch (ArgumentException)
            {
                failures.Add($"Notifications:Delivery:WorkerId must be {WorkerIds.MaxLength} characters or fewer and cannot contain whitespace or control characters.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
