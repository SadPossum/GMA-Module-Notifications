namespace Gma.Modules.Notifications.Tests.Contracts;

using System.Text.Json;
using Gma.Modules.Notifications.Contracts;
using Xunit;

[Trait("Category", "Unit")]
public sealed class UserNotificationRequestedIntegrationEventV2Tests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Event_normalizes_tags_and_uses_stable_v2_subject()
    {
        UserNotificationRequestedIntegrationEventV2 integrationEvent = Create(
            [
                new NotificationTag("Domain:Security", NotificationTagKind.Domain, "Security", "Security-related account activity."),
                new NotificationTag("delivery:Email", NotificationTagKind.Delivery, "Email")
            ],
            NotificationDeliveryPolicy.Mandatory);

        Assert.Equal(2, integrationEvent.Version);
        Assert.Equal(["delivery:email", "domain:security"], integrationEvent.Tags.Select(tag => tag.Key));
        Assert.Equal(NotificationDeliveryPolicy.Mandatory, integrationEvent.DeliveryPolicy);
        Assert.Equal(
            "gma.auth.user-notification-requested.v2",
            NotificationsIntegrationSubjects.CreateUserNotificationRequestedV2("auth"));
    }

    [Fact]
    public void Event_defaults_an_empty_tag_set_to_web_delivery()
    {
        UserNotificationRequestedIntegrationEventV2 integrationEvent = Create([]);

        NotificationTag tag = Assert.Single(integrationEvent.Tags);
        Assert.Equal("delivery:web", tag.Key);
        Assert.Equal(NotificationTagKind.Delivery, tag.Kind);
    }

    [Fact]
    public void Event_requires_a_delivery_tag_and_consistent_definitions()
    {
        Assert.Throws<ArgumentException>(() => Create(
            [new NotificationTag("domain:security", NotificationTagKind.Domain)]));
        Assert.Throws<ArgumentException>(() => Create(
            [
                new NotificationTag("delivery:email", NotificationTagKind.Delivery, "Email"),
                new NotificationTag("delivery:email", NotificationTagKind.Delivery, "Mail")
            ]));
        Assert.Throws<ArgumentException>(() =>
            new NotificationTag("domain:security", NotificationTagKind.Delivery));
    }

    [Fact]
    public void Event_serializes_contract_enums_as_stable_strings()
    {
        string json = JsonSerializer.Serialize(
            Create(
                [new NotificationTag("delivery:email", NotificationTagKind.Delivery)],
                NotificationDeliveryPolicy.RespectPreferences),
            JsonOptions);

        Assert.Contains("\"kind\":\"delivery\"", json, StringComparison.Ordinal);
        Assert.Contains("\"deliveryPolicy\":\"respect-preferences\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"deliveryPolicy\":1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Event_json_round_trips_through_the_durable_transport_contract()
    {
        UserNotificationRequestedIntegrationEventV2 expected = Create(
            [
                new NotificationTag("delivery:email", NotificationTagKind.Delivery),
                new NotificationTag("domain:security", NotificationTagKind.Domain)
            ],
            NotificationDeliveryPolicy.Mandatory);

        string json = JsonSerializer.Serialize(expected, JsonOptions);
        UserNotificationRequestedIntegrationEventV2 actual =
            JsonSerializer.Deserialize<UserNotificationRequestedIntegrationEventV2>(json, JsonOptions)!;

        Assert.Equal(expected.EventId, actual.EventId);
        Assert.Equal(expected.ScopeId, actual.ScopeId);
        Assert.Equal(expected.OccurredAtUtc, actual.OccurredAtUtc);
        Assert.Equal(expected.UserId, actual.UserId);
        Assert.Equal(expected.SourceModule, actual.SourceModule);
        Assert.Equal(expected.NotificationName, actual.NotificationName);
        Assert.Equal(expected.NotificationVersion, actual.NotificationVersion);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Body, actual.Body);
        Assert.Equal(expected.Severity, actual.Severity);
        Assert.Equal(expected.PayloadJson, actual.PayloadJson);
        Assert.Equal(expected.Tags, actual.Tags);
        Assert.Equal(expected.DeliveryPolicy, actual.DeliveryPolicy);
    }

    private static UserNotificationRequestedIntegrationEventV2 Create(
        IReadOnlyList<NotificationTag> tags,
        NotificationDeliveryPolicy policy = NotificationDeliveryPolicy.RespectPreferences) =>
        new(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            "tenant-a",
            new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero),
            "user-a",
            "auth",
            "security.login",
            1,
            "New login",
            "A new session was created.",
            NotificationSeverity.Warning,
                                 /*lang=json,strict*/
                                 "{\"sessionId\":\"safe-reference\"}",
            tags,
            policy);
}
