# Notification Enum Default Sentinel Task

Status: complete
Date: 2026-08-10

## Goal

Declare `NotificationDeliveryPolicy.Unknown` as the explicit EF sentinel for
the existing `RespectPreferences` database default.

This is a reusable Notifications persistence correction. It preserves the
provider-neutral domain contract, PostgreSQL and SQL Server schema defaults,
and all existing migrations. BunkFy-specific persistence hardening remains in
the product repository.

## Invariants

- `Unknown` is never persisted as a valid delivery policy.
- New notifications still select `RespectPreferences` when the value is unset.
- Explicit `Mandatory` and `RespectPreferences` values are always written.
- The model records the sentinel explicitly and no migration is generated.

## Verification

- Add a focused design-model test for the default and sentinel metadata.
- Run Notifications unit tests and both provider migration-drift checks once
  after the change is complete.

## Completion Evidence

The design model now records `Unknown` as an explicit sentinel while retaining
the existing `RespectPreferences` database default. The synchronized solution
built with zero warnings, both SQL Server and PostgreSQL migration snapshots
reported no drift, all 130 non-Docker tests passed, and the transitive package
audit reported no known vulnerabilities. Docker was not rerun because this
model-only correction changes no schema, query, transaction, or provider
behavior.
