namespace Gma.Modules.Notifications.Application.Commands;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;

public sealed record MarkNotificationReadCommand(Guid NotificationId, AccessSubject Subject, string? ScopeId)
    : ITransactionalCommand<Unit>;
