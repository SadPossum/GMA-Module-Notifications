namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class NotificationTagKindJsonConverter : JsonConverter<NotificationTagKind>
{
    public override NotificationTagKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        NotificationContractEnumJson.ReadString(ref reader, "Notification tag kind", NotificationContractEnumJson.ParseTagKind);

    public override void Write(Utf8JsonWriter writer, NotificationTagKind value, JsonSerializerOptions options) =>
        writer.WriteStringValue(NotificationContractEnumJson.FormatTagKind(value));
}
