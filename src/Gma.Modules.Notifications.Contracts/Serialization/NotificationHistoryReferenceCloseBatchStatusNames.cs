namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

public static class NotificationHistoryReferenceCloseBatchStatusNames
{
    public static string ToWireName(
        NotificationHistoryReferenceCloseBatchStatus status) =>
        status switch
        {
            NotificationHistoryReferenceCloseBatchStatus.Invalid => "invalid",
            NotificationHistoryReferenceCloseBatchStatus.InProgress =>
                "in-progress",
            NotificationHistoryReferenceCloseBatchStatus.Completed =>
                "completed",
            NotificationHistoryReferenceCloseBatchStatus.Replayed => "replayed",
            NotificationHistoryReferenceCloseBatchStatus.Stale => "stale",
            NotificationHistoryReferenceCloseBatchStatus.Busy => "busy",
            NotificationHistoryReferenceCloseBatchStatus.Conflict => "conflict",
            NotificationHistoryReferenceCloseBatchStatus.ScopeUnavailable =>
                "scope-unavailable",
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Notification history reference close batch status is invalid.")
        };

    public static bool TryParse(
        string? value,
        out NotificationHistoryReferenceCloseBatchStatus status)
    {
        status = (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "invalid" => NotificationHistoryReferenceCloseBatchStatus.Invalid,
            "in-progress" =>
                NotificationHistoryReferenceCloseBatchStatus.InProgress,
            "completed" =>
                NotificationHistoryReferenceCloseBatchStatus.Completed,
            "replayed" => NotificationHistoryReferenceCloseBatchStatus.Replayed,
            "stale" => NotificationHistoryReferenceCloseBatchStatus.Stale,
            "busy" => NotificationHistoryReferenceCloseBatchStatus.Busy,
            "conflict" => NotificationHistoryReferenceCloseBatchStatus.Conflict,
            "scope-unavailable" =>
                NotificationHistoryReferenceCloseBatchStatus.ScopeUnavailable,
            _ => NotificationHistoryReferenceCloseBatchStatus.Unknown
        };
        return status is not NotificationHistoryReferenceCloseBatchStatus.Unknown;
    }
}

internal sealed class NotificationHistoryReferenceCloseBatchStatusJsonConverter
    : JsonConverter<NotificationHistoryReferenceCloseBatchStatus>
{
    public override NotificationHistoryReferenceCloseBatchStatus Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        NotificationContractEnumJson.ReadString(
            ref reader,
            "Notification history reference close batch status",
            Parse);

    public override void Write(
        Utf8JsonWriter writer,
        NotificationHistoryReferenceCloseBatchStatus value,
        JsonSerializerOptions options) =>
        NotificationContractEnumJson.WriteString(
            writer,
            value,
            "Notification history reference close batch status",
            NotificationHistoryReferenceCloseBatchStatusNames.ToWireName);

    private static NotificationHistoryReferenceCloseBatchStatus? Parse(
        string? value) =>
        NotificationHistoryReferenceCloseBatchStatusNames.TryParse(
            value,
            out var status)
            ? status
            : null;
}
