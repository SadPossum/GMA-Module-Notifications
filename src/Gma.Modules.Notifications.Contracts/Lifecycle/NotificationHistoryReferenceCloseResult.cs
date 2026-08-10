namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationHistoryReferenceCloseResult(
    NotificationHistoryReferenceCloseStatus Status,
    NotificationHistoryReferenceCloseReceipt? Receipt);
