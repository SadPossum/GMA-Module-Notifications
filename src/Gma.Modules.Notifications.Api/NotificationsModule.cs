namespace Gma.Modules.Notifications.Api;

using System.Net.ServerSentEvents;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Gma.Modules.Notifications.Application;
using Gma.Modules.Notifications.Application.Commands;
using Gma.Modules.Notifications.Application.Queries;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Persistence;
using Gma.Framework.AccessControl;
using Gma.Framework.Api.Modules;
using Gma.Framework.Api.Observability;
using Gma.Framework.Api.Results;
using Gma.Framework.Api.Scoping;
using Gma.Framework.Cqrs;
using Gma.Framework.ModuleComposition;
using Gma.Framework.Naming;
using Gma.Framework.Scoping;
using Gma.Framework.Results;
using Gma.Framework.Security;

public sealed class NotificationsModule : IModule
{
    public string Name => NotificationsModuleMetadata.Name;

    public void AddServices(IHostApplicationBuilder builder)
    {
        builder.SelectModuleProfile(NotificationsProfiles.Default, "Gma.Modules.Notifications.Api");
        builder.Services.AddNotificationsApplication(builder.Configuration);
        builder.AddNotificationsPersistence();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/notifications")
            .WithModuleName(this.Name)
            .WithTags("Notifications")
            .RequireAuthorization();

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            bool? unreadOnly,
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(httpContext, scopeContext, out AccessSubject subject, out IResult? failure))
            {
                return failure;
            }

            Result<NotificationHistoryListResponse> result = await dispatcher.QueryAsync(
                new ListNotificationHistoryQuery(
                    subject,
                    CurrentScopeId(scopeContext),
                    unreadOnly ?? false,
                    page ?? Gma.Framework.Pagination.PageRequest.DefaultPage,
                    pageSize ?? Gma.Framework.Pagination.PageRequest.DefaultPageSize),
                cancellationToken).ConfigureAwait(false);

            return result.ToHttpResult(PublicErrorStatusCodes);
        })
            .RequireScope();

        group.MapGet("/broadcasts", async (
            int? page,
            int? pageSize,
            bool? unreadOnly,
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(httpContext, scopeContext, out AccessSubject subject, out IResult? failure))
            {
                return failure;
            }

            Result<NotificationBroadcastListResponse> result = await dispatcher.QueryAsync(
                new ListNotificationBroadcastsQuery(
                    CurrentScopeId(scopeContext),
                    NotificationBroadcastRecipientKind.User,
                    subject.Id,
                    unreadOnly ?? false,
                    page ?? Gma.Framework.Pagination.PageRequest.DefaultPage,
                    pageSize ?? Gma.Framework.Pagination.PageRequest.DefaultPageSize),
                cancellationToken).ConfigureAwait(false);

            return result.ToHttpResult(PublicErrorStatusCodes);
        })
            .RequireScope();

        group.MapGet("/broadcasts/{broadcastId:guid}", async (
            Guid broadcastId,
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(httpContext, scopeContext, out AccessSubject subject, out IResult? failure))
            {
                return failure;
            }

            Result<NotificationBroadcastItem> result = await dispatcher.QueryAsync(
                new GetNotificationBroadcastQuery(
                    broadcastId,
                    CurrentScopeId(scopeContext),
                    NotificationBroadcastRecipientKind.User,
                    subject.Id),
                cancellationToken).ConfigureAwait(false);

            return result.ToHttpResult(PublicErrorStatusCodes);
        })
            .RequireScope();

        group.MapPost("/broadcasts/{broadcastId:guid}/read", async (
            Guid broadcastId,
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(httpContext, scopeContext, out AccessSubject subject, out IResult? failure))
            {
                return failure;
            }

            Result<Unit> result = await dispatcher.SendAsync(
                new MarkNotificationBroadcastReadCommand(
                    broadcastId,
                    CurrentScopeId(scopeContext),
                    NotificationBroadcastRecipientKind.User,
                    subject.Id),
                cancellationToken).ConfigureAwait(false);

            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult(PublicErrorStatusCodes);
        })
            .RequireScope();

        group.MapPost("/broadcasts/read-all", async (
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(httpContext, scopeContext, out AccessSubject subject, out IResult? failure))
            {
                return failure;
            }

            Result<MarkAllNotificationBroadcastsReadResponse> result = await dispatcher.SendAsync(
                new MarkAllNotificationBroadcastsReadCommand(
                    CurrentScopeId(scopeContext),
                    NotificationBroadcastRecipientKind.User,
                    subject.Id),
                cancellationToken).ConfigureAwait(false);

            return result.ToHttpResult(PublicErrorStatusCodes);
        })
            .RequireScope();

        group.MapGet("/broadcasts/stream", async (
            long? afterSequence,
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            ILogger<NotificationsModule> logger,
            IOptions<NotificationStreamOptions> streamOptions,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(httpContext, scopeContext, out AccessSubject subject, out IResult? failure))
            {
                return failure;
            }

            if (afterSequence is < 0)
            {
                return Results.Problem(
                    title: NotificationsApplicationErrors.StreamCursorInvalid.Code,
                    detail: NotificationsApplicationErrors.StreamCursorInvalid.Message,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            string? scopeId = CurrentScopeId(scopeContext);
            long cursor;
            if (afterSequence.HasValue)
            {
                cursor = afterSequence.Value;
            }
            else
            {
                Result<long> cursorResult = await ResolveCurrentBroadcastCursorAsync(
                    dispatcher,
                        scopeId,
                        NotificationBroadcastRecipientKind.User,
                        subject.Id,
                        cancellationToken).ConfigureAwait(false);
                if (cursorResult.IsFailure)
                {
                    return cursorResult.ToHttpResult(PublicErrorStatusCodes);
                }

                cursor = cursorResult.Value;
            }

            IResult stream = TypedResults.ServerSentEvents(
                StreamBroadcastsAsync(
                    dispatcher,
                    scopeId,
                    NotificationBroadcastRecipientKind.User,
                    subject.Id,
                    cursor,
                    streamOptions.Value,
                    logger,
                    httpContext.RequestAborted));
            return stream;
        })
            .RequireScope()
            .DisableRequestTimeout();

        group.MapGet("/{notificationId:guid}", async (
            Guid notificationId,
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(httpContext, scopeContext, out AccessSubject subject, out IResult? failure))
            {
                return failure;
            }

            Result<NotificationHistoryItem> result = await dispatcher.QueryAsync(
                new GetNotificationHistoryItemQuery(notificationId, subject, CurrentScopeId(scopeContext)),
                cancellationToken).ConfigureAwait(false);

            return result.ToHttpResult(PublicErrorStatusCodes);
        })
            .RequireScope();

        group.MapPost("/{notificationId:guid}/read", async (
            Guid notificationId,
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(httpContext, scopeContext, out AccessSubject subject, out IResult? failure))
            {
                return failure;
            }

            Result<Unit> result = await dispatcher.SendAsync(
                new MarkNotificationReadCommand(notificationId, subject, CurrentScopeId(scopeContext)),
                cancellationToken).ConfigureAwait(false);

            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult(PublicErrorStatusCodes);
        })
            .RequireScope();

        group.MapPost("/read-all", async (
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(httpContext, scopeContext, out AccessSubject subject, out IResult? failure))
            {
                return failure;
            }

            Result<MarkAllNotificationsReadResponse> result = await dispatcher.SendAsync(
                new MarkAllNotificationsReadCommand(subject, CurrentScopeId(scopeContext)),
                cancellationToken).ConfigureAwait(false);

            return result.ToHttpResult(PublicErrorStatusCodes);
        })
            .RequireScope();

        group.MapGet("/history/stream", async (
            long? afterSequence,
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            ILogger<NotificationsModule> logger,
            IOptions<NotificationStreamOptions> streamOptions,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(httpContext, scopeContext, out AccessSubject subject, out IResult? failure))
            {
                return failure;
            }

            if (afterSequence is < 0)
            {
                return Results.Problem(
                    title: NotificationsApplicationErrors.StreamCursorInvalid.Code,
                    detail: NotificationsApplicationErrors.StreamCursorInvalid.Message,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            long cursor;
            if (afterSequence.HasValue)
            {
                cursor = afterSequence.Value;
            }
            else
            {
                Result<long> cursorResult = await ResolveCurrentUserCursorAsync(
                    dispatcher,
                    subject,
                    CurrentScopeId(scopeContext),
                    cancellationToken).ConfigureAwait(false);
                if (cursorResult.IsFailure)
                {
                    return cursorResult.ToHttpResult(PublicErrorStatusCodes);
                }

                cursor = cursorResult.Value;
            }

            IResult stream = TypedResults.ServerSentEvents(
                StreamUserHistoryAsync(
                    dispatcher,
                    subject,
                    CurrentScopeId(scopeContext),
                    cursor,
                    streamOptions.Value,
                    logger,
                    httpContext.RequestAborted));
            return stream;
        })
            .RequireScope()
            .DisableRequestTimeout();
    }

    private static Task<Result<long>> ResolveCurrentUserCursorAsync(
        IRequestDispatcher dispatcher,
        AccessSubject subject,
        string? scopeId,
        CancellationToken cancellationToken) =>
        dispatcher.QueryAsync(
            new GetNotificationStreamCursorQuery(subject, scopeId),
            cancellationToken);

    private static Task<Result<long>> ResolveCurrentBroadcastCursorAsync(
        IRequestDispatcher dispatcher,
        string? scopeId,
        NotificationBroadcastRecipientKind recipientKind,
        string recipientId,
        CancellationToken cancellationToken) =>
        dispatcher.QueryAsync(
            new GetNotificationBroadcastStreamCursorQuery(scopeId, recipientKind, recipientId),
            cancellationToken);

    private static async IAsyncEnumerable<SseItem<NotificationHistoryItem>> StreamUserHistoryAsync(
        IRequestDispatcher dispatcher,
        AccessSubject subject,
        string? scopeId,
        long initialCursor,
        NotificationStreamOptions options,
        ILogger logger,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long afterSequence = initialCursor;
        using PeriodicTimer pollTimer = new(options.PollInterval);

        while (!cancellationToken.IsCancellationRequested)
        {
            Result<IReadOnlyList<NotificationHistoryItem>> result = await dispatcher.QueryAsync(
                new StreamNotificationHistoryQuery(subject, scopeId, afterSequence, options.BatchSize),
                cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                LogStreamQueryFailure(logger, "history", result.Error);
                yield break;
            }

            foreach (NotificationHistoryItem item in result.Value)
            {
                afterSequence = item.StreamSequence;
                yield return new SseItem<NotificationHistoryItem>(item, "notification");
            }

            if (!await pollTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                yield break;
            }
        }
    }

    private static async IAsyncEnumerable<SseItem<NotificationBroadcastItem>> StreamBroadcastsAsync(
        IRequestDispatcher dispatcher,
        string? scopeId,
        NotificationBroadcastRecipientKind recipientKind,
        string recipientId,
        long initialCursor,
        NotificationStreamOptions options,
        ILogger logger,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long afterSequence = initialCursor;
        using PeriodicTimer pollTimer = new(options.PollInterval);

        while (!cancellationToken.IsCancellationRequested)
        {
            Result<IReadOnlyList<NotificationBroadcastItem>> result = await dispatcher.QueryAsync(
                new StreamNotificationBroadcastsQuery(
                    scopeId,
                    recipientKind,
                    recipientId,
                    afterSequence,
                    options.BatchSize),
                cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                LogStreamQueryFailure(logger, "broadcast", result.Error);
                yield break;
            }

            foreach (NotificationBroadcastItem item in result.Value)
            {
                afterSequence = item.StreamSequence;
                yield return new SseItem<NotificationBroadcastItem>(item, "notification-broadcast");
            }

            if (!await pollTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                yield break;
            }
        }
    }

    private static bool TryResolveUserContext(
        HttpContext httpContext,
        IScopeContext scopeContext,
        out AccessSubject subject,
        out IResult? failure)
    {
        subject = null!;
        failure = null;

        string? candidateUserId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                                  httpContext.User.FindFirstValue(ApplicationClaimNames.Subject);
        if (!NotificationRecipientUserIds.TryNormalize(candidateUserId, out string normalizedUserId))
        {
            failure = Results.Unauthorized();
            return false;
        }

        if (scopeContext.IsEnabled)
        {
            string? tokenScopeId = httpContext.User.FindFirstValue(ApplicationClaimNames.ScopeId);
            if (!ScopeIds.TryNormalize(tokenScopeId, out string? normalizedTokenScopeId) ||
                !string.Equals(normalizedTokenScopeId, scopeContext.ScopeId, StringComparison.Ordinal))
            {
                failure = Results.Forbid();
                return false;
            }
        }

        if (!AccessSubject.TryCreate(AccessSubjectKind.User, normalizedUserId, out AccessSubject? resolvedSubject))
        {
            failure = Results.Unauthorized();
            return false;
        }

        subject = resolvedSubject;
        return true;
    }

    private static string? CurrentScopeId(IScopeContext scopeContext) =>
        scopeContext.ScopeId;

    private static void LogStreamQueryFailure(ILogger logger, string streamName, Error error)
    {
        logger.LogWarning(
            "Notification {StreamName} stream query failed and the stream will be closed. Error: {ErrorCode}.",
            streamName,
            error.Code);
    }

    private static readonly ApiErrorStatusCodeMap PublicErrorStatusCodes = ApiErrorStatusCodeMap.Create(
        new ApiErrorStatusCode(NotificationsApplicationErrors.NotificationNotFound.Code, StatusCodes.Status404NotFound),
        new ApiErrorStatusCode(NotificationsApplicationErrors.BroadcastNotFound.Code, StatusCodes.Status404NotFound),
        new ApiErrorStatusCode(NotificationsApplicationErrors.AccessDenied.Code, StatusCodes.Status403Forbidden));
}
