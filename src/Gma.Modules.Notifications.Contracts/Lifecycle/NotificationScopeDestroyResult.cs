namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationScopeDestroyResult(
    NotificationScopeDestroyStatus Status,
    NotificationScopeDestroyProgress? Progress,
    NotificationScopeDestroyReceipt? Receipt);
