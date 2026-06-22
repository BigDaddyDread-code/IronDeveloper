# A02 - Operation Status Read Repository Adapter

## Purpose

A02 adds a dedicated read-only operation status repository adapter for canonical `GovernedOperationStatus` records.

Review line:

```text
Operation status must come from a canonical backend status record, not from interpretation folklore.
```

Killjoy:

```text
A run report can support status. It must not become the status oracle forever.
```

## Files Changed

- `IronDev.Core/Governance/GovernedOperationStatusReadRepository.cs`
- `IronDev.Infrastructure/Governance/GovernedOperationStatusReadRepository.cs`
- `IronDev.Infrastructure/Governance/OperationStatusFrontendReadinessBackendTruthSource.cs`
- `IronDev.Api/Program.cs`
- `IronDev.IntegrationTests/BlockA02OperationStatusReadRepositoryAdapterTests.cs`
- `Docs/receipts/A02_OPERATION_STATUS_READ_REPOSITORY_ADAPTER.md`

## Source And Repository Wired

- `IGovernedOperationStatusReadRepository` is the read-only canonical operation status repository contract.
- `GovernedOperationStatusReadRepository` is the narrow first adapter over supplied canonical operation status records.
- `OperationStatusFrontendReadinessBackendTruthSource` exposes repository-backed operation status to frontend readiness.
- Production API registration places the operation-status source before the run-report source, so canonical operation status wins over run-report interpretation.

The repository does not create, repair, refresh, or infer operation status records.

## Tenant Scope

Tenant-scoped status records require record-level tenant ownership.

- Matching tenant may read.
- Wrong tenant returns not found.
- Tenantless tenant-scoped records fail closed.
- Unscoped reads cannot access tenant-scoped records.

Record ownership is the boundary. Source-level tenant filtering is not enough.

## Invalid Status Behavior

Stored status is validated through `GovernedOperationStatusValidator`.

Invalid stored status is not exposed as usable authority. It is returned as a blocked diagnostic status with:

- `StoredOperationStatusInvalid`
- `valid-governed-operation-status-record`
- `inspect operation status producer`
- explicit forbidden actions against executing from invalid status

Missing operation status returns not found/null and does not synthesize completed, eligible, or blocked success.

## Frontend Readiness Integration

Frontend readiness operation-status reads now consult the repository-backed source before run reports.

Returned status still passes through the existing `FrontendReadinessReadApi` sanitizer and keeps:

- blocked reasons
- missing evidence
- next safe actions
- forbidden actions
- evidence refs
- receipt refs
- observed timestamp
- expiry timestamp
- authority warnings
- read-only boundary

Compact mode still cannot hide missing evidence or forbidden actions.

## Boundary

```text
A02 adds a read-only operation status repository adapter.
It does not create operation status records.
It does not mutate source.
It does not create approval.
It does not satisfy policy.
It does not execute source apply, rollback, commit, push, PR, merge, release, deployment, memory promotion, or workflow continuation.
```

Operation status is display truth, not authority.

Eligible status is not execution authority.
Completed status is not downstream authority.
Evidence refs are not approval.
Receipt refs are not continuation.
Validation passed is not policy satisfaction.
Run report status is not canonical status unless explicitly recorded as canonical status.
UI cannot decide status.
Memory cannot decide status.

## Intentionally Unwired

- No frontend UI changes.
- No mutation endpoint.
- No executor or provider path.
- No approval creation.
- No policy satisfaction creation.
- No memory promotion.
- No workflow continuation.
- No release or deployment behavior.
- No new SQL migration or broad status projection system.

## Validation

- Focused A02: 21/21 passed
- Focused A01: 20/20 passed
- Existing PR29/PR30/PR31/PR32 frontend readiness lane plus A01/A02: 229/229 passed
- Stable governance/status corridor through A02: 1054/1054 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed with normal LF/CRLF warnings
- `git diff --cached --check`: passed

## Review Traps

Reject this PR if:

- run report interpretation still wins over canonical operation status
- operation status is created or repaired by the read repository
- invalid stored status is treated as usable
- missing records become fake completed or eligible states
- tenantless records are visible
- wrong-tenant records are visible
- timestamps are replaced with current time
- evidence refs become approval
- receipt refs become authority
- eligible status becomes execution authority
- completed status becomes downstream authority
- UI files are touched
- mutation endpoints are added
- executors are wired
- workflow continuation is added
- memory promotion is added
- release or deploy behavior is added
