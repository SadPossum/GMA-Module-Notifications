namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class NotificationDeliveryPolicyJsonConverter : JsonConverter<NotificationDeliveryPolicy>
{
    public override NotificationDeliveryPolicy Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        NotificationContractEnumJson.ReadString(ref reader, "Notification delivery policy", NotificationContractEnumJson.ParseDeliveryPolicy);

    public override void Write(Utf8JsonWriter writer, NotificationDeliveryPolicy value, JsonSerializerOptions options) =>
        writer.WriteStringValue(NotificationContractEnumJson.FormatDeliveryPolicy(value));
}
