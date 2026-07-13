namespace Gma.Modules.Notifications.Contracts;

using FrameworkNotificationTags = Framework.Notifications.NotificationTags;

public sealed record NotificationTag
{
    public const int DisplayNameMaxLength = 128;
    public const int DescriptionMaxLength = 512;

    public NotificationTag(
        string key,
        NotificationTagKind kind,
        string? displayName = null,
        string? description = null)
    {
        this.Key = FrameworkNotificationTags.Normalize(key, nameof(key));
        this.Kind = NormalizeKind(kind);
        ValidateNamespace(this.Key, this.Kind);
        this.DisplayName = NormalizeOptional(displayName, DisplayNameMaxLength, nameof(displayName));
        this.Description = NormalizeOptional(description, DescriptionMaxLength, nameof(description));
    }

    public string Key { get; }
    public NotificationTagKind Kind { get; }
    public string? DisplayName { get; }
    public string? Description { get; }

    private static NotificationTagKind NormalizeKind(NotificationTagKind kind) =>
        kind is NotificationTagKind.Delivery or NotificationTagKind.Domain
            ? kind
            : throw new ArgumentOutOfRangeException(nameof(kind), kind, "Notification tag kind must be delivery or domain.");

    private static void ValidateNamespace(string key, NotificationTagKind kind)
    {
        bool valid = kind switch
        {
            NotificationTagKind.Delivery => key.StartsWith("delivery:", StringComparison.Ordinal),
            NotificationTagKind.Domain => key.StartsWith("domain:", StringComparison.Ordinal),
            _ => false
        };

        if (!valid)
        {
            throw new ArgumentException(
                $"Notification tag '{key}' does not match its '{kind}' kind.",
                nameof(key));
        }
    }

    private static string? NormalizeOptional(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();
        if (normalized.Length > maxLength || normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"{parameterName} must be {maxLength} characters or fewer and cannot contain control characters.",
                parameterName);
        }

        return normalized;
    }
}
