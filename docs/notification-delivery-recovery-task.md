# Notification Delivery Recovery Task

Status: completed
Date: 2026-08-10

## Goal

Make durable notification delivery recover deterministically after worker loss
and operator retry while preserving immutable attempt history, bounded work,
provider-neutral semantics, and the existing at-least-once contract.

## Audit Findings

1. `RetryManually` resets `Attempts` to zero while old attempt rows are retained.
   The next delivery writes attempt number one again and conflicts with the
   unique `(ScopeId, DeliveryId, AttemptNumber)` index. The documented operator
   recovery path therefore fails after any delivery that already has history.
2. Claiming increments `Attempts` before the provider call. If a worker exits on
   the final allowed attempt, the expired row remains `processing` with
   `Attempts == MaxAttempts`. The claim query excludes it forever, so it is
   neither retried nor made terminal.
3. Reclaiming an earlier expired lease increments the counter but records no
   immutable outcome for the abandoned attempt. Attempt numbers can have gaps
   and operators cannot distinguish an adapter result from worker loss.

## Ownership Boundary

- Notifications Domain owns delivery state transitions, monotonic attempt
  numbering, retry budgets, lease ownership, and terminal exhaustion.
- Notifications Persistence owns provider-specific skip-locked claiming and
  the atomic recording of an expired lease with its immutable attempt row.
- Notifications Application owns the operator retry use case and supplies the
  configured bounded number of additional attempts.
- Adapters continue to own provider calls and must use the stable delivery id
  as their provider idempotency key.
- Hosts own retry and lease configuration, worker capacity, provider limits,
  monitoring thresholds, and operator access.
- Framework, other reusable modules, products, and cross-module extensions do
  not change for this delivery-recovery slice.

## Delivery Slice

1. Keep `Attempts` monotonic for the lifetime of a delivery. An operator retry
   adds one configured retry budget to the current attempt number instead of
   resetting history-visible numbering.
2. When claiming finds an expired `processing` lease, record exactly one
   `exception` attempt with a bounded `worker-lease-expired` code in the same
   transaction that releases the stale lease.
3. Reclaim an expired delivery only when another attempt remains. When the
   abandoned attempt consumed the final budget, transition it to `exhausted`
   and do not call an adapter again.
4. Keep provider selection, stable delivery ids, existing attempt rows, and
   notification content unchanged during manual retry and lease recovery.
5. Prove equivalent behavior through the PostgreSQL and SQL Server claim paths
   without adding a persistence migration.
6. Add a standalone `eng/verify.ps1` entry point that includes solution sync,
   boundaries, build, migration drift, fast tests, package audit, and an
   optional single Docker gate.

## Invariants

- Attempt numbers are positive, unique, and strictly increasing per delivery.
- Manual retry grants a bounded fresh attempt budget but never removes or
  renumbers immutable history.
- One expired lease produces at most one abandoned-attempt record.
- A final expired lease cannot remain claimable or permanently `processing`.
- Lease recovery and claim happen under the existing provider transaction and
  row-lock boundary.
- Cancellation propagates; logs, metrics, and attempt codes contain no scope,
  recipient, payload, destination, or exception text.

## Verification

- Domain tests cover bounded retry-budget extension and monotonic counters.
- Worker tests cover reclaim before the limit and exhaustion at the limit,
  including immutable abandoned-attempt history.
- Operator retry followed by real delivery produces the next attempt number
  rather than a duplicate.
- Existing PostgreSQL and SQL Server delivery scenarios exercise the changed
  claim SQL and prove terminal final-lease recovery.
- One consolidated standalone verification runs after focused editing tests.

## Local Verification Evidence

- `eng/verify.ps1 -SkipDocker` passed solution synchronization, module
  boundaries, restore, a zero-warning build, PostgreSQL and SQL Server
  migration-drift checks, all 121 non-Docker tests, and the transitive package
  vulnerability audit.
- The two changed provider scenarios passed against real PostgreSQL and SQL
  Server containers: two passed, zero failed, zero skipped.
- No persistence migration is required; the existing attempts, maximum-attempts,
  lease, and immutable attempt-row schema supports the corrected semantics.
- Functional commit `80c54d00e771a35da3e534ac3a0f129b82c176e0`
  passed exact Validate run `31381043435`, including Ubuntu, Windows, and the
  PostgreSQL/SQL Server relational job, plus Security Baseline run
  `31381043440`.
- Downstream Skeleton and BunkFy pin evidence is recorded in those repositories.

## Explicitly Deferred

- Periodic reauthorization and bounded lifetimes for long-lived user and admin
  notification streams. This needs a separate API/Administration ownership
  decision and BunkFy composition proof.
- Skipping the replaceable preference evaluator for `mandatory` V2/V3
  requests. This is a separate application-policy reliability increment.
- Cross-session read-state change streaming and stale cursor recovery after a
  database restore.
- Vendor transport implementations, destination directories, templates,
  alert thresholds, and measured production capacity envelopes.
- A lease heartbeat for adapters that exceed the configured lease. Stable
  provider idempotency remains the required at-least-once protection.
