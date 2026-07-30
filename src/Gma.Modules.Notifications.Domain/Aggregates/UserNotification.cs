namespace Gma.Modules.Notifications.Domain.Aggregates;

using Gma.Framework.Domain.Models;
using Gma.Framework.Naming;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Domain.Errors;
using Gma.Modules.Notifications.Domain.ValueObjects;

public sealed class UserNotification : ScopedAggregateRoot<Guid>
{
    public const int UserIdMaxLength = 256;
    public const int ModuleMaxLength = 128;
    public const int NameMaxLength = 128;
    public const int TitleMaxLength = 256;
    public const int BodyMaxLength = 4096;
    public const int SeverityMaxLength = NotificationSeverityNames.MaxLength;

    private readonly List<UserNotificationTag> tags = [];
    private readonly List<UserNotificationReference> references = [];

    private UserNotification() { }

    private UserNotification(Guid id, string scopeId)
        : base(id, scopeId)
    {
    }

    public NotificationRecipient Recipient { get; private set; }
    public NotificationSource Source { get; private set; } = null!;
    public NotificationContent Content { get; private set; } = null!;
    public NotificationSeverity Severity { get; private set; }
    public long StreamSequence { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ReadAtUtc { get; private set; }
    public NotificationPayload Payload { get; private set; }
    public NotificationDeliveryPolicy DeliveryPolicy { get; private set; }
    public bool IsInboxVisible { get; private set; }
    public IReadOnlyCollection<UserNotificationTag> Tags => this.tags;
    public IReadOnlyCollection<UserNotificationReference> References =>
        this.references;

    public static Result<UserNotification> Create(
        Guid id,
        string scopeId,
        string userId,
        string module,
        string name,
        int version,
        string title,
        string? body,
        NotificationSeverity severity,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset createdAtUtc,
        string payloadJson) =>
        Create(
            id,
            scopeId,
            userId,
            module,
            name,
            version,
            title,
            body,
            severity,
            occurredAtUtc,
            createdAtUtc,
            payloadJson,
            ["delivery:web"],
            NotificationDeliveryPolicy.RespectPreferences,
            isInboxVisible: true,
            references: []);

    public static Result<UserNotification> Create(
        Guid id,
        string scopeId,
        string userId,
        string module,
        string name,
        int version,
        string title,
        string? body,
        NotificationSeverity severity,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset createdAtUtc,
        string payloadJson,
        IReadOnlyCollection<string> tags,
        NotificationDeliveryPolicy deliveryPolicy,
        bool isInboxVisible) =>
        Create(
            id,
            scopeId,
            userId,
            module,
            name,
            version,
            title,
            body,
            severity,
            occurredAtUtc,
            createdAtUtc,
            payloadJson,
            tags,
            deliveryPolicy,
            isInboxVisible,
            []);

    public static Result<UserNotification> Create(
        Guid id,
        string scopeId,
        string userId,
        string module,
        string name,
        int version,
        string title,
        string? body,
        NotificationSeverity severity,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset createdAtUtc,
        string payloadJson,
        IReadOnlyCollection<string> tags,
        NotificationDeliveryPolicy deliveryPolicy,
        bool isInboxVisible,
        IReadOnlyCollection<NotificationHistoryReferenceKey> references)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<UserNotification>(NotificationsDomainErrors.NotificationIdRequired);
        }

        if (!ScopeIds.TryNormalize(scopeId, out string? normalizedScopeId))
        {
            return Result.Failure<UserNotification>(NotificationsDomainErrors.TenantInvalid);
        }

        Result<NotificationRecipient> recipient = NotificationRecipient.Create(userId);
        if (recipient.IsFailure)
        {
            return Result.Failure<UserNotification>(recipient.Error);
        }

        Result<NotificationSource> source = NotificationSource.Create(module, name, version);
        if (source.IsFailure)
        {
            return Result.Failure<UserNotification>(source.Error);
        }

        Result<NotificationContent> content = NotificationContent.Create(title, body);
        if (content.IsFailure)
        {
            return Result.Failure<UserNotification>(content.Error);
        }

        if (!IsValidSeverity(severity))
        {
            return Result.Failure<UserNotification>(NotificationsDomainErrors.SeverityInvalid);
        }

        Result<NotificationPayload> payload = NotificationPayload.Create(payloadJson);
        if (payload.IsFailure)
        {
            return Result.Failure<UserNotification>(payload.Error);
        }

        if (deliveryPolicy is not NotificationDeliveryPolicy.RespectPreferences and not NotificationDeliveryPolicy.Mandatory ||
            tags is null ||
            references is null)
        {
            return Result.Failure<UserNotification>(NotificationsDomainErrors.DeliveryStatusInvalid);
        }

        string[] normalizedTags = tags
            .Select(NotificationTagKey.Create)
            .Where(result => result.IsSuccess)
            .Select(result => result.Value.Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (normalizedTags.Length != tags.Count || normalizedTags.Length == 0 ||
            !normalizedTags.Any(tag => tag.StartsWith("delivery:", StringComparison.Ordinal)))
        {
            return Result.Failure<UserNotification>(NotificationsDomainErrors.TagKeyInvalid);
        }

        if (references.Count >
            NotificationHistoryReferenceKey.MaxProducerCount)
        {
            return Result.Failure<UserNotification>(
                NotificationsDomainErrors.HistoryReferenceCountInvalid);
        }

        if (references.Any(reference =>
                reference is null ||
                string.Equals(
                    reference.Namespace,
                    NotificationHistoryReferenceKey.RecipientNamespace,
                    StringComparison.Ordinal)) ||
            references.Distinct().Count() != references.Count)
        {
            return Result.Failure<UserNotification>(
                NotificationsDomainErrors.HistoryReferenceInvalid);
        }

        UserNotification notification = new(id, normalizedScopeId!)
        {
            Recipient = recipient.Value,
            Source = source.Value,
            Content = content.Value,
            Severity = severity,
            OccurredAtUtc = occurredAtUtc,
            CreatedAtUtc = createdAtUtc,
            Payload = payload.Value,
            DeliveryPolicy = deliveryPolicy,
            IsInboxVisible = isInboxVisible
        };

        foreach (string tag in normalizedTags)
        {
            Result<UserNotificationTag> assignment = UserNotificationTag.Create(id, normalizedScopeId!, tag);
            if (assignment.IsFailure)
            {
                return Result.Failure<UserNotification>(assignment.Error);
            }

            notification.tags.Add(assignment.Value);
        }

        Result<NotificationHistoryReferenceKey> recipientReference =
            NotificationHistoryReferenceKey.ForRecipient(
                normalizedScopeId!,
                recipient.Value.UserId);
        if (recipientReference.IsFailure)
        {
            return Result.Failure<UserNotification>(
                recipientReference.Error);
        }

        NotificationHistoryReferenceKey[] normalizedReferences = references
            .Append(recipientReference.Value)
            .OrderBy(reference => reference.Namespace, StringComparer.Ordinal)
            .ThenBy(reference => reference.Digest, StringComparer.Ordinal)
            .ToArray();
        if (normalizedReferences.Length >
            NotificationHistoryReferenceKey.MaxStoredCount)
        {
            return Result.Failure<UserNotification>(
                NotificationsDomainErrors.HistoryReferenceCountInvalid);
        }

        foreach (NotificationHistoryReferenceKey reference in
                 normalizedReferences)
        {
            Result<UserNotificationReference> assignment =
                UserNotificationReference.Create(
                    id,
                    normalizedScopeId!,
                    reference);
            if (assignment.IsFailure)
            {
                return Result.Failure<UserNotification>(assignment.Error);
            }

            notification.references.Add(assignment.Value);
        }

        return Result.Success(notification);
    }

    public bool MarkRead(DateTimeOffset readAtUtc)
    {
        if (this.ReadAtUtc is not null)
        {
            return false;
        }

        this.ReadAtUtc = readAtUtc;
        return true;
    }

    public void SetInboxVisibility(bool isInboxVisible)
    {
        this.IsInboxVisible = isInboxVisible;
    }

    private static bool IsValidSeverity(NotificationSeverity severity) =>
        severity is
            NotificationSeverity.Info or
            NotificationSeverity.Success or
            NotificationSeverity.Warning or
            NotificationSeverity.Error;
}
