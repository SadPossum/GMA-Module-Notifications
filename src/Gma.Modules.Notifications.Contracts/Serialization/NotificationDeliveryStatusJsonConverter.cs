namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class NotificationDeliveryStatusJsonConverter : JsonConverter<NotificationDeliveryStatus>
{
    public override NotificationDeliveryStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        NotificationContractEnumJson.ReadString(ref reader, "Notification delivery status", NotificationContractEnumJson.ParseDeliveryStatus);

    public override void Write(Utf8JsonWriter writer, NotificationDeliveryStatus value, JsonSerializerOptions options) =>
        writer.WriteStringValue(NotificationContractEnumJson.FormatDeliveryStatus(value));
}
