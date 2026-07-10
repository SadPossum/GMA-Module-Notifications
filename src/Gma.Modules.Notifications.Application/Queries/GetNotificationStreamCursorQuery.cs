namespace Gma.Modules.Notifications.Application.Queries;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;

public sealed record GetNotificationStreamCursorQuery(AccessSubject Subject, string? ScopeId)
    : IQuery<long>;
