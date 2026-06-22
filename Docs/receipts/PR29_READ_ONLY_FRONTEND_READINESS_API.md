# PR29 - Read-Only Frontend Readiness API

## Review Line

The frontend reads backend truth. It does not invent authority.

## Killjoy

The first frontend surface is a window, not a cockpit.

## Purpose

PR29 adds stable backend read-only contracts and HTTP endpoints for future frontend consumption. The API exposes governed operation status, timeline, patch package metadata, validation result metadata, evidence metadata, and receipt metadata without adding any frontend UI or mutation surface.

Read API is not action API. Status output is not authority.

## Endpoint List

The read-only HTTP surface is:

- `GET /api/frontend-readiness/operations/{operationId}/status`
- `GET /api/frontend-readiness/operations/{operationId}/timeline`
- `GET /api/frontend-readiness/patch-packages/{packageId}/metadata`
- `GET /api/frontend-readiness/validation-results/{validationResultId}/metadata`
- `GET /api/frontend-readiness/evidence/{evidenceRef}/metadata`
- `GET /api/frontend-readiness/receipts/{receiptRef}/metadata`

The corresponding backend contract is `IFrontendReadinessReadApi`.

## Read-Only Boundary

Every response carries `FrontendReadBoundary`.

Default boundary:

- `ReadOnly = true`
- `StatusOnly = true`
- `CanCreateApproval = false`
- `CanAcceptApproval = false`
- `CanSatisfyPolicy = false`
- `CanExecute = false`
- `CanMutateSource = false`
- `CanRollback = false`
- `CanCommit = false`
- `CanPush = false`
- `CanCreatePullRequest = false`
- `CanMarkReadyForReview = false`
- `CanMerge = false`
- `CanRelease = false`
- `CanDeploy = false`
- `CanPromoteMemory = false`
- `CanContinueWorkflow = false`

## Operation Status Fields

Operation status output exposes:

- operation id
- operation kind
- subject
- state
- blocked reasons
- missing evidence
- next safe actions
- forbidden actions
- evidence refs
- receipt refs
- authority warnings
- observed timestamp
- expiry timestamp when present

The API preserves backend state. It does not turn `Blocked` into `Eligible`, and it does not turn `Eligible` into executable permission.

## Timeline Fields

Timeline output exposes:

- operation id
- entry id
- event kind
- summary
- evidence refs
- receipt refs
- observed timestamp

Timeline is read-only. Timeline is not workflow continuation.

## Patch Package Metadata Fields

Patch package metadata output exposes:

- package id
- repository
- branch
- run id
- patch hash
- proposed file paths
- artifact refs
- evidence refs
- receipt refs
- review summary ref
- known risks ref

Patch package metadata is not source apply authority.

## Validation Result Metadata Fields

Validation result metadata output exposes:

- validation result id
- repository
- branch
- run id
- patch hash
- outcome
- what ran
- what passed
- what failed
- what was skipped
- stale indicator
- evidence refs
- receipt refs

Validation result metadata is not approval, policy satisfaction, source apply authority, validation refresh, or workflow continuation.

## Evidence And Receipt Metadata

Evidence refs and receipt refs are exposed as references only.

- Evidence ref is not approval.
- Receipt ref is not authority.
- Receipt ref is not workflow continuation.
- A PR URL is not a release candidate ref.
- Memory metadata is not memory promotion.

The read API redacts hidden reasoning, scratchpad, raw prompt/completion/tool output, secret-like strings, and private material markers from display text.

## Visibility Proof

Forbidden actions remain visible.

Missing evidence remains visible.

Compact-mode requests cannot hide authority-critical fields. If compact mode is requested, the API still returns missing evidence, forbidden actions, warnings, refs, and the read-only boundary.

## No Mutation Proof

This slice does not add:

- mutation endpoint
- approval creation endpoint
- approval acceptance endpoint
- policy satisfaction endpoint
- source apply endpoint
- rollback endpoint
- commit endpoint
- push endpoint
- pull request creation or update endpoint
- ready-for-review endpoint
- merge endpoint
- release endpoint
- deployment endpoint
- memory promotion endpoint
- workflow continuation endpoint
- frontend/UI implementation
- provider gateway

The controller exposes only `GET` routes.

## Standing Authority Lines

- The frontend reads backend truth. It does not invent authority.
- Read API is not action API.
- Status output is not authority.
- Evidence refs are not approval.
- Receipt refs are not authority.
- Timeline is not workflow continuation.
- Patch package metadata is not source apply.
- Validation result metadata is not approval.
- Freshness metadata is not mutation permission.
- Draft PR metadata is not ready-for-review authority.
- PR URL is not release candidate ref.
- Memory metadata is not memory promotion.
- Forbidden actions must remain visible.
- Next safe action is guidance, not execution.
- UI is not authority.

## Validation

- Focused PR29: 48/48 passed.
- Focused PR28: 55/55 passed.
- Focused PR27: 49/49 passed.
- Focused PR26: 51/51 passed.
- Focused PR25: 38/38 passed.
- Focused PR24: 44/44 passed.
- Focused PR23: 41/41 passed.
- Focused PR22: 30/30 passed.
- Focused PR21: 44/44 passed.
- Focused PR20: 31/31 passed.
- Focused CA: 16/16 passed.
- BJ through PR29 corridor: 852/852 passed.
- Build: 0 errors / 4 warnings.
- `git diff --check`: passed with normal LF/CRLF warnings.
- `git diff --cached --check`: passed with normal LF/CRLF warnings.
- `git diff --check HEAD~1 HEAD`: passed.
