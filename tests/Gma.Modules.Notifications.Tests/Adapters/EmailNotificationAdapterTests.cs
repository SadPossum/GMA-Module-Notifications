namespace Gma.Modules.Notifications.Tests.Adapters;

using System.Text.Json;
using Gma.Framework.Email;
using Gma.Framework.Notifications;
using Gma.Modules.Notifications.Adapters.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[Trait("Category", "Unit")]
public sealed class EmailNotificationAdapterTests
{
    [Fact]
    public async Task Adapter_resolves_destination_and_preserves_delivery_id_as_idempotency_key()
    {
        RecordingEmailSender sender = new();
        using ServiceProvider provider = BuildProvider(
            new StaticResolver(NotificationEmailDestinationResult.Resolved("user@example.com")),
            sender);
        IUserNotificationSink sink = Assert.Single(provider.GetServices<IUserNotificationSink>());
        Guid deliveryId = Guid.CreateVersion7();

        NotificationSinkDeliveryResult result = await sink.DeliverAsync(
            new NotificationSinkDeliveryRequest(deliveryId, Message(), attempt: 1, isDurable: true),
            CancellationToken.None);

        Assert.Equal(NotificationSinkDeliveryMode.Durable, sink.DeliveryModes);
        Assert.Equal([NotificationTags.Email], sink.DeliveryTags);
        Assert.Equal(NotificationSinkDeliveryOutcome.Delivered, result.Outcome);
        EmailSendRequest request = Assert.IsType<EmailSendRequest>(sender.Request);
        Assert.Equal("user@example.com", request.RecipientAddress);
        Assert.Equal($"notification:{deliveryId:N}", request.IdempotencyKey);
        Assert.Equal("[GMA] Account signed in", request.Subject);
        Assert.Equal("A new session was created.", request.TextBody);
    }

    [Fact]
    public async Task Adapter_rejects_missing_destination_without_calling_sender()
    {
        RecordingEmailSender sender = new();
        using ServiceProvider provider = BuildProvider(
            new StaticResolver(NotificationEmailDestinationResult.Unavailable()),
            sender);
        IUserNotificationSink sink = Assert.Single(provider.GetServices<IUserNotificationSink>());

        NotificationSinkDeliveryResult result = await sink.DeliverAsync(
            new NotificationSinkDeliveryRequest(Guid.CreateVersion7(), Message(), attempt: 1, isDurable: true),
            CancellationToken.None);

        Assert.Equal(NotificationSinkDeliveryOutcome.Rejected, result.Outcome);
        Assert.Equal("email-address-unavailable", result.Code);
        Assert.Null(sender.Request);
    }

    [Fact]
    public async Task Adapter_rejects_invalid_rendered_content_without_wasting_retries()
    {
        RecordingEmailSender sender = new();
        using ServiceProvider provider = BuildProvider(
            new StaticResolver(NotificationEmailDestinationResult.Resolved("user@example.com")),
            sender,
            new StaticRenderer(new NotificationEmailContent(
                new string('x', EmailSendRequest.SubjectMaxLength + 1),
                "Body")));
        IUserNotificationSink sink = Assert.Single(provider.GetServices<IUserNotificationSink>());

        NotificationSinkDeliveryResult result = await sink.DeliverAsync(
            new NotificationSinkDeliveryRequest(Guid.CreateVersion7(), Message(), attempt: 1, isDurable: true),
            CancellationToken.None);

        Assert.Equal(NotificationSinkDeliveryOutcome.Rejected, result.Outcome);
        Assert.Equal("email-content-invalid", result.Code);
        Assert.Null(sender.Request);
    }

    private static ServiceProvider BuildProvider(
        IUserNotificationEmailAddressResolver resolver,
        IEmailSender sender,
        IUserNotificationEmailRenderer? renderer = null)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{NotificationEmailAdapterOptions.SectionName}:Enabled"] = "true",
                [$"{NotificationEmailAdapterOptions.SectionName}:SenderAddress"] = "no-reply@example.com",
                [$"{NotificationEmailAdapterOptions.SectionName}:SubjectPrefix"] = "[GMA]"
            })
            .Build();
        ServiceCollection services = new();
        services.AddSingleton(resolver);
        services.AddSingleton(sender);
        if (renderer is not null)
        {
            services.AddSingleton<IUserNotificationEmailRenderer>(renderer);
        }

        services.AddNotificationEmailAdapter(configuration);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static UserNotificationMessage Message() =>
        new(
            Guid.CreateVersion7(),
            "auth",
            "account.signed-in",
            1,
            "tenant-a",
            "user-a",
            "Account signed in",
            "A new session was created.",
            NotificationSeverity.Warning,
            DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(new { }),
            [NotificationTags.Email, "domain:security"]);

    private sealed class StaticResolver(NotificationEmailDestinationResult result)
        : IUserNotificationEmailAddressResolver
    {
        public ValueTask<NotificationEmailDestinationResult> ResolveAsync(
            UserNotificationMessage message,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(result);
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public EmailSendRequest? Request { get; private set; }

        public ValueTask<EmailSendResult> SendAsync(
            EmailSendRequest request,
            CancellationToken cancellationToken = default)
        {
            this.Request = request;
            return ValueTask.FromResult(EmailSendResult.Delivered("provider-message-1"));
        }
    }

    private sealed class StaticRenderer(NotificationEmailContent content) : IUserNotificationEmailRenderer
    {
        public ValueTask<NotificationEmailContent> RenderAsync(
            UserNotificationMessage notification,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(content);
    }
}
