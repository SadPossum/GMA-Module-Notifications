namespace Gma.Modules.Notifications.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Modules.Notifications.Application.Commands;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.ValueObjects;
using DomainTagOrigin = Domain.ValueObjects.NotificationTagOrigin;

internal sealed class CreateNotificationTagDefinitionCommandHandler(
    INotificationRoutingRepository repository,
    IIdGenerator idGenerator,
    ISystemClock clock)
    : ICommandHandler<CreateNotificationTagDefinitionCommand, AdminNotificationTagDefinitionItem>
{
    public async Task<Result<AdminNotificationTagDefinitionItem>> HandleAsync(
        CreateNotificationTagDefinitionCommand command,
        CancellationToken cancellationToken)
    {
        Result<NotificationTagKey> tagKey = NotificationTagKey.Create(command.Key);
        if (tagKey.IsFailure)
        {
            return Result.Failure<AdminNotificationTagDefinitionItem>(tagKey.Error);
        }

        if (await repository.GetTagDefinitionAsync(tagKey.Value.Value, cancellationToken).ConfigureAwait(false) is not null)
        {
            return Result.Failure<AdminNotificationTagDefinitionItem>(NotificationsApplicationErrors.TagDefinitionAlreadyExists);
        }

        Result<NotificationTagDefinition> definition = NotificationTagDefinition.Create(
            idGenerator.NewId(),
            command.ScopeId,
            tagKey.Value.Value,
            NotificationRoutingContractMapper.TagKind(command.Kind),
            command.DisplayName,
            command.Description,
            DomainTagOrigin.Operator,
            "notifications",
            command.ActorId,
            clock.UtcNow);
        if (definition.IsFailure)
        {
            return Result.Failure<AdminNotificationTagDefinitionItem>(definition.Error);
        }

        await repository.AddTagDefinitionAsync(definition.Value, cancellationToken).ConfigureAwait(false);
        return Result.Success(NotificationRoutingContractMapper.TagDefinition(definition.Value));
    }
}
