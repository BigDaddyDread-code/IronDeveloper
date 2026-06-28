# F09a - System Owner Role Catalog Contract

## Review Line

System owner evidence is not system authority.

## Catalog Boundary Line

A system owner role name is not system authority.

## Purpose

Block F09a adds the minimal Core governance catalog contract for a system accountability owner role.

F09 required an existing system owner or equivalent system-scoped ownership role before adding boundary tests. The precondition failed: the catalog had `SystemReadOnly`, `AutomationAgent`, `Auditor`, `TenantAdministrator`, `OperationsReviewer`, and `ExecutorOperatorCandidate`, but none of those are equivalent to a system owner role.

This slice adds the missing descriptive role and stops there.

## Files Changed

- `IronDev.Core/Governance/RoleCatalogModels.cs`
- `IronDev.Core/Governance/RoleCatalogService.cs`
- `IronDev.Core/Governance/RoleCatalogValidator.cs`
- `IronDev.IntegrationTests/BlockF09aSystemOwnerRoleCatalogContractTests.cs`
- `Docs/receipts/F09A_SYSTEM_OWNER_ROLE_CATALOG_CONTRACT.md`

## Catalog Entry Summary

The catalog adds exactly one system accountability owner role:

- `GovernanceRoleKind.SystemAccountabilityOwner`
- `RoleId = role:f01:system-accountability-owner`
- `ScopeKind = GlobalCatalog`
- `DisplayName = System Accountability Owner`

The entry is catalog-only and descriptive. It names accountability responsibility for future governed visibility and boundary checks.

## Boundary Rules

- System owner role name is not system authority.
- System owner evidence is not platform authority.
- System owner evidence is not root authority.
- System owner evidence is not global authority.
- System owner evidence is not identity authority.
- System owner evidence is not role assignment.
- System owner evidence is not permission management.
- System owner evidence is not access.
- System owner evidence is not cross-tenant visibility.
- System owner evidence is not tenant-boundary override.
- System owner evidence is not impersonation authority.
- System owner evidence is not approval authority.
- System owner evidence is not policy satisfaction.
- System owner evidence is not validation freshness.
- System owner evidence is not source safety.
- System owner evidence is not diagnostic execution authority.
- System owner evidence is not retry, rollback, or recovery authority.
- System owner evidence is not mutation authority.
- System owner evidence is not workflow continuation.
- System owner evidence is not merge, release, or deployment authority.
- System owner evidence is not break-glass authority.
- System owner evidence is not governance override authority.
- System owner evidence is not redaction bypass.
- System owner evidence is not secret, credential, raw payload, or private-reasoning disclosure.

## Unsafe Markers

The role catalog validator rejects system-owner authority-shaped text including:

- `IsSystemOwner = true`
- `SystemOwnerGranted = true`
- `SystemOwnerAssigned = true`
- `PlatformOwner = true`
- `RootOwner = true`
- `GlobalOwner = true`
- `CanAccessEverything = true`
- `CanAccessAllTenants = true`
- `CanBypassTenantBoundary = true`
- `CanImpersonate = true`
- `CanAssignRoles = true`
- `CanGrantAccess = true`
- `CanManagePermissions = true`
- `CanApprove = true`
- `SatisfiesPolicy = true`
- `CanRefreshValidation = true`
- `CanProveSourceSafety = true`
- `CanRunDiagnostic = true`
- `CanRetry = true`
- `CanRollback = true`
- `CanRecover = true`
- `CanMutate = true`
- `CanApplyPatch = true`
- `CanCommit = true`
- `CanPush = true`
- `CanCreatePullRequest = true`
- `CanReadyForReview = true`
- `CanMerge = true`
- `CanRelease = true`
- `CanDeploy = true`
- `CanContinue = true`
- `CanBreakGlass = true`
- `CanOverrideGovernance = true`
- `BypassRedaction = true`
- `ShowSecrets = true`
- `ShowCredentials = true`
- `ShowRawPayload = true`
- `ShowPrivateReasoning = true`

It also rejects authority-shaped system-owner prose such as system owner may operate globally, inspect all tenants, grant itself access, assign roles, manage permissions, approve or satisfy policy, continue workflow, override governance, bypass redaction, reveal secrets, or execute break glass.

## Test Summary

Focused tests prove:

- the default catalog contains exactly one system accountability owner role
- the role is catalog-only and descriptive
- the role text contains no authority-grant wording
- the role catalog validator accepts system accountability owner only as a catalog entry
- the role catalog validator rejects system-owner authority-shaped text
- the default catalog remains valid
- the default F02 matrix remains valid
- system owner matrix entries do not expose raw payload, credential, or private-reasoning material
- system owner role-name evidence cannot stand in for approval, policy, source-safety, validation, workflow, merge, release, or deployment evidence
- static scan proves no production authority surface was added

## Reported Validation

- F09a focused tests: 93/93 passed
- F08 + F09a compatibility: 172/172 passed
- F07-F09a compatibility: 245/245 passed
- F06-F09a compatibility: 289/289 passed
- F05-F09a compatibility: 326/326 passed
- F04-F09a compatibility: 379/379 passed
- F01-F09a compatibility: 778/778 passed
- F02 matrix compatibility: 148/148 passed
- F03 hard-stop regressions: 145/145 passed
- E01-E18 corridor: 1630/1630 passed
- C11 secret scan: 9/9 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Known Limitations

F09a does not implement system owner identity, system owner assignment, system owner authority, platform owner authority, root authority, global authority, cross-tenant access, tenant-boundary override, permission management, role assignment, role grant/revoke, user/group/principal model, impersonation, access control, approval authority, policy satisfaction, validation refresh, source safety proof, diagnostic execution, retry execution, rollback execution, recovery execution, workflow continuation, source mutation, commit, push, PR mutation, ready-for-review, merge, release, deployment, break-glass authority, governance override, redaction bypass, secret disclosure, credential disclosure, raw payload disclosure, private reasoning disclosure, API exposure, CLI exposure, UI exposure, persistence, SQL storage, read model projection, or GitHub sync.

F09a does not implement system owner authority.
F09a does not implement platform owner authority.
F09a does not implement root authority.
F09a does not implement break-glass authority.
F09a does not implement governance override.

F09a does not implement F09's system owner boundary-test suite. F09 remains the next regression-only PR after this catalog role exists.

## Stack

- Base branch: `governance/tenant-admin-role-boundary-contract`
- Head branch: `governance/system-owner-role-catalog-contract`
- Stack: F09a -> F08 -> F07 -> F06 -> F05 -> F04 -> F03 -> F02 -> F01 -> Block E tip -> main roll-up later

## Killjoy

Owning accountability is not owning the controls.
