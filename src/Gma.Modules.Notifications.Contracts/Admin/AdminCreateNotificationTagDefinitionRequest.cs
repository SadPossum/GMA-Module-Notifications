namespace Gma.Modules.Notifications.Contracts;

public sealed record AdminCreateNotificationTagDefinitionRequest(
    string Key,
    NotificationTagKind Kind,
    string DisplayName,
    string Description);
