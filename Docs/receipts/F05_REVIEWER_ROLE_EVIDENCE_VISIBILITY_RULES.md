# F05 — Reviewer Role Evidence Visibility Rules

## Review Line

Reviewer role evidence visibility is not reviewer authority.

## Purpose

Block F05 defines a Core-only visibility classification contract for evidence that says something about a reviewer role without turning that evidence into reviewer authority, access authority, approval authority, or policy satisfaction.

F05 produces candidate visibility classifications only. It does not implement reviewer identity, reviewer assignment, reviewer permission, approval acceptance, policy satisfaction, access control, persistence, API, CLI, UI, workflow continuation, source mutation, PR mutation, release, or deployment.

## Files Changed

- `IronDev.Core/Governance/ReviewerRoleEvidenceVisibilityModels.cs`
- `IronDev.Core/Governance/ReviewerRoleEvidenceVisibilityService.cs`
- `IronDev.Core/Governance/ReviewerRoleEvidenceVisibilityValidator.cs`
- `IronDev.IntegrationTests/BlockF05ReviewerRoleEvidenceVisibilityRulesTests.cs`
- `Docs/receipts/F05_REVIEWER_ROLE_EVIDENCE_VISIBILITY_RULES.md`

## Model Summary

The model defines:

- `ReviewerRoleEvidenceVisibilityRequest`
- `ReviewerRoleEvidenceMaterialKind`
- `ReviewerRoleEvidenceRequestedIntent`
- `ReviewerRoleEvidenceVisibilityClassification`
- `ReviewerRoleEvidenceVisibilityDecision`

Candidate outcomes are intentionally named:

- `MetadataOnlyCandidate`
- `SummaryCandidate`
- `RedactedSummaryCandidate`

The decision also preserves explicit false authority fields:

- `GrantsReviewerAuthority = false`
- `GrantsRoleAssignmentAuthority = false`
- `GrantsVisibilityAuthority = false`
- `GrantsAccess = false`
- `GrantsApprovalAuthority = false`
- `SatisfiesPolicy = false`
- `GrantsMutationAuthority = false`
- `GrantsWorkflowContinuation = false`
- `BypassesRedaction = false`
- `DisclosesPrivateReasoning = false`

## Service Behavior

The service fails closed by:

- validating request shape and safe text
- requiring role catalog evidence reference
- requiring visibility matrix evidence reference
- requiring reviewer evidence reference
- confirming the requested role key is the F01 reviewer role
- blocking unknown material and unknown intent
- blocking every action or authority intent
- hiding raw payload, credential material, and private reasoning material
- blocking authority-marker material
- requiring redaction evidence for redacted review rationale summary
- returning bounded candidate classifications for safe read-only material

## Forbidden Authority Outcomes

F05 must never produce:

- `FullAccess`
- `RawPayloadVisible`
- `ReviewerGranted`
- `ApprovalSatisfied`
- `PolicySatisfied`
- `CanApprove`
- `CanMerge`
- `CanMutate`
- `CanContinueWorkflow`
- `CanBypassRedaction`
- `CanViewPrivateReasoning`

Boundary rules:

- Reviewer role evidence is not reviewer authority.
- Reviewer role evidence visibility is not access.
- Reviewer role evidence visibility does not grant access.
- Reviewer role evidence visibility does not approve work.
- Reviewer role evidence visibility does not satisfy policy.
- Reviewer role evidence visibility does not authorize mutation.
- Reviewer role evidence visibility does not bypass redaction.
- A reviewer claim is not a reviewer assignment.
- A reviewer assignment claim is not a granted role.
- A review request is not approval.
- A review comment is not approval.
- A review outcome summary is not policy satisfaction.
- A redacted rationale is not raw reasoning.
- A visibility classification is not a visibility decision.
- A visibility decision is not action authority.

## Tests

Focused tests prove:

- reviewer evidence metadata does not grant reviewer authority
- reviewer assignment claim summary does not grant role assignment authority
- review request summary does not satisfy approval
- review participation summary does not satisfy policy
- review comment summary does not authorize merge
- review outcome summary does not authorize workflow continuation
- redacted rationale summary requires redaction evidence
- raw payload, credential material, and private reasoning are hidden
- authority marker material is blocked
- action, approval, policy, mutation, workflow continuation, visibility grant, redaction bypass, and private reasoning disclosure intents are blocked
- unknown material and unknown intent fail closed
- non-reviewer role fails closed
- missing catalog, matrix, and reviewer evidence fail closed
- every decision has all authority flags false

## Validation

- Focused F05: 37/37 passed
- F04 + F05 compatibility: 90/90 passed
- F01-F05 compatibility: 489/489 passed
- F03 visibility permission hard-stop regressions: 145/145 passed
- E01-E18 corridor: 1630/1630 passed
- C11 secret scan: 9/9 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Known Limitations

F05 does not implement actual reviewer assignment, actual reviewer identity, actual role authority, access control, permission resolution, approval acceptance, policy satisfaction, GitHub reviewer sync, API exposure, CLI exposure, UI visibility, persistence, SQL storage, read model projection, workflow continuation, source mutation, commit, push, PR mutation, release, or deployment.

## Stack

- Base branch: `governance/viewer-readonly-enforcement`
- Head branch: `governance/reviewer-role-evidence-visibility-rules`
- Stack: F05 -> F04 -> F03 -> F02 -> F01 -> Block E tip -> main roll-up later

## Killjoy

A visible reviewer signal is not a reviewer.
