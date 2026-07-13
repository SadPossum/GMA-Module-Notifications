namespace Gma.Modules.Notifications.Application.Handlers;

using Gma.Framework.Cqrs;
using Gma.Framework.Pagination;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Application.Queries;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.ValueObjects;
using ContractDeliveryStatus = Contracts.NotificationDeliveryStatus;
using DomainDeliveryStatus = Domain.ValueObjects.NotificationDeliveryStatus;

internal sealed class ListNotificationDeliveriesQueryHandler(INotificationRoutingRepository repository)
    : IQueryHandler<ListNotificationDeliveriesQuery, AdminNotificationDeliveryListResponse>
{
    public async Task<Result<AdminNotificationDeliveryListResponse>> HandleAsync(
        ListNotificationDeliveriesQuery query,
        CancellationToken cancellationToken)
    {
        DomainDeliveryStatus? status = query.Status is null
            ? null
            : ToDomainStatus(query.Status.Value);
        if (status == DomainDeliveryStatus.Unknown)
        {
            return Result.Failure<AdminNotificationDeliveryListResponse>(NotificationsApplicationErrors.DeliveryStatusInvalid);
        }

        if (!string.IsNullOrWhiteSpace(query.DeliveryTag) && NotificationTagKey.Create(query.DeliveryTag).IsFailure)
        {
            return Result.Failure<AdminNotificationDeliveryListResponse>(NotificationsApplicationErrors.DeliveryStatusInvalid);
        }

        if (!string.IsNullOrWhiteSpace(query.UserId))
        {
            Result<NotificationRecipient> recipient = NotificationRecipient.Create(query.UserId);
            if (recipient.IsFailure)
            {
                return Result.Failure<AdminNotificationDeliveryListResponse>(recipient.Error);
            }
        }

        AdminNotificationDeliveryListResponse response = await repository.ListDeliveriesAsync(
                status,
                query.UserId,
                query.DeliveryTag,
                PageRequest.Normalize(query.Page, query.PageSize),
                cancellationToken)
            .ConfigureAwait(false);
        return Result.Success(response);
    }

    private static DomainDeliveryStatus ToDomainStatus(ContractDeliveryStatus status) => status switch
    {
        ContractDeliveryStatus.Pending => DomainDeliveryStatus.Pending,
        ContractDeliveryStatus.Processing => DomainDeliveryStatus.Processing,
        ContractDeliveryStatus.RetryScheduled => DomainDeliveryStatus.RetryScheduled,
        ContractDeliveryStatus.Delivered => DomainDeliveryStatus.Delivered,
        ContractDeliveryStatus.Rejected => DomainDeliveryStatus.Rejected,
        ContractDeliveryStatus.Exhausted => DomainDeliveryStatus.Exhausted,
        ContractDeliveryStatus.Suppressed => DomainDeliveryStatus.Suppressed,
        ContractDeliveryStatus.Unroutable => DomainDeliveryStatus.Unroutable,
        _ => DomainDeliveryStatus.Unknown
    };
}
