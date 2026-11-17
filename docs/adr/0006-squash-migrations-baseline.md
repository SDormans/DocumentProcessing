# 0006. Squash pre-release migrations into `InitialBaseline`

- Status: Accepted
- Date: 2026-11-17

## Context

Ten migrations from the prototype (including `DescribeYourChanges` and `SyncCurrentModel`) were noisy and partly redundant. No production database existed yet, but local and demo databases did.

## Decision

Replace them with a single `InitialBaseline` migration. On startup, a database that is fully on the old history has only its `__EFMigrationsHistory` rows rewritten; tables are untouched. A database stopped partway through the old history is refused. Concurrent instances serialise the upgrade with a PostgreSQL advisory lock.

## Consequences

- Production starts from one clean migration; every later change is a normal incremental migration.
- An integration test asserts the baseline contains the whole model (`HasPendingModelChanges` is false).
- The adoption code can be removed once no pre-baseline databases remain.
