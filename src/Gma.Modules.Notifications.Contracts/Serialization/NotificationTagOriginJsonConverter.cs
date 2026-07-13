namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class NotificationTagOriginJsonConverter : JsonConverter<NotificationTagOrigin>
{
    public override NotificationTagOrigin Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        NotificationContractEnumJson.ReadString(ref reader, "Notification tag origin", NotificationContractEnumJson.ParseTagOrigin);

    public override void Write(Utf8JsonWriter writer, NotificationTagOrigin value, JsonSerializerOptions options) =>
        writer.WriteStringValue(NotificationContractEnumJson.FormatTagOrigin(value));
}
