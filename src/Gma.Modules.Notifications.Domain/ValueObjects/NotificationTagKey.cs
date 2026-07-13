namespace Gma.Modules.Notifications.Domain.ValueObjects;

using Gma.Framework.Naming;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Domain.Errors;

public sealed record NotificationTagKey
{
    public const int MaxLength = 128;

    private NotificationTagKey(string value) => this.Value = value;

    public string Value { get; }

    public static Result<NotificationTagKey> Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result.Failure<NotificationTagKey>(NotificationsDomainErrors.TagKeyInvalid);
        }

        string[] segments = value.Trim().Split(':', StringSplitOptions.None);
        if (segments.Length != 2)
        {
            return Result.Failure<NotificationTagKey>(NotificationsDomainErrors.TagKeyInvalid);
        }

        try
        {
            string tagNamespace = SharedNameSegments.NormalizeKebabSegment(
                segments[0],
                "notification tag namespace",
                nameof(value));
            string name = SharedNameSegments.NormalizeKebabSegment(
                segments[1],
                "notification tag name",
                nameof(value));
            string normalized = $"{tagNamespace}:{name}";

            return normalized.Length <= MaxLength
                ? Result.Success(new NotificationTagKey(normalized))
                : Result.Failure<NotificationTagKey>(NotificationsDomainErrors.TagKeyInvalid);
        }
        catch (ArgumentException)
        {
            return Result.Failure<NotificationTagKey>(NotificationsDomainErrors.TagKeyInvalid);
        }
    }

    public bool IsDelivery => this.Value.StartsWith("delivery:", StringComparison.Ordinal);
    public bool IsDomain => this.Value.StartsWith("domain:", StringComparison.Ordinal);

    public override string ToString() => this.Value;
}
