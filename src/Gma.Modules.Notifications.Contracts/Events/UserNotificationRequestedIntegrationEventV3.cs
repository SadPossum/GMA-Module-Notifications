namespace Gma.Modules.Notifications.Contracts;

using Gma.Framework.Messaging;
using Gma.Framework.Naming;
using Gma.Framework.Notifications;
using Gma.Framework.Scoping;
using FrameworkNotificationNames = Framework.Notifications.NotificationNames;

[IntegrationEventName(EventType)]
[IntegrationEventVersion(EventVersion)]
[ScopeAware]
public sealed record UserNotificationRequestedIntegrationEventV3
    : IntegrationEvent, IScopedIntegrationEvent
{
    public const string EventType = UserNotificationRequestedIntegrationEvent.EventType;
    public const int EventVersion = 3;

    public UserNotificationRequestedIntegrationEventV3(
        Guid eventId,
        string scopeId,
        DateTimeOffset occurredAtUtc,
        string userId,
        string sourceModule,
        string notificationName,
        int notificationVersion,
        string title,
        string? body,
        NotificationSeverity severity,
        string payloadJson,
        IReadOnlyList<NotificationTag> tags,
        IReadOnlyList<NotificationHistoryReference> references,
        NotificationDeliveryPolicy deliveryPolicy =
            NotificationDeliveryPolicy.RespectPreferences)
        : base(eventId, occurredAtUtc, EventType, EventVersion)
    {
        this.ScopeId = ScopeIds.Normalize(scopeId, nameof(scopeId));
        this.UserId = NotificationRecipientUserIds.Normalize(
            userId,
            nameof(userId));
        this.SourceModule = NormalizeSourceModule(sourceModule);
        this.NotificationName = NormalizeNotificationName(notificationName);
        this.NotificationVersion = notificationVersion > 0
            ? notificationVersion
            : throw new ArgumentOutOfRangeException(
                nameof(notificationVersion),
                notificationVersion,
                "Notification version must be positive.");
        this.Title = NormalizeText(
            title,
            UserNotificationRequestedIntegrationEvent.TitleMaxLength,
            nameof(title));
        this.Body = string.IsNullOrWhiteSpace(body)
            ? null
            : NormalizeText(
                body,
                UserNotificationRequestedIntegrationEvent.BodyMaxLength,
                nameof(body));
        this.Severity = NormalizeSeverity(severity);
        this.PayloadJson = NormalizePayloadJson(payloadJson);
        this.Tags = CopyTags(tags);
        this.References = NotificationHistoryReference.CopyDistinct(
            references,
            nameof(references));
        this.DeliveryPolicy = NormalizePolicy(deliveryPolicy);
    }

    public string ScopeId { get; }
    public string UserId { get; }
    public string SourceModule { get; }
    public string NotificationName { get; }
    public int NotificationVersion { get; }
    public string Title { get; }
    public string? Body { get; }
    public NotificationSeverity Severity { get; }
    public string PayloadJson { get; }
    public IReadOnlyList<NotificationTag> Tags { get; }
    public IReadOnlyList<NotificationHistoryReference> References { get; }
    public NotificationDeliveryPolicy DeliveryPolicy { get; }
    string IScopedIntegrationEvent.ScopeId => this.ScopeId;

    private static System.Collections.ObjectModel.ReadOnlyCollection<
        NotificationTag> CopyTags(IReadOnlyList<NotificationTag> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        NotificationTag[] normalized = tags
            .Select(tag =>
                tag ??
                throw new ArgumentException(
                    "Notification tags cannot contain null values.",
                    nameof(tags)))
            .GroupBy(tag => tag.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                NotificationTag[] definitions = group.Distinct().ToArray();
                if (definitions.Length != 1)
                {
                    throw new ArgumentException(
                        $"Notification tag '{group.Key}' has conflicting definitions.",
                        nameof(tags));
                }

                return definitions[0];
            })
            .OrderBy(tag => tag.Key, StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length == 0)
        {
            normalized =
            [
                new NotificationTag(
                    NotificationTags.Web,
                    NotificationTagKind.Delivery)
            ];
        }

        if (normalized.Length > NotificationTags.MaxCount)
        {
            throw new ArgumentException(
                $"Notifications can contain at most {NotificationTags.MaxCount} distinct tags.",
                nameof(tags));
        }

        if (!normalized.Any(tag => tag.Kind == NotificationTagKind.Delivery))
        {
            throw new ArgumentException(
                "Notifications must contain at least one delivery tag.",
                nameof(tags));
        }

        return Array.AsReadOnly(normalized);
    }

    private static string NormalizeSourceModule(string sourceModule)
    {
        string normalized = SharedNameSegments.NormalizeKebabSegment(
            sourceModule,
            "source module",
            nameof(sourceModule));
        return normalized.Length <=
               UserNotificationRequestedIntegrationEvent.SourceModuleMaxLength
            ? normalized
            : throw new ArgumentException(
                $"sourceModule must be {UserNotificationRequestedIntegrationEvent.SourceModuleMaxLength} characters or fewer.",
                nameof(sourceModule));
    }

    private static string NormalizeNotificationName(string notificationName)
    {
        string normalized = FrameworkNotificationNames.NormalizeName(
            notificationName,
            nameof(notificationName));
        return normalized.Length <=
               UserNotificationRequestedIntegrationEvent.NameMaxLength
            ? normalized
            : throw new ArgumentException(
                $"notificationName must be {UserNotificationRequestedIntegrationEvent.NameMaxLength} characters or fewer.",
                nameof(notificationName));
    }

    private static string NormalizeText(
        string value,
        int maxLength,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalized = value.Trim();
        if (normalized.Length > maxLength ||
            normalized.Any(character =>
                char.IsControl(character) &&
                character is not '\r' and not '\n' and not '\t'))
        {
            throw new ArgumentException(
                $"{parameterName} must be {maxLength} characters or fewer and cannot contain control characters other than tab or line breaks.",
                parameterName);
        }

        return normalized;
    }

    private static NotificationSeverity NormalizeSeverity(
        NotificationSeverity severity) =>
        severity is not NotificationSeverity.Unknown &&
        Enum.IsDefined(severity)
            ? severity
            : throw new ArgumentOutOfRangeException(
                nameof(severity),
                severity,
                "Notification severity must be defined and non-unknown.");

    private static NotificationDeliveryPolicy NormalizePolicy(
        NotificationDeliveryPolicy policy) =>
        policy is NotificationDeliveryPolicy.RespectPreferences or
            NotificationDeliveryPolicy.Mandatory
            ? policy
            : throw new ArgumentOutOfRangeException(
                nameof(policy),
                policy,
                "Notification delivery policy is invalid.");

    private static string NormalizePayloadJson(string payloadJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        if (payloadJson.Length >
            UserNotificationRequestedIntegrationEvent.PayloadJsonMaxLength)
        {
            throw new ArgumentException(
                $"payloadJson must be {UserNotificationRequestedIntegrationEvent.PayloadJsonMaxLength} characters or fewer.",
                nameof(payloadJson));
        }

        try
        {
            using System.Text.Json.JsonDocument document =
                System.Text.Json.JsonDocument.Parse(payloadJson);
            string normalizedJson = System.Text.Json.JsonSerializer.Serialize(
                document.RootElement);
            return normalizedJson.Length <=
                   UserNotificationRequestedIntegrationEvent.PayloadJsonMaxLength
                ? normalizedJson
                : throw new ArgumentException(
                    $"payloadJson must be {UserNotificationRequestedIntegrationEvent.PayloadJsonMaxLength} characters or fewer.",
                    nameof(payloadJson));
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new ArgumentException(
                "payloadJson must be valid JSON.",
                nameof(payloadJson),
                exception);
        }
    }
}
