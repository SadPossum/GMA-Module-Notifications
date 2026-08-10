namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationHistoryReferenceCloseBatchResult(
    NotificationHistoryReferenceCloseBatchStatus Status,
    NotificationHistoryReferenceCloseBatchProgress? Progress,
    NotificationHistoryReferenceCloseBatchReceipt? Receipt);
