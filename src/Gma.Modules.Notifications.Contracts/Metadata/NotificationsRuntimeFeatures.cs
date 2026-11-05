namespace Gma.Modules.Notifications.Contracts;

using Gma.Framework.ModuleComposition;

/// <summary>Executable capabilities provided by the owning Notifications runtime.</summary>
public static class NotificationsRuntimeFeatures
{
    /// <summary>The enabled delivery worker claims and completes work across open scopes.</summary>
    public static readonly CompositionFeatureId CrossScopeDeliveryClaims =
        new("notifications.cross-scope-delivery-claims");

    /// <summary>Advertises the enabled worker's isolated, scope-fenced maintenance context.</summary>
    public static ProvidedCompositionFeature CrossScopeDeliveryClaimsProvided(string provider) =>
        new(CrossScopeDeliveryClaims, provider,
            "Enabled delivery claims and completion use an isolated module-owned maintenance context with closed-scope fences.");
}
