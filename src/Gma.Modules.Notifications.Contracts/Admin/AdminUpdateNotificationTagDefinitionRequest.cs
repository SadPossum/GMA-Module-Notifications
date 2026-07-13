namespace Gma.Modules.Notifications.Contracts;

public sealed record AdminUpdateNotificationTagDefinitionRequest(
    string DisplayName,
    string Description,
    bool IsActive);
