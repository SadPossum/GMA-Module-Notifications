namespace Gma.Modules.Notifications.Domain.ValueObjects;

using Gma.Framework.Naming;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Domain.Errors;

public sealed class NotificationDeliveryProvider : IEquatable<NotificationDeliveryProvider>
{
    public const int MaxLength = 128;

    private NotificationDeliveryProvider(string value) => this.Value = value;

    public string Value { get; }

    public static Result<NotificationDeliveryProvider> Create(string value)
    {
        try
        {
            string normalized = SharedNameSegments.NormalizeKebabSegment(
                value,
                "notification delivery provider",
                nameof(value));
            return normalized.Length <= MaxLength
                ? Result.Success(new NotificationDeliveryProvider(normalized))
                : Result.Failure<NotificationDeliveryProvider>(NotificationsDomainErrors.DeliveryProviderInvalid);
        }
        catch (ArgumentException)
        {
            return Result.Failure<NotificationDeliveryProvider>(NotificationsDomainErrors.DeliveryProviderInvalid);
        }
    }

    public bool Equals(NotificationDeliveryProvider? other) =>
        other is not null && string.Equals(this.Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => this.Equals(obj as NotificationDeliveryProvider);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(this.Value);

    public override string ToString() => this.Value;
}
