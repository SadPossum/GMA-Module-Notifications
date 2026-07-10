namespace Gma.Modules.Notifications.Application;

using Gma.Modules.Notifications.Domain.Entities;
using Gma.Modules.Notifications.Domain.Errors;
using Gma.Modules.Notifications.Domain.ValueObjects;
using Gma.Framework.Naming;
using Gma.Framework.Results;
using ContractRecipientKind = Gma.Modules.Notifications.Contracts.NotificationBroadcastRecipientKind;
using DomainRecipientKind = Gma.Modules.Notifications.Domain.ValueObjects.NotificationBroadcastRecipientKind;

public sealed record NotificationBroadcastRecipientContext
{
    private NotificationBroadcastRecipientContext(
        string? scopeId,
        DomainRecipientKind recipientKind,
        NotificationRecipient recipient,
        string recipientScope)
    {
        this.ScopeId = scopeId;
        this.RecipientKind = recipientKind;
        this.Recipient = recipient;
        this.RecipientScope = recipientScope;
    }

    public string? ScopeId { get; }
    public DomainRecipientKind RecipientKind { get; }
    public NotificationRecipient Recipient { get; }
    public string RecipientId => this.Recipient.UserId;
    public string RecipientKindName => NotificationBroadcastRecipientKindNames.ToWireName(this.RecipientKind);
    public string RecipientScope { get; }

    public static Result<NotificationBroadcastRecipientContext> Create(
        string? scopeId,
        ContractRecipientKind recipientKind,
        string recipientId)
    {
        string? normalizedScopeId = null;
        if (!string.IsNullOrWhiteSpace(scopeId))
        {
            if (!ScopeIds.TryNormalize(scopeId, out normalizedScopeId))
            {
                return Result.Failure<NotificationBroadcastRecipientContext>(NotificationsDomainErrors.TenantInvalid);
            }
        }

        DomainRecipientKind normalizedRecipientKind = NotificationBroadcastRecipientKindMapper.ToDomainValue(recipientKind);
        if (normalizedRecipientKind is not DomainRecipientKind.User and not DomainRecipientKind.Admin)
        {
            return Result.Failure<NotificationBroadcastRecipientContext>(
                NotificationsDomainErrors.BroadcastRecipientKindInvalid);
        }

        Result<NotificationRecipient> normalizedRecipient = NotificationRecipient.Create(recipientId);
        if (normalizedRecipient.IsFailure)
        {
            return Result.Failure<NotificationBroadcastRecipientContext>(normalizedRecipient.Error);
        }

        Result<string> recipientScope = NotificationBroadcastRead.CreateRecipientScope(normalizedScopeId);
        return recipientScope.IsFailure
            ? Result.Failure<NotificationBroadcastRecipientContext>(recipientScope.Error)
            : Result.Success(new NotificationBroadcastRecipientContext(
                normalizedScopeId,
                normalizedRecipientKind,
                normalizedRecipient.Value,
                recipientScope.Value));
    }
}
