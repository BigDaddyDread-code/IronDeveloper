# F10a - External Viewer Role Catalog Contract

## Review Line

External viewer role evidence is not external access authority.

## Catalog Boundary Line

External viewer evidence is not visibility authority.

## Purpose

Block F10a adds the minimal Core governance catalog contract for an external viewer role.

F10 required an existing external viewer role or equivalent external-facing read-only role before adding redaction classification rules. The precondition failed: the catalog had `Observer`, `Auditor`, `SystemReadOnly`, `TenantAdministrator`, `SystemAccountabilityOwner`, `Reviewer`, `ApproverCandidate`, `OperationsReviewer`, `ExecutorOperatorCandidate`, and `AutomationAgent`, but none of those are equivalent to an external viewer role.

This slice adds the missing descriptive role and stops there.

## Files Changed

- `IronDev.Core/Governance/RoleCatalogModels.cs`
- `IronDev.Core/Governance/RoleCatalogService.cs`
- `IronDev.Core/Governance/RoleCatalogValidator.cs`
- `IronDev.IntegrationTests/BlockF10aExternalViewerRoleCatalogContractTests.cs`
- `Docs/receipts/F10A_EXTERNAL_VIEWER_ROLE_CATALOG_CONTRACT.md`

## Catalog Entry Summary

The catalog adds exactly one external viewer role:

- `GovernanceRoleKind.ExternalViewer`
- `RoleId = role:f01:external-viewer`
- `ScopeKind = ProjectScoped`
- `DisplayName = External Viewer`

The entry is catalog-only and descriptive. It names an external-facing read-only responsibility marker for future governed redaction and visibility checks.

## Boundary Rules

- External viewer role evidence is not external access authority.
- External viewer evidence is not visibility authority.
- External viewer evidence is not role assignment.
- External viewer evidence is not access.
- External viewer evidence is not a share link.
- External viewer evidence is not raw export authority.
- External viewer evidence is not cross-tenant visibility.
- External viewer evidence is not platform visibility.
- External viewer evidence is not approval authority.
- External viewer evidence is not policy satisfaction.
- External viewer evidence is not validation freshness.
- External viewer evidence is not source safety.
- External viewer evidence is not diagnostic execution authority.
- External viewer evidence is not retry, rollback, or recovery authority.
- External viewer evidence is not mutation authority.
- External viewer evidence is not workflow continuation.
- External viewer evidence is not merge, release, or deployment authority.
- External viewer evidence is not redaction bypass.
- External viewer evidence is not secret, credential, raw payload, raw source, raw log, or private-reasoning disclosure.

## Unsafe Markers

The role catalog validator rejects external-viewer authority-shaped text including:

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

- the default catalog contains exactly one external viewer role
- the role is catalog-only, external-facing, and read-only
- the role text contains no authority-grant wording
- the role catalog validator accepts external viewer only as a catalog entry
- the role catalog validator rejects external-viewer authority-shaped text
- the default catalog remains valid
- the default F02 matrix remains valid
- external viewer matrix entries do not expose raw payload, credential, or private-reasoning material
- external viewer role-name evidence cannot stand in for access, visibility, share-link, export, redaction, approval, policy, validation, source-safety, workflow, merge, release, or deployment evidence
- static scan proves no production access, identity, share-link, export, mutation, or downstream authority surface was added

## Reported Validation

- F10a focused tests: 80/80 passed
- F09 + F10 compatibility: not run; F09 is intentionally deferred and F10 was split to F10a
- F09a + F10a compatibility: 173/173 passed
- F08-F10a compatibility: 252/252 passed
- F07-F10a compatibility: 325/325 passed
- F06-F10a compatibility: 369/369 passed
- F05-F10a compatibility: 406/406 passed
- F04-F10a compatibility: 459/459 passed
- F01-F10a compatibility: 858/858 passed
- F02 matrix compatibility: 148/148 passed
- F03 hard-stop regressions: 145/145 passed
- E01-E18 corridor: 1630/1630 passed
- C11 secret scan: 9/9 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Known Limitations

F10a does not implement external viewer identity, external viewer assignment, external viewer authority, external access grant, share links, raw exports, cross-tenant access, platform visibility, permission management, role assignment, role grant/revoke, user/group/principal model, access control, approval authority, policy satisfaction, validation refresh, source safety proof, diagnostic execution, retry execution, rollback execution, recovery execution, workflow continuation, source mutation, commit, push, PR mutation, ready-for-review, merge, release, deployment, redaction bypass, secret disclosure, credential disclosure, raw payload disclosure, raw provider response disclosure, raw source disclosure, raw log disclosure, private reasoning disclosure, API exposure, CLI exposure, UI exposure, persistence, SQL storage, read model projection, or GitHub sync.

F10a does not implement F10's external viewer redaction classification contract. F10 remains the next Core-only redaction-rule PR after this catalog role exists.

F10a does not implement F09's system owner boundary-test suite. F09 remains intentionally deferred in this stack, and this PR is based on F09a.

F10a does not implement external viewer authority.
F10a does not implement external access grant.
F10a does not implement share links.
F10a does not implement raw exports.
F10a does not implement redaction bypass.

## Stack

- Base branch: `governance/system-owner-role-catalog-contract`
- Head branch: `governance/external-viewer-role-catalog-contract`
- Stack: F10a -> F09a -> F08 -> F07 -> F06 -> F05 -> F04 -> F03 -> F02 -> F01 -> Block E tip -> main roll-up later
- F09 boundary tests are not included and remain deferred.

## Killjoy

A redacted view is not a permission slip.
