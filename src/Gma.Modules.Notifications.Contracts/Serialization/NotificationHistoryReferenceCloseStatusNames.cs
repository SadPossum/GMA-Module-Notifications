namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

public static class NotificationHistoryReferenceCloseStatusNames
{
    public static string ToWireName(
        NotificationHistoryReferenceCloseStatus status) =>
        status switch
        {
            NotificationHistoryReferenceCloseStatus.Invalid => "invalid",
            NotificationHistoryReferenceCloseStatus.Completed => "completed",
            NotificationHistoryReferenceCloseStatus.Replayed => "replayed",
            NotificationHistoryReferenceCloseStatus.Stale => "stale",
            NotificationHistoryReferenceCloseStatus.Busy => "busy",
            NotificationHistoryReferenceCloseStatus.Conflict => "conflict",
            NotificationHistoryReferenceCloseStatus.Overflow => "overflow",
            NotificationHistoryReferenceCloseStatus.ScopeUnavailable =>
                "scope-unavailable",
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Notification history reference close status is invalid.")
        };

    public static bool TryParse(
        string? value,
        out NotificationHistoryReferenceCloseStatus status)
    {
        status = (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "invalid" => NotificationHistoryReferenceCloseStatus.Invalid,
            "completed" => NotificationHistoryReferenceCloseStatus.Completed,
            "replayed" => NotificationHistoryReferenceCloseStatus.Replayed,
            "stale" => NotificationHistoryReferenceCloseStatus.Stale,
            "busy" => NotificationHistoryReferenceCloseStatus.Busy,
            "conflict" => NotificationHistoryReferenceCloseStatus.Conflict,
            "overflow" => NotificationHistoryReferenceCloseStatus.Overflow,
            "scope-unavailable" =>
                NotificationHistoryReferenceCloseStatus.ScopeUnavailable,
            _ => NotificationHistoryReferenceCloseStatus.Unknown
        };
        return status is not NotificationHistoryReferenceCloseStatus.Unknown;
    }
}

internal sealed class NotificationHistoryReferenceCloseStatusJsonConverter
    : JsonConverter<NotificationHistoryReferenceCloseStatus>
{
    public override NotificationHistoryReferenceCloseStatus Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        NotificationContractEnumJson.ReadString(
            ref reader,
            "Notification history reference close status",
            Parse);

    public override void Write(
        Utf8JsonWriter writer,
        NotificationHistoryReferenceCloseStatus value,
        JsonSerializerOptions options) =>
        NotificationContractEnumJson.WriteString(
            writer,
            value,
            "Notification history reference close status",
            NotificationHistoryReferenceCloseStatusNames.ToWireName);

    private static NotificationHistoryReferenceCloseStatus? Parse(
        string? value) =>
        NotificationHistoryReferenceCloseStatusNames.TryParse(
            value,
            out var status)
            ? status
            : null;
}
