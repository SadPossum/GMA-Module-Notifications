namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class NotificationSeverityJsonConverter : JsonConverter<NotificationSeverity>
{
    public override NotificationSeverity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        NotificationContractEnumJson.ReadString(ref reader, "Notification severity", NotificationContractEnumJson.ParseSeverity);

    public override void Write(Utf8JsonWriter writer, NotificationSeverity value, JsonSerializerOptions options) =>
        writer.WriteStringValue(NotificationContractEnumJson.FormatSeverity(value));
}
