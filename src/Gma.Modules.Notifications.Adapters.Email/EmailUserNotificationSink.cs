namespace Gma.Modules.Notifications.Adapters.Email;

using Gma.Framework.Email;
using Gma.Framework.Naming;
using Gma.Framework.Notifications;
using Microsoft.Extensions.Options;

internal sealed class EmailUserNotificationSink(
    IUserNotificationEmailAddressResolver addressResolver,
    IUserNotificationEmailRenderer renderer,
    IEmailSender emailSender,
    IOptions<NotificationEmailAdapterOptions> options)
    : IUserNotificationSink
{
    private readonly NotificationEmailAdapterOptions settings = options.Value;

    public string ProviderName => SharedNameSegments.NormalizeKebabSegment(
        this.settings.ProviderName,
        "email notification provider",
        nameof(this.settings.ProviderName));

    public IReadOnlyCollection<string> DeliveryTags { get; } = [NotificationTags.Email];
    public NotificationSinkDeliveryMode DeliveryModes => NotificationSinkDeliveryMode.Durable;

    public async ValueTask<NotificationSinkDeliveryResult> DeliverAsync(
        NotificationSinkDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        NotificationEmailDestinationResult destination = await addressResolver
            .ResolveAsync(request.Message.ScopeId, request.Message.UserId, cancellationToken)
            .ConfigureAwait(false);
        if (destination.Outcome == NotificationEmailDestinationOutcome.Retry)
        {
            return NotificationSinkDeliveryResult.Retry(
                destination.Code ?? "email-address-resolution-retry",
                destination.RetryAtUtc);
        }

        if (destination.Outcome != NotificationEmailDestinationOutcome.Resolved ||
            !EmailSendRequest.IsValidAddress(destination.Address))
        {
            return NotificationSinkDeliveryResult.Rejected(
                destination.Code ?? "email-address-unavailable");
        }

        NotificationEmailContent content = await renderer
            .RenderAsync(request.Message, cancellationToken)
            .ConfigureAwait(false);
        EmailSendRequest sendRequest;
        try
        {
            sendRequest = new EmailSendRequest(
                    destination.Address!,
                    PrefixSubject(content.Subject, this.settings.SubjectPrefix),
                    content.TextBody,
                    content.HtmlBody,
                    $"notification:{request.DeliveryId:N}",
                    this.settings.SenderAddress,
                    this.settings.SenderName);
        }
        catch (ArgumentException)
        {
            return NotificationSinkDeliveryResult.Rejected("email-content-invalid");
        }

        EmailSendResult sent = await emailSender.SendAsync(
                sendRequest,
                cancellationToken)
            .ConfigureAwait(false);

        return sent.Outcome switch
        {
            EmailSendOutcome.Delivered => NotificationSinkDeliveryResult.Delivered(sent.ProviderMessageId),
            EmailSendOutcome.Retry => NotificationSinkDeliveryResult.Retry(
                sent.Code ?? "email-provider-retry",
                sent.RetryAtUtc),
            EmailSendOutcome.Rejected => NotificationSinkDeliveryResult.Rejected(
                sent.Code ?? "email-provider-rejected"),
            _ => NotificationSinkDeliveryResult.Retry("email-provider-unknown")
        };
    }

    private static string PrefixSubject(string subject, string? prefix) =>
        string.IsNullOrWhiteSpace(prefix)
            ? subject
            : $"{prefix.Trim()} {subject}";
}
