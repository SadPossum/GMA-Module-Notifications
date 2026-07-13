namespace Gma.Modules.Notifications.Application;

public sealed class NotificationDeliveryOptions
{
    public const string SectionName = "Notifications:Delivery";

    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 50;
    public int MaxConcurrency { get; set; } = 8;
    public int PollIntervalSeconds { get; set; } = 5;
    public int LeaseSeconds { get; set; } = 60;
    public int MaxAttempts { get; set; } = 8;
    public int RetryBaseSeconds { get; set; } = 5;
    public int RetryMaxMinutes { get; set; } = 30;
    public int AttemptRetentionDays { get; set; } = 90;
    public string? WorkerId { get; set; }
}
