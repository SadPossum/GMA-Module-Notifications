namespace Gma.Modules.Notifications.Contracts;

public sealed record AdminNotificationTagDefinitionItem(
    Guid Id,
    string Key,
    NotificationTagKind Kind,
    string DisplayName,
    string Description,
    NotificationTagOrigin Origin,
    string Owner,
    bool IsActive,
    int Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string CreatedBy,
    string UpdatedBy);
