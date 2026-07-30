namespace Gma.Modules.Notifications.Contracts;

using System.Security.Cryptography;
using System.Text;
using Gma.Framework.Naming;
using Gma.Framework.Scoping;

public sealed record NotificationHistoryReference
{
    public const int NamespaceMaxLength = 64;
    public const int DigestLength = 64;
    public const int MaxCount = 8;
    public const int CanonicalCoordinateMaxLength = 2048;
    public const string RecipientNamespace = "recipient";

    public NotificationHistoryReference(string @namespace, string digest)
    {
        string normalizedNamespace = SharedNameSegments.NormalizeKebabSegment(
            @namespace,
            "notification history reference namespace",
            nameof(@namespace));
        if (normalizedNamespace.Length > NamespaceMaxLength)
        {
            throw new ArgumentException(
                $"namespace must be {NamespaceMaxLength} characters or fewer.",
                nameof(@namespace));
        }

        string normalizedDigest = digest?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedDigest.Length != DigestLength ||
            normalizedDigest.Any(character =>
                character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                $"digest must be a lowercase {DigestLength}-character SHA-256 value.",
                nameof(digest));
        }

        this.Namespace = normalizedNamespace;
        this.Digest = normalizedDigest;
    }

    public string Namespace { get; }
    public string Digest { get; }

    public static NotificationHistoryReference FromCanonicalCoordinate(
        string referenceNamespace,
        string canonicalCoordinate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalCoordinate);
        if (!string.Equals(
                canonicalCoordinate,
                canonicalCoordinate.Trim(),
                StringComparison.Ordinal) ||
            canonicalCoordinate.Length > CanonicalCoordinateMaxLength ||
            canonicalCoordinate.Any(character => char.IsControl(character)))
        {
            throw new ArgumentException(
                $"canonicalCoordinate must be normalized, {CanonicalCoordinateMaxLength} characters or fewer, and cannot contain control characters.",
                nameof(canonicalCoordinate));
        }

        string digest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonicalCoordinate)))
            .ToLowerInvariant();
        return new NotificationHistoryReference(referenceNamespace, digest);
    }

    public static NotificationHistoryReference ForRecipient(
        string scopeId,
        string userId)
    {
        string normalizedScopeId = ScopeIds.Normalize(scopeId, nameof(scopeId));
        string normalizedUserId = NotificationRecipientUserIds.Normalize(
            userId,
            nameof(userId));
        return FromCanonicalCoordinate(
            RecipientNamespace,
            $"gma-notification-recipient/v1|{normalizedScopeId}|{normalizedUserId}");
    }

    internal static System.Collections.ObjectModel.ReadOnlyCollection<
        NotificationHistoryReference> CopyDistinct(
        IReadOnlyList<NotificationHistoryReference> references,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(references, parameterName);
        NotificationHistoryReference[] supplied = references
            .Select(reference =>
                reference ??
                throw new ArgumentException(
                    "Notification history references cannot contain null values.",
                    parameterName))
            .ToArray();
        if (supplied.Any(reference =>
                string.Equals(
                    reference.Namespace,
                    RecipientNamespace,
                    StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"The '{RecipientNamespace}' history reference namespace is reserved by Notifications.",
                parameterName);
        }

        NotificationHistoryReference[] normalized = supplied
            .Distinct()
            .OrderBy(reference => reference.Namespace, StringComparer.Ordinal)
            .ThenBy(reference => reference.Digest, StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length != supplied.Length)
        {
            throw new ArgumentException(
                "Notification history references cannot contain duplicates.",
                parameterName);
        }

        if (normalized.Length > MaxCount)
        {
            throw new ArgumentException(
                $"Notifications can contain at most {MaxCount} distinct history references.",
                parameterName);
        }

        return Array.AsReadOnly(normalized);
    }
}
