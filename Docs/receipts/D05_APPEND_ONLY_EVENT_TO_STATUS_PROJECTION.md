# D05 -- Append-Only Event-to-Status Projection

## Purpose

D05 adds a deterministic append-only event-to-status projection for governed operations.

It consumes the D01 operation identity contract, D03 correlation metadata rules, and D04 timeline vocabulary without changing their behavior. It does not wire status read repositories, timeline read repositories, API endpoints, SQL persistence, UI, runners, or executors.

## Stack

Base branch while stacked: `status/governed-operation-timeline-read-model`

Branch: `status/append-only-event-to-status-projection`

## Files Changed

- `IronDev.Core/Governance/OperationStatusProjectionModels.cs`
- `IronDev.Core/Governance/OperationStatusProjectionValidator.cs`
- `IronDev.Core/Governance/OperationStatusProjector.cs`
- `IronDev.IntegrationTests/BlockD05AppendOnlyEventToStatusProjectionTests.cs`
- `Docs/receipts/D05_APPEND_ONLY_EVENT_TO_STATUS_PROJECTION.md`

## Boundary

Append-only operation events can project deterministic display status. Projected status does not mint identity, perform lookup, assemble timelines, resolve evidence, determine blockers, validate freshness, choose next safe action, approve work, satisfy policy, execute mutation, retry, rollback, merge, release, deploy, promote memory, or continue workflow.

Projection input is supplied as an immutable event set. The projector sorts by append position, then recorded timestamp, then projection event ID. It does not read stores, write stores, persist projection output, update events, delete events, replace events, or compact events.

Projection order is determinism only. It is not authority order.

## Metadata-Only Events

The following events remain source event IDs but do not change projected status:

- `EvidenceObserved`
- `ReceiptObserved`
- `ValidationObserved`
- `AuthorityBoundaryObserved`

Validation evidence does not imply validation freshness.

Evidence does not imply approval.

Receipts do not imply authority.

Authority-boundary events do not grant authority.

## Status Events

Status-changing events are mapped explicitly:

- `OperationMinted` -> `Minted`
- `RunStarted` / `RunLinked` -> `RunObserved`
- `PatchArtifactCreated` / `PatchArtifactLinked` -> `PatchArtifactObserved`
- `SourceApplyStarted` / `SourceApplyObserved` -> `SourceApplyObserved`
- `CommitPackageCreated` -> `CommitPackageObserved`
- `CommitObserved` -> `CommitObserved`
- `PushObserved` -> `PushObserved`
- `PullRequestObserved` -> `PullRequestObserved`
- `BlockedObserved` -> `BlockedObserved`
- `InterruptedObserved` -> `InterruptedObserved`
- `RecoveryObserved` -> `RecoveryObserved`
- `RollbackObserved` -> `RollbackObserved`
- `FailedObserved` -> `FailedObserved`
- `CompletedObserved` -> `CompletedObserved`

No other event text, source, surface ID, reference ID, receipt text, evidence text, or correlation ID can change projected status.

## Validation

Local validation recorded on June 24, 2026:

- D05 focused tests: 89/89 passed
- D01-D04 focused stack: 216/216 passed
- A02 + A05 read-adapter corridor: 61/61 passed
- governance/status corridor with D05: 531/531 passed
- `dotnet build IronDev.slnx --no-restore -v:minimal`: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed after staging, with normal LF/CRLF working-copy warnings from Git
