namespace Gma.Modules.Notifications.Persistence;

public sealed class NotificationRetentionOptions
{
    public const string SectionName = "Notifications:Retention";

    public bool Enabled { get; set; }
    public int ReadHistoryDays { get; set; } = 90;
    public int UnreadHistoryDays { get; set; } = 365;
    public int BroadcastDays { get; set; } = 365;
    public int BatchSize { get; set; } = 500;
    public int IntervalMinutes { get; set; } = 60;
}
