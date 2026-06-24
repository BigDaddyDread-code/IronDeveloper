# D06 -- Status Projection Rebuild Test

## Purpose

D06 proves that governed operation display status can be rebuilt deterministically from the same append-only projection event set.

D06 is test-only plus receipt. It consumes the D05 projector directly and does not add a production rebuild service, SQL store, projection table, event store, API, UI, status repository wiring, or executor behavior.

## Stack

Base branch while stacked: `status/append-only-event-to-status-projection`

Branch: `status/status-projection-rebuild-test`

## Files Changed

- `IronDev.IntegrationTests/BlockD06StatusProjectionRebuildTests.cs`
- `Docs/receipts/D06_STATUS_PROJECTION_REBUILD_TEST.md`

## Boundary

Status projection rebuild proof shows deterministic display status can be rebuilt from append-only events. It does not mint identity, perform lookup, assemble timelines, resolve evidence, determine blockers, validate freshness, choose next safe action, approve work, satisfy policy, execute mutation, retry, rollback, merge, release, deploy, promote memory, or continue workflow.

Rebuilt status is display truth only. It is not approval, policy satisfaction, validation freshness, source apply authority, rollback authority, retry permission, commit authority, push authority, PR creation authority, merge readiness, release readiness, deployment readiness, memory promotion, workflow continuation, or a gate override.

## Rebuild Proof

D06 proves:

- empty valid streams rebuild to `NoEvents`
- ordered streams rebuild to the expected final display status
- shuffled streams rebuild identically to ordered streams
- repeated rebuilds over the same event set are identical
- metadata-only events remain in source event IDs but do not change status
- duplicate event IDs fail closed
- duplicate append positions fail closed
- invalid streams fail closed without a projected status
- input event lists are not mutated
- input event objects are not mutated

## No Production Expansion

D06 adds no production code and does not modify D01, D02, D03, D04, or D05 behavior.

It adds no API, SQL, UI, store, repository wiring, resolver, formatter, runner, executor, source apply, commit, push, PR creation, merge, release, deploy, memory promotion, workflow continuation, or CI behavior.

## Validation

Local validation recorded on June 24, 2026:

- D06 focused tests: 62/62 passed
- D05 focused tests: 89/89 passed
- D01-D04 focused stack: 216/216 passed
- A02 + A05 read-adapter corridor: 61/61 passed
- governance/status corridor with D06: 593/593 passed
- `dotnet build IronDev.slnx --no-restore -v:minimal`: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed after staging, with normal LF/CRLF working-copy warnings from Git
