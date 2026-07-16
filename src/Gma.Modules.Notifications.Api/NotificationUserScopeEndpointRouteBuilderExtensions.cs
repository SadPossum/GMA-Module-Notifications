namespace Gma.Modules.Notifications.Api;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

internal static class NotificationUserScopeEndpointRouteBuilderExtensions
{
    public static RouteHandlerBuilder RequireNotificationUserScope(this RouteHandlerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddEndpointFilter<NotificationUserScopeAuthorizationFilter>();
    }
}
