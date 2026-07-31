namespace Gma.Modules.Notifications.Domain.Entities;

using Gma.Framework.Domain;
using Gma.Framework.Naming;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Domain.Errors;
using Gma.Modules.Notifications.Domain.ValueObjects;

public sealed class NotificationHistoryReferenceState : IScopedEntity
{
    private NotificationHistoryReferenceState() { }

    private NotificationHistoryReferenceState(
        string scopeId,
        NotificationHistoryReferenceKey reference)
    {
        this.ScopeId = scopeId;
        this.Namespace = reference.Namespace;
        this.Digest = reference.Digest;
    }

    public string ScopeId { get; private set; } = string.Empty;
    public string Namespace { get; private set; } = string.Empty;
    public string Digest { get; private set; } = string.Empty;
    public long Version { get; private set; }
    public bool IsClosed { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public Guid? CloseOperationId { get; private set; }
    public string? CloseRequestSha256 { get; private set; }

    public static Result<NotificationHistoryReferenceState> Create(
        string scopeId,
        NotificationHistoryReferenceKey reference)
    {
        if (!ScopeIds.TryNormalize(scopeId, out string? normalizedScopeId) ||
            reference is null)
        {
            return Result.Failure<NotificationHistoryReferenceState>(
                NotificationsDomainErrors.HistoryReferenceInvalid);
        }

        return Result.Success(
            new NotificationHistoryReferenceState(
                normalizedScopeId!,
                reference));
    }

    public bool RegisterNotification()
    {
        return this.AdvanceVersion();
    }

    public bool EnsureOpen()
    {
        if (this.IsClosed)
        {
            return false;
        }

        if (this.Version == 0)
        {
            this.Version = 1;
        }

        return true;
    }

    public bool RecordRemoval()
    {
        return this.AdvanceVersion();
    }

    private bool AdvanceVersion()
    {
        if (this.IsClosed)
        {
            return false;
        }

        checked
        {
            this.Version++;
        }

        return true;
    }

    public NotificationHistoryReferenceCloseTransition Close(
        Guid operationId,
        string requestSha256,
        DateTimeOffset closedAtUtc)
    {
        if (operationId == Guid.Empty ||
            !IsSha256(requestSha256) ||
            closedAtUtc == default)
        {
            return NotificationHistoryReferenceCloseTransition.Invalid;
        }

        if (this.IsClosed)
        {
            return this.CloseOperationId == operationId &&
                   string.Equals(
                       this.CloseRequestSha256,
                       requestSha256,
                       StringComparison.Ordinal)
                ? NotificationHistoryReferenceCloseTransition.Replay
                : NotificationHistoryReferenceCloseTransition.Conflict;
        }

        checked
        {
            this.Version++;
        }

        this.IsClosed = true;
        this.ClosedAtUtc = closedAtUtc;
        this.CloseOperationId = operationId;
        this.CloseRequestSha256 = requestSha256;
        return NotificationHistoryReferenceCloseTransition.Completed;
    }

    public NotificationHistoryReferenceKey ToKey() =>
        NotificationHistoryReferenceKey.Create(
            this.Namespace,
            this.Digest).Value;

    private static bool IsSha256(string? value) =>
        value?.Length == NotificationHistoryReferenceKey.DigestLength &&
        value.All(character =>
            character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}

public enum NotificationHistoryReferenceCloseTransition
{
    Unknown = 0,
    Completed = 1,
    Replay = 2,
    Conflict = 3,
    Invalid = 4
}
