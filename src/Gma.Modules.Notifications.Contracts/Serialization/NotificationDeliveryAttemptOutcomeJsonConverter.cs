namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class NotificationDeliveryAttemptOutcomeJsonConverter : JsonConverter<NotificationDeliveryAttemptOutcome>
{
    public override NotificationDeliveryAttemptOutcome Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        NotificationContractEnumJson.ReadString(
            ref reader,
            "Notification delivery attempt outcome",
            NotificationContractEnumJson.ParseDeliveryAttemptOutcome);

    public override void Write(Utf8JsonWriter writer, NotificationDeliveryAttemptOutcome value, JsonSerializerOptions options) =>
        writer.WriteStringValue(NotificationContractEnumJson.FormatDeliveryAttemptOutcome(value));
}
