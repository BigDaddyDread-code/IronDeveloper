# F08 - Tenant Admin Role Boundary Tests

## Review Line

Tenant admin evidence is not platform authority.

## Catalog Boundary Line

A tenant admin role name is not tenant admin authority.

## Purpose

Block F08 adds the minimal Core governance catalog contract for a tenant administrator role and proves that tenant-admin evidence remains evidence only.

This slice records the tenant-scoped role introduced by PR #613 and adds boundary tests showing that tenant admin evidence cannot become platform authority, cross-tenant authority, permission authority, approval authority, policy satisfaction, mutation authority, workflow continuation, operational authority, release/deploy authority, redaction bypass, secret disclosure, or private-reasoning disclosure.

## Files Changed In This PR

- `IronDev.Core/Governance/RoleCatalogValidator.cs`
- `IronDev.IntegrationTests/BlockF08aTenantAdminRoleCatalogContractTests.cs` renamed and reworked as `IronDev.IntegrationTests/BlockF08TenantAdminRoleBoundaryTests.cs`
- `Docs/receipts/F08_TENANT_ADMIN_ROLE_BOUNDARY_TESTS.md`
- Removed superseded `Docs/receipts/F08A_TENANT_ADMIN_ROLE_CATALOG_CONTRACT.md`

## Catalog Role Source

The tenant administrator catalog role was introduced by merged PR #613 and is present in this PR's base.

## Catalog Entry Summary

The base catalog contains exactly one tenant administrator role:

- `GovernanceRoleKind.TenantAdministrator`
- `RoleId = role:f01:tenant-administrator`
- `ScopeKind = TenantScoped`
- `DisplayName = Tenant Administrator`

The entry is catalog-only and descriptive. It names a tenant-scoped responsibility marker for future governed visibility and boundary checks.

## Test Scope

Focused tests cover:

- catalog role name and scope
- role description authority wording
- catalog validator acceptance and unsafe-text rejection
- tenant-admin evidence non-authority flags
- tenant-scoped evidence not becoming global, platform, or cross-tenant authority
- approval, policy, source-safety, validation, and workflow hard stops
- F02 matrix compatibility
- F04/F05/F06/F07 boundary non-overrides
- hostile tenant-admin marker handling
- static no-surface checks
- receipt boundary wording

## Boundary Rules

- Tenant admin role is not platform admin.
- Tenant admin role name is not tenant admin authority.
- Tenant admin role evidence is not role authority.
- Tenant admin role evidence is not platform authority.
- Tenant admin evidence is not cross-tenant visibility.
- Tenant admin evidence is not access.
- Tenant admin evidence is not role assignment.
- Tenant admin evidence is not permission management.
- Tenant admin evidence is not impersonation authority.
- Tenant admin evidence is not approval authority.
- Tenant admin evidence is not policy satisfaction.
- Tenant admin evidence is not validation freshness.
- Tenant admin evidence is not source safety.
- Tenant admin evidence is not diagnostic execution authority.
- Tenant admin evidence is not retry, rollback, or recovery authority.
- Tenant admin evidence is not mutation authority.
- Tenant admin evidence is not workflow continuation.
- Tenant admin evidence is not merge, release, or deployment authority.
- Tenant admin evidence is not redaction bypass.
- Tenant admin evidence is not secret disclosure.
- Tenant admin evidence is not private reasoning disclosure.
- Tenant-scoped evidence does not become global authority.

## Unsafe Markers

The role catalog validator rejects tenant-admin authority-shaped text including:

- `IsTenantAdmin = true`
- `TenantAdminGranted = true`
- `TenantAdminAssigned = true`
- `PlatformAdmin = true`
- `GlobalAdmin = true`
- `CanAccessAllTenants = true`
- `CanBypassTenantBoundary = true`
- `CanImpersonate = true`
- `CanAssignRoles = true`
- `CanGrantAccess = true`
- `CanApprove = true`
- `SatisfiesPolicy = true`
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
- `CanContinueWorkflow = true`
- `BypassRedaction = true`
- `ShowSecrets = true`
- `ShowCredentials = true`
- `ShowRawPayload = true`
- `ShowPrivateReasoning = true`

It also rejects authority-shaped tenant-admin prose such as tenant admin may operate globally, inspect all tenants, grant itself access, assign roles, manage permissions, approve or satisfy policy, continue workflow, bypass redaction, or reveal secrets.

## Test Summary

Focused tests prove:

- the default catalog contains exactly one tenant administrator role
- the tenant administrator role is tenant-scoped only
- the role description contains no authority-grant wording
- the role catalog validator accepts tenant administrator only as a catalog entry
- the role catalog validator rejects tenant-admin authority-shaped text
- tenant-admin evidence keeps all action, access, approval, policy, mutation, workflow, release, deployment, disclosure, and redaction-bypass authority flags false
- tenant-scoped evidence does not become global, platform, or cross-tenant authority
- F02 matrix validation remains conservative
- tenant admin matrix entries do not expose raw payload, credential, or private-reasoning material
- tenant admin visibility does not override F04, F05, F06, or F07 boundaries
- hostile tenant-admin text is rejected or classified hidden
- static scan proves F08 added no production authority surface

## Reported Validation

- F08 focused tests: 79/79 passed
- F07 + F08 compatibility: 152/152 passed
- F06-F08 compatibility: 196/196 passed
- F05-F08 compatibility: 233/233 passed
- F04-F08 compatibility: 286/286 passed
- F01-F08 compatibility: 685/685 passed
- F02 matrix compatibility: 148/148 passed
- F03 hard-stop regressions: 145/145 passed
- E01-E18 corridor: 1630/1630 passed
- C11 secret scan: 9/9 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Known Limitations

F08 does not implement tenant admin identity, tenant admin assignment, tenant admin authority, platform admin authority, global admin authority, cross-tenant access, tenant permission management, role assignment, role grant/revoke, user/group/principal model, impersonation, access control, approval authority, policy satisfaction, validation refresh, source safety proof, diagnostic execution, retry execution, rollback execution, recovery execution, workflow continuation, source mutation, commit, push, PR mutation, ready-for-review, merge, release, deployment, redaction bypass, secret disclosure, private reasoning disclosure, API exposure, CLI exposure, UI exposure, persistence, SQL storage, read model projection, or GitHub sync.

F08 does not implement tenant admin authority. F08 does not implement platform admin authority. F08 does not implement cross-tenant access. F08 does not implement permission management. F08 does not implement workflow continuation.

F08 supersedes the accidental F08a split artifacts from PR #613 so the tenant-admin catalog boundary is represented by this F08 receipt and focused F08 test file.

## Stack

- Base branch: `governance/operator-support-diagnostic-visibility-rules`
- Head branch: `governance/tenant-admin-role-boundary-contract`
- Stack: F08 -> F07 -> F06 -> F05 -> F04 -> F03 -> F02 -> F01 -> Block E tip -> main roll-up later

## Killjoy

Admin of a tenant is not god of the system.
