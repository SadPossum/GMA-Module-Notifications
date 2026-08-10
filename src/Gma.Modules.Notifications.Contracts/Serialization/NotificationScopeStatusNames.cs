namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

public static class NotificationScopeStatusNames
{
    public static string ToWireName(NotificationScopeStatus status) =>
        status switch
        {
            NotificationScopeStatus.Missing => "missing",
            NotificationScopeStatus.Open => "open",
            NotificationScopeStatus.Closed => "closed",
            NotificationScopeStatus.ScopeUnavailable => "scope-unavailable",
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Notification scope status is invalid.")
        };

    public static bool TryParse(
        string? value,
        out NotificationScopeStatus status)
    {
        status = (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "missing" => NotificationScopeStatus.Missing,
            "open" => NotificationScopeStatus.Open,
            "closed" => NotificationScopeStatus.Closed,
            "scope-unavailable" => NotificationScopeStatus.ScopeUnavailable,
            _ => NotificationScopeStatus.Unknown
        };
        return status is not NotificationScopeStatus.Unknown;
    }
}

internal sealed class NotificationScopeStatusJsonConverter
    : JsonConverter<NotificationScopeStatus>
{
    public override NotificationScopeStatus Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        NotificationContractEnumJson.ReadString(
            ref reader,
            "Notification scope status",
            Parse);

    public override void Write(
        Utf8JsonWriter writer,
        NotificationScopeStatus value,
        JsonSerializerOptions options) =>
        NotificationContractEnumJson.WriteString(
            writer,
            value,
            "Notification scope status",
            NotificationScopeStatusNames.ToWireName);

    private static NotificationScopeStatus? Parse(string? value) =>
        NotificationScopeStatusNames.TryParse(value, out var status)
            ? status
            : null;
}
