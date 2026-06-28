# F14 - Missing-Evidence Visibility by Role

## Purpose

F14 adds a Core-only contract that classifies what a role may be told about missing evidence.

Missing-evidence visibility is not evidence satisfaction.

Seeing the gap is not filling it.

## Files Changed

- `IronDev.Core/Governance/MissingEvidenceVisibilityModels.cs`
- `IronDev.Core/Governance/MissingEvidenceVisibilityService.cs`
- `IronDev.Core/Governance/MissingEvidenceVisibilityValidator.cs`
- `IronDev.IntegrationTests/BlockF14MissingEvidenceVisibilityByRoleTests.cs`
- `Docs/receipts/F14_MISSING_EVIDENCE_VISIBILITY_BY_ROLE.md`

## Model Summary

F14 defines:

- `MissingEvidenceKind`
- `MissingEvidenceMaterialKind`
- `MissingEvidenceVisibilityIntent`
- `MissingEvidenceVisibilityClassification`
- `MissingEvidenceVisibilityRequest`
- `MissingEvidenceVisibilityDecision`

The decision model preserves all authority/action/disclosure flags as false:

- evidence satisfaction
- evidence creation
- evidence override
- evidence waiver
- role assignment
- visibility authority
- access grant
- approval acceptance
- policy satisfaction
- validation refresh
- source-safety proof
- diagnostic execution
- retry, rollback, and recovery authority
- mutation authority
- workflow continuation
- merge, release, and deployment authority
- redaction bypass
- secret, credential, raw-payload, and private-reasoning disclosure

Every decision keeps `RequiresSeparateAuthority = true`.

## Service Behavior Summary

`MissingEvidenceVisibilityService` consumes:

- F01 role catalog
- F02 role visibility matrix
- F13 forbidden action catalog
- a missing-evidence visibility request

The service validates request shape, safe text, required evidence references, F01, F02, and F13. It blocks unknown roles, unknown missing-evidence kinds, unknown materials, unknown intents, non-read-only intents, raw/secret/credential/private materials, missing tenant-boundary evidence, missing redaction evidence, and F13-forbidden role-evidence-derived authority.

The service returns only bounded candidate classifications:

- `PresenceOnlyCandidate`
- `CategoryOnlyCandidate`
- `RedactedSummaryCandidate`

or a blocked/hidden classification.

It does not implement evidence creation, evidence satisfaction, evidence override, evidence waiver, remediation, workflow continuation, authorization, permission resolution, access control, or runtime action blocking.

It does not implement evidence satisfaction.

It does not implement workflow continuation.

## Role Visibility Summary

F14 applies conservative role ceilings:

- External viewer: presence-only candidate at most.
- Observer, system-read-only, automation agent, and tenant administrator: category-only candidate at most.
- Reviewer, auditor, security reviewer, release reviewer, approver candidate, operations/support-like roles, and system accountability owner: redacted-summary candidate at most.

Tenant administrator visibility requires tenant-boundary evidence where scoped missing-evidence summaries are involved.

Redacted summaries require redaction evidence.

## F13 Defensive Mapping Summary

F14 maps missing-evidence kinds to F13 forbidden action kinds defensively. Examples:

- approval evidence -> approval acceptance
- policy satisfaction evidence -> policy satisfaction
- validation freshness evidence -> validation refresh
- source safety evidence -> source safety proof
- retry, rollback, recovery, and diagnostic authority -> execution authority categories
- patch apply, commit, push, PR, ready-for-review, merge, release, and deployment evidence -> their corresponding mutation/release/deploy action categories
- secret, credential, raw payload, raw provider response, raw source, raw log, and private reasoning disclosure evidence -> disclosure action categories

This mapping is not a grant. If F13 explicitly forbids role-evidence-derived authority for a mapped category, F14 blocks the visibility request. If F13 has no explicit entry, F14 still returns only bounded visibility and still requires separate authority.

## Boundary Rules

Missing evidence is not evidence.

Missing evidence reference is not evidence.

Missing evidence category is not authority.

Missing evidence summary is not remediation authority.

Seeing missing approval evidence is not approval.

Seeing missing policy evidence is not policy satisfaction.

Seeing missing validation evidence is not validation freshness.

Seeing missing source-safety evidence is not source safety.

Seeing missing diagnostic authority is not diagnostic execution.

Seeing missing rollback authority is not rollback execution.

Seeing missing mutation authority is not mutation authority.

Seeing missing workflow evidence is not workflow continuation.

Seeing missing release evidence is not release authority.

Seeing missing deployment evidence is not deployment authority.

Seeing missing redaction evidence is not redaction bypass.

Missing-evidence visibility is not access, role assignment, visibility authority, an action queue, or a permission system.

## Unsafe Markers

The validator rejects unsafe authority-shaped markers such as:

- `IsEvidenceSatisfied = true`
- `CreatesEvidence = true`
- `OverridesMissingEvidence = true`
- `WaivesEvidenceRequirement = true`
- `CanApprove = true`
- `CanSatisfyPolicy = true`
- `CanRefreshValidation = true`
- `CanProveSourceSafety = true`
- `CanRunDiagnostic = true`
- `CanRetry = true`
- `CanRollback = true`
- `CanRecover = true`
- `CanMutate = true`
- `CanContinueWorkflow = true`
- `CanMerge = true`
- `CanRelease = true`
- `CanDeploy = true`
- `CanBypassRedaction = true`
- `CanViewSecrets = true`
- `CanViewCredentials = true`
- `CanViewRawPayload = true`
- `CanViewPrivateReasoning = true`
- `CanProceed = true`

It also rejects semantic smells that try to turn missing-evidence visibility into evidence satisfaction, approval, remediation authority, workflow continuation, mutation authority, release authority, or redaction bypass.

## Test Summary

Focused F14 tests prove:

- safe requests validate
- unsafe text is rejected and not echoed
- unknown role, missing-evidence kind, material, and intent fail closed
- required evidence references are required
- raw payload, provider response, source, diff, patch, log, credential, secret, and private-reasoning materials are blocked
- non-read-only intents are blocked
- evidence satisfaction, evidence creation, override, waiver, approval, policy, validation refresh, source-safety, execution, mutation, workflow, release/deploy, redaction bypass, and disclosure intents are blocked
- every missing-evidence kind maps to a F13 action where applicable
- F13 forbidden entries block role-evidence-derived authority
- F13 omission does not become allow
- external viewer, read-only, reviewer/auditor, approver, operations/support, tenant administrator, system accountability owner, and automation agent role ceilings hold
- every decision preserves false authority/action/disclosure flags and `RequiresSeparateAuthority = true`
- classification vocabulary avoids allowed/authorized/satisfied/can-proceed language
- static scan proves no API, CLI, UI, OpenAPI, persistence, SQL, provider, authorization handler, permission resolver, access-control, evidence writer, workflow, mutation, release, deploy, retry, rollback, or recovery surface was added

## Reported Validation

Local validation run on `governance/missing-evidence-visibility-by-role`:

- F14 focused tests: 77/77 passed
- F13 + F14 compatibility: 178/178 passed
- F12-F14 compatibility: 221/221 passed
- F11-F14 compatibility: 305/305 passed
- F10-F14 compatibility: 403/403 passed
- F10a + F14 compatibility: 157/157 passed
- F09 + F14 compatibility: not run; F09 boundary tests remain intentionally deferred
- F09a + F14 compatibility: 170/170 passed
- F08-F14 compatibility: 655/655 passed
- F07-F14 compatibility: 728/728 passed
- F06-F14 compatibility: 772/772 passed
- F05-F14 compatibility: 809/809 passed
- F04-F14 compatibility: 862/862 passed
- F01-F14 compatibility: 1261/1261 passed
- F02 matrix compatibility: 148/148 passed
- F03 hard-stop regressions: 145/145 passed
- Exact E01-E18 corridor: 1630/1630 passed
- C11 secret scan: 9/9 passed
- `dotnet build IronDev.slnx --no-restore`: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed after staging the F14 files

GitHub CI has not been claimed. GitHub CI must not be treated as evidence unless it runs and passes on the current head.

## Known Limitations

F14 does not implement:

- evidence creation
- evidence satisfaction
- evidence override
- evidence waiver
- approval acceptance
- policy satisfaction
- validation refresh
- source-safety proof
- diagnostic execution
- retry execution
- rollback execution
- recovery execution
- workflow continuation
- source mutation
- source apply
- commit
- push
- PR mutation
- ready-for-review
- merge
- release
- deployment
- redaction bypass
- secret disclosure
- credential disclosure
- raw payload disclosure
- private reasoning disclosure
- authorization
- permission resolution
- access control
- role assignment
- role grant or revoke
- identity, user, group, or principal model
- runtime action blocking
- API exposure
- CLI exposure
- UI exposure
- OpenAPI generation
- screen access
- endpoint invocation
- route guards
- client-side permission decisions
- persistence
- SQL storage
- read model projection
- GitHub sync

F09 boundary tests remain intentionally deferred.

## Stack

- Block: F14
- Branch: `governance/missing-evidence-visibility-by-role`
- Base: `governance/forbidden-action-catalog-by-role`
- Stack: F14 -> F13 -> F12 -> F11 -> F10 -> F10a -> F09a -> F08 -> F07 -> F06 -> F05 -> F04 -> F03 -> F02 -> F01 -> Block E tip -> main roll-up later

## Review Line

Missing-evidence visibility is not evidence satisfaction.

## Killjoy Line

Seeing the gap is not filling it.
