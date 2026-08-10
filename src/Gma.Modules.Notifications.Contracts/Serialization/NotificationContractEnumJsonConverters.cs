namespace Gma.Modules.Notifications.Contracts;

using System.Text.Json;
using System.Text.Json.Serialization;

internal static class NotificationContractEnumJson
{
    public static TEnum ReadString<TEnum>(
        ref Utf8JsonReader reader,
        string displayName,
        Func<string?, TEnum?> parse)
        where TEnum : struct, Enum
    {
        if (reader.TokenType is not JsonTokenType.String)
        {
            throw new JsonException($"{displayName} must be a string.");
        }

        TEnum? parsed = parse(reader.GetString());
        return parsed ?? throw new JsonException($"{displayName} is invalid.");
    }

    public static void WriteString<TEnum>(
        Utf8JsonWriter writer,
        TEnum value,
        string displayName,
        Func<TEnum, string> format)
        where TEnum : struct, Enum
    {
        try
        {
            writer.WriteStringValue(format(value));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new JsonException($"{displayName} is invalid.", exception);
        }
    }

    public static NotificationSeverity? ParseSeverity(string? value)
    {
        string normalized = Normalize(value);
        if (Enum.TryParse(normalized, ignoreCase: true, out NotificationSeverity severity) &&
            severity is not NotificationSeverity.Unknown &&
            Enum.IsDefined(severity))
        {
            return severity;
        }

        return normalized switch
        {
            "info" => NotificationSeverity.Info,
            "success" => NotificationSeverity.Success,
            "warning" => NotificationSeverity.Warning,
            "error" => NotificationSeverity.Error,
            _ => null
        };
    }

    public static string FormatSeverity(NotificationSeverity severity) =>
        severity switch
        {
            NotificationSeverity.Info => "info",
            NotificationSeverity.Success => "success",
            NotificationSeverity.Warning => "warning",
            NotificationSeverity.Error => "error",
            _ => throw new JsonException("Notification severity is invalid.")
        };

    public static NotificationTagKind? ParseTagKind(string? value) =>
        Normalize(value) switch
        {
            "delivery" => NotificationTagKind.Delivery,
            "domain" => NotificationTagKind.Domain,
            _ => null
        };

    public static string FormatTagKind(NotificationTagKind kind) =>
        kind switch
        {
            NotificationTagKind.Delivery => "delivery",
            NotificationTagKind.Domain => "domain",
            _ => throw new JsonException("Notification tag kind is invalid.")
        };

    public static NotificationDeliveryPolicy? ParseDeliveryPolicy(string? value) =>
        Normalize(value) switch
        {
            "respect-preferences" => NotificationDeliveryPolicy.RespectPreferences,
            "mandatory" => NotificationDeliveryPolicy.Mandatory,
            _ => null
        };

    public static string FormatDeliveryPolicy(NotificationDeliveryPolicy policy) =>
        policy switch
        {
            NotificationDeliveryPolicy.RespectPreferences => "respect-preferences",
            NotificationDeliveryPolicy.Mandatory => "mandatory",
            _ => throw new JsonException("Notification delivery policy is invalid.")
        };

    public static NotificationTagOrigin? ParseTagOrigin(string? value) =>
        Normalize(value) switch
        {
            "system" => NotificationTagOrigin.System,
            "module" => NotificationTagOrigin.Module,
            "operator" => NotificationTagOrigin.Operator,
            _ => null
        };

    public static string FormatTagOrigin(NotificationTagOrigin origin) => origin switch
    {
        NotificationTagOrigin.System => "system",
        NotificationTagOrigin.Module => "module",
        NotificationTagOrigin.Operator => "operator",
        _ => throw new JsonException("Notification tag origin is invalid.")
    };

    public static NotificationDeliveryStatus? ParseDeliveryStatus(string? value) =>
        Normalize(value) switch
        {
            "pending" => NotificationDeliveryStatus.Pending,
            "processing" => NotificationDeliveryStatus.Processing,
            "retry-scheduled" => NotificationDeliveryStatus.RetryScheduled,
            "delivered" => NotificationDeliveryStatus.Delivered,
            "rejected" => NotificationDeliveryStatus.Rejected,
            "exhausted" => NotificationDeliveryStatus.Exhausted,
            "suppressed" => NotificationDeliveryStatus.Suppressed,
            "unroutable" => NotificationDeliveryStatus.Unroutable,
            _ => null
        };

    public static string FormatDeliveryStatus(NotificationDeliveryStatus status) => status switch
    {
        NotificationDeliveryStatus.Pending => "pending",
        NotificationDeliveryStatus.Processing => "processing",
        NotificationDeliveryStatus.RetryScheduled => "retry-scheduled",
        NotificationDeliveryStatus.Delivered => "delivered",
        NotificationDeliveryStatus.Rejected => "rejected",
        NotificationDeliveryStatus.Exhausted => "exhausted",
        NotificationDeliveryStatus.Suppressed => "suppressed",
        NotificationDeliveryStatus.Unroutable => "unroutable",
        _ => throw new JsonException("Notification delivery status is invalid.")
    };

    public static NotificationDeliveryAttemptOutcome? ParseDeliveryAttemptOutcome(string? value) =>
        Normalize(value) switch
        {
            "delivered" => NotificationDeliveryAttemptOutcome.Delivered,
            "retry" => NotificationDeliveryAttemptOutcome.Retry,
            "rejected" => NotificationDeliveryAttemptOutcome.Rejected,
            "exception" => NotificationDeliveryAttemptOutcome.Exception,
            _ => null
        };

    public static string FormatDeliveryAttemptOutcome(NotificationDeliveryAttemptOutcome outcome) => outcome switch
    {
        NotificationDeliveryAttemptOutcome.Delivered => "delivered",
        NotificationDeliveryAttemptOutcome.Retry => "retry",
        NotificationDeliveryAttemptOutcome.Rejected => "rejected",
        NotificationDeliveryAttemptOutcome.Exception => "exception",
        _ => throw new JsonException("Notification delivery attempt outcome is invalid.")
    };

    public static NotificationBroadcastAudience? ParseAudience(string? value)
    {
        string normalized = Normalize(value);
        if (Enum.TryParse(normalized, ignoreCase: true, out NotificationBroadcastAudience audience) &&
            audience is not NotificationBroadcastAudience.Unknown &&
            Enum.IsDefined(audience))
        {
            return audience;
        }

        return normalized switch
        {
            "tenant-users" => NotificationBroadcastAudience.TenantUsers,
            "tenant-admins" => NotificationBroadcastAudience.TenantAdmins,
            "platform-users" => NotificationBroadcastAudience.PlatformUsers,
            "platform-admins" => NotificationBroadcastAudience.PlatformAdmins,
            _ => null
        };
    }

    public static string FormatAudience(NotificationBroadcastAudience audience) =>
        audience switch
        {
            NotificationBroadcastAudience.TenantUsers => "tenant-users",
            NotificationBroadcastAudience.TenantAdmins => "tenant-admins",
            NotificationBroadcastAudience.PlatformUsers => "platform-users",
            NotificationBroadcastAudience.PlatformAdmins => "platform-admins",
            _ => throw new JsonException("Notification broadcast audience is invalid.")
        };

    public static NotificationBroadcastRecipientKind? ParseRecipientKind(string? value)
    {
        string normalized = Normalize(value);
        if (Enum.TryParse(normalized, ignoreCase: true, out NotificationBroadcastRecipientKind recipientKind) &&
            recipientKind is not NotificationBroadcastRecipientKind.Unknown &&
            Enum.IsDefined(recipientKind))
        {
            return recipientKind;
        }

        return normalized switch
        {
            "user" => NotificationBroadcastRecipientKind.User,
            "admin" => NotificationBroadcastRecipientKind.Admin,
            _ => null
        };
    }

    public static string FormatRecipientKind(NotificationBroadcastRecipientKind recipientKind) =>
        recipientKind switch
        {
            NotificationBroadcastRecipientKind.User => "user",
            NotificationBroadcastRecipientKind.Admin => "admin",
            _ => throw new JsonException("Notification broadcast recipient kind is invalid.")
        };

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();
}
