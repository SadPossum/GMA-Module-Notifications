namespace Gma.Modules.Notifications.Application.Commands;

using Gma.Framework.Cqrs;
using Gma.Modules.Notifications.Contracts;

public sealed record SetNotificationPreferenceCommand(
    string ScopeId,
    string UserId,
    string TagKey,
    bool Enabled)
    : ITransactionalCommand<NotificationPreferenceItem>;

public sealed record CreateNotificationTagDefinitionCommand(
    string ScopeId,
    string Key,
    NotificationTagKind Kind,
    string DisplayName,
    string Description,
    string ActorId)
    : ITransactionalCommand<AdminNotificationTagDefinitionItem>;

public sealed record UpdateNotificationTagDefinitionCommand(
    string Key,
    string DisplayName,
    string Description,
    bool IsActive,
    string ActorId)
    : ITransactionalCommand<AdminNotificationTagDefinitionItem>;

public sealed record SetNotificationDeliveryRouteCommand(
    string ScopeId,
    string DeliveryTag,
    NotificationDeliveryProviderCode Provider,
    bool IsActive,
    string ActorId)
    : ITransactionalCommand<AdminNotificationDeliveryRouteItem>;

public sealed record RetryNotificationDeliveryCommand(Guid DeliveryId)
    : ITransactionalCommand<Unit>;
