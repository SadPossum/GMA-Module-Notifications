namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationHistoryReferenceSnapshot(
    NotificationHistoryReferenceStatus Status,
    long Version,
    int RecordCount,
    long LatestStreamSequence);
