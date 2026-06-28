# F06 - Approver Role Request Decision Visibility Rules

## Review Line

Approver request/decision visibility is not approval authority.

## Purpose

Block F06 defines a Core-only visibility classification contract for approver-role request and approver-role decision evidence.

F06 produces candidate visibility classifications only. It does not implement approver identity, approver assignment, approver authority, approval acceptance, policy satisfaction, access control, persistence, API, CLI, UI, workflow continuation, source mutation, PR mutation, ready-for-review, merge, release, or deployment.

## Files Changed

- `IronDev.Core/Governance/ApproverRoleRequestDecisionVisibilityModels.cs`
- `IronDev.Core/Governance/ApproverRoleRequestDecisionVisibilityService.cs`
- `IronDev.Core/Governance/ApproverRoleRequestDecisionVisibilityValidator.cs`
- `IronDev.IntegrationTests/BlockF06ApproverRoleRequestDecisionVisibilityRulesTests.cs`
- `Docs/receipts/F06_APPROVER_ROLE_REQUEST_DECISION_VISIBILITY_RULES.md`

## Model Summary

The model defines:

- `ApproverRoleRequestDecisionVisibilityRequest`
- `ApproverRoleRequestDecisionMaterialKind`
- `ApproverRoleRequestDecisionRequestedIntent`
- `ApproverRoleRequestDecisionVisibilityClassification`
- `ApproverRoleRequestDecisionVisibilityDecision`

Candidate outcomes are intentionally named:

- `MetadataOnlyCandidate`
- `SummaryCandidate`
- `RedactedSummaryCandidate`

The decision preserves explicit false authority fields:

- `GrantsApproverAuthority = false`
- `GrantsRoleAssignmentAuthority = false`
- `CreatesApproverRequest = false`
- `AcceptsApproverRequest = false`
- `GrantsVisibilityAuthority = false`
- `GrantsAccess = false`
- `GrantsApprovalAuthority = false`
- `AcceptsApproval = false`
- `SatisfiesPolicy = false`
- `GrantsMutationAuthority = false`
- `GrantsWorkflowContinuation = false`
- `GrantsMergeAuthority = false`
- `GrantsReleaseAuthority = false`
- `GrantsDeploymentAuthority = false`
- `BypassesRedaction = false`
- `DisclosesPrivateReasoning = false`

## Service Behavior

The service fails closed by:

- validating request shape and safe text
- requiring role catalog evidence reference
- requiring visibility matrix evidence reference
- requiring approver request or decision evidence reference
- validating the F01 role catalog
- validating the F02 visibility matrix
- confirming the requested role key is the F01 approver candidate role
- blocking unknown material and unknown intent
- blocking every action or authority intent
- hiding raw payload, credential material, and private reasoning material
- blocking authority-marker material
- checking the F02 matrix for the requested surface and material candidate
- requiring redaction evidence for redacted approver request or decision rationale summary
- returning bounded candidate classifications for safe read-only material

## Forbidden Authority Outcomes

F06 must never produce:

- `ApproverGranted`
- `ApproverAssigned`
- `ApprovalAccepted`
- `ApprovalSatisfied`
- `PolicySatisfied`
- `CanApprove`
- `CanMerge`
- `CanMutate`
- `CanContinueWorkflow`
- `CanRelease`
- `CanDeploy`
- `FullAccess`
- `RawPayloadVisible`
- `CanBypassRedaction`
- `CanViewPrivateReasoning`

Boundary rules:

- Approver role request visibility is not approver assignment.
- Approver role decision visibility is not approver authority.
- Approver request/decision visibility does not grant access.
- Approver request/decision visibility does not accept approval.
- Approver request/decision visibility does not satisfy policy.
- Approver request/decision visibility does not authorize mutation.
- Approver request/decision visibility does not authorize merge, release, or deployment.
- An approver role request is not a granted role.
- An approver role decision summary is not accepted approval.
- An approval package reference is not accepted approval.
- A visible approval-shaped record is not approval.
- A visible decision outcome is not policy satisfaction.
- A redacted approval rationale is not raw reasoning.
- A visibility classification is not a visibility decision.
- A visibility decision is not action authority.
- Policy evidence reference is not policy satisfaction.
- Redaction evidence reference is not redaction bypass.

## Tests

Focused tests prove:

- approver request metadata does not grant approver authority
- approver request summary does not create an approver request
- approver request rationale requires redaction evidence
- approver decision metadata does not grant role assignment authority
- approver decision summary does not accept approval
- approver decision outcome summary does not satisfy policy
- approval package reference summary does not accept approval
- approval package reference summary does not satisfy policy
- raw payload, credential material, and private reasoning are hidden
- authority marker material is blocked
- create approver request, grant role, assign role, approval, policy, mutation, workflow, merge, release, deploy, visibility grant, redaction bypass, and private reasoning disclosure intents are blocked
- unknown material and unknown intent fail closed
- non-approver role fails closed
- missing catalog, matrix, and approver request/decision evidence fail closed
- matrix denial returns hidden
- every decision has all authority flags false

## Validation

- Focused F06: 44/44 passed
- F05 + F06 compatibility: 81/81 passed
- F04-F06 compatibility: 134/134 passed
- F01-F06 compatibility: 533/533 passed
- F03 visibility permission hard-stop regressions: 145/145 passed
- E01-E18 corridor: 1630/1630 passed
- C11 secret scan: 9/9 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Known Limitations

F06 does not implement actual approver assignment, actual approver identity, actual approver authority, approval acceptance, policy satisfaction, access control, permission resolution, GitHub reviewer/approver sync, API exposure, CLI exposure, UI visibility, persistence, SQL storage, read model projection, workflow continuation, source mutation, commit, push, PR mutation, ready-for-review, merge, release, or deployment.

## Stack

- Base branch: `governance/reviewer-role-evidence-visibility-rules`
- Head branch: `governance/approver-request-decision-visibility-rules`
- Stack: F06 -> F05 -> F04 -> F03 -> F02 -> F01 -> Block E tip -> main roll-up later

## Killjoy

Seeing an approval-shaped decision is not approval.
