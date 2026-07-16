namespace Gma.Modules.Notifications.Api;

using System.Security.Claims;
using Gma.Framework.AccessControl;
using Gma.Framework.Scoping;

public interface INotificationUserScopeAuthorizer
{
    Task<bool> AuthorizeAsync(
        ClaimsPrincipal principal,
        AccessSubject subject,
        IScopeContext scopeContext,
        CancellationToken cancellationToken);
}
