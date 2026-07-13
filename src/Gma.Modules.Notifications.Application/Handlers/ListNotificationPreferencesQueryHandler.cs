namespace Gma.Modules.Notifications.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Application.Queries;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.Aggregates;

internal sealed class ListNotificationPreferencesQueryHandler(INotificationRoutingRepository repository)
    : IQueryHandler<ListNotificationPreferencesQuery, NotificationPreferenceListResponse>
{
    public async Task<Result<NotificationPreferenceListResponse>> HandleAsync(
        ListNotificationPreferencesQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<NotificationTagDefinition> definitions = await repository
            .ListTagDefinitionsAsync(activeOnly: false, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<NotificationPreference> preferences = await repository
            .ListPreferencesAsync(query.UserId, cancellationToken)
            .ConfigureAwait(false);
        Dictionary<string, NotificationPreference> byTag = preferences.ToDictionary(
            preference => preference.TagKey.Value,
            StringComparer.Ordinal);

        return Result.Success(new NotificationPreferenceListResponse(
            definitions
                .Select(definition => NotificationRoutingContractMapper.Preference(
                    definition,
                    byTag.GetValueOrDefault(definition.Key.Value)))
                .ToArray()));
    }
}
