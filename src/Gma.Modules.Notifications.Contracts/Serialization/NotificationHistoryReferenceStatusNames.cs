namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

public static class NotificationHistoryReferenceStatusNames
{
    public static string ToWireName(NotificationHistoryReferenceStatus status) =>
        status switch
        {
            NotificationHistoryReferenceStatus.Missing => "missing",
            NotificationHistoryReferenceStatus.Open => "open",
            NotificationHistoryReferenceStatus.Closed => "closed",
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Notification history reference status is invalid.")
        };

    public static bool TryParse(
        string? value,
        out NotificationHistoryReferenceStatus status)
    {
        status = (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "missing" => NotificationHistoryReferenceStatus.Missing,
            "open" => NotificationHistoryReferenceStatus.Open,
            "closed" => NotificationHistoryReferenceStatus.Closed,
            _ => NotificationHistoryReferenceStatus.Unknown
        };
        return status is not NotificationHistoryReferenceStatus.Unknown;
    }
}

internal sealed class NotificationHistoryReferenceStatusJsonConverter
    : JsonConverter<NotificationHistoryReferenceStatus>
{
    public override NotificationHistoryReferenceStatus Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        NotificationContractEnumJson.ReadString(
            ref reader,
            "Notification history reference status",
            Parse);

    public override void Write(
        Utf8JsonWriter writer,
        NotificationHistoryReferenceStatus value,
        JsonSerializerOptions options) =>
        NotificationContractEnumJson.WriteString(
            writer,
            value,
            "Notification history reference status",
            NotificationHistoryReferenceStatusNames.ToWireName);

    private static NotificationHistoryReferenceStatus? Parse(string? value) =>
        NotificationHistoryReferenceStatusNames.TryParse(value, out var status)
            ? status
            : null;
}
