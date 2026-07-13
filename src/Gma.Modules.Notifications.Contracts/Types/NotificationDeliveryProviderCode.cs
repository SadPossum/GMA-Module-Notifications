namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json.Serialization;
using Gma.Framework.Naming;

[JsonConverter(typeof(NotificationDeliveryProviderCodeJsonConverter))]
public sealed class NotificationDeliveryProviderCode : IEquatable<NotificationDeliveryProviderCode>
{
    public const int MaxLength = 128;

    public NotificationDeliveryProviderCode(string value)
    {
        string normalized = SharedNameSegments.NormalizeKebabSegment(
            value,
            "notification delivery provider",
            nameof(value));
        this.Value = normalized.Length <= MaxLength
            ? normalized
            : throw new ArgumentException($"Notification delivery provider must be {MaxLength} characters or fewer.", nameof(value));
    }

    public string Value { get; }

    public bool Equals(NotificationDeliveryProviderCode? other) =>
        other is not null && string.Equals(this.Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => this.Equals(obj as NotificationDeliveryProviderCode);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(this.Value);
    public override string ToString() => this.Value;
}
