# Notification Stream Access Lease Task

Status: complete
Date: 2026-08-10

## Goal

Bound every durable notification SSE connection by current authorization and
authentication lifetime without turning stream traffic into connection-scaled
database polling or coupling Notifications to a product membership model.

## Audit Findings

1. User history and broadcast endpoints authorize only while opening the
   response. A revoked workspace membership does not close an existing stream.
2. Admin history and broadcast endpoints similarly run the audited admin
   operation once, so a removed permission remains effective until disconnect.
3. Neither surface closes at the authenticated principal's expiry, and both can
   remain connected indefinitely when the client and proxy keep the socket alive.
4. BunkFy's workspace authorizer checks three compatible membership roles with
   separate queries. Repeating that pattern for two streams would make access
   revalidation unnecessarily expensive.

## Ownership Boundary

- Notifications Application owns access-lease timing, bounded connection
  lifetime, authentication-expiry capping, and host configuration.
- Notifications API owns periodic invocation of the replaceable
  `INotificationUserScopeAuthorizer`; it does not know workspace membership.
- Notifications Admin API owns periodic invocation of GMA Administration's
  existing `IAdminAuthorizationService` for the original operation, actor,
  tenant, and resource scope.
- Access Control may provide a generic batch assignment-existence query so a
  product authorizer can evaluate compatible roles in one database round trip.
- BunkFy owns which assignments constitute current workspace membership and
  proves that revocation closes its user streams.
- Framework best-effort SSE and SignalR transports are separate optional
  front doors. BunkFy has their shared `Notifications:Enabled` switch disabled;
  their token-expiry and revocation behavior belongs to a later framework
  realtime slice rather than this durable-module change.

## Delivery Slice

1. Add validated durable-stream settings for an authorization revalidation
   interval and a maximum connection lifetime, with secure finite defaults.
2. Create one lease per connection. Its deadline is the earlier of the
   configured lifetime and a valid `exp` claim on the authenticated principal.
3. Reauthorize no more often than the configured interval, but shorten an idle
   wait when a revalidation or expiry deadline comes first.
4. Close cleanly when the lease expires or authorization is denied. Fail closed
   on revalidation errors and log only the stream name, outcome, and exception
   type.
5. Recheck user streams through the existing replaceable scope authorizer and
   admin streams through the existing admin permission service. Do not rerun an
   audited admin operation merely to test continued access.
6. Add a generic Access Control batch assignment check and use it in BunkFy's
   workspace authorizer so both role compatibility and future revalidation stay
   bounded to one query per check.

## Invariants

- Initial endpoint authorization remains mandatory; a lease never grants access.
- Revoked access is observed within the configured revalidation interval plus
  only the in-flight authorization/query duration.
- A stream never outlives a valid authentication-expiry claim or its configured
  maximum lifetime.
- Revalidation uses the original subject, actor, tenant, scope, permission, and
  resource scope; no client-controlled identity is reread from event payloads.
- Full result batches still drain immediately, and one process-wide sequence
  monitor remains the only idle database poller for notification data.
- Cancellation is propagated and no subject, scope, recipient, payload, token,
  destination, or exception text is added to logs or metrics.

## Verification

- Lease unit tests cover scheduling, denial, configured lifetime, authentication
  expiry, malformed expiry fallback, and bounded wake duration.
- Public API tests prove the replaceable authorizer is invoked again and denial
  closes both durable user stream shapes.
- Admin API tests prove the original permission is re-evaluated and revocation
  closes both durable admin stream shapes without duplicate operation audits.
- Access Control tests prove one batch call handles normalized roles, active and
  expired assignments, exact scope, and an empty role set.
- BunkFy tests prove owner, current marker, and legacy membership compatibility
  through the batch contract and reject a subject after all assignments vanish.
- One consolidated non-Docker verification runs per changed repository at the
  slice boundary; no persistence migration or repeated Docker gate is expected.

## Completion Evidence

- `eng/verify.ps1 -SkipDocker` passed solution synchronization, boundaries, a
  zero-warning build, both provider migration-drift checks, all 129 non-Docker
  tests, and the transitive vulnerability scan.
- BunkFy's deterministic composition tests replay one real item, revoke access,
  advance the lease clock, and wake the stream without sleeps. Public history,
  public broadcasts, admin history, and the admin broadcast inbox all close;
  each admin stream retains exactly one audited operation entry.
- BunkFy's complete `eng/verify.ps1` gate passed, including source-package and
  solution guards, every migration-drift check, the fast repository suites, and
  all 58 composition integration tests.

## Explicitly Deferred

- Framework best-effort SSE and SignalR connection expiry/revalidation.
- Cross-session read-state events and stale-cursor recovery after restore.
- Combining history and broadcast feeds into one transport or adding a
  multi-region realtime backplane.
- Provider capacity measurements, operational alert thresholds, and product
  presence semantics.
