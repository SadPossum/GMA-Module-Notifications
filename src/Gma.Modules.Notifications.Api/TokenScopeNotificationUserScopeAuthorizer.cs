namespace Gma.Modules.Notifications.Api;

using System.Security.Claims;
using Gma.Framework.AccessControl;
using Gma.Framework.Naming;
using Gma.Framework.Scoping;
using Gma.Framework.Security;

internal sealed class TokenScopeNotificationUserScopeAuthorizer : INotificationUserScopeAuthorizer
{
    public Task<bool> AuthorizeAsync(
        ClaimsPrincipal principal,
        AccessSubject subject,
        IScopeContext scopeContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(scopeContext);
        cancellationToken.ThrowIfCancellationRequested();

        if (!scopeContext.IsEnabled)
        {
            return Task.FromResult(true);
        }

        string? tokenScopeId = principal.FindFirstValue(ApplicationClaimNames.ScopeId);
        bool authorized = ScopeIds.TryNormalize(tokenScopeId, out string? normalizedTokenScopeId) &&
            string.Equals(normalizedTokenScopeId, scopeContext.ScopeId, StringComparison.Ordinal);
        return Task.FromResult(authorized);
    }
}
