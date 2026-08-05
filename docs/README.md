# Notifications Module

Production hardening work is tracked in [Notifications Production Hardening Task](notifications-production-hardening-task.md).
The reusable reference lifecycle is specified in [Notification History Lifecycle Task](notification-history-lifecycle-task.md).
Large-history and generic scope closure are tracked in
[Notification Lifecycle Scaling Task](notification-lifecycle-scaling-task.md).

The optional Notifications module owns durable, addressed user notifications. It stores inbox history and read state, persists a tenant-scoped tag catalog and user preferences, plans durable adapter deliveries, records immutable attempts/receipts, and exposes user and operator APIs. It also owns durable audience broadcasts.

Notifications are not a backend event bus or an authorization engine. Producer modules decide the recipient and semantic intent. Integration events and the source module outbox remain the authoritative durable business path.

## Projects

```text
Gma.Modules.Notifications.Contracts
Gma.Modules.Notifications.Domain
Gma.Modules.Notifications.Application
Gma.Modules.Notifications.Persistence
Gma.Modules.Notifications.Persistence.SqlServerMigrations
Gma.Modules.Notifications.Persistence.PostgreSqlMigrations
Gma.Modules.Notifications.Api
Gma.Modules.Notifications.Admin.Contracts
Gma.Modules.Notifications.AdminApi
Gma.Modules.Notifications.AdminCli
Gma.Modules.Notifications.Adapters.Email
```

`Gma.Modules.Notifications.AdminCli` is a composition-only adapter. It wires
the module for hosts that need Notifications-owned persistence but does not
claim operator commands that the generic module does not provide.

`NotificationsProfiles.Default` provides the `history`, `broadcasts`, `preferences`, `routing`, and `durable-delivery` composition features and requires scope context. Applications explicitly select the API/admin surfaces and any delivery adapters they need. Realtime SSE/SignalR remains a separately composed, best-effort framework concern.

## Tagged Notification Contract

New producers should publish `UserNotificationRequestedIntegrationEventV2`. It preserves the V1 event name with contract version `2` and adds:

- typed tags;
- `respect-preferences` or `mandatory` delivery policy;
- stable lowercase string JSON representations.

Tags have two deliberately separate namespaces:

- `delivery:*` selects an inbox or delivery pipeline, for example `delivery:web`, `delivery:email`, `delivery:push`, or `delivery:sms`;
- `domain:*` describes product meaning, for example `domain:security`, `domain:order-updates`, or `domain:marketing`.

Every request needs at least one delivery tag. A V2 request with an empty tag collection defaults to `delivery:web`. Tag keys are canonical, deduplicated, bounded, and cannot be declared with conflicting kinds.

```csharp
new UserNotificationRequestedIntegrationEventV2(
    eventId,
    scopeId,
    occurredAtUtc,
    userId,
    "ordering",
    "ordering.order-updated",
    1,
    "Order updated",
    body: null,
    NotificationSeverity.Info,
    payloadJson,
    [
        new NotificationTag("delivery:web", NotificationTagKind.Delivery),
        new NotificationTag("delivery:email", NotificationTagKind.Delivery),
        new NotificationTag("domain:order-updates", NotificationTagKind.Domain)
    ],
    NotificationDeliveryPolicy.RespectPreferences);
```

The first valid V2 request registers missing definitions with safe defaults. Known system delivery tags are owned by Notifications; other tags are attributed to their producer module. Operators can create/update definitions and deactivate tags. Deactivation fails closed for new delivery planning, including `mandatory` requests; mandatory bypasses user preferences, not operator safety controls.

V1 requests remain consumable. They are projected as web-inbox history and receive a delivered `delivery:web` audit row. Producers should migrate to V2 for tags, preferences, and adapter delivery.

## Preferences And Routing

Preferences are tenant- and user-scoped. Missing preferences mean enabled. Under `respect-preferences`:

- disabling a domain tag suppresses every delivery route on that notification;
- disabling a delivery tag suppresses only that pipeline;
- a replaceable `INotificationPreferenceEvaluator` can add product-local policy without coupling to another module database.

`mandatory` bypasses stored/user preference suppression for security or legal notices. Producers, not operators, choose that policy in the durable contract.

Routes map a delivery tag to one active adapter provider. If no explicit route exists and exactly one compatible durable provider is registered, that provider is selected. Zero providers produces an auditable `unroutable` job; multiple providers without a route produce `route-ambiguous`. This prevents configuration order from silently selecting a vendor.

## Durable Delivery Guarantees

The projector writes notification history, tags, and all planned delivery jobs in the Notifications unit of work. Delivery is then:

- at least once;
- claimed in bounded batches with provider-specific skip-locked row leases on PostgreSQL and SQL Server;
- protected by expiring worker leases;
- executed with bounded concurrency;
- retried with bounded exponential backoff;
- terminal as `delivered`, `rejected`, `exhausted`, `suppressed`, or `unroutable`;
- auditable through immutable attempt rows and optional provider receipt ids.

The database prevents duplicate plans for the same notification, delivery tag, and provider. Adapters receive the stable delivery id and must use it as their provider idempotency key. The included email adapter sends `notification:{deliveryId}` through the shared email transport abstraction.

Adapter exception messages are logged only by exception type and are never stored. Persisted attempt codes are bounded semantic codes. Notification content, tenant/user ids, destinations, and payload fields are not metric dimensions.

Operators may retry `rejected`, `exhausted`, and `unroutable` jobs after repairing a provider or route. A retry resets the job to pending while retaining the previous immutable attempt history.

`BatchSize` bounds one healthy drain cycle; `MaxConcurrency` bounds each in-process delivery wave. A full cycle immediately starts another cycle without waiting for the poll interval. Multiple replicas lease disjoint rows, and expired leases can be reclaimed after a worker exits. An adapter-provided retry timestamp is clamped to `RetryMaxMinutes` so a provider cannot accidentally strand work beyond the host's configured recovery bound.

## Email Adapter And PII Boundary

`Gma.Modules.Notifications.Adapters.Email` is optional and disabled by default. It depends on two application-owned seams:

- `IUserNotificationEmailAddressResolver` resolves a destination from the full notification message at attempt time;
- `Gma.Framework.Email.IEmailSender` sends through the chosen provider.

Notifications persistence never becomes an email-address directory or a credential vault. The resolver can read the product's profile/account model; the email sender adapter owns vendor credentials. Replace `IUserNotificationEmailRenderer` when product templates are required; the default renderer is plain text.

```csharp
builder.Services.AddSingleton<IUserNotificationEmailAddressResolver, ProductEmailAddressResolver>();
builder.Services.AddSingleton<IEmailSender, ProductEmailSender>();
builder.Services.AddNotificationEmailAdapter(builder.Configuration);
```

```json
{
  "Notifications": {
    "Adapters": {
      "Email": {
        "Enabled": true,
        "ProviderName": "email-primary",
        "SenderAddress": "notifications@example.com",
        "SenderName": "Example",
        "SubjectPrefix": "[Example]"
      }
    }
  }
}
```

Other pipelines implement `IUserNotificationSink`, declare supported `delivery:*` tags, and set `DeliveryModes` to `Durable`. Best-effort live sinks set `BestEffort`; a sink can opt into both explicitly. This prevents the publisher and durable worker from double-sending through the same adapter accidentally.

### Cross-module integrations

Notifications exposes contracts, the request projector, and adapter seams for optional product integrations. Reusable bridges that know about another module belong in a composition-owned extensions repository, not in this module. `Gma.Extensions.Auth.Notifications`, for example, can map Auth events into mandatory security notifications without making either module depend on the other.

Version 3 addressed-notification requests may carry up to eight producer-owned
history references. Each reference is a bounded namespace plus a SHA-256 digest
of a versioned canonical coordinate. The digest is pseudonymous, not
anonymised: producers define a high-entropy opaque coordinate and must not
place raw identifiers, names, addresses, booking references, or free text in
the reference.

`INotificationHistoryLifecycle` is an in-process application boundary for
authorized product adapters. It can prepare one empty versioned reference,
read a bounded exact-reference snapshot or page, and close that reference with
an idempotent operation id. `CloseAsync` remains an atomic operation for small
histories. `CloseBatchAsync` closes the tombstone on its first accepted call and
then removes large histories over restart-safe bounded batches, recording
durable progress until it replaces that progress with an immutable completion
receipt. Both modes refuse active delivery leases, advance companion reference
versions, and prevent later projection from recreating the addressed history.
There is deliberately no user or admin endpoint for this surface: products own
authorization, legal decisions, subject discovery, export assembly, and
restore orchestration.

Notifications also owns a monotonic state for every mutated tenant scope. The
state advances once per Notifications unit of work and is checked by tracked
writes, inbox handling, set-based maintenance, raw receipt writes, retention,
and delivery claiming. Closing that state suppresses late inbox messages and
prevents background work from recreating or externally delivering closed-scope
data. Global broadcasts remain outside tenant scope lifecycle. The state is a
module-local consistency boundary, not a framework tenant registry or a public
administration API.

`INotificationScopeLifecycle` exposes a read-only, in-process scope snapshot and
typed export pages for product composition. Pages use stable keyset cursors,
are capped at 200 records, and must match one selected scope revision. The
export covers tenant notification history, preferences, routing/tag
configuration, delivery and attempt state, tenant broadcasts and reads, and
reference lifecycle proof. It never exports module inbox rows or interprets
opaque producer payload JSON. Products remain responsible for authorization,
artifact schemas, export sinks, and final revision verification.

The same application port offers resumable scope destruction as a separate
operation. Its first accepted call closes the module-owned scope tombstone;
later calls remove one bounded batch through inbox, tenant broadcast,
configuration, and notification aggregate stages. Active delivery leases or an
exact-reference close already in progress return a retryable busy status. The
terminal tombstone, append-only payload-free scope receipt, and existing
reference lifecycle proof are deliberately retained. Products decide when
export is accepted and when destruction may begin.

## User API

All user endpoints require authentication and scope context. Tenant claims must match the active scope.

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/notifications/preferences` | List configured tags with the current user's effective preference. |
| `PUT` | `/api/notifications/preferences/{tagKey}` | Enable or disable one active tag for the current user. |
| `GET` | `/api/notifications` | List visible web-inbox history. |
| `GET` | `/api/notifications/{notificationId}` | Get one visible notification. |
| `POST` | `/api/notifications/{notificationId}/read` | Mark one notification read. |
| `POST` | `/api/notifications/read-all` | Mark all visible notifications read. |
| `GET` | `/api/notifications/history/stream` | Stream newly committed web-inbox rows. |
| `GET` | `/api/notifications/broadcasts` | List visible broadcasts. |
| `GET` | `/api/notifications/broadcasts/{broadcastId}` | Get one visible broadcast. |
| `POST` | `/api/notifications/broadcasts/{broadcastId}/read` | Mark one broadcast read. |
| `POST` | `/api/notifications/broadcasts/read-all` | Mark all visible broadcasts read. |
| `GET` | `/api/notifications/broadcasts/stream` | Stream newly committed broadcasts. |

History exposes only rows whose V2 or V3 plan made `delivery:web` visible. Suppressed email-only jobs do not leak into the web inbox.

Durable SSE streams use one database sequence-head monitor per process, rather than one database polling loop per connection. The monitor wakes only the affected stream kind, full result batches drain immediately, and an idle connection receives a `heartbeat` event with `null` data. Heartbeat timeout also performs a fallback query, so a stream recovers if the monitor temporarily cannot reach the database. This is process-local coordination, not a multi-region backplane; each application replica runs its own monitor.

`Notifications:DurableStreams:MonitorEnabled` defaults to `true`. Hosts that start the API only for metadata generation or another database-independent task may set it to `false`; stream endpoints and heartbeat fallback remain available, but connected clients detect new rows only on the heartbeat interval. Production serving processes should keep the monitor enabled.

## Admin API

Admin endpoints use the shared audited executor and scoped RBAC permissions.

| Method | Route | Permission |
| --- | --- | --- |
| `GET` | `/api/admin/notifications/tags` | `notifications.configuration.read` |
| `POST` | `/api/admin/notifications/tags` | `notifications.configuration.write` |
| `PUT` | `/api/admin/notifications/tags/{tagKey}` | `notifications.configuration.write` |
| `GET` | `/api/admin/notifications/routes` | `notifications.configuration.read` |
| `PUT` | `/api/admin/notifications/routes/{deliveryTag}` | `notifications.configuration.write` |
| `GET` | `/api/admin/notifications/deliveries` | `notifications.deliveries.read` |
| `GET` | `/api/admin/notifications/deliveries/{deliveryId}` | `notifications.deliveries.read` |
| `POST` | `/api/admin/notifications/deliveries/{deliveryId}/retry` | `notifications.deliveries.retry` |
| `GET` | `/api/admin/notifications` | `notifications.history.read` |
| `GET` | `/api/admin/notifications/{notificationId}` | `notifications.history.read` |
| `GET/POST` | `/api/admin/notifications/broadcasts` | broadcast read/create permission |
| `GET/POST` | `/api/admin/notifications/platform-broadcasts` | global broadcast read/create grant |

Delivery lists support `status`, `userId`, `deliveryTag`, `page`, and `pageSize`. Responses use typed string contract values for provider, status, outcome, origin, tag kind, and policy.

## Persistence And Migration

The `notifications` schema owns:

- `user_notifications`, `user_notification_tags`, and indexed
  `user_notification_references`;
- `notification_history_reference_states` and append-only
  `notification_history_close_receipts`;
- mutable in-progress `notification_history_batch_close_operations` and
  append-only `notification_history_batch_close_receipts`;
- `tag_definitions` and `preferences`;
- `delivery_routes`, `deliveries`, and `delivery_attempts`;
- `notification_broadcasts` and recipient read receipts;
- `inbox_messages` for idempotent integration-event consumption.

SQL Server and PostgreSQL have provider-specific migrations. The V2 migration backfills legacy notification rows with `delivery:web` and `respect-preferences`, preserving existing inbox behavior. Run the selected provider migrations before enabling the module.

Retention is disabled until the product chooses policy. When enabled, cleanup
is bounded, includes old attempt rows according to
`Notifications:Delivery:AttemptRetentionDays`, and advances every affected
history-reference version before deleting notification content. Open
zero-record reference state is retained so an old frozen version cannot become
valid again after recreation. Closed state and close receipts are also retained
to keep replay suppression and restore proof intact. A future compaction policy
must therefore account for outstanding product decisions and backup expiry; it
cannot be a blind row-age cleanup.

Notification titles, bodies, payload JSON, recipient ids, delivery destinations resolved by adapters, and provider receipts may be sensitive operational or personal data. Products must minimize producer payloads, select retention values, configure database/backups/log encryption and access controls, and verify any legal deletion requirements. The reusable module cannot select those policies for every host. Provider adapters must honor the stable delivery id as an idempotency key and must reject time-limited work after the producer-specific expiry encoded in the message or resolved by a composition-owned bridge.

```json
{
  "Notifications": {
    "Delivery": {
      "Enabled": true,
      "BatchSize": 50,
      "MaxConcurrency": 8,
      "PollIntervalSeconds": 5,
      "LeaseSeconds": 60,
      "MaxAttempts": 8,
      "RetryBaseSeconds": 5,
      "RetryMaxMinutes": 30,
      "AttemptRetentionDays": 90
    },
    "Retention": {
      "Enabled": false,
      "ReadHistoryDays": 90,
      "UnreadHistoryDays": 365,
      "BroadcastDays": 365,
      "BatchSize": 500,
      "MaxBatchesPerCategoryPerCycle": 4,
      "IntervalMinutes": 60
    },
    "DurableStreams": {
      "MonitorEnabled": true,
      "BatchSize": 25,
      "PollInterval": "00:00:01",
      "HeartbeatInterval": "00:00:15"
    }
  }
}
```

## Composition

```csharp
builder.AddModule<NotificationsModule>();
builder.AddAdminApiModule<NotificationsAdminApiModule>();
builder.Services.AddUserNotificationRequestSubscription(OrderingModuleMetadata.Name);
```

Producer subscriptions are explicit and producer-scoped. V1, V2, and V3 have
distinct durable consumer bindings. The physical V3 subject is:

```text
{application-namespace}.{producer-module}.user-notification-requested.v3
```

The in-process `IUserNotificationHistoryWriter` also projects through the V2 planner, so direct runtime publishing and outbox-driven ingestion use the same tag/preference/routing rules.
The module also contributes an `IUserNotificationDeliveryPolicyEvaluator`, so best-effort web sinks consult the persisted plan before sending. A suppressed or missing plan fails closed instead of bypassing a user's tag preferences during live delivery.

## Boundaries

- Producers reference only `Gma.Modules.Notifications.Contracts` and decide recipients, tags, policy, and safe content.
- Products compose adapters; producer modules never reference adapter, application, domain, persistence, API, or admin projects.
- Notifications persistence does not query another module's tables or store provider secrets/destination addresses. Composition-owned resolvers may call narrow public contract readers at delivery time.
- Durable business decisions use source-module state and integration events, never notification history or delivery receipts.
- Live SSE/SignalR is optional and best effort; durable inbox and adapter jobs do not depend on a realtime backplane.
- Multi-instance deployments must size database connections, lease settings, and provider limits, and must alert on pending age/exhausted counts.
