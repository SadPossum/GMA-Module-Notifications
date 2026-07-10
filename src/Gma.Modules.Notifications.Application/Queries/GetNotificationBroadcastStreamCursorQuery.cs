namespace Gma.Modules.Notifications.Application.Queries;

using Gma.Modules.Notifications.Contracts;
using Gma.Framework.Cqrs;

public sealed record GetNotificationBroadcastStreamCursorQuery(
    string? ScopeId,
    NotificationBroadcastRecipientKind RecipientKind,
    string RecipientId) : IQuery<long>;
