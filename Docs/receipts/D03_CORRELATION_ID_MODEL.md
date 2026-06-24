# D03 - Correlation ID Model

## Purpose

D03 defines a canonical correlation ID model for linking operation-related status, evidence metadata, receipt metadata, timeline events, governance events, validation results, patch package metadata, and future read-model records.

D01 established canonical `OperationId`.

D02 established read-only lookup by external reference IDs.

D03 establishes correlation identity rules.

## Stack Base

D03 is stacked on `status/operation-lookup-by-reference-ids`.

After D02 merges, D03 should be retargeted or rebased to `main`.

## Files Changed

- `IronDev.Core/Governance/OperationCorrelationModels.cs`
- `IronDev.Core/Governance/OperationCorrelationValidator.cs`
- `IronDev.IntegrationTests/BlockD03CorrelationIdModelTests.cs`
- `Docs/receipts/D03_CORRELATION_ID_MODEL.md`

## Canonical Correlation ID Shape

Canonical correlation IDs use:

- `corr_` prefix
- lowercase hexadecimal suffix, 16 to 64 characters
- or lowercase GUID-without-braces suffix
- no whitespace
- no path separators
- no URLs
- no control characters
- no prose
- no authority words

Valid examples:

- `corr_0123456789abcdef`
- `corr_01234567-89ab-cdef-0123-456789abcdef`

Invalid examples:

- `op_0123456789abcdef`
- `run-123`
- `patch-artifact-123`
- `source-apply-123`
- `commit-package-123`
- `0123456789abcdef0123456789abcdef01234567`
- `push-123`
- `pr-566`
- `receipt-123`
- `evidence-123`
- `approved-for-merge`
- `release-ready`
- `https://github.com/org/repo/pull/566`

## Tenant, Project, And Operation Scope Boundary

Correlation links require:

- tenant scope
- project scope
- canonical `OperationId`
- canonical `CorrelationId`
- known surface kind
- surface ID
- observed timestamp
- source

Correlation groups validate that all links share the same tenant ID, project ID, operation ID, and correlation ID.

Cross-tenant, cross-project, cross-operation, and cross-correlation links fail closed.

## Correlation Is Not Operation Identity

Correlation ID is not `OperationId`.

Correlation ID cannot equal `OperationId`.

Correlation ID cannot be shaped like `OperationId`.

Correlation ID cannot be shaped like run, patch artifact, source apply, commit package, commit SHA, push, PR, receipt, or evidence IDs.

Correlation validation does not mint operation IDs.

Correlation validation does not mint correlation IDs.

Correlation validation does not derive operation IDs from correlation IDs.

Correlation validation does not derive correlation IDs from operation IDs.

## Correlation Is Not Lookup

Correlation links do not perform operation lookup.

Correlation groups do not select records from D02 lookup results.

Correlation groups do not resolve evidence, receipts, missing evidence, forbidden actions, blocked-state explanations, next safe actions, or authority warnings.

## Correlation Is Not Projection

Correlation groups are not timeline projection.

Correlation groups are not status projection.

Correlation groups do not infer timeline ordering beyond deterministic link validation requirements.

Correlation groups do not infer status.

## Correlation Is Not Authority

Correlation IDs connect scoped operation records across status, evidence, receipts, validation, and events. They do not replace OperationId, mint identity, perform lookup, project status, create timelines, approve work, satisfy policy, validate freshness, execute mutation, retry, rollback, merge, release, deploy, promote memory, or continue workflow.

Correlation does not imply:

- approval
- policy satisfaction
- validation freshness
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

## No API, SQL, UI, Projection, Executor, Or Mutation Boundary

D03 does not add API controllers, endpoints, OpenAPI changes, frontend changes, SQL migrations, SQL stores, event projection, status projection, timeline projection, lookup behavior, resolver formatting, missing evidence resolution, forbidden action resolution, receipt resolution, evidence resolution, blocked-state formatting, next-safe-action formatting, authority-warning formatting, executors, runners, source apply, commit, push, PR creation, merge, release, deploy, memory promotion, or workflow continuation.

## Validation

- Focused D03 tests: 56/56 passed
- Focused D02 tests: 39/39 passed
- Focused D01 tests: 54/54 passed
- A02 + A05 read-adapter corridor: 61/61 passed
- Governance/status corridor: 314/314 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Review Traps

Reject D03 if:

- correlation ID can replace operation ID
- correlation ID can be shaped like operation ID
- correlation ID can be shaped like run, patch, apply, commit, push, PR, receipt, or evidence IDs
- correlation ID is used to mint operation identity
- operation ID is derived from correlation ID
- correlation ID is derived from operation ID
- group validation permits cross-tenant links
- group validation permits cross-project links
- group validation permits cross-operation links
- group validation treats correlation as timeline projection
- group validation treats correlation as status projection
- correlation links carry raw payload
- correlation links carry approval, policy, or execution authority fields
- D03 modifies D02 lookup behavior
- D03 adds lookup behavior
- D03 adds API endpoints
- D03 adds SQL persistence
- D03 adds projection behavior
- D03 adds resolver formatting
- D03 adds UI behavior
- D03 touches executors
- D03 touches release, deploy, memory, or workflow authority code

## Killjoy

A correlation ID is a thread through evidence. It is not the operation, not approval, and not permission.
