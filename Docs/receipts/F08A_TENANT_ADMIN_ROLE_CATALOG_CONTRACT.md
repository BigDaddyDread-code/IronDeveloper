# F08a - Tenant Admin Role Catalog Contract

## Review Line

A tenant admin role name is not tenant admin authority.

## Purpose

Block F08a adds the minimal Core governance catalog contract for a tenant admin role.

This slice names one tenant-scoped responsibility type so F08 can remain regression-only. It does not implement tenant admin identity, assignment, authority, access, permissions, approval, policy satisfaction, mutation, workflow continuation, diagnostics, retry, rollback, recovery, merge, release, deployment, redaction bypass, secret disclosure, or private-reasoning disclosure.

## Files Changed

- `IronDev.Core/Governance/RoleCatalogModels.cs`
- `IronDev.Core/Governance/RoleCatalogService.cs`
- `IronDev.Core/Governance/RoleCatalogValidator.cs`
- `IronDev.IntegrationTests/BlockF08aTenantAdminRoleCatalogContractTests.cs`
- `Docs/receipts/F08A_TENANT_ADMIN_ROLE_CATALOG_CONTRACT.md`

## Catalog Entry Summary

The catalog adds:

- `GovernanceRoleKind.TenantAdministrator`
- `RoleId = role:f01:tenant-administrator`
- `ScopeKind = TenantScoped`
- `DisplayName = Tenant Administrator`

The entry is catalog-only and descriptive. It names a tenant-scoped responsibility marker for future governed visibility and boundary checks.

## Scope Summary

The tenant admin role is tenant-scoped only. It is not global scoped, platform scoped, project scoped, operation scoped, workflow scoped, release scoped, or environment scoped.

## Boundary Rules

- Tenant admin role catalog entry is not tenant admin authority.
- Tenant admin role name is not role assignment.
- Tenant admin role name is not permission.
- Tenant admin role name is not access.
- Tenant admin role name is not cross-tenant visibility.
- Tenant admin role name is not platform authority.
- Tenant admin role name is not approval authority.
- Tenant admin role name is not policy satisfaction.
- Tenant admin role name is not mutation authority.
- Tenant admin role name is not diagnostic authority.
- Tenant admin role name is not retry authority.
- Tenant admin role name is not rollback authority.
- Tenant admin role name is not recovery authority.
- Tenant admin role name is not workflow continuation.
- Tenant admin role name is not merge authority.
- Tenant admin role name is not release authority.
- Tenant admin role name is not deployment authority.
- Tenant admin role name is not redaction bypass.
- Tenant admin role name is not secret disclosure.
- Tenant admin role name is not private-reasoning disclosure.

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
- `CanContinue = true`
- `BypassRedaction = true`
- `ShowSecrets = true`
- `ShowCredentials = true`
- `ShowRawPayload = true`
- `ShowPrivateReasoning = true`

## Test Summary

Focused tests prove:

- the default catalog contains exactly one tenant admin role
- the tenant admin role is tenant-scoped only
- the role description contains no authority-grant wording
- the role does not imply tenant, platform, global, cross-tenant, access, role assignment, permission management, impersonation, approval, policy, validation, source-safety, diagnostic, retry, rollback, recovery, mutation, workflow, merge, release, deployment, redaction, secret, credential, raw payload, or private-reasoning authority
- the role catalog validator accepts tenant admin only as a catalog entry
- the role catalog validator rejects tenant-admin authority-shaped text
- F01 catalog validation still passes
- F02 matrix validation still passes
- tenant admin matrix entries do not expose raw payload, credential, or private-reasoning material
- static scan proves no production authority surface was added

## Validation

- F08a focused tests: 64/64 passed
- F07 + F08a compatibility: 137/137 passed
- F06-F08a compatibility: 181/181 passed
- F05-F08a compatibility: 218/218 passed
- F04-F08a compatibility: 271/271 passed
- F01-F08a compatibility: 670/670 passed
- F02 matrix compatibility: 148/148 passed
- F03 hard-stop regressions: 145/145 passed
- E01-E18 corridor: 1630/1630 passed
- C11 secret scan: 9/9 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Known Limitations

F08a does not implement tenant admin identity, tenant admin assignment, tenant admin authority, platform admin authority, global admin authority, cross-tenant access, tenant permission management, role assignment, role grant/revoke, user/group/principal model, impersonation, access control, approval authority, policy satisfaction, validation refresh, source safety proof, diagnostic execution, retry execution, rollback execution, recovery execution, workflow continuation, source mutation, commit, push, PR mutation, ready-for-review, merge, release, deployment, redaction bypass, secret disclosure, private reasoning disclosure, API exposure, CLI exposure, UI exposure, persistence, SQL storage, read model projection, or GitHub sync.

F08a does not implement tenant admin authority. F08a does not implement platform admin authority. F08a does not implement cross-tenant access. F08a does not implement permission management. F08a does not implement workflow continuation.

F08a does not implement F08's broad tenant-admin regression suite. That remains the next regression-only PR.

## Stack

- Base branch: `governance/operator-support-diagnostic-visibility-rules`
- Head branch: `governance/tenant-admin-role-catalog-contract`
- Stack: F08a -> F07 -> F06 -> F05 -> F04 -> F03 -> F02 -> F01 -> Block E tip -> main roll-up later

## Killjoy

Naming the admin is not granting the keys.
