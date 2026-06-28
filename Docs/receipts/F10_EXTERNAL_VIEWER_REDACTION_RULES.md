# F10 - External Viewer Redaction Rules

## Review Line

External viewer redaction is not access authority.

## Purpose

Block F10 defines a Core-only redaction classification contract for external-viewer visibility.

This slice decides whether material intended for an external viewer must be hidden, metadata-only candidate visibility, or redacted-summary candidate visibility.

Redaction rules are a brake, not a key.

## Precondition Result

F10 required an existing external viewer role or equivalent external-facing read-only role before adding redaction classification rules.

This PR uses the F10a catalog role:

- `GovernanceRoleKind.ExternalViewer`
- `RoleId = role:f01:external-viewer`
- `DisplayName = External Viewer`

No external viewer role is created by this F10 PR.

F09 system owner boundary tests remain intentionally deferred. This PR is based on F10a, which is based on F09a.

## Files Changed

- `IronDev.Core/Governance/ExternalViewerRedactionModels.cs`
- `IronDev.Core/Governance/ExternalViewerRedactionService.cs`
- `IronDev.Core/Governance/ExternalViewerRedactionValidator.cs`
- `IronDev.Core/Governance/RoleVisibilityMatrixService.cs`
- `IronDev.IntegrationTests/BlockF10ExternalViewerRedactionRulesTests.cs`
- `Docs/receipts/F10_EXTERNAL_VIEWER_REDACTION_RULES.md`

## Model Summary

F10 adds:

- `ExternalViewerRedactionRequest`
- `ExternalViewerRedactionMaterialKind`
- `ExternalViewerRedactionRequestedIntent`
- `ExternalViewerRedactionClassification`
- `ExternalViewerRedactionDecision`
- `ExternalViewerRedactionValidationResult`

The decision model carries explicit false authority and disclosure flags for external viewer authority, role assignment, visibility authority, access, share links, raw export, cross-tenant visibility, platform visibility, approval, policy satisfaction, validation refresh, source safety proof, diagnostic execution, retry, rollback, recovery, mutation, workflow continuation, merge, release, deployment, redaction bypass, secrets, credentials, raw payload/source/logs, and private reasoning.

## Service Behavior Summary

The classifier:

- validates request shape and safe text
- requires role catalog evidence reference
- requires visibility matrix evidence reference
- requires source evidence reference
- validates the F01 role catalog
- validates the F02 visibility matrix
- confirms the requested role key is the F10a external viewer role
- blocks unknown material and unknown intent
- blocks every non-read-only intent
- blocks raw payload, raw provider response, raw source, raw diff, raw patch, and raw log
- blocks secrets, credentials, and private reasoning
- blocks authority-marker material
- blocks approval records and policy satisfaction records unless represented as redacted summaries
- blocks source patch, commit package, push receipt, PR mutation receipt, and release/deploy receipt unless represented as redacted receipt summary
- checks F02 matrix candidate visibility for the requested surface/material
- requires tenant-boundary evidence for tenant-scoped and project-scoped external metadata
- requires redaction evidence for every redacted summary
- returns only bounded candidate classifications

F10 also adds the smallest F02 matrix hints needed for external-viewer metadata and redacted-summary candidate checks. These hints remain descriptive and require separate role assignment and visibility decisions.

## Boundary Rules

- External viewer redaction is not access authority.
- External viewer role evidence is not external access.
- Redacted visibility is not raw visibility.
- Redacted summary is not raw payload.
- Redacted summary is not raw source.
- Redacted summary is not raw log.
- Redacted summary is not private reasoning.
- Redaction evidence is not redaction bypass.
- Tenant-boundary evidence is not cross-tenant visibility.
- Visibility matrix evidence is not access.
- Policy evidence reference is not policy satisfaction.
- Approval summary is not approval authority.
- Policy summary is not policy satisfaction.
- Validation summary is not validation freshness.
- Release readiness summary is not release authority.
- Receipt summary is not downstream authority.
- External viewer cannot see secrets, credentials, raw payloads, raw provider responses, raw source, raw logs, or private reasoning.
- A visibility classification is not a visibility decision.
- A visibility decision is not action authority.

## Unsafe Markers

The validator rejects external-viewer authority-shaped text including:

- `ExternalAccessGranted = true`
- `ExternalViewerGranted = true`
- `ExternalViewerAssigned = true`
- `CanCreateShareLink = true`
- `CanExportRawData = true`
- `CanViewRawPayload = true`
- `CanViewRawSource = true`
- `CanViewRawLog = true`
- `CanViewSecrets = true`
- `CanViewCredentials = true`
- `CanViewPrivateReasoning = true`
- `CanBypassRedaction = true`
- `CanAccessAllTenants = true`
- `CanViewPlatformData = true`
- `CanApprove = true`
- `SatisfiesPolicy = true`
- `ValidationRefreshed = true`
- `SourceSafetyProven = true`
- `CanRunDiagnostic = true`
- `CanRetry = true`
- `CanRollback = true`
- `CanRecover = true`
- `CanMutate = true`
- `CanContinueWorkflow = true`
- `CanMerge = true`
- `CanRelease = true`
- `CanDeploy = true`

It also rejects authority-shaped external-viewer prose such as external viewer may see raw payload, see secrets, see credentials, see private reasoning, bypass redaction, inspect all tenants, access platform data, export raw data, receive provider response, approve policy, or continue workflow.

## Test Summary

Focused tests prove:

- external viewer role precondition is satisfied by F10a
- external viewer role evidence does not grant access, assignment, visibility, share-link, export, approval, policy, mutation, workflow, merge, release, deployment, redaction bypass, or disclosure authority
- public and operation metadata can become metadata-only candidates without authority
- tenant-scoped and project-scoped metadata require tenant-boundary evidence
- every redacted summary requires redaction evidence
- redacted operation status, validation, review, approval, diagnostic, audit, release-readiness, policy, error, log, and receipt summaries remain redacted-summary candidates only
- raw payload, raw provider response, raw source, raw diff, raw patch, raw log, credentials, secrets, and private reasoning are not visible
- authority-marker, approval-record, policy-satisfaction-record, mutation-adjacent, and release/deploy materials are blocked
- external access, share-link, raw export, redaction bypass, cross-tenant, platform, approval, policy, validation, source-safety, diagnostic, retry, rollback, recovery, mutation, workflow, merge, release, and deployment intents are blocked
- unknown material and unknown intent fail closed
- non-external-viewer role fails closed
- missing catalog, matrix, source, redaction, or tenant-boundary evidence fails closed
- matrix denial returns hidden/not-visible
- hostile external-viewer text is hidden and not echoed into fingerprints
- every decision has all authority and disclosure flags false
- static scan proves no identity, permission, access-control, API, CLI, UI, persistence, provider, approval, policy, diagnostic, retry, rollback, recovery, mutation, workflow, merge, release, deploy, export, share-link, or redaction-bypass surface was added

## Reported Validation

- F10 focused tests: 98/98 passed
- F09 + F10 compatibility: not run; F09 is intentionally deferred and F10 is based on F10a/F09a
- F09a + F10 compatibility: 191/191 passed
- F08-F10 compatibility: 270/270 passed
- F07-F10 compatibility: 343/343 passed
- F06-F10 compatibility: 387/387 passed
- F05-F10 compatibility: 424/424 passed
- F04-F10 compatibility: 477/477 passed
- F01-F10 compatibility: 956/956 passed
- F02 matrix compatibility: 148/148 passed
- F03 hard-stop regressions: 145/145 passed
- E01-E18 corridor: 1630/1630 passed
- C11 secret scan: 9/9 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Known Limitations

F10 does not implement external viewer identity, external viewer assignment, external viewer authority, external access grant, share links, raw exports, cross-tenant access, platform visibility, permission management, role assignment, role grant/revoke, user/group/principal model, access control, approval authority, policy satisfaction, validation refresh, source safety proof, diagnostic execution, retry execution, rollback execution, recovery execution, workflow continuation, source mutation, commit, push, PR mutation, ready-for-review, merge, release, deployment, redaction bypass, secret disclosure, credential disclosure, raw payload disclosure, raw provider response disclosure, raw source disclosure, raw log disclosure, private reasoning disclosure, API exposure, CLI exposure, UI exposure, persistence, SQL storage, read model projection, or GitHub sync.

F10 does not implement external access grant.

F10 does not implement share links.

F10 does not implement raw exports.

F10 does not implement redaction bypass.

F10 does not implement a real redaction engine, external sharing/export tooling, invite flow, permission resolver, or runtime access-control check.

F10 does not implement F09's system owner boundary-test suite. F09 remains intentionally deferred in this stack.

## Stack

- Base branch: `governance/external-viewer-role-catalog-contract`
- Head branch: `governance/external-viewer-redaction-rules`
- Stack: F10 -> F10a -> F09a -> F08 -> F07 -> F06 -> F05 -> F04 -> F03 -> F02 -> F01 -> Block E tip -> main roll-up later
- F09 boundary tests are not included and remain deferred.

## Killjoy

A redacted view is not a permission slip.
