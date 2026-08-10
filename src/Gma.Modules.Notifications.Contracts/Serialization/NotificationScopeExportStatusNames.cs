namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

public static class NotificationScopeExportStatusNames
{
    public static string ToWireName(NotificationScopeExportStatus status) =>
        status switch
        {
            NotificationScopeExportStatus.Invalid => "invalid",
            NotificationScopeExportStatus.Completed => "completed",
            NotificationScopeExportStatus.Missing => "missing",
            NotificationScopeExportStatus.Closed => "closed",
            NotificationScopeExportStatus.Stale => "stale",
            NotificationScopeExportStatus.ScopeUnavailable => "scope-unavailable",
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Notification scope export status is invalid.")
        };

    public static bool TryParse(
        string? value,
        out NotificationScopeExportStatus status)
    {
        status = (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "invalid" => NotificationScopeExportStatus.Invalid,
            "completed" => NotificationScopeExportStatus.Completed,
            "missing" => NotificationScopeExportStatus.Missing,
            "closed" => NotificationScopeExportStatus.Closed,
            "stale" => NotificationScopeExportStatus.Stale,
            "scope-unavailable" =>
                NotificationScopeExportStatus.ScopeUnavailable,
            _ => NotificationScopeExportStatus.Unknown
        };
        return status is not NotificationScopeExportStatus.Unknown;
    }
}

internal sealed class NotificationScopeExportStatusJsonConverter
    : JsonConverter<NotificationScopeExportStatus>
{
    public override NotificationScopeExportStatus Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        NotificationContractEnumJson.ReadString(
            ref reader,
            "Notification scope export status",
            Parse);

    public override void Write(
        Utf8JsonWriter writer,
        NotificationScopeExportStatus value,
        JsonSerializerOptions options) =>
        NotificationContractEnumJson.WriteString(
            writer,
            value,
            "Notification scope export status",
            NotificationScopeExportStatusNames.ToWireName);

    private static NotificationScopeExportStatus? Parse(string? value) =>
        NotificationScopeExportStatusNames.TryParse(value, out var status)
            ? status
            : null;
}
