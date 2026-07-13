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

internal sealed class SetNotificationPreferenceCommandHandler(
    INotificationRoutingRepository repository,
    IIdGenerator idGenerator,
    ISystemClock clock)
    : ICommandHandler<SetNotificationPreferenceCommand, NotificationPreferenceItem>
{
    public async Task<Result<NotificationPreferenceItem>> HandleAsync(
        SetNotificationPreferenceCommand command,
        CancellationToken cancellationToken)
    {
        Result<NotificationTagKey> tagKey = NotificationTagKey.Create(command.TagKey);
        if (tagKey.IsFailure)
        {
            return Result.Failure<NotificationPreferenceItem>(tagKey.Error);
        }

        NotificationTagDefinition? definition = await repository
            .GetTagDefinitionAsync(tagKey.Value.Value, cancellationToken)
            .ConfigureAwait(false);
        if (definition is null)
        {
            return Result.Failure<NotificationPreferenceItem>(NotificationsApplicationErrors.TagDefinitionNotFound);
        }

        if (!definition.IsActive)
        {
            return Result.Failure<NotificationPreferenceItem>(NotificationsApplicationErrors.TagDefinitionInactive);
        }

        NotificationPreference? preference = await repository
            .GetPreferenceAsync(command.UserId, tagKey.Value.Value, cancellationToken)
            .ConfigureAwait(false);
        if (preference is null)
        {
            Result<NotificationPreference> created = NotificationPreference.Create(
                idGenerator.NewId(),
                command.ScopeId,
                command.UserId,
                tagKey.Value.Value,
                command.Enabled,
                clock.UtcNow);
            if (created.IsFailure)
            {
                return Result.Failure<NotificationPreferenceItem>(created.Error);
            }

            preference = created.Value;
            await repository.AddPreferenceAsync(preference, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            preference.SetEnabled(command.Enabled, clock.UtcNow);
        }

        return Result.Success(NotificationRoutingContractMapper.Preference(definition, preference));
    }
}
