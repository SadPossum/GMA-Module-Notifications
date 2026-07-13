namespace Gma.Modules.Notifications.Application;

using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Domain.Aggregates;
using Gma.Modules.Notifications.Domain.ValueObjects;
using ContractTagKind = Contracts.NotificationTagKind;
using ContractTagOrigin = Contracts.NotificationTagOrigin;
using DomainTagKind = Domain.ValueObjects.NotificationTagKind;
using DomainTagOrigin = Domain.ValueObjects.NotificationTagOrigin;

internal static class NotificationRoutingContractMapper
{
    public static NotificationPreferenceItem Preference(
        NotificationTagDefinition definition,
        NotificationPreference? preference) =>
        new(
            definition.Key.Value,
            TagKind(definition.Kind),
            definition.DisplayName,
            definition.Description,
            preference?.Enabled ?? true,
            definition.IsActive);

    public static AdminNotificationTagDefinitionItem TagDefinition(NotificationTagDefinition definition) =>
        new(
            definition.Id,
            definition.Key.Value,
            TagKind(definition.Kind),
            definition.DisplayName,
            definition.Description,
            TagOrigin(definition.Origin),
            definition.Owner,
            definition.IsActive,
            definition.Version,
            definition.CreatedAtUtc,
            definition.UpdatedAtUtc,
            definition.CreatedBy,
            definition.UpdatedBy);

    public static AdminNotificationDeliveryRouteItem Route(NotificationDeliveryRoute route) =>
        new(
            route.Id,
            route.DeliveryTag.Value,
            new NotificationDeliveryProviderCode(route.Provider.Value),
            route.IsActive,
            route.Version,
            route.UpdatedAtUtc,
            route.UpdatedBy);

    public static DomainTagKind TagKind(ContractTagKind kind) => kind switch
    {
        ContractTagKind.Delivery => DomainTagKind.Delivery,
        ContractTagKind.Domain => DomainTagKind.Domain,
        _ => DomainTagKind.Unknown
    };

    private static ContractTagKind TagKind(DomainTagKind kind) => kind switch
    {
        DomainTagKind.Delivery => ContractTagKind.Delivery,
        DomainTagKind.Domain => ContractTagKind.Domain,
        _ => ContractTagKind.Unknown
    };

    private static ContractTagOrigin TagOrigin(DomainTagOrigin origin) => origin switch
    {
        DomainTagOrigin.System => ContractTagOrigin.System,
        DomainTagOrigin.Module => ContractTagOrigin.Module,
        DomainTagOrigin.Operator => ContractTagOrigin.Operator,
        _ => ContractTagOrigin.Unknown
    };
}
