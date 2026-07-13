namespace Gma.Modules.Notifications.Adapters.Email;

using Gma.Framework.Notifications;

public sealed record NotificationEmailContent(
    string Subject,
    string? TextBody,
    string? HtmlBody = null);

public interface IUserNotificationEmailRenderer
{
    ValueTask<NotificationEmailContent> RenderAsync(
        UserNotificationMessage notification,
        CancellationToken cancellationToken = default);
}

internal sealed class PlainTextUserNotificationEmailRenderer : IUserNotificationEmailRenderer
{
    public ValueTask<NotificationEmailContent> RenderAsync(
        UserNotificationMessage notification,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new NotificationEmailContent(
            notification.Title,
            notification.Body ?? notification.Title));
    }
}
