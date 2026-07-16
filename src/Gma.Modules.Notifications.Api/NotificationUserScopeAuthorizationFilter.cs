namespace Gma.Modules.Notifications.Api;

using Gma.Framework.AccessControl;
using Gma.Framework.Scoping;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

internal sealed class NotificationUserScopeAuthorizationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (!NotificationUserContext.TryResolveSubject(context.HttpContext.User, out AccessSubject? subject))
        {
            return Results.Unauthorized();
        }

        INotificationUserScopeAuthorizer authorizer = context.HttpContext.RequestServices
            .GetRequiredService<INotificationUserScopeAuthorizer>();
        IScopeContext scopeContext = context.HttpContext.RequestServices
            .GetRequiredService<IScopeContext>();
        bool authorized = await authorizer.AuthorizeAsync(
            context.HttpContext.User,
            subject,
            scopeContext,
            context.HttpContext.RequestAborted).ConfigureAwait(false);
        return authorized
            ? await next(context).ConfigureAwait(false)
            : Results.Forbid();
    }
}
