# A11 - Frontend Readiness OpenAPI Contract Lock

## Review Line

An undocumented boundary is a disappearing boundary.

## Purpose

A11 locks the frontend-readiness OpenAPI contract so frontend consumers cannot silently lose read state, freshness, warnings, errors, missing evidence, forbidden actions, or read-only boundary fields.

## Files Changed

- `IronDev.Api/Controllers/FrontendReadinessController.cs`
- `IronDev.Core/Governance/FrontendReadinessReadModels.cs`
- `IronDev.IntegrationTests/BlockA11FrontendReadinessOpenApiContractLockTests.cs`
- `Docs/receipts/A11_FRONTEND_READINESS_OPENAPI_CONTRACT_LOCK.md`

## Boundary

A11 locks the frontend-readiness OpenAPI contract.
It does not add a new backend read adapter.
It does not add UI.
It does not generate frontend clients.
It does not create approval.
It does not satisfy policy.
It does not grant source apply authority.
It does not execute source apply, rollback, commit, push, PR, merge, release, deployment, memory promotion, or workflow continuation.
The documented API contract must preserve read state, freshness, warnings, errors, missing evidence, forbidden actions, and read-only boundary fields.

## Endpoint Contract Locked

The seven frontend-readiness read endpoints are locked as `GET` endpoints:

- `/api/frontend-readiness/operations/{operationId}/status`
- `/api/frontend-readiness/operations/{operationId}/timeline`
- `/api/frontend-readiness/patch-packages/{packageId}/metadata`
- `/api/frontend-readiness/patch-packages/{packageId}/artifacts`
- `/api/frontend-readiness/validation-results/{validationResultId}/metadata`
- `/api/frontend-readiness/evidence/{evidenceRef}/metadata`
- `/api/frontend-readiness/receipts/{receiptRef}/metadata`

Each read endpoint documents the optional `compact` query parameter and the same read-only envelope shape for success, not found, and unavailable responses.

## Envelope Fields Locked

Every frontend-readiness read envelope must document:

- `status`
- `data`
- `readState`
- `freshness`
- `boundary`
- `mutationOccurred`
- `warnings`
- `errors`

`mutationOccurred` remains a documented read-envelope field and remains false for read endpoints.

## Read-State Fields Locked

`FrontendReadinessReadState` keeps:

- `kind`
- `hasData`
- `isFinal`
- `isFallback`
- `isRedacted`
- `isStale`
- `isExpired`
- `isAuthorityGrant`
- `allowsMutation`
- `freshness`
- `reasons`
- `missingRefs`
- `warnings`
- `nextSafeActions`
- `boundary`

The read-state enum is locked to `Available`, `NotFound`, `Empty`, `Redacted`, `Unavailable`, `Invalid`, `Expired`, `Stale`, `NotVisible`, and `Unknown`.

## Freshness Fields Locked

`FrontendReadinessFreshnessState` keeps:

- `kind`
- `freshnessKnown`
- `isStale`
- `isExpired`
- `observedAtUtc`
- `expiresAtUtc`
- `evaluatedAtUtc`
- `reasons`
- `warnings`

The freshness enum is locked to `Current`, `Stale`, `Expired`, `Unknown`, and `NotApplicable`.

## Boundary Fields Locked

`FrontendReadBoundary` keeps:

- `readOnly`
- `statusOnly`
- `canCreateApproval`
- `canAcceptApproval`
- `canSatisfyPolicy`
- `canExecute`
- `canMutateSource`
- `canRollback`
- `canCommit`
- `canPush`
- `canCreatePullRequest`
- `canMarkReadyForReview`
- `canMerge`
- `canRelease`
- `canDeploy`
- `canPromoteMemory`
- `canContinueWorkflow`

These fields are visible contract fields, not requestable actions.

## Data Schemas Locked

A11 locks the operation status schema fields for blocked reasons, missing evidence, next-safe actions, forbidden actions, evidence refs, receipt refs, authority warnings, boundary, observed time, and expiry time.

A11 locks evidence metadata as reference-only metadata with raw-payload and boundary indicators.

A11 locks receipt metadata as reference-only metadata with visible `grantsAuthority` and `continuesWorkflow` indicators.

A11 locks operation timeline metadata with event evidence refs, receipt refs, and timestamps.

A11 locks patch package metadata separately from source apply authority, including patch hash, proposed file paths, artifact refs, evidence refs, receipt refs, review summary ref, known risks ref, freshness, and boundary.

A11 locks patch package artifact display metadata, including authority warnings, validation stale marker, refs, freshness, and boundary.

A11 locks validation metadata, including validation outcome, what ran, what passed, what failed, what was skipped, stale marker, refs, freshness, and boundary.

## Compact-Mode Behavior

Compact mode is documented as a query parameter, but it does not have a reduced authority-blind schema. Compact responses must still document read state, freshness, boundary, warnings, errors, forbidden actions, missing evidence, and authority warnings.

## No-Mutation Behavior

OpenAPI documentation is not authority.
Schema fields are not executable actions.
Read endpoints remain read-only.
No frontend files or generated clients are added.
No source apply, rollback, commit, push, PR, merge, release, deployment, memory promotion, validation execution, validation refresh, or workflow continuation path is wired.

## Validation

- Focused A11 tests: 256/256 passed.
- A10 compatibility tests: 68/68 passed.
- A08/A09 compatibility tests: 137/137 passed.
- A01-A11 read adapter stack: 710/710 passed.
- Frontend readiness lane plus A01-A11: 898/898 passed.
- Build: 0 errors / 4 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## Review Traps

Reject this slice if:

- OpenAPI omits `readState`, `freshness`, `boundary`, `warnings`, or `errors`
- OpenAPI omits forbidden actions, missing evidence, authority warnings, refs, timestamps, or boundary fields
- read-state enum values disappear
- freshness enum values disappear
- boundary authority flags disappear
- compact mode has a reduced authority-blind schema
- read endpoints gain mutation verbs
- UI files are touched
- frontend clients are generated
- mutation endpoints are added for read resources
- executors are wired
- raw payload readers are added
- action request creation is added
- validation refresh, run, retry, or repair is added
- workflow continuation, memory promotion, release, or deploy behavior is added

## Intentionally Unwired

A11 does not add a new read adapter, SQL migration, durable store, UI, generated frontend client, action request flow, validation execution, validation refresh, raw payload reader, source apply execution, rollback execution, commit, push, PR creation, merge, release, deployment, memory promotion, or workflow continuation.

## Killjoy

If the contract does not name the boundary, the client will forget it exists.
