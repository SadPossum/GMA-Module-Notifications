namespace Gma.Modules.Notifications.Api;

using System.Net.ServerSentEvents;
using Gma.Framework.AccessControl;
using Gma.Framework.Api.Modules;
using Gma.Framework.Api.Observability;
using Gma.Framework.Api.Results;
using Gma.Framework.Api.Scoping;
using Gma.Framework.Cqrs;
using Gma.Framework.ModuleComposition;
using Gma.Framework.Results;
using Gma.Framework.Scoping;
using Gma.Modules.Notifications.Application;
using Gma.Modules.Notifications.Application.Commands;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Application.Queries;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class NotificationsModule : IModule
{
    public string Name => NotificationsModuleMetadata.Name;

    public void AddServices(IHostApplicationBuilder builder)
    {
        builder.SelectModuleProfile(NotificationsProfiles.Default, "Gma.Modules.Notifications.Api");
        builder.Services.TryAddScoped<INotificationUserScopeAuthorizer, TokenScopeNotificationUserScopeAuthorizer>();
        builder.Services.AddNotificationsApplication(builder.Configuration);
        builder.AddNotificationsPersistence();
        builder.AddNotificationsDurableStreams();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/notifications")
            .WithModuleName(this.Name)
            .WithTags("Notifications")
            .RequireAuthorization();

        group.MapGet("/preferences", async (
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(httpContext, out AccessSubject subject, out IResult? failure))
            {
                return failure;
            }

            Result<NotificationPreferenceListResponse> result = await dispatcher.QueryAsync(
                new ListNotificationPreferencesQuery(subject.Id),
                cancellationToken).ConfigureAwait(false);
            return result.ToHttpResult(PublicErrorStatusCodes);
        })
            .RequireScope()
            .RequireNotificationUserScope();

        group.MapPut("/preferences/{tagKey}", async (
            string tagKey,
            SetNotificationPreferenceRequest request,
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(httpContext, out AccessSubject subject, out IResult? failure))
            {
                return failure;
            }

            Result<NotificationPreferenceItem> result = await dispatcher.SendAsync(
                new SetNotificationPreferenceCommand(
                    RequiredScopeId(scopeContext),
                    subject.Id,
                    tagKey,
                    request.Enabled),
                cancellationToken).ConfigureAwait(false);
            return result.ToHttpResult(PublicErrorStatusCodes);
        })
            .RequireScope()
            .RequireNotificationUserScope();

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            bool? unreadOnly,
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(httpContext, out AccessSubject subject, out IResult? failure))
            {
                return failure;
            }

            Result<NotificationHistoryListResponse> result = await dispatcher.QueryAsync(
                new ListNotificationHistoryQuery(
                    subject,
                    CurrentScopeId(scopeContext),
                    unreadOnly ?? false,
                    page ?? Framework.Pagination.PageRequest.DefaultPage,
                    pageSize ?? Framework.Pagination.PageRequest.DefaultPageSize),
                cancellationToken).ConfigureAwait(false);

            return result.ToHttpResult(PublicErrorStatusCodes);
        })
            .RequireScope()
            .RequireNotificationUserScope();

        group.MapGet("/broadcasts", async (
            int? page,
            int? pageSize,
            bool? unreadOnly,
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(httpContext, out AccessSubject subject, out IResult? failure))
            {
                return failure;
            }

            Result<NotificationBroadcastListResponse> result = await dispatcher.QueryAsync(
                new ListNotificationBroadcastsQuery(
                    CurrentScopeId(scopeContext),
                    NotificationBroadcastRecipientKind.User,
                    subject.Id,
                    unreadOnly ?? false,
                    page ?? Framework.Pagination.PageRequest.DefaultPage,
                    pageSize ?? Framework.Pagination.PageRequest.DefaultPageSize),
                cancellationToken).ConfigureAwait(false);

            return result.ToHttpResult(PublicErrorStatusCodes);
        })
            .RequireScope()
            .RequireNotificationUserScope();

        group.MapGet("/broadcasts/{broadcastId:guid}", async (
            Guid broadcastId,
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(httpContext, out AccessSubject subject, out IResult? failure))
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
            .RequireScope()
            .RequireNotificationUserScope();

        group.MapPost("/broadcasts/{broadcastId:guid}/read", async (
            Guid broadcastId,
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(httpContext, out AccessSubject subject, out IResult? failure))
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
            .RequireScope()
            .RequireNotificationUserScope();

        group.MapPost("/broadcasts/read-all", async (
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(httpContext, out AccessSubject subject, out IResult? failure))
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
            .RequireScope()
            .RequireNotificationUserScope();

        group.MapGet("/broadcasts/stream", async (
            long? afterSequence,
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            INotificationStreamPulse streamPulse,
            ILogger<NotificationsModule> logger,
            IOptions<NotificationStreamOptions> streamOptions,
            INotificationUserScopeAuthorizer userScopeAuthorizer,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(httpContext, out AccessSubject subject, out IResult? failure))
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

            NotificationStreamOptions options = streamOptions.Value;
            NotificationStreamAccessLease accessLease = NotificationStreamAccessLease.Create(
                options,
                httpContext.User,
                ResolveTimeProvider(httpContext));
            IResult stream = TypedResults.ServerSentEvents(
                StreamBroadcastsAsync(
                    dispatcher,
                    scopeId,
                    NotificationBroadcastRecipientKind.User,
                    subject.Id,
                    cursor,
                    options,
                    accessLease,
                    token => userScopeAuthorizer.AuthorizeAsync(
                        httpContext.User,
                        subject,
                        scopeContext,
                        token),
                    streamPulse,
                    logger,
                    httpContext.RequestAborted));
            return stream;
        })
            .RequireScope()
            .RequireNotificationUserScope()
            .DisableRequestTimeout();

        group.MapGet("/{notificationId:guid}", async (
            Guid notificationId,
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(httpContext, out AccessSubject subject, out IResult? failure))
            {
                return failure;
            }

            Result<NotificationHistoryItem> result = await dispatcher.QueryAsync(
                new GetNotificationHistoryItemQuery(notificationId, subject, CurrentScopeId(scopeContext)),
                cancellationToken).ConfigureAwait(false);

            return result.ToHttpResult(PublicErrorStatusCodes);
        })
            .RequireScope()
            .RequireNotificationUserScope();

        group.MapPost("/{notificationId:guid}/read", async (
            Guid notificationId,
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(httpContext, out AccessSubject subject, out IResult? failure))
            {
                return failure;
            }

            Result<Unit> result = await dispatcher.SendAsync(
                new MarkNotificationReadCommand(notificationId, subject, CurrentScopeId(scopeContext)),
                cancellationToken).ConfigureAwait(false);

            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult(PublicErrorStatusCodes);
        })
            .RequireScope()
            .RequireNotificationUserScope();

        group.MapPost("/read-all", async (
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(httpContext, out AccessSubject subject, out IResult? failure))
            {
                return failure;
            }

            Result<MarkAllNotificationsReadResponse> result = await dispatcher.SendAsync(
                new MarkAllNotificationsReadCommand(subject, CurrentScopeId(scopeContext)),
                cancellationToken).ConfigureAwait(false);

            return result.ToHttpResult(PublicErrorStatusCodes);
        })
            .RequireScope()
            .RequireNotificationUserScope();

        group.MapGet("/history/stream", async (
            long? afterSequence,
            HttpContext httpContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            INotificationStreamPulse streamPulse,
            ILogger<NotificationsModule> logger,
            IOptions<NotificationStreamOptions> streamOptions,
            INotificationUserScopeAuthorizer userScopeAuthorizer,
            CancellationToken cancellationToken) =>
        {
            if (!TryResolveUserContext(httpContext, out AccessSubject subject, out IResult? failure))
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

            NotificationStreamOptions options = streamOptions.Value;
            NotificationStreamAccessLease accessLease = NotificationStreamAccessLease.Create(
                options,
                httpContext.User,
                ResolveTimeProvider(httpContext));
            IResult stream = TypedResults.ServerSentEvents(
                StreamUserHistoryAsync(
                    dispatcher,
                    subject,
                    CurrentScopeId(scopeContext),
                    cursor,
                    options,
                    accessLease,
                    token => userScopeAuthorizer.AuthorizeAsync(
                        httpContext.User,
                        subject,
                        scopeContext,
                        token),
                    streamPulse,
                    logger,
                    httpContext.RequestAborted));
            return stream;
        })
            .RequireScope()
            .RequireNotificationUserScope()
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

    private static async IAsyncEnumerable<SseItem<object?>> StreamUserHistoryAsync(
        IRequestDispatcher dispatcher,
        AccessSubject subject,
        string? scopeId,
        long initialCursor,
        NotificationStreamOptions options,
        NotificationStreamAccessLease accessLease,
        Func<CancellationToken, Task<bool>> authorize,
        INotificationStreamPulse streamPulse,
        ILogger logger,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long afterSequence = initialCursor;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!await CanContinueStreamAsync(
                    accessLease,
                    authorize,
                    logger,
                    "history",
                    cancellationToken).ConfigureAwait(false))
            {
                yield break;
            }

            long observedVersion = streamPulse.CaptureVersion(NotificationStreamKind.History);
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
                yield return new SseItem<object?>(item, "notification");
            }

            if (result.Value.Count == options.BatchSize)
            {
                continue;
            }

            bool changed = await streamPulse.WaitForChangeAsync(
                    NotificationStreamKind.History,
                    observedVersion,
                    accessLease.LimitWaitInterval(options.HeartbeatInterval),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!changed)
            {
                if (!await CanContinueStreamAsync(
                        accessLease,
                        authorize,
                        logger,
                        "history",
                        cancellationToken).ConfigureAwait(false))
                {
                    yield break;
                }

                yield return new SseItem<object?>(null, "heartbeat");
            }
        }
    }

    private static async IAsyncEnumerable<SseItem<object?>> StreamBroadcastsAsync(
        IRequestDispatcher dispatcher,
        string? scopeId,
        NotificationBroadcastRecipientKind recipientKind,
        string recipientId,
        long initialCursor,
        NotificationStreamOptions options,
        NotificationStreamAccessLease accessLease,
        Func<CancellationToken, Task<bool>> authorize,
        INotificationStreamPulse streamPulse,
        ILogger logger,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long afterSequence = initialCursor;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!await CanContinueStreamAsync(
                    accessLease,
                    authorize,
                    logger,
                    "broadcast",
                    cancellationToken).ConfigureAwait(false))
            {
                yield break;
            }

            long observedVersion = streamPulse.CaptureVersion(NotificationStreamKind.Broadcasts);
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
                yield return new SseItem<object?>(item, "notification-broadcast");
            }

            if (result.Value.Count == options.BatchSize)
            {
                continue;
            }

            bool changed = await streamPulse.WaitForChangeAsync(
                    NotificationStreamKind.Broadcasts,
                    observedVersion,
                    accessLease.LimitWaitInterval(options.HeartbeatInterval),
                    cancellationToken)
                .ConfigureAwait(false);
            if (!changed)
            {
                if (!await CanContinueStreamAsync(
                        accessLease,
                        authorize,
                        logger,
                        "broadcast",
                        cancellationToken).ConfigureAwait(false))
                {
                    yield break;
                }

                yield return new SseItem<object?>(null, "heartbeat");
            }
        }
    }

    private static async Task<bool> CanContinueStreamAsync(
        NotificationStreamAccessLease accessLease,
        Func<CancellationToken, Task<bool>> authorize,
        ILogger logger,
        string streamName,
        CancellationToken cancellationToken)
    {
        try
        {
            NotificationStreamAccessOutcome outcome = await accessLease
                .EvaluateAsync(authorize, cancellationToken)
                .ConfigureAwait(false);
            if (outcome == NotificationStreamAccessOutcome.Active)
            {
                return true;
            }

            logger.LogDebug(
                "Notification {StreamName} stream access lease ended with {AccessOutcome}.",
                streamName,
                outcome);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Notification {StreamName} stream access revalidation failed with {ExceptionType}; the stream will be closed.",
                streamName,
                exception.GetType().Name);
            return false;
        }
    }

    private static bool TryResolveUserContext(
        HttpContext httpContext,
        out AccessSubject subject,
        out IResult? failure)
    {
        subject = null!;
        failure = null;

        if (!NotificationUserContext.TryResolveSubject(httpContext.User, out AccessSubject? resolvedSubject))
        {
            failure = Results.Unauthorized();
            return false;
        }

        subject = resolvedSubject;
        return true;
    }

    private static string? CurrentScopeId(IScopeContext scopeContext) =>
        scopeContext.ScopeId;

    private static string RequiredScopeId(IScopeContext scopeContext) =>
        scopeContext.ScopeId ?? string.Empty;

    private static TimeProvider ResolveTimeProvider(HttpContext httpContext) =>
        httpContext.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;

    private static void LogStreamQueryFailure(ILogger logger, string streamName, Error error)
    {
        logger.LogWarning(
            "Notification {StreamName} stream query failed and the stream will be closed. Error: {ErrorCode}.",
            streamName,
            error.Code);
    }

    private static readonly ApiErrorStatusCodeMap PublicErrorStatusCodes = ApiErrorStatusCodeMap.Create(
        new ApiErrorStatusCode(NotificationsApplicationErrors.NotificationNotFound.Code, StatusCodes.Status404NotFound),
        new ApiErrorStatusCode(NotificationsApplicationErrors.TagDefinitionNotFound.Code, StatusCodes.Status404NotFound),
        new ApiErrorStatusCode(NotificationsApplicationErrors.TagDefinitionInactive.Code, StatusCodes.Status409Conflict),
        new ApiErrorStatusCode(NotificationsApplicationErrors.BroadcastNotFound.Code, StatusCodes.Status404NotFound),
        new ApiErrorStatusCode(NotificationsApplicationErrors.AccessDenied.Code, StatusCodes.Status403Forbidden));
}
