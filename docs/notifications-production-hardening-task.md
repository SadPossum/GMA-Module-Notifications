# Notifications Production Hardening Task

Status: complete
Date: 2026-07-19

## Goal

Make the optional Notifications module production-ready for durable addressed notifications, broad broadcasts, adapter delivery and recoverable live feeds without moving product audience rules, identity directories, provider transports or deployment policy into the module.

## Ownership Boundary

- Framework owns dependency-neutral notification messages, publisher and sink contracts, best-effort delivery coordination, and generic realtime primitives;
- Notifications owns durable inbox history, read state, preferences, tag definitions, routes, leased delivery jobs, attempts, broadcasts, retention and durable stream recovery;
- Extensions own reusable bridges that understand more than one module, such as Auth security notifications;
- producer modules own recipient selection, semantic tags, mandatory-versus-preference-aware intent, safe content and durable source events;
- products own business deep links, audience readers, role policy, templates, destination directories and product-specific notification workflows;
- hosts own credentials, provider quotas, proxy timeouts, database capacity, encryption, retention values, alert thresholds and multi-region topology.

## Audit Baseline

- the durable delivery worker claims at most `MaxConcurrency` jobs and then always waits `PollIntervalSeconds`, so `BatchSize` does not provide the documented batch throughput and a healthy backlog drains unnecessarily slowly;
- adapter-requested retry timestamps are accepted without the configured maximum retry-delay bound;
- every durable SSE connection polls its history or broadcast query independently, once per configured interval, and idle streams emit no heartbeat; this creates connection-proportional database load and permits proxies to close otherwise healthy idle streams;
- the database model has scope-oriented sequence indexes but no global sequence indexes suitable for one shared cross-scope stream-change monitor;
- worker leases, provider-specific insert-if-missing broadcast receipts and generated stream sequences are tested only through EF InMemory in the standalone module;
- the repository has no reusable-module boundary guard, migration-drift check, Linux CI leg, PostgreSQL behavior lane or explicit transitive package vulnerability audit;
- the Auth integration can retry an email-verification notification after its one-time code has expired;
- BunkFy's workspace notification authorizer accepts a matching token scope before consulting current workspace assignments, leaving notification access valid until token expiry after a membership revocation;
- durable content can contain personal or operational data, while retention is deliberately disabled until each product chooses a policy.

## Delivery Slice

1. Drain delivery work continuously in bounded concurrency waves, honoring `BatchSize` as a per-cycle bound without leasing jobs that wait behind an unbounded in-process queue.
2. Clamp adapter-requested retry times to the configured maximum retry delay and retain stable delivery ids as provider idempotency keys.
3. Add a module-owned, process-shared durable-stream change monitor. Poll global history/broadcast sequence heads once per process, wake connected streams only after relevant table movement, immediately drain full result batches, and use a bounded heartbeat fallback for recovery.
4. Add provider migrations for efficient global sequence-head reads while preserving the existing scope/user stream indexes.
5. Add relational integration coverage: PostgreSQL proves migration application, generated stream sequences, shared monitor heads, disjoint multi-worker claims and concurrent idempotent broadcast receipts; SQL Server executes its provider-specific disjoint lease path.
6. Add focused unit tests for batch draining, retry clamping, shared stream wakeups, heartbeat behavior and configuration validation.
7. Add standalone module boundary, migration-drift, Windows/Linux, package-audit and required PostgreSQL CI gates.
8. Make the Auth-Notifications email destination bridge fail terminally when a verification request has expired.
9. Make BunkFy authorize notification scope against current workspace assignments even when the token carries the requested scope.
10. Document capacity, retention, sensitive-content, heartbeat, provider-idempotency and deployment responsibilities.
11. Publish exact Notifications and Extensions heads, then verify GMA Skeleton and BunkFy against those heads.

## Non-Goals

- product-specific recipient discovery, actor exclusion, deep links, highlighting or UI attention state;
- a vendor email, SMS, push or realtime transport;
- storing email addresses, phone numbers, device tokens, provider credentials or product role definitions in Notifications;
- treating notification history or delivery receipts as authoritative business state;
- an external multi-region stream backplane or provider-specific database notification mechanism;
- changing the documented at-least-once delivery contract into exactly-once delivery;
- selecting a universal retention period or encrypting application databases inside a reusable module.

## Acceptance Criteria

- an active backlog drains without a poll delay between healthy concurrency waves, while no wave claims more than `MaxConcurrency` and no cycle exceeds `BatchSize`;
- an adapter cannot schedule a retry beyond `RetryMaxMinutes`;
- idle durable streams do not query once per client per second, remain alive through heartbeat events, recover through bounded fallback queries, and drain more than one result batch without waiting;
- stream monitoring does not depend on in-process publication and therefore observes writes committed by another replica;
- PostgreSQL proves that concurrent workers do not claim the same lease, concurrent read receipts remain singular, and sequence values are generated and ordered; SQL Server proves its skip-locked lease path against the queue index;
- Auth verification mail is rejected after `ExpiresAtUtc`;
- a BunkFy user whose current owner/member assignments are absent cannot access workspace notifications using only a still-valid scoped token;
- source projects reference no other reusable module and contain no product-specific source;
- SQL Server and PostgreSQL migration models have no pending changes;
- standalone build, fast tests, boundary checks and package audit pass on Windows and Linux, and the required relational lane passes;
- Skeleton and BunkFy pass against the exact published Notifications and Extensions heads.

## Module Evidence

- standalone build succeeds with warnings treated as errors;
- 69 fast tests pass;
- four relational integration tests pass: PostgreSQL proves generated stream heads, shared monitor observation, disjoint concurrent claims, bounded batch/concurrency behavior, expired-lease recovery, translated retention queries and concurrent idempotent broadcast read receipts; SQL Server proves its provider-specific disjoint lease path and translated retention queries;
- reusable-module boundaries and both provider migration models pass their repository guards;
- the transitive package vulnerability audit reports no vulnerable packages.

## Downstream Evidence

- Notifications implementation head `d5cc3ce` passes Windows, Linux and required relational CI;
- Extensions head `ee0447f` rejects expired Auth one-time notifications and passes its Windows/Linux CI;
- Skeleton head `adcf09e` records the hardened Notifications and Extensions heads, keeps relational internals module-owned, passes exact submodule-head and source-package guards, and passes Windows/Linux full verification;
- BunkFy backend head `ce295c9` requires current workspace owner/member assignment for notification access, configures durable-stream heartbeats, includes the module-owned relational project, passes exact submodule-head and source-package guards, and passes Windows/Linux full verification plus 32 Docker integration tests;
- no framework code or product-specific rule moved into Notifications: generic primitives remain in Framework, Auth mapping remains in Extensions, workspace authorization remains in BunkFy, and provider/deployment policy remains host-owned.
