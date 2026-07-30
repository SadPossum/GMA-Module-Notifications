namespace Gma.Modules.Notifications.Domain.ValueObjects;

using System.Security.Cryptography;
using System.Text;
using Gma.Framework.Naming;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Domain.Errors;

public sealed record NotificationHistoryReferenceKey
{
    public const int NamespaceMaxLength = 64;
    public const int DigestLength = 64;
    public const int MaxProducerCount = 8;
    public const int MaxStoredCount = MaxProducerCount + 1;
    public const string RecipientNamespace = "recipient";

    private NotificationHistoryReferenceKey(
        string referenceNamespace,
        string digest)
    {
        this.Namespace = referenceNamespace;
        this.Digest = digest;
    }

    public string Namespace { get; }
    public string Digest { get; }

    public static Result<NotificationHistoryReferenceKey> Create(
        string? referenceNamespace,
        string? digest)
    {
        string normalizedNamespace;
        try
        {
            normalizedNamespace = SharedNameSegments.NormalizeKebabSegment(
                referenceNamespace ?? string.Empty,
                "notification history reference namespace",
                nameof(referenceNamespace));
        }
        catch (ArgumentException)
        {
            return Result.Failure<NotificationHistoryReferenceKey>(
                NotificationsDomainErrors.HistoryReferenceInvalid);
        }

        string normalizedDigest = digest?.Trim().ToLowerInvariant() ??
                                  string.Empty;
        if (normalizedNamespace.Length > NamespaceMaxLength ||
            normalizedDigest.Length != DigestLength ||
            normalizedDigest.Any(character =>
                character is not (>= '0' and <= '9') and
                    not (>= 'a' and <= 'f')))
        {
            return Result.Failure<NotificationHistoryReferenceKey>(
                NotificationsDomainErrors.HistoryReferenceInvalid);
        }

        return Result.Success(
            new NotificationHistoryReferenceKey(
                normalizedNamespace,
                normalizedDigest));
    }

    public static Result<NotificationHistoryReferenceKey> ForRecipient(
        string scopeId,
        string userId)
    {
        if (!ScopeIds.TryNormalize(scopeId, out string? normalizedScopeId))
        {
            return Result.Failure<NotificationHistoryReferenceKey>(
                NotificationsDomainErrors.HistoryReferenceInvalid);
        }

        Result<NotificationRecipient> recipient =
            NotificationRecipient.Create(userId);
        if (recipient.IsFailure)
        {
            return Result.Failure<NotificationHistoryReferenceKey>(
                NotificationsDomainErrors.HistoryReferenceInvalid);
        }

        string canonical =
            $"gma-notification-recipient/v1|{normalizedScopeId}|{recipient.Value.UserId}";
        string digest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
        return Create(RecipientNamespace, digest);
    }
}
