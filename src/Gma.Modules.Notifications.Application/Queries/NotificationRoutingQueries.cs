namespace Gma.Modules.Notifications.Application.Queries;

using Gma.Framework.Cqrs;
using Gma.Modules.Notifications.Contracts;

public sealed record ListNotificationPreferencesQuery(string UserId)
    : IQuery<NotificationPreferenceListResponse>;

public sealed record ListNotificationTagDefinitionsQuery(bool ActiveOnly = false)
    : IQuery<IReadOnlyList<AdminNotificationTagDefinitionItem>>;

public sealed record ListNotificationDeliveryRoutesQuery
    : IQuery<IReadOnlyList<AdminNotificationDeliveryRouteItem>>;

public sealed record GetNotificationDeliveryQuery(Guid DeliveryId)
    : IQuery<AdminNotificationDeliveryItem>;

public sealed record ListNotificationDeliveriesQuery(
    NotificationDeliveryStatus? Status = null,
    string? UserId = null,
    string? DeliveryTag = null,
    int Page = Framework.Pagination.PageRequest.DefaultPage,
    int PageSize = Framework.Pagination.PageRequest.DefaultPageSize)
    : IQuery<AdminNotificationDeliveryListResponse>;
