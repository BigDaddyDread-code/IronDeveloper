# F07 - Operator Support Diagnostic Visibility Rules

## Review Line

Operator/support diagnostic visibility is not operational authority.

## Purpose

Block F07 defines a Core-only visibility classification contract for operator/support diagnostic evidence.

F07 produces candidate visibility classifications only. It does not implement operator identity, support identity, operator assignment, support assignment, operator authority, support authority, diagnostic execution, validation refresh, source-safety proof, retry execution, rollback execution, recovery execution, access control, persistence, API, CLI, UI, workflow continuation, source mutation, PR mutation, merge, release, or deployment.

## Files Changed

- `IronDev.Core/Governance/OperatorSupportDiagnosticVisibilityModels.cs`
- `IronDev.Core/Governance/OperatorSupportDiagnosticVisibilityService.cs`
- `IronDev.Core/Governance/OperatorSupportDiagnosticVisibilityValidator.cs`
- `IronDev.IntegrationTests/BlockF07OperatorSupportDiagnosticVisibilityRulesTests.cs`
- `Docs/receipts/F07_OPERATOR_SUPPORT_DIAGNOSTIC_VISIBILITY_RULES.md`

## Model Summary

The model defines:

- `OperatorSupportDiagnosticVisibilityRequest`
- `OperatorSupportDiagnosticMaterialKind`
- `OperatorSupportDiagnosticRequestedIntent`
- `OperatorSupportDiagnosticVisibilityClassification`
- `OperatorSupportDiagnosticVisibilityDecision`

Candidate outcomes are intentionally named:

- `MetadataOnlyCandidate`
- `SummaryCandidate`
- `RedactedSummaryCandidate`

The decision preserves explicit false authority fields:

- `GrantsOperatorAuthority = false`
- `GrantsSupportAuthority = false`
- `GrantsRoleAssignmentAuthority = false`
- `GrantsVisibilityAuthority = false`
- `GrantsAccess = false`
- `GrantsDiagnosticExecutionAuthority = false`
- `RefreshesValidation = false`
- `ProvesSourceSafety = false`
- `GrantsRetryAuthority = false`
- `GrantsRollbackAuthority = false`
- `GrantsRecoveryAuthority = false`
- `GrantsApprovalAuthority = false`
- `SatisfiesPolicy = false`
- `GrantsMutationAuthority = false`
- `GrantsWorkflowContinuation = false`
- `GrantsMergeAuthority = false`
- `GrantsReleaseAuthority = false`
- `GrantsDeploymentAuthority = false`
- `BypassesRedaction = false`
- `DisclosesSecrets = false`
- `DisclosesPrivateReasoning = false`

## Service Behavior

The service fails closed by:

- validating request shape and safe text
- requiring role catalog evidence reference
- requiring visibility matrix evidence reference
- requiring diagnostic evidence reference
- validating the F01 role catalog
- validating the F02 visibility matrix
- confirming the requested role key is an existing F01 operator/support role
- blocking unknown material and unknown intent
- blocking every action or authority intent
- hiding raw logs, raw payloads, raw provider responses, credentials, secrets, and private reasoning
- blocking authority-marker material
- hiding source patch, commit package, push receipt, PR mutation receipt, and release/deploy receipt material unless represented only as safe summaries
- checking the F02 matrix for the requested surface and material candidate
- requiring redaction evidence for redacted error, log, and diagnostic rationale summaries
- returning bounded candidate classifications for safe read-only material

## Forbidden Authority Outcomes

F07 must never produce:

- `CanRunDiagnostic`
- `ValidationRefreshed`
- `SourceSafetyProven`
- `CanRetry`
- `CanRollback`
- `CanRecover`
- `CanMutate`
- `CanApplyPatch`
- `CanCommit`
- `CanPush`
- `CanCreatePullRequest`
- `CanMarkReadyForReview`
- `CanMerge`
- `CanRelease`
- `CanDeploy`
- `CanApprove`
- `PolicySatisfied`
- `WorkflowContinued`
- `FullAccess`
- `RawLogVisible`
- `RawPayloadVisible`
- `SecretVisible`
- `CredentialVisible`
- `CanBypassRedaction`
- `CanViewPrivateReasoning`

Boundary rules:

- Operator/support diagnostic visibility is not operational authority.
- Operator/support diagnostic visibility does not grant access.
- Operator/support diagnostic visibility does not refresh validation.
- Operator/support diagnostic visibility does not prove source safety.
- Operator/support diagnostic visibility does not authorize retry, rollback, or recovery.
- Operator/support diagnostic visibility does not authorize mutation.
- Operator/support diagnostic visibility does not authorize merge, release, or deployment.
- Diagnostic evidence is not permission to execute diagnostics.
- Failure classification is not retry authority.
- Retry classification is not retry execution.
- Rollback readiness is not rollback execution.
- Recovery recommendation is not recovery execution.
- Validation summary is not validation freshness.
- Source-safety evidence is not source-safety authority.
- Operation status is not workflow continuation.
- A visible incident is not permission to mutate incident state.
- A visible log summary is not a raw log.
- A redacted diagnostic rationale is not private reasoning.
- A visibility classification is not a visibility decision.
- A visibility decision is not action authority.
- Policy evidence reference is not policy satisfaction.
- Redaction evidence reference is not redaction bypass.

## Tests

Focused tests prove:

- operation status metadata does not grant operator authority
- operation status summary does not grant support authority
- validation summary does not refresh validation
- failure classification and retry classification summaries do not authorize retry
- rollback readiness summary does not execute rollback
- recovery recommendation summary does not execute recovery
- dependency and environment summaries do not grant access or prove source safety
- queue or runner state summary does not continue workflow
- redacted error, log, and diagnostic rationale summaries require redaction evidence
- raw logs, raw payloads, raw provider responses, credentials, secrets, and private reasoning are not visible
- authority marker material is blocked
- mutation-adjacent materials are hidden unless represented only as safe summaries
- diagnostic execution, validation refresh, source-safety proof, retry, rollback, recovery, mutation, approval, policy, workflow, merge, release, deployment, access grant, redaction bypass, secret disclosure, and private reasoning disclosure intents are blocked
- unknown material and unknown intent fail closed
- non-operator/support role fails closed
- missing catalog, matrix, and diagnostic evidence fail closed
- matrix denial returns hidden
- every decision has all authority flags false

## Validation

- Focused F07: 73/73 passed
- F06 + F07 compatibility: 117/117 passed
- F05-F07 compatibility: 154/154 passed
- F04-F07 compatibility: 207/207 passed
- F01-F07 compatibility: 606/606 passed
- F03 visibility permission hard-stop regressions: 145/145 passed
- E01-E18 corridor: 1630/1630 passed
- C11 secret scan: 9/9 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Known Limitations

F07 does not implement actual operator assignment, actual support assignment, actual operator identity, actual support identity, actual operator authority, actual support authority, access control, permission resolution, diagnostic execution, validation refresh, source safety proof, retry execution, rollback execution, recovery execution, approval authority, policy satisfaction, workflow continuation, GitHub sync, API exposure, CLI exposure, UI visibility, persistence, SQL storage, read model projection, source mutation, commit, push, PR mutation, ready-for-review, merge, release, deployment, secret disclosure, or private reasoning disclosure.

## Stack

- Base branch: `governance/approver-request-decision-visibility-rules`
- Head branch: `governance/operator-support-diagnostic-visibility-rules`
- Stack: F07 -> F06 -> F05 -> F04 -> F03 -> F02 -> F01 -> Block E tip -> main roll-up later

## Killjoy

Seeing the failure is not permission to fix it.
