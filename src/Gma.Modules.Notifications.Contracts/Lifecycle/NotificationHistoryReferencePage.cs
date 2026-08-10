namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationHistoryReferencePage(
    NotificationHistoryReferenceStatus Status,
    long ReferenceVersion,
    IReadOnlyList<NotificationHistoryReferenceRecord> Records,
    long NextStreamSequence,
    bool HasMore);
