# A05 Operation Timeline Read Adapter

## Purpose

A05 adds a read-only operation timeline/event repository adapter so frontend readiness can show what happened without treating timeline history as approval, policy satisfaction, execution authority, memory promotion, release/deployment permission, or workflow continuation.

Review line:

> A timeline explains what happened. It does not decide what happens next.

## Files Changed

- `IronDev.Core/Governance/OperationTimelineReadRepository.cs`
- `IronDev.Infrastructure/Governance/OperationTimelineReadRepository.cs`
- `IronDev.Infrastructure/Governance/OperationTimelineFrontendReadinessBackendTruthSource.cs`
- `IronDev.Api/Program.cs`
- `IronDev.IntegrationTests/BlockA05OperationTimelineReadAdapterTests.cs`
- `Docs/receipts/A05_OPERATION_TIMELINE_READ_ADAPTER.md`

## Repository And Source Wiring

A05 adds `IOperationTimelineReadRepository` with a narrow record-backed adapter over canonical timeline event metadata. The adapter reads by operation id and returns sanitized frontend timeline metadata with a read-only boundary.

`OperationTimelineFrontendReadinessBackendTruthSource` reads through that repository. `Program.cs` registers the source before the run-report fallback:

```text
OperationStatusFrontendReadinessBackendTruthSource
EvidenceMetadataFrontendReadinessBackendTruthSource
ReceiptMetadataFrontendReadinessBackendTruthSource
OperationTimelineFrontendReadinessBackendTruthSource
RunReportFrontendReadinessBackendTruthSource
BackendFrontendReadinessReadApi
```

## Tenant Scope

Timeline visibility is enforced per event record.

- Matching tenant can read tenant-scoped timeline entries.
- Wrong tenant entries are omitted and produce issue metadata.
- Tenantless tenant-scoped entries fail closed.
- Unscoped reads cannot access tenant-scoped entries.
- Global entries must be explicitly marked non-tenant-scoped.
- Mixed visible and non-visible timelines return only visible entries.

Source visibility is not used as a substitute for record ownership.

## Unsafe Material

Unsafe timeline entries are not returned as ordinary timeline text. Entries are redacted when they carry raw payload, private material, hidden material, raw patch/full diff material, authority claims, approval claims, policy-satisfaction claims, workflow-continuation claims, execution claims, or unsafe authority-shaped text.

Redacted entries use:

```text
EventKind: RedactedTimelineEvent
Summary: [redacted: timeline event unavailable]
EvidenceRefs: []
ReceiptRefs: []
```

A05 does not read raw timeline/event payloads. It does not expose hidden reasoning, raw prompts, raw completions, raw tool output, raw patches, full diffs, secrets, or private material.

## Ordering

Timeline entries are returned deterministically:

```text
ObservedAtUtc ascending
EntryId ascending
```

Default or missing timestamps are not replaced with the current time. Invalid timestamp evidence is redacted or rejected.

## Boundary

A05 adds a read-only operation timeline/event repository adapter.

It does not create approval.
It does not satisfy policy.
It does not grant authority.
It does not continue workflow.
It does not execute source apply, rollback, commit, push, PR, merge, release, deployment, memory promotion, or workflow continuation.

Timeline evidence refs remain references only.
Receipt refs remain references only.
Timeline entries cannot approve themselves.
Timeline entries cannot continue themselves.
Missing timelines fail closed.

## Frontend Readiness

The repository-backed timeline source has priority over run-report fallback timelines. Missing repository timelines do not invent fake history. The existing frontend readiness sanitizer still runs over repository timeline output and forces `FrontendReadBoundary.ReadOnlyStatus`.

## Validation

Local validation:

- A05 focused: 40/40 passed
- A04 focused: 35/35 passed
- A03 focused: 28/28 passed
- A02 focused: 21/21 passed
- A01 focused: 20/20 passed
- A01-A05 read adapter stack: 144/144 passed
- Frontend readiness lane plus A01-A05: 332/332 passed
- Stable governance/status corridor through A05: 1157/1157 passed
- Wider confidence lane through A05: 1292/1292 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Review Traps

Reject A05 if:

- run-report timeline still wins over canonical timeline
- repository reads raw timeline/event payloads
- repository exposes raw prompt/completion/tool-output
- repository exposes hidden reasoning/private scratchpad
- repository exposes raw patch/full diff
- repository exposes secrets/private keys/bearer tokens/API keys/passwords
- unsafe timeline entries are returned as safe entries
- missing timeline becomes fake timeline
- timeline becomes approval
- timeline satisfies policy
- timeline enables execution
- timeline continues workflow
- timeline promotes memory
- timeline enables release/deployment
- tenantless entries are visible
- wrong-tenant entries are visible
- event timestamps are replaced with current time
- event ordering is nondeterministic
- UI files are touched
- mutation endpoints are added
- executors are wired
- workflow continuation is added
- memory promotion is added
- release or deploy behavior is added

## Intentionally Unwired

A05 does not add UI, SQL migrations, event creation, event repair, payload viewing, patch package metadata, validation result metadata, role/visibility modeling, source apply, commit, push, PR, rollback, release, deploy, memory promotion, or workflow continuation.
