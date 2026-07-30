namespace Gma.Modules.Notifications.Persistence.Repositories;

using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

internal sealed class NotificationHistoryLifecycleRepository(
    NotificationsDbContext dbContext)
    : INotificationHistoryLifecycleRepository
{
    public async Task<bool> RegisterAsync(
        UserNotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        NotificationHistoryReferenceKey[] references = notification.References
            .Select(assignment => assignment.ToKey())
            .OrderBy(key => key.Namespace, StringComparer.Ordinal)
            .ThenBy(key => key.Digest, StringComparer.Ordinal)
            .ToArray();
        List<(
            NotificationHistoryReferenceKey Reference,
            NotificationHistoryReferenceState? State)> resolved = [];
        foreach (NotificationHistoryReferenceKey reference in references)
        {
            NotificationHistoryReferenceState? state = dbContext
                .NotificationHistoryReferenceStates
                .Local
                .SingleOrDefault(candidate =>
                    candidate.ScopeId == notification.ScopeId &&
                    candidate.Namespace == reference.Namespace &&
                    candidate.Digest == reference.Digest);
            state ??= await dbContext.NotificationHistoryReferenceStates
                .SingleOrDefaultAsync(
                    candidate =>
                        candidate.ScopeId == notification.ScopeId &&
                        candidate.Namespace == reference.Namespace &&
                        candidate.Digest == reference.Digest,
                    cancellationToken)
                .ConfigureAwait(false);
            resolved.Add((reference, state));
        }

        if (resolved.Any(item => item.State?.IsClosed == true))
        {
            return false;
        }

        foreach ((
                     NotificationHistoryReferenceKey reference,
                     NotificationHistoryReferenceState? existing) in resolved)
        {
            NotificationHistoryReferenceState state = existing ??
                NotificationHistoryReferenceState
                    .Create(notification.ScopeId, reference).Value;
            if (existing is null)
            {
                await dbContext.NotificationHistoryReferenceStates
                    .AddAsync(state, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!state.RegisterNotification())
            {
                return false;
            }
        }

        return true;
    }
}
