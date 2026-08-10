namespace Gma.Modules.Notifications.Contracts;

public sealed record NotificationScopeTagDefinitionExportRecord(
    Guid DefinitionId,
    string TagKey,
    NotificationTagKind Kind,
    string DisplayName,
    string Description,
    NotificationTagOrigin Origin,
    string Owner,
    bool IsActive,
    int Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string CreatedBy,
    string UpdatedBy)
    : NotificationScopeExportRecord;
