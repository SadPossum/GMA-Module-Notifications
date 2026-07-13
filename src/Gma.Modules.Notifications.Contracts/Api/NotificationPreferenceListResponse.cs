namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationPreferenceListResponse(
    IReadOnlyList<NotificationPreferenceItem> Items);
