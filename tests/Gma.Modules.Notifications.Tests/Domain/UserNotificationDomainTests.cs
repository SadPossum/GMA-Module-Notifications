namespace Gma.Modules.Notifications.Tests;

using System.Text.Json;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Errors;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Gma.Framework.Results;
using Xunit;

[Trait("Category", "Unit")]
public sealed class UserNotificationDomainTests
{
    [Fact]
    public void Create_normalizes_notification_identity_and_payload()
    {
        Result<UserNotification> result = UserNotification.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            " tenant-a ",
            " user-a ",
            " Catalog ",
            "Catalog.Item-Updated",
            1,
            " Item updated ",
            "  The item changed.  ",
            NotificationSeverity.Success,
            new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 4, 12, 1, 0, TimeSpan.Zero),
            "{ \"sku\": \"SKU-1\" }");

        Assert.True(result.IsSuccess);
        Assert.Equal("tenant-a", result.Value.ScopeId);
        Assert.Equal("user-a", result.Value.Recipient.UserId);
        Assert.Equal("catalog", result.Value.Source.Module);
        Assert.Equal("catalog.item-updated", result.Value.Source.Name);
        Assert.Equal("Item updated", result.Value.Content.Title);
        Assert.Equal("The item changed.", result.Value.Content.Body);
        Assert.Equal(NotificationSeverity.Success, result.Value.Severity);
        using JsonDocument document = JsonDocument.Parse(result.Value.Payload.Json);
        Assert.Equal("SKU-1", document.RootElement.GetProperty("sku").GetString());
    }

    [Fact]
    public void Create_rejects_invalid_payload_json()
    {
        Result<UserNotification> result = UserNotification.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "tenant-a",
            "user-a",
            "catalog",
            "catalog.item-updated",
            1,
            "Item updated",
            null,
            NotificationSeverity.Info,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "{");

        Assert.True(result.IsFailure);
        Assert.Equal(NotificationsDomainErrors.PayloadInvalid, result.Error);
    }

    [Fact]
    public void Mark_read_is_idempotent()
    {
        UserNotification notification = CreateNotification();
        DateTimeOffset readAt = new(2026, 7, 4, 12, 2, 0, TimeSpan.Zero);

        Assert.True(notification.MarkRead(readAt));
        Assert.False(notification.MarkRead(readAt.AddMinutes(1)));
        Assert.Equal(readAt, notification.ReadAtUtc);
    }

    [Fact]
    public void Create_adds_recipient_and_validated_producer_references()
    {
        NotificationHistoryReferenceKey producer =
            NotificationHistoryReferenceKey.Create(
                "product-subject",
                new string('a', 64)).Value;

        Result<UserNotification> result = UserNotification.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "tenant-a",
            "user-a",
            "catalog",
            "catalog.item-updated",
            1,
            "Item updated",
            null,
            NotificationSeverity.Info,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "{}",
            ["delivery:web"],
            NotificationDeliveryPolicy.RespectPreferences,
            isInboxVisible: true,
            [producer]);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.References.Count);
        Assert.Contains(
            result.Value.References,
            reference =>
                reference.Namespace == "product-subject" &&
                reference.Digest == new string('a', 64));
        Assert.Contains(
            result.Value.References,
            reference =>
                reference.Namespace ==
                NotificationHistoryReferenceKey.RecipientNamespace);
    }

    [Fact]
    public void Create_rejects_a_producer_owned_recipient_reference()
    {
        NotificationHistoryReferenceKey reserved =
            NotificationHistoryReferenceKey.ForRecipient(
                "tenant-b",
                "user-b").Value;

        Result<UserNotification> result = UserNotification.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "tenant-a",
            "user-a",
            "catalog",
            "catalog.item-updated",
            1,
            "Item updated",
            null,
            NotificationSeverity.Info,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "{}",
            ["delivery:web"],
            NotificationDeliveryPolicy.RespectPreferences,
            isInboxVisible: true,
            [reserved]);

        Assert.True(result.IsFailure);
        Assert.Equal(
            NotificationsDomainErrors.HistoryReferenceInvalid,
            result.Error);
    }

    [Fact]
    public void Recipient_reference_normalizes_and_rejects_invalid_coordinates()
    {
        Result<NotificationHistoryReferenceKey> normalized =
            NotificationHistoryReferenceKey.ForRecipient(
                " tenant-a ",
                " user-a ");
        Result<NotificationHistoryReferenceKey> canonical =
            NotificationHistoryReferenceKey.ForRecipient(
                "tenant-a",
                "user-a");
        Result<NotificationHistoryReferenceKey> invalidScope =
            NotificationHistoryReferenceKey.ForRecipient(
                " ",
                "user-a");
        Result<NotificationHistoryReferenceKey> invalidRecipient =
            NotificationHistoryReferenceKey.ForRecipient(
                "tenant-a",
                " ");

        Assert.True(normalized.IsSuccess);
        Assert.Equal(canonical.Value, normalized.Value);
        Assert.True(invalidScope.IsFailure);
        Assert.True(invalidRecipient.IsFailure);
    }

    private static UserNotification CreateNotification() =>
        UserNotification.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "tenant-a",
            "user-a",
            "catalog",
            "catalog.item-updated",
            1,
            "Item updated",
            null,
            NotificationSeverity.Info,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "{}").Value;
}
