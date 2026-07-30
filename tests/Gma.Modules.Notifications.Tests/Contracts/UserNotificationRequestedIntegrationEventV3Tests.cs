namespace Gma.Modules.Notifications.Tests.Contracts;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gma.Modules.Notifications.Contracts;
using Xunit;

[Trait("Category", "Unit")]
public sealed class UserNotificationRequestedIntegrationEventV3Tests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public void Reference_hashes_the_exact_normalized_coordinate()
    {
        const string coordinate = "product-subject/v1|tenant-a|record-42";

        NotificationHistoryReference reference =
            NotificationHistoryReference.FromCanonicalCoordinate(
                "product-subject",
                coordinate);

        Assert.Equal("product-subject", reference.Namespace);
        Assert.Equal(
            Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(coordinate)))
                .ToLowerInvariant(),
            reference.Digest);
        Assert.Throws<ArgumentException>(() =>
            NotificationHistoryReference.FromCanonicalCoordinate(
                "product-subject",
                $" {coordinate}"));
    }

    [Fact]
    public void Event_orders_references_and_uses_the_stable_v3_subject()
    {
        NotificationHistoryReference second =
            NotificationHistoryReference.FromCanonicalCoordinate(
                "z-resource",
                "resource/v1|tenant-a|2");
        NotificationHistoryReference first =
            NotificationHistoryReference.FromCanonicalCoordinate(
                "a-resource",
                "resource/v1|tenant-a|1");

        UserNotificationRequestedIntegrationEventV3 integrationEvent =
            Create([second, first]);

        Assert.Equal(3, integrationEvent.Version);
        Assert.Equal(
            ["a-resource", "z-resource"],
            integrationEvent.References.Select(reference =>
                reference.Namespace));
        Assert.Equal(
            "gma.auth.user-notification-requested.v3",
            NotificationsIntegrationSubjects
                .CreateUserNotificationRequestedV3("auth"));
    }

    [Fact]
    public void Event_rejects_duplicate_reserved_and_excess_references()
    {
        NotificationHistoryReference reference =
            NotificationHistoryReference.FromCanonicalCoordinate(
                "product-subject",
                "product-subject/v1|tenant-a|record-42");
        NotificationHistoryReference reserved =
            NotificationHistoryReference.ForRecipient(
                "tenant-a",
                "user-a");
        NotificationHistoryReference[] excess =
            Enumerable.Range(
                    0,
                    NotificationHistoryReference.MaxCount + 1)
                .Select(index =>
                    NotificationHistoryReference.FromCanonicalCoordinate(
                        $"resource-{index}",
                        $"resource/v1|tenant-a|{index}"))
                .ToArray();

        Assert.Throws<ArgumentException>(() =>
            Create([reference, reference]));
        Assert.Throws<ArgumentException>(() => Create([reserved]));
        Assert.Throws<ArgumentException>(() => Create(excess));
    }

    [Fact]
    public void Event_round_trips_references_through_the_transport_contract()
    {
        UserNotificationRequestedIntegrationEventV3 expected =
            Create(
            [
                NotificationHistoryReference.FromCanonicalCoordinate(
                    "product-subject",
                    "product-subject/v1|tenant-a|record-42")
            ]);

        string json = JsonSerializer.Serialize(expected, JsonOptions);
        UserNotificationRequestedIntegrationEventV3 actual =
            JsonSerializer.Deserialize<
                UserNotificationRequestedIntegrationEventV3>(
                json,
                JsonOptions)!;

        Assert.Equal(expected.EventId, actual.EventId);
        Assert.Equal(expected.ScopeId, actual.ScopeId);
        Assert.Equal(expected.References, actual.References);
        Assert.Equal(expected.Tags, actual.Tags);
        Assert.Equal(expected.DeliveryPolicy, actual.DeliveryPolicy);
    }

    private static UserNotificationRequestedIntegrationEventV3 Create(
        IReadOnlyList<NotificationHistoryReference> references) =>
        new(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            "tenant-a",
            new DateTimeOffset(
                2026,
                7,
                30,
                12,
                0,
                0,
                TimeSpan.Zero),
            "user-a",
            "auth",
            "security.login",
            1,
            "New login",
            "A new session was created.",
            NotificationSeverity.Warning,
            /*lang=json,strict*/
            "{\"sessionId\":\"safe-reference\"}",
            [new NotificationTag(
                "delivery:web",
                NotificationTagKind.Delivery)],
            references);
}
