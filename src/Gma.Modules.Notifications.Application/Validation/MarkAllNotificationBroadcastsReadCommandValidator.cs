namespace Gma.Modules.Notifications.Application.Validation;

using Gma.Modules.Notifications.Application.Commands;
using Gma.Framework.Cqrs;

internal sealed class MarkAllNotificationBroadcastsReadCommandValidator
    : ICommandValidator<MarkAllNotificationBroadcastsReadCommand>
{
    public IEnumerable<string> Validate(MarkAllNotificationBroadcastsReadCommand command)
    {
        foreach (string failure in NotificationBroadcastValidation.ValidateScopeId(command.ScopeId))
        {
            yield return failure;
        }

        foreach (string failure in NotificationBroadcastValidation.ValidateRecipient(
            command.RecipientKind,
            command.RecipientId))
        {
            yield return failure;
        }
    }
}
