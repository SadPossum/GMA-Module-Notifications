namespace Gma.Modules.Notifications.Adapters.Email;

using Gma.Framework.Email;
using Gma.Framework.Naming;
using Microsoft.Extensions.Options;

internal sealed class NotificationEmailAdapterOptionsValidator : IValidateOptions<NotificationEmailAdapterOptions>
{
    public ValidateOptionsResult Validate(string? name, NotificationEmailAdapterOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        List<string> failures = [];
        try
        {
            _ = SharedNameSegments.NormalizeKebabSegment(options.ProviderName, "email notification provider", nameof(options.ProviderName));
        }
        catch (ArgumentException)
        {
            failures.Add("Notifications:Adapters:Email:ProviderName must be a kebab-case name segment.");
        }

        if (options.SenderAddress is not null && !EmailSendRequest.IsValidAddress(options.SenderAddress))
        {
            failures.Add("Notifications:Adapters:Email:SenderAddress must be a valid email address.");
        }

        if (options.SenderName?.Length > 256 || options.SenderName?.Any(char.IsControl) == true)
        {
            failures.Add("Notifications:Adapters:Email:SenderName must be 256 characters or fewer and cannot contain control characters.");
        }

        if (options.SubjectPrefix?.Length > 128 || options.SubjectPrefix?.Any(char.IsControl) == true)
        {
            failures.Add("Notifications:Adapters:Email:SubjectPrefix must be 128 characters or fewer and cannot contain control characters.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
