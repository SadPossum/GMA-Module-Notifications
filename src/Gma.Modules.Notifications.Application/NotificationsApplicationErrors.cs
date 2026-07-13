namespace Gma.Modules.Notifications.Application;

using Gma.Framework.Results;
using Gma.Modules.Notifications.Domain.Errors;

public static class NotificationsApplicationErrors
{
    public static readonly Error NotificationNotFound = NotificationsDomainErrors.NotificationNotFound;
    public static readonly Error BroadcastNotFound = NotificationsDomainErrors.BroadcastNotFound;
    public static readonly Error TagDefinitionNotFound = new("Notifications.TagDefinitionNotFound", "Notification tag definition was not found.");
    public static readonly Error TagDefinitionAlreadyExists = new("Notifications.TagDefinitionAlreadyExists", "Notification tag definition already exists.");
    public static readonly Error TagDefinitionInactive = new("Notifications.TagDefinitionInactive", "Notification tag definition is inactive.");
    public static readonly Error DeliveryProviderUnsupported = new("Notifications.DeliveryProviderUnsupported", "The notification delivery provider does not support this delivery tag.");
    public static readonly Error DeliveryNotFound = new("Notifications.DeliveryNotFound", "Notification delivery was not found.");
    public static readonly Error DeliveryStatusInvalid = new("Notifications.DeliveryStatusInvalid", "Notification delivery status filter is invalid.");
    public static readonly Error AccessDenied = new("Notifications.AccessDenied", "Notification access is denied.");
    public static readonly Error StreamCursorInvalid = new("Notifications.StreamCursorInvalid", "Notification stream cursor is invalid.");
}
