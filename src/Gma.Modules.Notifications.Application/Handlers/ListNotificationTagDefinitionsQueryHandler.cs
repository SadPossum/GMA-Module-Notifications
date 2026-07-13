namespace Gma.Modules.Notifications.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Application.Queries;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.Aggregates;

internal sealed class ListNotificationTagDefinitionsQueryHandler(INotificationRoutingRepository repository)
    : IQueryHandler<ListNotificationTagDefinitionsQuery, IReadOnlyList<AdminNotificationTagDefinitionItem>>
{
    public async Task<Result<IReadOnlyList<AdminNotificationTagDefinitionItem>>> HandleAsync(
        ListNotificationTagDefinitionsQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<NotificationTagDefinition> definitions = await repository
            .ListTagDefinitionsAsync(query.ActiveOnly, cancellationToken)
            .ConfigureAwait(false);
        return Result.Success<IReadOnlyList<AdminNotificationTagDefinitionItem>>(
            definitions.Select(NotificationRoutingContractMapper.TagDefinition).ToArray());
    }
}
