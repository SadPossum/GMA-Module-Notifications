# Notification Lifecycle Scaling Task

Status: complete
Date: 2026-08-05

Increment A status: complete. Increment B is complete.

## Goal

Make Notifications lifecycle closure safe for large histories and complete
tenant or organization shutdowns without teaching the module about any product's
termination workflow, legal policy, record types, or owner graph.

The current exact-reference close is intentionally atomic and capped at 10,000
notifications. Raising that cap would increase transaction duration, lock scope,
memory use, and retry cost without making closure resumable.

## Ownership Boundary

- Notifications owns inbox copies, preferences, routes, broadcasts, delivery
  state, reference tombstones, bounded deletion, and generic lifecycle proof.
- Producers own opaque reference meaning and safe notification content.
- Products own authorization, eligibility, legal holds, export assembly,
  orchestration, ordering, restore replay, and interpretation of outcomes.
- Hosts own retention windows, backup expiry, production admission, scheduling,
  monitoring, and operator controls.
- Framework and cross-module extensions remain unchanged unless a second module
  proves a smaller product-neutral primitive is required.

No product owner key, case kind, country rule, or termination phase enters this
module.

## Increment A: Resumable Exact-Reference Close

Add a second in-process close contract alongside the existing atomic close.
One call removes at most a caller-selected batch within a strict module maximum.
The first accepted call closes the reference tombstone before deleting content,
so later projection cannot recreate history while deletion is incomplete.

The request carries only:

- one operation id;
- exact scope and opaque reference;
- the expected open reference version; and
- a fixed batch size.

The persisted operation binds those values with a versioned SHA-256 request
fingerprint. It stores cumulative removed count, deterministic rolling removal
proof, completed batch count, start time, and optional completion time. The
scope and operation id form the idempotency key; one reference can have only one
close operation.

Each call must:

1. validate current scope and exact replay identity;
2. acquire the existing serializable reference lifecycle boundary;
3. reject stale versions, conflicting operations, and leased delivery work;
4. mark the reference terminal on the first accepted batch;
5. select notification ids in stable order and delete one bounded batch;
6. advance every affected companion-reference version;
7. update a versioned rolling proof without retaining notification ids; and
8. return `InProgress` or atomically persist an immutable completion receipt.

The completion receipt contains no recipient, notification, scope-external
coordinate, title, body, payload, tag, destination, or provider value. It keeps
only the operation/reference coordinates, resulting reference version,
cumulative count, batch count, proof version and digest, and completion time.

Exact replay after completion returns the same receipt. Restart after a partial
batch resumes the persisted operation. A changed batch size or reference under
the same operation id conflicts. An active lease returns a retryable status
without deleting that batch.

The existing atomic close remains compatible for small histories. Its direct
record-id digest and result contract do not change.

## Increment B: Generic Scope Lifecycle

After exact-reference scaling is proven, add one product-neutral scope lifecycle
facade for complete Notifications ownership. It must separately model:

- scope admission/tombstone state so addressed history, preferences, routes,
  broadcasts, delivery retries, and read activity cannot regrow during closure;
- a bounded, versioned export of Notifications-owned state without inferring
  producer semantics from payload JSON;
- resumable hard deletion in dependency-safe table order;
- deliberate treatment of PII-minimised inbox/replay evidence; and
- terminal verification plus an immutable payload-free receipt.

The scope facade may reuse the resumable operation/proof pattern, but it must not
hide several independent stores behind one unbounded transaction. Export and
destruction remain separate operations. Closing a scope never deletes a global
identity or another scope's records.

### Increment B delivery order

1. [x] Add one module-owned `notification_scope_states` row with a monotonic
   concurrency version and terminal admission state. Every EF-tracked scoped
   mutation advances it once per unit of work. Missing state remains version
   zero; the first mutation creates version one.
2. [x] Let the module inbox participate in that same serializable state boundary.
   A closed scope acknowledges late messages as suppressed without inserting an
   inbox row or invoking its handler. The framework hook is opt-in and has no
   Notifications or product vocabulary.
3. [x] Cover direct set-based mutation paths and delivery claiming explicitly.
   Closing must conflict safely with a concurrent API command, inbox handler,
   retention batch, or delivery worker; it cannot rely on process-local flags.
4. [x] Expose a bounded typed export by store against one selected scope revision.
   Pages use stable keys and opaque payload JSON; a final snapshot must retain
   the selected revision. Inbox idempotency rows are operational journals, not
   portability content.
5. [x] Add resumable destruction in dependency-safe stages: scoped inbox rows,
   tenant broadcast reads and broadcasts, preferences, routes, tag definitions,
   then user notifications and cascading delivery state. Active delivery work
   or an exact-reference close already in progress pauses the scope operation.
6. [x] Retain the terminal scope tombstone, immutable scope receipt, and existing
   payload-free exact-reference proof rows as an explicit pseudonymous minimum.
   They are excluded from active-record counts and may be compacted only by a
   separately approved proof/backup-expiry policy.

The scope state is not a generic tenant registry. It exists only in the
Notifications schema and governs only Notifications-owned writes.

Steps 1-3 use a module-owned monotonic scope state for tracked writes, inbox
admission, provider-native receipt writes, set-based updates, retention, and
delivery claiming. Existing scopes are backfilled as open at revision one by
equivalent PostgreSQL and SQL Server migrations. Cross-scope module workers use
an internal bounded maintenance admission path; API and inbox work remain bound
to one exact active scope. One PostgreSQL scenario proves revision advancement,
closed-scope inbox suppression, retention fencing, and delivery exclusion.

Step 4 exposes twelve explicitly typed, keyset-paged stores through one
in-process application port. Each page is bounded at 200 records and verifies
the selected scope revision before and after its query. Notification payloads
remain opaque JSON, tenant and global broadcast data stay separated, and inbox
rows are intentionally absent. Focused tests cover every record type and one
PostgreSQL scenario proves both GUID and compound reference cursors plus stale
revision rejection.

Steps 5-6 close the module-owned scope tombstone on the first accepted call and
remove at most one caller-bounded ID batch per call in dependency-safe stage
order. Durable progress carries a versioned rolling proof without retaining row
identifiers. Active delivery leases and in-progress exact-reference closure
pause the operation before the tombstone is accepted. Completion replaces
progress with an append-only payload-free receipt; the tombstone and existing
reference state/proof remain linked and retained. Focused tests cover restart,
replay, conflict, both busy fences, and bounded progress. A PostgreSQL scenario
proves all stages, cascading delivery cleanup, global-record isolation,
retained proof, and receipt immutability.

Production hardening keeps both lifecycle boundaries terminal below the domain
layer. Scope and exact-reference state rows now enforce coherent open/closed
coordinates and provider-native triggers reject update or deletion after
closure. Batch progress and receipts enforce the domain's zero-or-positive
count shape, fixed proof version, bounded batch size where retained, and ordered
timestamps. The older atomic close receipt is also linked to its retained
reference state.

Normal scoped writes still use one monotonic module revision. Optimistic
conflicts on that one open state row are rebased for persistence only, with a
strict attempt limit; adapter calls and unrelated aggregate conflicts are never
retried. A scope found closed during rebase is rejected. This preserves exact
export fencing without serializing notification delivery in-process or risking
duplicate provider calls.

## Persistence

Increment A adds module-owned, scope-filtered progress and receipt tables.
Indexes begin with scope and operation or exact reference. Progress is mutable
only while incomplete; completion receipts are append-only and protected by
equivalent PostgreSQL and SQL Server constraints/triggers. Counts are monotonic,
batch size is immutable, proof digests are lowercase SHA-256, and timestamps are
non-default.

No notification identifiers are retained after a batch commits. The rolling
proof hashes a domain-separated previous proof plus deterministic batch number,
count, and batch-id digest. Its proof version is explicit so future algorithms
cannot be confused with existing direct-id receipts.

## Efficiency And Failure Semantics

- Work and memory are bounded by batch size, independent of total history.
- No `OFFSET`, tenant-wide payload scan, or JSON predicate is used.
- Stable primary-key order plus deletion of completed batches gives resumable
  progress without a large cursor ledger.
- Scope/reference indexes drive selection and lease checks.
- Serialization or optimistic-concurrency failures remain retryable; no partial
  receipt is presented as completion.
- A crash after commit resumes from durable progress. A crash before commit
  leaves the previous progress unchanged.
- Completion is impossible while referenced notifications remain.

## Delivery Slices

1. [x] Add Increment A contracts, domain progress/receipt models, and focused
   validation tests.
2. [x] Add bounded persistence execution, idempotent replay, projection
   suppression, companion-version advancement, and focused tests.
3. [x] Add equivalent PostgreSQL and SQL Server migrations, append-only proof,
   drift checks, and one coherent relational lifecycle scenario.
4. [x] Consume the generic close from one product adapter without product terms
   entering GMA.
5. [x] Design and implement Increment B one generic store at a time.

## Acceptance

- A history larger than the old atomic ceiling closes over several bounded
  calls without raising that ceiling.
- New projection is rejected after the first accepted batch.
- Exact restart and completed replay are idempotent; changed replay conflicts.
- Tenant/scope isolation and companion-reference revision behavior remain exact.
- Pending/retry delivery state is removed with its notification, while active
  leases pause the affected batch.
- Completion receipt count and rolling proof are deterministic and payload-free.
- Both provider models are drift free and protect immutable completion receipts.
- Existing atomic lifecycle behavior and all V1/V2/V3 notification behavior stay
  compatible.

## Verification

- `Gma.Modules.Notifications.Tests`: 115 passed.
- PostgreSQL and SQL Server migration drift checks passed.
- PostgreSQL scope destruction and retained-proof scenario: 1 passed.
- PostgreSQL concurrent delivery scenario: 1 passed.
- SQL Server concurrent delivery scenario: 1 passed.
- SQL Server atomic, resumable, and scope receipt insertion scenario: 1 passed.

## Non-Goals

- selecting product retention or erasure policy;
- authorizing a tenant shutdown;
- exporting product-owned notification semantics;
- deleting already delivered provider-side copies;
- compacting tombstones before product decisions and backup expiry are safe; or
- exposing a public or admin lifecycle endpoint.
