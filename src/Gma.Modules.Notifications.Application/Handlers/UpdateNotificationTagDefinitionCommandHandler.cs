namespace Gma.Modules.Notifications.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Time;
using Gma.Modules.Notifications.Application.Commands;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.ValueObjects;

internal sealed class UpdateNotificationTagDefinitionCommandHandler(
    INotificationRoutingRepository repository,
    ISystemClock clock)
    : ICommandHandler<UpdateNotificationTagDefinitionCommand, AdminNotificationTagDefinitionItem>
{
    public async Task<Result<AdminNotificationTagDefinitionItem>> HandleAsync(
        UpdateNotificationTagDefinitionCommand command,
        CancellationToken cancellationToken)
    {
        Result<NotificationTagKey> tagKey = NotificationTagKey.Create(command.Key);
        if (tagKey.IsFailure)
        {
            return Result.Failure<AdminNotificationTagDefinitionItem>(tagKey.Error);
        }

        NotificationTagDefinition? definition = await repository
            .GetTagDefinitionAsync(tagKey.Value.Value, cancellationToken)
            .ConfigureAwait(false);
        if (definition is null)
        {
            return Result.Failure<AdminNotificationTagDefinitionItem>(NotificationsApplicationErrors.TagDefinitionNotFound);
        }

        DateTimeOffset nowUtc = clock.UtcNow;
        Result updated = definition.Update(command.DisplayName, command.Description, command.ActorId, nowUtc);
        if (updated.IsFailure)
        {
            return Result.Failure<AdminNotificationTagDefinitionItem>(updated.Error);
        }

        Result activation = definition.SetActive(command.IsActive, command.ActorId, nowUtc);
        return activation.IsFailure
            ? Result.Failure<AdminNotificationTagDefinitionItem>(activation.Error)
            : Result.Success(NotificationRoutingContractMapper.TagDefinition(definition));
    }
}
