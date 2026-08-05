namespace Gma.Modules.Notifications.Domain.Entities;

using Gma.Framework.Domain;
using Gma.Framework.Naming;
using Gma.Framework.Results;
using Gma.Modules.Notifications.Domain.Errors;

public sealed class NotificationScopeState : IScopedEntity
{
    private NotificationScopeState() { }

    private NotificationScopeState(string scopeId) => this.ScopeId = scopeId;

    public string ScopeId { get; private set; } = string.Empty;
    public long Version { get; private set; }
    public bool IsClosed { get; private set; }
    public Guid? CloseOperationId { get; private set; }
    public string? CloseRequestSha256 { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public static Result<NotificationScopeState> Create(string scopeId) =>
        ScopeIds.TryNormalize(scopeId, out string? normalizedScopeId)
            ? Result.Success(new NotificationScopeState(normalizedScopeId!))
            : Result.Failure<NotificationScopeState>(
                NotificationsDomainErrors.ScopeStateInvalid);

    public bool RegisterMutation()
    {
        if (this.IsClosed || this.Version == long.MaxValue)
        {
            return false;
        }

        this.Version++;
        return true;
    }

    public NotificationScopeCloseTransition Close(
        Guid operationId,
        string requestSha256,
        DateTimeOffset closedAtUtc)
    {
        if (operationId == Guid.Empty ||
            !IsSha256(requestSha256) ||
            closedAtUtc == default)
        {
            return NotificationScopeCloseTransition.Invalid;
        }

        if (this.IsClosed)
        {
            return this.CloseOperationId == operationId &&
                string.Equals(
                    this.CloseRequestSha256,
                    requestSha256,
                    StringComparison.Ordinal)
                ? NotificationScopeCloseTransition.Replayed
                : NotificationScopeCloseTransition.Conflict;
        }

        if (this.Version == long.MaxValue)
        {
            return NotificationScopeCloseTransition.Invalid;
        }

        this.Version++;
        this.IsClosed = true;
        this.CloseOperationId = operationId;
        this.CloseRequestSha256 = requestSha256;
        this.ClosedAtUtc = closedAtUtc;
        return NotificationScopeCloseTransition.Completed;
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character =>
            character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
