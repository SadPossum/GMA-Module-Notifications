namespace Gma.Modules.Notifications.Persistence;

using System.Data;
using Gma.Framework.Notifications;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ContractDeliveryPolicy = Contracts.NotificationDeliveryPolicy;
using ContractNotificationSeverity = Contracts.NotificationSeverity;
using ContractTagKind = Contracts.NotificationTagKind;
using FrameworkDeliveryPolicy = Framework.Notifications.NotificationDeliveryPolicy;
using FrameworkNotificationSeverity = Framework.Notifications.NotificationSeverity;

internal sealed class NotificationHistoryWriter(
    IUserNotificationRequestProjector projector,
    NotificationsDbContext dbContext,
    ILogger<NotificationHistoryWriter> logger)
    : IUserNotificationHistoryWriter
{
    public async ValueTask SaveAsync(UserNotificationMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            await using IDbContextTransaction? transaction =
                await this.BeginTransactionAsync(cancellationToken)
                    .ConfigureAwait(false);
            await projector.ProjectAsync(
                    new UserNotificationRequestedIntegrationEventV2(
                        message.Id,
                        message.ScopeId,
                        message.OccurredAtUtc,
                        message.UserId,
                        message.Module,
                        message.Name,
                        message.Version,
                        message.Title,
                        message.Body,
                        ToContractSeverity(message.Severity),
                        message.Payload.GetRawText(),
                        message.Tags.Select(ToContractTag).ToArray(),
                        ToContractPolicy(message.DeliveryPolicy)),
                    cancellationToken)
                .ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(
                "A user notification could not be converted to a durable request because {ExceptionType} was raised.",
                exception.GetType().Name);
        }
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational() ||
            dbContext.Database.CurrentTransaction is not null)
        {
            return null;
        }

        return await dbContext.Database
            .BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static ContractNotificationSeverity ToContractSeverity(FrameworkNotificationSeverity severity) =>
        severity switch
        {
            FrameworkNotificationSeverity.Info => ContractNotificationSeverity.Info,
            FrameworkNotificationSeverity.Success => ContractNotificationSeverity.Success,
            FrameworkNotificationSeverity.Warning => ContractNotificationSeverity.Warning,
            FrameworkNotificationSeverity.Error => ContractNotificationSeverity.Error,
            _ => ContractNotificationSeverity.Unknown
        };

    private static NotificationTag ToContractTag(string tag) =>
        new(
            tag,
            tag.StartsWith(NotificationTags.DeliveryNamespace + ":", StringComparison.Ordinal)
                ? ContractTagKind.Delivery
                : ContractTagKind.Domain);

    private static ContractDeliveryPolicy ToContractPolicy(FrameworkDeliveryPolicy policy) => policy switch
    {
        FrameworkDeliveryPolicy.RespectPreferences => ContractDeliveryPolicy.RespectPreferences,
        FrameworkDeliveryPolicy.Mandatory => ContractDeliveryPolicy.Mandatory,
        _ => ContractDeliveryPolicy.Unknown
    };
}
