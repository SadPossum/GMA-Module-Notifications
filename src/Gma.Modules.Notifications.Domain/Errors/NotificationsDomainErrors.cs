namespace Gma.Modules.Notifications.Domain.Errors;

using Gma.Framework.Results;

public static class NotificationsDomainErrors
{
    public static readonly Error DeliveryProviderInvalid = new("Notifications.DeliveryProviderInvalid", "Notification delivery provider is invalid.");
    public static readonly Error NotificationIdRequired = new("Notifications.NotificationIdRequired", "Notification id is required.");
    public static readonly Error TenantInvalid = new("Notifications.TenantInvalid", "Notification scope id is invalid.");
    public static readonly Error UserIdInvalid = new("Notifications.UserIdInvalid", "Notification user id is invalid.");
    public static readonly Error ModuleInvalid = new("Notifications.ModuleInvalid", "Notification module is invalid.");
    public static readonly Error NameInvalid = new("Notifications.NameInvalid", "Notification name is invalid.");
    public static readonly Error VersionInvalid = new("Notifications.VersionInvalid", "Notification version is invalid.");
    public static readonly Error TitleInvalid = new("Notifications.TitleInvalid", "Notification title is invalid.");
    public static readonly Error BodyInvalid = new("Notifications.BodyInvalid", "Notification body is invalid.");
    public static readonly Error SeverityInvalid = new("Notifications.SeverityInvalid", "Notification severity is invalid.");
    public static readonly Error PayloadInvalid = new("Notifications.PayloadInvalid", "Notification payload JSON is invalid.");
    public static readonly Error NotificationNotFound = new("Notifications.NotificationNotFound", "Notification was not found.");
    public static readonly Error BroadcastAudienceInvalid = new("Notifications.BroadcastAudienceInvalid", "Notification broadcast audience is invalid.");
    public static readonly Error BroadcastRecipientKindInvalid = new("Notifications.BroadcastRecipientKindInvalid", "Notification broadcast recipient kind is invalid.");
    public static readonly Error PlatformBroadcastTenantForbidden = new("Notifications.PlatformBroadcastTenantForbidden", "Platform notification broadcasts cannot be scope-aware.");
    public static readonly Error BroadcastNotFound = new("Notifications.BroadcastNotFound", "Notification broadcast was not found.");
    public static readonly Error TagKeyInvalid = new("Notifications.TagKeyInvalid", "Notification tag key is invalid.");
    public static readonly Error TagKindInvalid = new("Notifications.TagKindInvalid", "Notification tag kind does not match its namespace.");
    public static readonly Error TagDefinitionIdRequired = new("Notifications.TagDefinitionIdRequired", "Notification tag definition id is required.");
    public static readonly Error TagDefinitionInvalid = new("Notifications.TagDefinitionInvalid", "Notification tag definition is invalid.");
    public static readonly Error PreferenceInvalid = new("Notifications.PreferenceInvalid", "Notification preference is invalid.");
    public static readonly Error DeliveryRouteInvalid = new("Notifications.DeliveryRouteInvalid", "Notification delivery route is invalid.");
    public static readonly Error DeliveryInvalid = new("Notifications.DeliveryInvalid", "Notification delivery is invalid.");
    public static readonly Error DeliveryStatusInvalid = new("Notifications.DeliveryStatusInvalid", "Notification delivery status is invalid.");
    public static readonly Error DeliveryCannotBeClaimed = new("Notifications.DeliveryCannotBeClaimed", "Notification delivery cannot be claimed.");
    public static readonly Error DeliveryLeaseLost = new("Notifications.DeliveryLeaseLost", "Notification delivery lease is no longer owned by this worker.");
    public static readonly Error DeliveryResultInvalid = new("Notifications.DeliveryResultInvalid", "Notification delivery result is invalid.");
    public static readonly Error DeliveryCannotBeRetried = new("Notifications.DeliveryCannotBeRetried", "Notification delivery cannot be retried from its current state.");
    public static readonly Error DeliveryAttemptInvalid = new("Notifications.DeliveryAttemptInvalid", "Notification delivery attempt is invalid.");
}
