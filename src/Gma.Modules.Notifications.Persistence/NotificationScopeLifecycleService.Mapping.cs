namespace Gma.Modules.Notifications.Persistence;

using System.Text.Json;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.Entities;
using ContractAttemptOutcome = Contracts.NotificationDeliveryAttemptOutcome;
using ContractAudience = Contracts.NotificationBroadcastAudience;
using ContractDeliveryPolicy = Contracts.NotificationDeliveryPolicy;
using ContractDeliveryStatus = Contracts.NotificationDeliveryStatus;
using ContractRecipientKind = Contracts.NotificationBroadcastRecipientKind;
using ContractSeverity = Contracts.NotificationSeverity;
using ContractTagKind = Contracts.NotificationTagKind;
using ContractTagOrigin = Contracts.NotificationTagOrigin;
using DomainAttemptOutcome = Domain.ValueObjects.NotificationDeliveryAttemptOutcome;
using DomainAudience = Domain.ValueObjects.NotificationBroadcastAudience;
using DomainDeliveryPolicy = Domain.ValueObjects.NotificationDeliveryPolicy;
using DomainDeliveryStatus = Domain.ValueObjects.NotificationDeliveryStatus;
using DomainRecipientKind = Domain.ValueObjects.NotificationBroadcastRecipientKind;
using DomainSeverity = Domain.ValueObjects.NotificationSeverity;
using DomainTagKind = Domain.ValueObjects.NotificationTagKind;
using DomainTagOrigin = Domain.ValueObjects.NotificationTagOrigin;

internal sealed partial class NotificationScopeLifecycleService
{
    private static NotificationScopeExportRecord Map(
        UserNotification notification) =>
        new NotificationScopeUserNotificationExportRecord(
            notification.Id,
            notification.Recipient.UserId,
            notification.Source.Module,
            notification.Source.Name,
            notification.Source.Version,
            notification.Content.Title,
            notification.Content.Body,
            ToContract(notification.Severity),
            notification.StreamSequence,
            notification.OccurredAtUtc,
            notification.CreatedAtUtc,
            notification.ReadAtUtc,
            ParsePayload(notification.Payload.Json),
            ToContract(notification.DeliveryPolicy),
            notification.IsInboxVisible,
            notification.Tags
                .Select(tag => tag.Key.Value)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            notification.References
                .Select(reference => new NotificationHistoryReference(
                    reference.Namespace,
                    reference.Digest))
                .OrderBy(reference => reference.Namespace, StringComparer.Ordinal)
                .ThenBy(reference => reference.Digest, StringComparer.Ordinal)
                .ToArray());

    private static NotificationScopeExportRecord Map(
        NotificationPreference preference) =>
        new NotificationScopePreferenceExportRecord(
            preference.Id,
            preference.UserId,
            preference.TagKey.Value,
            preference.Enabled,
            preference.Version,
            preference.UpdatedAtUtc);

    private static NotificationScopeExportRecord Map(
        NotificationDeliveryRoute route) =>
        new NotificationScopeDeliveryRouteExportRecord(
            route.Id,
            route.DeliveryTag.Value,
            new NotificationDeliveryProviderCode(route.Provider.Value),
            route.IsActive,
            route.Version,
            route.UpdatedAtUtc,
            route.UpdatedBy);

    private static NotificationScopeExportRecord Map(
        NotificationTagDefinition definition) =>
        new NotificationScopeTagDefinitionExportRecord(
            definition.Id,
            definition.Key.Value,
            ToContract(definition.Kind),
            definition.DisplayName,
            definition.Description,
            ToContract(definition.Origin),
            definition.Owner,
            definition.IsActive,
            definition.Version,
            definition.CreatedAtUtc,
            definition.UpdatedAtUtc,
            definition.CreatedBy,
            definition.UpdatedBy);

    private static NotificationScopeExportRecord Map(
        NotificationDelivery delivery) =>
        new NotificationScopeDeliveryExportRecord(
            delivery.Id,
            delivery.NotificationId,
            delivery.DeliveryTag.Value,
            new NotificationDeliveryProviderCode(delivery.Provider.Value),
            ToContract(delivery.Status),
            delivery.Attempts,
            delivery.MaxAttempts,
            delivery.CreatedAtUtc,
            delivery.NextAttemptAtUtc,
            delivery.LockedBy,
            delivery.LockedUntilUtc,
            delivery.DeliveredAtUtc,
            delivery.CompletedAtUtc,
            delivery.LastCode,
            delivery.ProviderMessageId,
            delivery.ConcurrencyStamp);

    private static NotificationScopeExportRecord Map(
        NotificationDeliveryAttempt attempt) =>
        new NotificationScopeDeliveryAttemptExportRecord(
            attempt.Id,
            attempt.DeliveryId,
            attempt.AttemptNumber,
            new NotificationDeliveryProviderCode(attempt.Provider.Value),
            ToContract(attempt.Outcome),
            attempt.StartedAtUtc,
            attempt.CompletedAtUtc,
            attempt.Code,
            attempt.ProviderMessageId);

    private static NotificationScopeExportRecord Map(
        NotificationBroadcast broadcast) =>
        new NotificationScopeBroadcastExportRecord(
            broadcast.Id,
            ToContract(broadcast.Audience),
            broadcast.Source.Module,
            broadcast.Source.Name,
            broadcast.Source.Version,
            broadcast.Content.Title,
            broadcast.Content.Body,
            ToContract(broadcast.Severity),
            broadcast.StreamSequence,
            broadcast.OccurredAtUtc,
            broadcast.CreatedAtUtc,
            ParsePayload(broadcast.Payload.Json));

    private static NotificationScopeExportRecord Map(
        NotificationBroadcastRead read) =>
        new NotificationScopeBroadcastReadExportRecord(
            read.Id,
            read.BroadcastId,
            ToContract(read.RecipientKind),
            read.Recipient.UserId,
            read.ReadAtUtc);

    private static NotificationScopeExportRecord Map(
        NotificationHistoryReferenceState state) =>
        new NotificationScopeHistoryReferenceStateExportRecord(
            new NotificationHistoryReference(state.Namespace, state.Digest),
            state.Version,
            state.IsClosed,
            state.ClosedAtUtc,
            state.CloseOperationId,
            state.CloseRequestSha256);

    private static NotificationScopeExportRecord Map(
        NotificationHistoryCloseReceipt receipt) =>
        new NotificationScopeHistoryCloseReceiptExportRecord(
            receipt.OperationId,
            new NotificationHistoryReference(
                receipt.Namespace,
                receipt.Digest),
            receipt.RequestSha256,
            receipt.ResultingVersion,
            receipt.RemovedRecordCount,
            receipt.RemovedRecordIdsSha256,
            receipt.CompletedAtUtc);

    private static NotificationScopeExportRecord Map(
        NotificationHistoryBatchCloseOperation operation) =>
        new NotificationScopeHistoryBatchCloseOperationExportRecord(
            operation.OperationId,
            new NotificationHistoryReference(
                operation.Namespace,
                operation.Digest),
            operation.RequestSha256,
            operation.ExpectedVersion,
            operation.ResultingVersion,
            operation.BatchSize,
            operation.RemovedRecordCount,
            operation.CompletedBatchCount,
            operation.ProofVersion,
            operation.RemovalProofSha256,
            operation.StartedAtUtc,
            operation.UpdatedAtUtc);

    private static NotificationScopeExportRecord Map(
        NotificationHistoryBatchCloseReceipt receipt) =>
        new NotificationScopeHistoryBatchCloseReceiptExportRecord(
            receipt.OperationId,
            new NotificationHistoryReference(
                receipt.Namespace,
                receipt.Digest),
            receipt.RequestSha256,
            receipt.ResultingVersion,
            receipt.RemovedRecordCount,
            receipt.CompletedBatchCount,
            receipt.RemovalProofVersion,
            receipt.RemovalProofSha256,
            receipt.StartedAtUtc,
            receipt.CompletedAtUtc);

    private static JsonElement ParsePayload(string payloadJson)
    {
        using JsonDocument document = JsonDocument.Parse(payloadJson);
        return document.RootElement.Clone();
    }

    private static ContractSeverity ToContract(DomainSeverity value) =>
        value switch
        {
            DomainSeverity.Info => ContractSeverity.Info,
            DomainSeverity.Success => ContractSeverity.Success,
            DomainSeverity.Warning => ContractSeverity.Warning,
            DomainSeverity.Error => ContractSeverity.Error,
            _ => ContractSeverity.Unknown
        };

    private static ContractDeliveryPolicy ToContract(
        DomainDeliveryPolicy value) =>
        value switch
        {
            DomainDeliveryPolicy.RespectPreferences =>
                ContractDeliveryPolicy.RespectPreferences,
            DomainDeliveryPolicy.Mandatory => ContractDeliveryPolicy.Mandatory,
            _ => ContractDeliveryPolicy.Unknown
        };

    private static ContractDeliveryStatus ToContract(
        DomainDeliveryStatus value) =>
        value switch
        {
            DomainDeliveryStatus.Pending => ContractDeliveryStatus.Pending,
            DomainDeliveryStatus.Processing => ContractDeliveryStatus.Processing,
            DomainDeliveryStatus.RetryScheduled =>
                ContractDeliveryStatus.RetryScheduled,
            DomainDeliveryStatus.Delivered => ContractDeliveryStatus.Delivered,
            DomainDeliveryStatus.Rejected => ContractDeliveryStatus.Rejected,
            DomainDeliveryStatus.Exhausted => ContractDeliveryStatus.Exhausted,
            DomainDeliveryStatus.Suppressed => ContractDeliveryStatus.Suppressed,
            DomainDeliveryStatus.Unroutable => ContractDeliveryStatus.Unroutable,
            _ => ContractDeliveryStatus.Unknown
        };

    private static ContractAttemptOutcome ToContract(
        DomainAttemptOutcome value) =>
        value switch
        {
            DomainAttemptOutcome.Delivered => ContractAttemptOutcome.Delivered,
            DomainAttemptOutcome.Retry => ContractAttemptOutcome.Retry,
            DomainAttemptOutcome.Rejected => ContractAttemptOutcome.Rejected,
            DomainAttemptOutcome.Exception => ContractAttemptOutcome.Exception,
            _ => ContractAttemptOutcome.Unknown
        };

    private static ContractTagKind ToContract(DomainTagKind value) =>
        value switch
        {
            DomainTagKind.Delivery => ContractTagKind.Delivery,
            DomainTagKind.Domain => ContractTagKind.Domain,
            _ => ContractTagKind.Unknown
        };

    private static ContractTagOrigin ToContract(DomainTagOrigin value) =>
        value switch
        {
            DomainTagOrigin.System => ContractTagOrigin.System,
            DomainTagOrigin.Module => ContractTagOrigin.Module,
            DomainTagOrigin.Operator => ContractTagOrigin.Operator,
            _ => ContractTagOrigin.Unknown
        };

    private static ContractAudience ToContract(DomainAudience value) =>
        value switch
        {
            DomainAudience.TenantUsers => ContractAudience.TenantUsers,
            DomainAudience.TenantAdmins => ContractAudience.TenantAdmins,
            DomainAudience.PlatformUsers => ContractAudience.PlatformUsers,
            DomainAudience.PlatformAdmins => ContractAudience.PlatformAdmins,
            _ => ContractAudience.Unknown
        };

    private static ContractRecipientKind ToContract(DomainRecipientKind value) =>
        value switch
        {
            DomainRecipientKind.User => ContractRecipientKind.User,
            DomainRecipientKind.Admin => ContractRecipientKind.Admin,
            _ => ContractRecipientKind.Unknown
        };
}
