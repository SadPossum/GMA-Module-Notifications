namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

public static class NotificationScopeDestroyStatusNames
{
    public static string ToWireName(NotificationScopeDestroyStatus status) =>
        status switch
        {
            NotificationScopeDestroyStatus.Invalid => "invalid",
            NotificationScopeDestroyStatus.InProgress => "in-progress",
            NotificationScopeDestroyStatus.Completed => "completed",
            NotificationScopeDestroyStatus.Replayed => "replayed",
            NotificationScopeDestroyStatus.Stale => "stale",
            NotificationScopeDestroyStatus.Busy => "busy",
            NotificationScopeDestroyStatus.Conflict => "conflict",
            NotificationScopeDestroyStatus.ScopeUnavailable =>
                "scope-unavailable",
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Notification scope destroy status is invalid.")
        };

    public static bool TryParse(
        string? value,
        out NotificationScopeDestroyStatus status)
    {
        status = (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "invalid" => NotificationScopeDestroyStatus.Invalid,
            "in-progress" => NotificationScopeDestroyStatus.InProgress,
            "completed" => NotificationScopeDestroyStatus.Completed,
            "replayed" => NotificationScopeDestroyStatus.Replayed,
            "stale" => NotificationScopeDestroyStatus.Stale,
            "busy" => NotificationScopeDestroyStatus.Busy,
            "conflict" => NotificationScopeDestroyStatus.Conflict,
            "scope-unavailable" =>
                NotificationScopeDestroyStatus.ScopeUnavailable,
            _ => NotificationScopeDestroyStatus.Unknown
        };
        return status is not NotificationScopeDestroyStatus.Unknown;
    }
}

internal sealed class NotificationScopeDestroyStatusJsonConverter
    : JsonConverter<NotificationScopeDestroyStatus>
{
    public override NotificationScopeDestroyStatus Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        NotificationContractEnumJson.ReadString(
            ref reader,
            "Notification scope destroy status",
            Parse);

    public override void Write(
        Utf8JsonWriter writer,
        NotificationScopeDestroyStatus value,
        JsonSerializerOptions options) =>
        NotificationContractEnumJson.WriteString(
            writer,
            value,
            "Notification scope destroy status",
            NotificationScopeDestroyStatusNames.ToWireName);

    private static NotificationScopeDestroyStatus? Parse(string? value) =>
        NotificationScopeDestroyStatusNames.TryParse(value, out var status)
            ? status
            : null;
}
