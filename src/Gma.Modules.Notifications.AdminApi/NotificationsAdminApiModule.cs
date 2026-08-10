namespace Gma.Modules.Notifications.AdminApi;

using System.Net.ServerSentEvents;
using Gma.Framework.Administration;
using Gma.Framework.Administration.Api;
using Gma.Framework.Api.Observability;
using Gma.Framework.Api.Results;
using Gma.Framework.Cqrs;
using Gma.Framework.ModuleComposition;
using Gma.Framework.Pagination;
using Gma.Framework.Results;
using Gma.Framework.Scoping;
using Gma.Modules.Notifications.Admin.Contracts;
using Gma.Modules.Notifications.Application;
using Gma.Modules.Notifications.Application.Commands;
using Gma.Modules.Notifications.Application.Ports;
using Gma.Modules.Notifications.Application.Queries;
using Gma.Modules.Notifications.Contracts;
using Gma.Modules.Notifications.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class NotificationsAdminApiModule : IAdminApiModule
{
    public string Name => NotificationsModuleMetadata.Name;

    public void AddServices(IHostApplicationBuilder builder)
    {
        builder.SelectModuleProfile(NotificationsProfiles.Default, "Gma.Modules.Notifications.AdminApi");
        builder.Services.AddNotificationsApplication(builder.Configuration);
        builder.AddNotificationsPersistence();
        builder.AddNotificationsDurableStreams();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder history = endpoints.MapGroup("/api/admin/notifications")
            .WithModuleName(this.Name)
            .WithTags("Notifications Admin")
            .RequireAuthorization();

        history.MapGet("/tags", async (
            bool? activeOnly,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(NotificationsAdminOperationNames.TagsList, NotificationsAdminPermissions.ConfigurationRead),
                requireTenant: true,
                token => dispatcher.QueryAsync(
                    new ListNotificationTagDefinitionsQuery(activeOnly ?? false),
                    token),
                cancellationToken).ConfigureAwait(false));

        history.MapPost("/tags", async (
            AdminCreateNotificationTagDefinitionRequest request,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IAdminActorContext actorContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(NotificationsAdminOperationNames.TagsCreate, NotificationsAdminPermissions.ConfigurationWrite),
                requireTenant: true,
                token => dispatcher.SendAsync(
                    new CreateNotificationTagDefinitionCommand(
                        RequiredScopeId(scopeContext),
                        request.Key,
                        request.Kind,
                        request.DisplayName,
                        request.Description,
                        RequiredActorId(actorContext)),
                    token),
                cancellationToken,
                errorStatusCodes: AdminErrorStatusCodes).ConfigureAwait(false));

        history.MapPut("/tags/{tagKey}", async (
            string tagKey,
            AdminUpdateNotificationTagDefinitionRequest request,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IAdminActorContext actorContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(NotificationsAdminOperationNames.TagsUpdate, NotificationsAdminPermissions.ConfigurationWrite),
                requireTenant: true,
                token => dispatcher.SendAsync(
                    new UpdateNotificationTagDefinitionCommand(
                        tagKey,
                        request.DisplayName,
                        request.Description,
                        request.IsActive,
                        RequiredActorId(actorContext)),
                    token),
                cancellationToken,
                errorStatusCodes: AdminErrorStatusCodes).ConfigureAwait(false));

        history.MapGet("/routes", async (
            HttpContext httpContext,
            AdminApiExecutor executor,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(NotificationsAdminOperationNames.RoutesList, NotificationsAdminPermissions.ConfigurationRead),
                requireTenant: true,
                token => dispatcher.QueryAsync(new ListNotificationDeliveryRoutesQuery(), token),
                cancellationToken).ConfigureAwait(false));

        history.MapPut("/routes/{deliveryTag}", async (
            string deliveryTag,
            AdminSetNotificationDeliveryRouteRequest request,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IAdminActorContext actorContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(NotificationsAdminOperationNames.RoutesSet, NotificationsAdminPermissions.ConfigurationWrite),
                requireTenant: true,
                token => dispatcher.SendAsync(
                    new SetNotificationDeliveryRouteCommand(
                        RequiredScopeId(scopeContext),
                        deliveryTag,
                        request.Provider,
                        request.IsActive,
                        RequiredActorId(actorContext)),
                    token),
                cancellationToken,
                errorStatusCodes: AdminErrorStatusCodes).ConfigureAwait(false));

        history.MapGet("/deliveries", async (
            string? status,
            string? userId,
            string? deliveryTag,
            int? page,
            int? pageSize,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(NotificationsAdminOperationNames.DeliveriesList, NotificationsAdminPermissions.DeliveriesRead),
                requireTenant: true,
                token => dispatcher.QueryAsync(
                    new ListNotificationDeliveriesQuery(
                        ParseDeliveryStatus(status),
                        userId,
                        deliveryTag,
                        page ?? PageRequest.DefaultPage,
                        pageSize ?? PageRequest.DefaultPageSize),
                    token),
                cancellationToken,
                errorStatusCodes: AdminErrorStatusCodes).ConfigureAwait(false));

        history.MapGet("/deliveries/{deliveryId:guid}", async (
            Guid deliveryId,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(NotificationsAdminOperationNames.DeliveriesGet, NotificationsAdminPermissions.DeliveriesRead),
                requireTenant: true,
                token => dispatcher.QueryAsync(new GetNotificationDeliveryQuery(deliveryId), token),
                cancellationToken,
                errorStatusCodes: AdminErrorStatusCodes).ConfigureAwait(false));

        history.MapPost("/deliveries/{deliveryId:guid}/retry", async (
            Guid deliveryId,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(NotificationsAdminOperationNames.DeliveriesRetry, NotificationsAdminPermissions.DeliveriesRetry),
                requireTenant: true,
                token => dispatcher.SendAsync(new RetryNotificationDeliveryCommand(deliveryId), token),
                cancellationToken,
                onSuccess: _ => Results.NoContent(),
                errorStatusCodes: AdminErrorStatusCodes).ConfigureAwait(false));

        history.MapGet("/", async (
            string? userId,
            bool? unreadOnly,
            int? page,
            int? pageSize,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(NotificationsAdminOperationNames.HistoryList, NotificationsAdminPermissions.HistoryRead),
                requireTenant: true,
                token => dispatcher.QueryAsync(
                    new ListTenantNotificationHistoryQuery(
                        userId,
                        unreadOnly ?? false,
                        page ?? PageRequest.DefaultPage,
                        pageSize ?? PageRequest.DefaultPageSize),
                    token),
                cancellationToken).ConfigureAwait(false));

        history.MapGet("/{notificationId:guid}", async (
            Guid notificationId,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(NotificationsAdminOperationNames.HistoryGet, NotificationsAdminPermissions.HistoryRead),
                requireTenant: true,
                token => dispatcher.QueryAsync(new GetTenantNotificationHistoryItemQuery(notificationId), token),
                cancellationToken,
                errorStatusCodes: AdminErrorStatusCodes).ConfigureAwait(false));

        history.MapGet("/broadcasts", async (
            int? page,
            int? pageSize,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(NotificationsAdminOperationNames.BroadcastsList, NotificationsAdminPermissions.BroadcastsRead),
                requireTenant: true,
                token => dispatcher.QueryAsync(
                    new ListTenantNotificationBroadcastsQuery(
                        RequiredScopeId(scopeContext),
                        page ?? PageRequest.DefaultPage,
                        pageSize ?? PageRequest.DefaultPageSize),
                    token),
                cancellationToken).ConfigureAwait(false));

        history.MapPost("/broadcasts", async (
            AdminCreateNotificationBroadcastRequest request,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(NotificationsAdminOperationNames.BroadcastsCreate, NotificationsAdminPermissions.BroadcastsCreate),
                requireTenant: true,
                token => dispatcher.SendAsync(
                    new CreateNotificationBroadcastCommand(
                        request.Audience,
                        RequiredScopeId(scopeContext),
                        NotificationsModuleMetadata.Name,
                        request.Name,
                        request.Version,
                        request.Title,
                        request.Body,
                        request.Severity,
                        request.OccurredAtUtc,
                        PayloadJson(request)),
                    token),
                cancellationToken).ConfigureAwait(false));

        history.MapGet("/platform-broadcasts", async (
            int? page,
            int? pageSize,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(NotificationsAdminOperationNames.BroadcastsList, NotificationsAdminPermissions.BroadcastsRead),
                requireTenant: false,
                token => dispatcher.QueryAsync(
                    new ListPlatformNotificationBroadcastsQuery(
                        page ?? PageRequest.DefaultPage,
                        pageSize ?? PageRequest.DefaultPageSize),
                    token),
                cancellationToken).ConfigureAwait(false));

        history.MapPost("/platform-broadcasts", async (
            AdminCreateNotificationBroadcastRequest request,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(NotificationsAdminOperationNames.BroadcastsCreate, NotificationsAdminPermissions.BroadcastsCreate),
                requireTenant: false,
                token => dispatcher.SendAsync(
                    new CreateNotificationBroadcastCommand(
                        request.Audience,
                        null,
                        NotificationsModuleMetadata.Name,
                        request.Name,
                        request.Version,
                        request.Title,
                        request.Body,
                        request.Severity,
                        request.OccurredAtUtc,
                        PayloadJson(request)),
                    token),
                cancellationToken).ConfigureAwait(false));

        history.MapGet("/broadcasts/inbox", async (
            bool? unreadOnly,
            int? page,
            int? pageSize,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IAdminActorContext actorContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(NotificationsAdminOperationNames.BroadcastsInboxList, NotificationsAdminPermissions.BroadcastsRead),
                requireTenant: true,
                token => dispatcher.QueryAsync(
                    new ListNotificationBroadcastsQuery(
                        RequiredScopeId(scopeContext),
                        NotificationBroadcastRecipientKind.Admin,
                        RequiredActorId(actorContext),
                        unreadOnly ?? false,
                        page ?? PageRequest.DefaultPage,
                        pageSize ?? PageRequest.DefaultPageSize),
                    token),
                cancellationToken).ConfigureAwait(false));

        history.MapPost("/broadcasts/inbox/{broadcastId:guid}/read", async (
            Guid broadcastId,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IAdminActorContext actorContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(NotificationsAdminOperationNames.BroadcastsInboxMarkRead, NotificationsAdminPermissions.BroadcastsRead),
                requireTenant: true,
                token => dispatcher.SendAsync(
                    new MarkNotificationBroadcastReadCommand(
                        broadcastId,
                        RequiredScopeId(scopeContext),
                        NotificationBroadcastRecipientKind.Admin,
                        RequiredActorId(actorContext)),
                    token),
                cancellationToken,
                onSuccess: _ => Results.NoContent(),
                errorStatusCodes: AdminErrorStatusCodes).ConfigureAwait(false));

        history.MapPost("/broadcasts/inbox/read-all", async (
            HttpContext httpContext,
            AdminApiExecutor executor,
            IAdminActorContext actorContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            CancellationToken cancellationToken) =>
            await executor.ExecuteAsync(
                httpContext,
                AdminOperation.Create(NotificationsAdminOperationNames.BroadcastsInboxMarkRead, NotificationsAdminPermissions.BroadcastsRead),
                requireTenant: true,
                token => dispatcher.SendAsync(
                    new MarkAllNotificationBroadcastsReadCommand(
                        RequiredScopeId(scopeContext),
                        NotificationBroadcastRecipientKind.Admin,
                        RequiredActorId(actorContext)),
                    token),
                cancellationToken).ConfigureAwait(false));

        history.MapGet("/broadcasts/inbox/stream", async (
            long? afterSequence,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IAdminAuthorizationService authorization,
            IAdminActorContext actorContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            INotificationStreamPulse streamPulse,
            ILogger<NotificationsAdminApiModule> logger,
            IOptions<NotificationStreamOptions> streamOptions,
            CancellationToken cancellationToken) =>
        {
            AdminOperation operation = AdminOperation.Create(
                NotificationsAdminOperationNames.BroadcastsInboxStream,
                NotificationsAdminPermissions.BroadcastsRead);
            return await executor.ExecuteAsync(
                httpContext,
                operation,
                requireTenant: true,
                async token =>
                {
                    if (afterSequence is < 0)
                    {
                        return Result.Failure<IResult>(NotificationsApplicationErrors.StreamCursorInvalid);
                    }

                    string scopeId = RequiredScopeId(scopeContext);
                    AdminActor actor = RequiredActor(actorContext);
                    string actorId = actor.Id;
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
                            NotificationBroadcastRecipientKind.Admin,
                            actorId,
                            token).ConfigureAwait(false);
                        if (cursorResult.IsFailure)
                        {
                            return Result.Failure<IResult>(cursorResult.Error);
                        }

                        cursor = cursorResult.Value;
                    }

                    NotificationStreamOptions options = streamOptions.Value;
                    NotificationStreamAccessLease accessLease = NotificationStreamAccessLease.Create(
                        options,
                        httpContext.User,
                        ResolveTimeProvider(httpContext));
                    return Result.Success<IResult>(TypedResults.ServerSentEvents(
                        StreamBroadcastsAsync(
                            dispatcher,
                            scopeId,
                            NotificationBroadcastRecipientKind.Admin,
                            actorId,
                            cursor,
                            options,
                            accessLease,
                            token => IsAdminAuthorizedAsync(
                                authorization,
                                actor,
                                operation,
                                scopeId,
                                token),
                            streamPulse,
                            logger,
                            httpContext.RequestAborted)));
                },
                cancellationToken,
                onSuccess: result => result).ConfigureAwait(false);
        });

        history.MapGet("/history/stream", async (
            long? afterSequence,
            string? userId,
            HttpContext httpContext,
            AdminApiExecutor executor,
            IAdminAuthorizationService authorization,
            IAdminActorContext actorContext,
            IScopeContext scopeContext,
            IRequestDispatcher dispatcher,
            INotificationStreamPulse streamPulse,
            ILogger<NotificationsAdminApiModule> logger,
            IOptions<NotificationStreamOptions> streamOptions,
            CancellationToken cancellationToken) =>
        {
            AdminOperation operation = AdminOperation.Create(
                NotificationsAdminOperationNames.HistoryStream,
                NotificationsAdminPermissions.HistoryRead);
            return await executor.ExecuteAsync(
                httpContext,
                operation,
                requireTenant: true,
                async token =>
                {
                    if (afterSequence is < 0)
                    {
                        return Result.Failure<IResult>(NotificationsApplicationErrors.StreamCursorInvalid);
                    }

                    long cursor;
                    if (afterSequence.HasValue)
                    {
                        cursor = afterSequence.Value;
                    }
                    else
                    {
                        Result<long> cursorResult = await ResolveCurrentTenantCursorAsync(
                            dispatcher,
                            userId,
                            token).ConfigureAwait(false);
                        if (cursorResult.IsFailure)
                        {
                            return Result.Failure<IResult>(cursorResult.Error);
                        }

                        cursor = cursorResult.Value;
                    }

                    string scopeId = RequiredScopeId(scopeContext);
                    AdminActor actor = RequiredActor(actorContext);
                    NotificationStreamOptions options = streamOptions.Value;
                    NotificationStreamAccessLease accessLease = NotificationStreamAccessLease.Create(
                        options,
                        httpContext.User,
                        ResolveTimeProvider(httpContext));
                    return Result.Success<IResult>(TypedResults.ServerSentEvents(
                        StreamTenantHistoryAsync(
                            dispatcher,
                            userId,
                            cursor,
                            options,
                            accessLease,
                            token => IsAdminAuthorizedAsync(
                                authorization,
                                actor,
                                operation,
                                scopeId,
                                token),
                            streamPulse,
                            logger,
                            httpContext.RequestAborted)));
                },
                cancellationToken,
                onSuccess: result => result).ConfigureAwait(false);
        });
    }

    private static Task<Result<long>> ResolveCurrentTenantCursorAsync(
        IRequestDispatcher dispatcher,
        string? userId,
        CancellationToken cancellationToken) =>
        dispatcher.QueryAsync(
            new GetTenantNotificationStreamCursorQuery(userId),
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

    private static async IAsyncEnumerable<SseItem<object?>> StreamTenantHistoryAsync(
        IRequestDispatcher dispatcher,
        string? userId,
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
                    "admin-history",
                    cancellationToken).ConfigureAwait(false))
            {
                yield break;
            }

            long observedVersion = streamPulse.CaptureVersion(NotificationStreamKind.History);
            Result<IReadOnlyList<AdminNotificationHistoryItem>> result = await dispatcher.QueryAsync(
                new StreamTenantNotificationHistoryQuery(userId, afterSequence, options.BatchSize),
                cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                LogStreamQueryFailure(logger, "admin-history", result.Error);
                yield break;
            }

            foreach (AdminNotificationHistoryItem item in result.Value)
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
                        "admin-history",
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
                    "admin-broadcast",
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
                LogStreamQueryFailure(logger, "admin-broadcast", result.Error);
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
                        "admin-broadcast",
                        cancellationToken).ConfigureAwait(false))
                {
                    yield break;
                }

                yield return new SseItem<object?>(null, "heartbeat");
            }
        }
    }

    private static async Task<bool> IsAdminAuthorizedAsync(
        IAdminAuthorizationService authorization,
        AdminActor actor,
        AdminOperation operation,
        string scopeId,
        CancellationToken cancellationToken)
    {
        AdminAuthorizationResult result = await authorization.AuthorizeAsync(
                actor,
                operation.Permission,
                scopeId,
                cancellationToken)
            .ConfigureAwait(false);
        return result.IsAuthorized;
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

    private static string RequiredScopeId(IScopeContext scopeContext) =>
        scopeContext.ScopeId ?? string.Empty;

    private static AdminActor RequiredActor(IAdminActorContext actorContext) =>
        actorContext.Actor ?? throw new InvalidOperationException("The admin actor context is unavailable.");

    private static string RequiredActorId(IAdminActorContext actorContext) =>
        actorContext.Actor?.Id ?? string.Empty;

    private static TimeProvider ResolveTimeProvider(HttpContext httpContext) =>
        httpContext.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;

    private static string PayloadJson(AdminCreateNotificationBroadcastRequest request) =>
        request.Payload?.GetRawText() ?? "{}";

    private static NotificationDeliveryStatus? ParseDeliveryStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "pending" => NotificationDeliveryStatus.Pending,
            "processing" => NotificationDeliveryStatus.Processing,
            "retry-scheduled" => NotificationDeliveryStatus.RetryScheduled,
            "delivered" => NotificationDeliveryStatus.Delivered,
            "rejected" => NotificationDeliveryStatus.Rejected,
            "exhausted" => NotificationDeliveryStatus.Exhausted,
            "suppressed" => NotificationDeliveryStatus.Suppressed,
            "unroutable" => NotificationDeliveryStatus.Unroutable,
            _ => NotificationDeliveryStatus.Unknown
        };
    }

    private static void LogStreamQueryFailure(ILogger logger, string streamName, Error error)
    {
        logger.LogWarning(
            "Notification {StreamName} stream query failed and the stream will be closed. Error: {ErrorCode}.",
            streamName,
            error.Code);
    }

    private static readonly ApiErrorStatusCodeMap AdminErrorStatusCodes = ApiErrorStatusCodeMap.Create(
        new ApiErrorStatusCode(NotificationsApplicationErrors.NotificationNotFound.Code, StatusCodes.Status404NotFound),
        new ApiErrorStatusCode(NotificationsApplicationErrors.TagDefinitionNotFound.Code, StatusCodes.Status404NotFound),
        new ApiErrorStatusCode(NotificationsApplicationErrors.DeliveryNotFound.Code, StatusCodes.Status404NotFound),
        new ApiErrorStatusCode(NotificationsApplicationErrors.TagDefinitionAlreadyExists.Code, StatusCodes.Status409Conflict),
        new ApiErrorStatusCode(NotificationsApplicationErrors.TagDefinitionInactive.Code, StatusCodes.Status409Conflict),
        new ApiErrorStatusCode(NotificationsApplicationErrors.DeliveryProviderUnsupported.Code, StatusCodes.Status409Conflict),
        new ApiErrorStatusCode(NotificationsApplicationErrors.BroadcastNotFound.Code, StatusCodes.Status404NotFound));
}
