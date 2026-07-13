namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class NotificationDeliveryProviderCodeJsonConverter : JsonConverter<NotificationDeliveryProviderCode>
{
    public override NotificationDeliveryProviderCode Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Notification delivery provider must be a string.");
        }

        try
        {
            return new NotificationDeliveryProviderCode(reader.GetString() ?? string.Empty);
        }
        catch (ArgumentException exception)
        {
            throw new JsonException("Notification delivery provider is invalid.", exception);
        }
    }

    public override void Write(
        Utf8JsonWriter writer,
        NotificationDeliveryProviderCode value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
