namespace Gma.Modules.Notifications.Application.Queries;

using Gma.Modules.Notifications.Contracts;
using Gma.Framework.Cqrs;

public sealed record GetNotificationBroadcastQuery(
    Guid BroadcastId,
    string? ScopeId,
    NotificationBroadcastRecipientKind RecipientKind,
    string RecipientId) : IQuery<NotificationBroadcastItem>;
