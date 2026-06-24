# D04 - Governed Operation Timeline Read Model

## Purpose

D04 adds a governed operation timeline read model that presents operation-related events in deterministic order without creating authority, status truth, projection behavior, resolver behavior, or mutation behavior.

D01 established canonical `OperationId`.

D02 established read-only lookup by external reference IDs.

D03 established scoped correlation ID rules.

D04 consumes those contracts to define a metadata-only timeline model for operation status, evidence, receipts, validation, patch/package metadata, source apply, commit, push, PR, interruption, rollback, and recovery observations.

## Stack Base

D04 is stacked on `status/operation-correlation-id-model`.

Current stack:

```text
main <- D02 <- D03 <- D04
```

After D03 merges, D04 should be retargeted or rebased to `main`.

## Files Changed

- `IronDev.Core/Governance/GovernedOperationTimelineModels.cs`
- `IronDev.Core/Governance/GovernedOperationTimelineValidator.cs`
- `IronDev.Core/Governance/GovernedOperationTimelineAssembler.cs`
- `IronDev.IntegrationTests/BlockD04GovernedOperationTimelineReadModelTests.cs`
- `Docs/receipts/D04_GOVERNED_OPERATION_TIMELINE_READ_MODEL.md`

## Deterministic Ordering

Timeline assembly sorts entries by:

1. `OccurredAtUtc`
2. `RecordedAtUtc`
3. `TimelineEventId`
4. `SurfaceKind`
5. `SurfaceId`
6. `Source`

Ordering is display order only.

Ordering does not imply causality, approval, policy satisfaction, validation freshness, downstream authority, release readiness, deployment readiness, retry permission, rollback permission, or workflow continuation.

## Metadata-Only And Redaction Boundary

Timeline entries are metadata-only.

Allowed display content is limited to short safe title, short safe summary, reference IDs, surface IDs, timestamps, source labels, and redaction reason.

Timeline entries reject raw evidence payload markers, raw receipt payload markers, raw validation log markers, raw request or response body markers, raw model prompt or response markers, hidden reasoning markers, secrets, tokens, API keys, connection strings, authorization headers, private keys, full diffs, and full patch markers.

Redacted entries remain visible as redacted metadata.

Redaction does not remove the event from the timeline.

Redaction reason is explanatory only.

Redaction reason is not authority.

## Timeline Is Not Status

The governed timeline explains observed history.

It does not calculate current status.

It does not calculate blocked state.

It does not calculate missing evidence.

It does not calculate forbidden actions.

It does not calculate validation freshness.

It does not choose next safe action.

## Timeline Is Not Projection

The governed timeline read model is not event-store projection.

It is not status projection.

It does not synthesize lifecycle transitions.

It does not infer missing events.

It does not read or write stores.

## Timeline Is Not Authority

The governed operation timeline is a metadata-only witness of observed operation history. It does not mint identity, perform lookup, project status, resolve evidence, determine blockers, validate freshness, choose next safe action, approve work, satisfy policy, execute mutation, retry, rollback, merge, release, deploy, promote memory, or continue workflow.

Timeline is not:

- operation identity
- operation lookup
- correlation authority
- evidence resolution
- receipt resolution
- validation freshness
- approval
- policy satisfaction
- source apply
- rollback
- retry permission
- commit
- push
- PR creation
- merge readiness
- release readiness
- deployment readiness
- memory promotion
- workflow continuation

Commit-observed events do not imply push.

Push-observed events do not imply PR creation.

PR-observed events do not imply merge readiness.

Completed-observed events do not imply release readiness.

Interrupted-observed events do not imply retry authority.

Rollback-observed events do not imply future rollback authority.

## No API, SQL, UI, Projection, Executor, Or Mutation Boundary

D04 does not add API controllers, endpoints, OpenAPI changes, frontend changes, SQL migrations, SQL stores, DB contexts, event-store projection, status projection, status mapper changes, lookup resolver behavior, evidence resolver behavior, receipt resolver behavior, missing evidence resolver behavior, forbidden action resolver behavior, validation freshness resolver behavior, blocked-state formatting, next-safe-action formatting, authority-warning formatting, executors, runners, source apply, commit, push, PR creation, merge, release, deploy, memory promotion, workflow continuation, or CI workflow changes.

## Validation

- Focused D04 tests: 67/67 passed
- Focused D03 tests: 56/56 passed
- Focused D02 tests: 39/39 passed
- Focused D01 tests: 54/54 passed
- A02 + A05 read-adapter corridor: 61/61 passed
- Governance/status corridor: 381/381 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Review Traps

Reject D04 if:

- timeline entries can replace operation ID
- timeline entries can replace correlation ID
- timeline assembly mints operation IDs
- timeline assembly mints correlation IDs
- timeline assembly performs D02 lookup
- timeline assembly reads from stores
- timeline assembly projects current status
- timeline assembly calculates blocked state
- timeline assembly calculates next safe action
- timeline assembly resolves evidence
- timeline assembly resolves receipts
- timeline entries expose raw payload
- timeline entries expose private reasoning
- timeline entries expose secrets
- timeline entries expose full patch content
- timeline event ordering implies causality or authority
- completed timeline event implies release readiness
- interrupted timeline event implies retry authority
- rollback timeline event implies rollback execution authority
- PR timeline event implies merge readiness
- timeline models include `Can*` fields
- timeline models include approval, policy, release, or deploy authority fields
- D04 modifies D02 lookup behavior
- D04 modifies D03 correlation behavior
- D04 changes API, SQL, or UI
- D04 touches executors
- D04 touches release, deploy, memory, or workflow authority code

## Killjoy

Humans will believe the timeline. Make sure the timeline is only a witness, not a judge.
