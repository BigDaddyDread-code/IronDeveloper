# F09 - System Owner Role Boundary Tests

## Purpose

F09 closes the deferred system-owner boundary gap by proving the existing `SystemAccountabilityOwner` role remains an accountability marker, not system/root/platform authority.

F09a created the SystemAccountabilityOwner catalog role.

F09 proper adds boundary tests only.

System owner evidence is not system authority.

Owning accountability is not owning the controls.

## Why F09 Was Deferred

F09 was deferred because the catalog role had to exist before its boundary could be tested. F09a introduced the catalog entry. F09 now verifies that the role name, F13 denials, F14 missing-evidence visibility, and F15 audit records do not turn accountability wording into control.

## Files Changed

- `IronDev.IntegrationTests/BlockF09SystemOwnerRoleBoundaryTests.cs`
- `Docs/receipts/F09_SYSTEM_OWNER_ROLE_BOUNDARY_TESTS.md`

## Role Catalog Boundary Summary

The tests verify that the F01 catalog contains exactly one `GovernanceRoleKind.SystemAccountabilityOwner` entry with stable role ID:

- `role:f01:system-accountability-owner`

The role is catalog metadata only. It may identify accountability responsibility and governed visibility context. It does not grant system authority, root access, platform administration, tenant-boundary override, role assignment, permission management, access, visibility, approval, policy, validation, source safety, execution, mutation, workflow, merge, release, deployment, redaction bypass, or sensitive/raw/private disclosure.

The tests also verify the system-owner entry is not confused with tenant administrator, automation agent, or executor operator candidate roles.

## F13 Forbidden-Action Coverage Summary

The tests use `ForbiddenActionCatalogService` to prove `SystemAccountabilityOwner` role evidence is forbidden for role/control-shaped authority including:

- role assignment, role grant, and role revoke
- permission management
- access and visibility grant
- platform and cross-tenant visibility
- impersonation
- approval acceptance
- policy satisfaction
- validation refresh
- source-safety proof
- diagnostic, retry, rollback, and recovery execution
- source mutation, patch apply, commit, push, and pull request creation
- workflow continuation
- merge, release, and deployment
- redaction bypass
- secret, credential, raw payload, and private-reasoning disclosure

The tests also prove F13 omission is not allow. An omitted action category still returns separate-authority-required with all authority flags false.

## F14 Missing-Evidence Visibility Coverage Summary

The tests use `MissingEvidenceVisibilityService` to prove system-owner missing-evidence visibility is bounded and non-authoritative.

Safe missing-evidence visibility may produce at most a bounded redacted-summary candidate. It does not satisfy evidence, create evidence, override missing evidence, waive evidence requirements, accept approval, satisfy policy, refresh validation, prove source safety, execute diagnostics, retry, rollback, recover, mutate source, continue workflow, merge, release, deploy, bypass redaction, or disclose secrets, credentials, raw payloads, or private reasoning.

Raw, secret, credential, and private missing-evidence materials remain blocked.

## F15 Audit Coverage Summary

The tests use `RolePermissionAuditService` to prove system-owner audit records remain witnesses only.

Audit-only observations create audit-only candidates only. Role-assignment, permission-grant, access-grant, platform-permission, and tenant-boundary audit events do not perform the change and do not become recorded authority, applied change, authorization decision, or permission decision.

Every audit decision and record keeps all authority, action, workflow, release, deployment, redaction, and disclosure flags false.

## Boundary Rules

System owner evidence is not system authority.

System accountability is not root access.

System accountability is not platform administration.

System accountability is not permission management.

System accountability is not role assignment.

System accountability is not access grant.

System accountability is not visibility grant.

System accountability is not approval acceptance.

System accountability is not policy satisfaction.

System accountability is not validation freshness.

System accountability is not source safety.

System accountability is not diagnostic execution.

System accountability is not retry authority.

System accountability is not rollback authority.

System accountability is not recovery authority.

System accountability is not mutation authority.

System accountability is not workflow continuation.

System accountability is not merge authority.

System accountability is not release authority.

System accountability is not deployment authority.

System accountability is not redaction bypass.

System accountability is not secret disclosure.

System accountability is not raw disclosure.

System accountability is not private-reasoning disclosure.

## Unsafe Markers

The F09 tests reject or guard against system-owner language that implies:

- role assignment
- role grant or revoke
- permission management
- access grant
- visibility grant
- platform authority
- cross-tenant authority
- approval acceptance
- policy satisfaction
- validation refresh
- source-safety proof
- diagnostic execution
- retry, rollback, or recovery execution
- source mutation
- patch apply, commit, push, pull request creation, or ready-for-review
- workflow continuation
- merge, release, or deploy
- redaction bypass
- secret, credential, raw payload, raw provider response, raw source, raw log, or private-reasoning disclosure

## Test Summary

Focused F09 tests prove:

- `SystemAccountabilityOwner` exists exactly once
- the role ID is stable
- the display name and role text remain accountability-shaped, not control-shaped
- the role is not confused with tenant administrator, automation agent, or executor operator candidate
- F13 explicitly forbids system-owner role-evidence-derived authority for the dangerous action categories it covers
- F13 omission remains separate-authority-required, not allow
- F14 missing-evidence visibility does not satisfy or create evidence
- F14 missing-evidence visibility does not grant approval, policy, validation, source-safety, execution, mutation, workflow, merge, release, deployment, redaction, or disclosure authority
- raw, secret, credential, and private missing-evidence materials remain blocked
- F15 audit records remain audit-only and do not assign roles, grant permissions, grant access, grant platform authority, override tenant boundaries, authorize, or decide permissions
- static scan proves F09 adds no production runtime authority surface

## Local Validation

Local validation on this branch:

- F09 focused tests: 99/99
- F09 + F15 compatibility: 140/140
- F14 + F15 + F09 compatibility: 217/217
- F13-F15 + F09 compatibility: 318/318
- F12-F15 + F09 compatibility: 361/361
- F11-F15 + F09 compatibility: 445/445
- F10-F15 + F09 compatibility: 543/543
- F10a + F09 compatibility: 179/179
- F09a + F09 compatibility: 192/192
- F08-F15 + F09 compatibility: 795/795
- F07-F15 + F09 compatibility: 868/868
- F06-F15 + F09 compatibility: 912/912
- F05-F15 + F09 compatibility: 949/949
- F04-F15 + F09 compatibility: 1002/1002
- F01-F15 + F09 compatibility: 1401/1401
- F02 matrix compatibility: 148/148
- F03 hard-stop regressions: 145/145
- exact E01-E18 corridor: 1630/1630
- C11 secret scan: 9/9
- `dotnet build IronDev.slnx --no-restore`: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

GitHub CI must not be claimed unless it runs and passes on the current head.

## Known Limitations

F09 does not implement role assignment.

F09 does not implement permission grant.

F09 does not implement API exposure.

F09 does not implement:

- role assignment
- role grant
- role revoke
- permission grant
- permission revoke
- access grant
- visibility grant
- external access grant
- tenant-boundary override
- platform permission
- authorization
- permission resolution
- access control
- identity
- user/group/principal model
- audit persistence
- SQL storage
- read model projection
- runtime audit writer
- evidence creation
- evidence satisfaction
- approval acceptance
- policy satisfaction
- validation refresh
- source safety proof
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
- API exposure
- CLI exposure
- UI exposure
- OpenAPI generation
- screen access
- endpoint invocation
- route guards
- GitHub sync

## Stack

- Block: F09
- Branch: `governance/system-owner-role-boundary-tests`
- Base: `governance/role-permission-change-audit-contract`
- Stack: F09-closeout -> F15 -> F14 -> F13 -> F12 -> F11 -> F10 -> F10a -> F09a -> F08 -> F07 -> F06 -> F05 -> F04 -> F03 -> F02 -> F01 -> Block E tip -> main roll-up later

## Review Line

System owner evidence is not system authority.

## Killjoy Line

Owning accountability is not owning the controls.
