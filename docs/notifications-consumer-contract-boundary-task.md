# Notifications Consumer Contract Boundary Task

Status: completed
Date: 2026-08-10

## Goal

Expose the existing product-neutral notification history lifecycle, scope
lifecycle, and direct notification-request projection capabilities through
Notifications Contracts. Product integrations must not reference Notifications
Application internals, while delivery, persistence, and projection behavior
remain unchanged.

## Audit Finding

`INotificationHistoryLifecycle`, `INotificationScopeLifecycle`, and all types in
their signatures currently live in two Application port files containing 449
lines and many public types. BunkFy's Operations Notifications extension uses
both capabilities for data-rights and tenant-termination orchestration.

`IUserNotificationRequestProjector` and
`IUserNotificationRequestProjectorV3` are also declared beside the internal
routing repository. They are deliberate reusable in-process facades used by
GMA Extensions Auth Notifications and BunkFy Operations Notifications, but both
consumers must reference Notifications Application to reach them. This
contradicts the module's documented rule that producers and composition-owned
extensions depend only on Notifications Contracts.

The underlying lifecycle protocols, persistence model, delivery planner,
request idempotency, and provider behavior are already covered and do not need
redesign in this slice.

## Contract Decision

1. Notifications Contracts owns `INotificationHistoryLifecycle`,
   `INotificationScopeLifecycle`, and every public request, result, status,
   progress, receipt, export record, and limit type in their signatures.
2. Notifications Contracts owns `IUserNotificationRequestProjector` and
   `IUserNotificationRequestProjectorV3`. Their names and signatures remain
   stable in this slice so direct and outbox-driven requests continue to use
   the same planner without a semantic migration.
3. Keep one public type per file. Remove the two oversized lifecycle port files
   and remove the projector interfaces from the internal routing repository
   file after all consumers migrate.
4. Public lifecycle enums use an explicit `Unknown = 0` sentinel and strict,
   stable kebab-case JSON names. Numeric, unknown, undefined, and future values
   fail closed. Existing export-store and destruction-stage numeric values stay
   unchanged; the other lifecycle statuses are response-only and are not
   persisted.
5. Notifications Application continues to implement and register the request
   projectors. Notifications Persistence implements and registers the
   lifecycle Contracts directly. Repositories, preference evaluation, delivery
   adapter catalog, stream pulse, handlers, and provider mechanics remain
   Application or Persistence concerns.
6. GMA Extensions Auth Notifications and BunkFy Operations Notifications move
   to Notifications Contracts only. Scoped architecture guards prevent either
   integration from regaining a Notifications Application reference.
7. Framework remains unchanged. These are optional Notifications-module
   capabilities, not framework-wide lifecycle or messaging primitives.

## Security And Privacy

- Moving the interfaces does not add an HTTP, administration, or remote
  endpoint and does not broaden authorization.
- Products continue to own subject discovery, legal decisions, export
  assembly, termination ordering, restore orchestration, and caller access.
- Notification references remain bounded pseudonymous digests. Raw producer
  identifiers, token material, and transport payloads are not added to the
  contracts.
- Request projection retains existing idempotency, preference, routing,
  delivery, scope-tombstone, and closed-reference checks.
- Export and destruction records retain their current explicit fields,
  bounded paging, revision checks, payload handling, receipts, and proofs.

## Delivery

- [x] Add split lifecycle and request-projector Contracts with strict enum
  serialization coverage.
- [x] Move Notifications Application, Persistence, and tests to the Contracts
  namespace; remove the public Application port types.
- [x] Move GMA Extensions Auth Notifications to Contracts only and add a scoped
  architecture guard.
- [x] Move BunkFy Operations Notifications to Contracts only and add a scoped
  architecture guard.
- [x] Update canonical Skeleton and product pins after focused verification.
- [x] Run one consolidated non-Docker gate per changed repository at the
  completed slice boundary, publish exact pins, and verify exact CI.

## Verification Plan

- Use focused Notifications contract serialization, lifecycle persistence,
  request projection, GMA Extensions Auth Notifications, BunkFy Operations
  Notifications, architecture, and Worker composition tests while editing.
- Run no local Docker/provider gate because the persistence model, migrations,
  generated SQL, provider mappings, transaction boundaries, and query behavior
  do not change.
- At the coherent slice boundary, run the complete non-Docker Notifications,
  GMA Extensions, Skeleton, and BunkFy backend gates once, plus the BunkFy root
  lightweight gate before publication.

## Verification Evidence

- Notifications functional commit
  `9f0559464b9a39c01c90ecc2549be08443223551`: synchronized solution,
  boundary checks, zero-warning build, PostgreSQL and SQL Server migration
  drift checks, 117 non-Docker tests, and package audit passed. Exact CI passed
  in Validate run `31358839357` and Security Baseline run `31358839346`.
  Documentation-index follow-up `87a98eb56324efde65d3d5a1906b3eb6bc92cf49`
  also passed Validate run `31359665405` and Security Baseline run
  `31359665384`.
- GMA Extensions functional commits
  `4628192fb58bb576d31bde4f4040a15643dd826a` and
  `4090189954ae42768e65b4c7533ea28860f19ab0`: explicit Runtime ownership,
  solution synchronization, extension-boundary checks, zero-warning build,
  all 37 tests, and package audit passed. Exact final-commit CI passed in
  Validate run `31359274164` and Security Baseline run `31359274200`.
- Canonical Skeleton pin commit
  `119a14b5cf6c91b0c05869b73eada2121b18a3a5`: all mounted solutions,
  selection matrices, security/release/source-package checks, zero-warning
  build, migration drift checks, and non-Docker tests passed. Exact CI passed
  in Validate run `31359984569`, Security Baseline run `31359984565`, and
  CodeQL run `31359984527`.
- BunkFy backend functional commit
  `97b349310e04520d753246f0037f235df22e7a57`: complete non-Docker backend
  verification passed, including the Operations Notifications suite,
  architecture, Worker composition, integration, migration drift, and package
  coverage. Exact CI passed in Validate run `31360629397` and Security Baseline
  run `31360629445`.
- BunkFy root pin commit
  `5ec8eb7adcda71c0c1f7075661a24414abecc00a`: the lightweight root
  composition, admission, release, and deployed-probe policy gate passed.
  Exact CI passed in Validate run `31360823044`, Security Baseline run
  `31360823040`, and CodeQL run `31360823002`.
- Local Docker was intentionally omitted because the slice did not change a
  persistence model, migration, generated SQL shape, transaction boundary,
  provider mapping, or query behavior. Notifications CI still exercised its
  relational integration job on the exact functional commit.

## Not In This Slice

- renaming or removing the versioned request-projector facades;
- changing direct projection into another integration-event hop;
- redesigning lifecycle revisions, references, cursors, batching, receipts,
  proofs, retention, or destruction ordering;
- changing notification audience selection, authorization, preferences,
  delivery routes, adapters, retries, or realtime streaming;
- adding lifecycle APIs or remote service boundaries; or
- generalizing notification-specific vocabulary into Framework.
