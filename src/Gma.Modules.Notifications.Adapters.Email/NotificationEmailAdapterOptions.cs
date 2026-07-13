namespace Gma.Modules.Notifications.Adapters.Email;

public sealed class NotificationEmailAdapterOptions
{
    public const string SectionName = "Notifications:Adapters:Email";

    public bool Enabled { get; set; }
    public string ProviderName { get; set; } = "email";
    public string? SenderAddress { get; set; }
    public string? SenderName { get; set; }
    public string? SubjectPrefix { get; set; }
}
