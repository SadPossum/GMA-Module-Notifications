# Notification History Lifecycle Task

Status: completed
Date: 2026-07-30

## Goal

Give products a reusable, bounded way to find and permanently close notification
history associated with an opaque subject or resource reference.

The capability must support product data lifecycle workflows without teaching
GMA Notifications about guests, staff, reservations, legal decisions, country
policy, or any product-specific record type.

## Ownership Boundary

- Notifications owns durable inbox records, delivery rows, indexed history
  references, lifecycle closure state, generic receipts, retention, and
  concurrency with notification projection.
- Producers own the meaning of non-recipient references and supply only
  normalized, non-reversible reference digests.
- Products own authorization, legal and business eligibility, subject
  discovery, export assembly, erasure decisions, restore orchestration, and
  interpretation of generic lifecycle outcomes.
- Hosts own retention periods, database protection, operational monitoring, and
  production admission policy.
- Framework remains unchanged in this slice. The optional durable module
  contract is the correct boundary for indexed history lifecycle behavior.

## Reference Contract

Notifications exposes a value consisting of:

- a bounded lowercase kebab-case namespace; and
- a lowercase 64-character SHA-256 digest.

The namespace describes only the producer-owned reference family. The digest
must be calculated over a versioned canonical coordinate outside Notifications.
Raw subject identifiers, email addresses, phone numbers, names, booking
references, and free text are rejected from this contract.

Every stored user notification receives a module-generated recipient reference
derived from its scope and normalized recipient id. Existing records are
backfilled with this generic recipient reference during migration.

A version 3 addressed-notification request may add a bounded set of
producer-supplied references. Versions 1 and 2 remain supported. Existing
records cannot be assigned guessed producer references by parsing arbitrary
payload JSON.

## Generic Lifecycle Surface

An in-process application contract provides:

1. a bounded, cursor-based history query for one exact scope and reference;
2. a reference snapshot containing state, version, record count, and latest
   stream sequence without returning a tenant-wide total;
3. an idempotent prepare operation that opens an empty versioned reference when
   a product must install a tombstone even if no history currently exists;
4. an idempotent close operation identified by a caller operation id and exact
   request fingerprint; and
5. a stable generic receipt that reports the closed reference version, removed
   record count, deterministic record-id digest, and completion time.

No public or admin HTTP endpoint is added. Product adapters must authorize and
orchestrate the in-process surface.

The history query returns immutable notification content and routing metadata.
Recipient read activity and mutable delivery-attempt state are deliberately
outside the reference version contract; products that own those concerns must
model their lifecycle separately.

## Close Semantics

Closing one reference must:

1. acquire the reference lifecycle lock before projection or mutation;
2. reject a changed expected version or conflicting operation replay;
3. block while a referenced delivery is actively leased for processing;
4. suppress pending or retry-scheduled delivery work;
5. remove referenced notification content and dependent delivery history;
6. persist terminal reference state and an immutable idempotency receipt in the
   same transaction; and
7. prevent future requests carrying the closed reference from recreating
   durable history or external delivery work.

If a notification carries several references, closure of any one reference
suppresses the whole notification. Projection resolves references in canonical
order, and database concurrency tokens plus serializable lifecycle mutations
prevent a projection from committing against a reference closed concurrently.

Reissuing the same close operation after a pre-close database restore performs
the close again and recreates the generic receipt. Reissuing it against an
already closed database returns the stored receipt byte-for-byte. The product's
external ledger remains authoritative for deciding when replay is required.

## Persistence

Add module-owned, scope-aware tables for:

- notification-to-reference assignments;
- reference lifecycle state and monotonic version;
- immutable close receipts.

Indexes begin with scope and reference. Reference assignments cascade with
their notification, while terminal lifecycle state and receipts do not.
PostgreSQL and SQL Server migrations must provide equivalent constraints,
indexes, append-only receipt protection, and projection/closure serialization.

The ordinary retention worker continues to delete expired notifications in
bounded batches. It must not delete terminal lifecycle state or close receipts
in this slice because doing so could permit erased history to reappear.
It also retains open zero-record reference versions: deleting and recreating
one could make an older frozen version valid again. Safe compaction requires a
host policy that accounts for outstanding product decisions and backup expiry,
so it is intentionally deferred.

## Security And Privacy

- Reference digests are treated as pseudonymous security metadata.
- Queries and logs never emit raw producer coordinates.
- Logs contain only stable outcome codes, counts, and exception type names.
- Metrics never tag scope, recipient, reference, operation, or notification ids.
- Close operations are bounded by one reference and one configured batch
  ceiling; overflow fails closed before mutation.
- No payload JSON search, tenant scan, or product callback runs inside a
  database query.

## Delivery Slices

1. Add the validated reference contract and version 3 request while preserving
   version 1 and 2 compatibility.
2. Persist recipient and producer references with provider migrations and
   indexed bounded reads.
3. Add serialized, idempotent close semantics and immutable generic receipts.
4. Add relational concurrency, migration, retention, and replay proof.
5. Publish the exact Notifications head and consume it from downstream
   compositions.

## Verification

- Contract tests reject malformed namespaces, non-SHA-256 digests, duplicates,
  conflicting references, and reference-count overflow.
- Existing V1/V2 behavior remains unchanged except for automatic recipient
  indexing.
- Bounded queries prove scope isolation, deterministic cursors, and no partial
  result on overflow.
- Projection and closure races cannot leave a notification attached to a closed
  reference.
- Preparing an empty reference produces a positive version, and a subsequent
  notification advances it so stale product decisions cannot close unseen data.
- Pending work is suppressed, active leases return a retryable outcome, and
  completed delivery history is removed with notification content.
- Exact replay returns one receipt; changed replay conflicts.
- PostgreSQL and SQL Server migrations backfill recipient references, preserve
  existing inbox behavior, and keep migration models drift free.
- Standalone build, fast tests, architecture checks, package audit, and one
  exact relational lifecycle scenario pass before publication.

## Non-Goals

- product data-rights case management or legal-policy evaluation;
- selecting retention periods for a host;
- discovering product subjects from notification content;
- parsing product payload JSON to infer historical references;
- deleting provider-side email, SMS, or push copies after delivery;
- exposing a support shortcut around product authorization; and
- moving Notifications persistence or lifecycle behavior into Framework.

## Completion Evidence

- synchronized solution and warning-free full build;
- module boundary and PostgreSQL/SQL Server migration-drift checks;
- all 90 non-Docker tests;
- one exact PostgreSQL lifecycle scenario covering prepare, projection,
  immutable paging, active lease refusal, close, replay, late suppression, and
  the projection/close serialization race;
- provider upgrade scenarios reuse their existing PostgreSQL and SQL Server
  containers to verify case-distinct legacy-recipient backfill; and
- transitive package audit with no known vulnerable dependency.
