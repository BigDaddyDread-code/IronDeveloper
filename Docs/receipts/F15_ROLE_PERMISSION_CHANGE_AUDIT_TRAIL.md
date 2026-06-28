# F15 - Role/Permission Change Audit Trail

## Purpose

F15 adds a Core-only audit contract for role/permission change evidence.

Role/permission audit records are not role/permission authority.

Writing down a power change is not performing it.

F15 records the shape of a witness. It does not create the power being witnessed.

## Files Changed

- `IronDev.Core/Governance/RolePermissionAuditModels.cs`
- `IronDev.Core/Governance/RolePermissionAuditService.cs`
- `IronDev.Core/Governance/RolePermissionAuditValidator.cs`
- `IronDev.IntegrationTests/BlockF15RolePermissionChangeAuditTests.cs`
- `Docs/receipts/F15_ROLE_PERMISSION_CHANGE_AUDIT_TRAIL.md`

## Model Summary

F15 defines:

- `RolePermissionAuditEventKind`
- `RolePermissionAuditSubjectKind`
- `RolePermissionAuditOutcomeKind`
- `RolePermissionAuditAuthoritySourceKind`
- `RolePermissionAuditClassification`
- `RolePermissionAuditRequest`
- `RolePermissionAuditRecord`
- `RolePermissionAuditDecision`

The record model is audit-only, immutable, and append-only as a contract. It does not imply persistence.

Every audit record preserves false authority/action/disclosure flags for:

- role assignment authority
- permission authority
- access
- visibility authority
- external access
- tenant-boundary override
- platform authority
- approval acceptance
- policy satisfaction
- validation refresh
- source-safety proof
- evidence creation
- evidence satisfaction
- missing-evidence override or waiver
- execution
- mutation
- workflow continuation
- merge, release, and deployment
- redaction bypass
- secret, credential, raw-payload, and private-reasoning disclosure

Every record and decision keeps `RequiresSeparateAuthority = true`.

## Service Behavior Summary

`RolePermissionAuditService.CreateAuditRecordCandidate(...)` consumes:

- F01 role catalog
- F13 forbidden action catalog
- a role/permission audit request

The service validates request shape, safe text, required evidence references, F01, and F13. It rejects unknown event, subject, outcome, and authority-source kinds. It rejects applied/granted/authorized/succeeded language and raw, secret, credential, and private-reasoning material.

When role IDs are supplied, they must resolve through the F01 catalog.

The service builds only in-memory audit record candidates. It does not append, persist, write, repair, publish, or execute anything.

## F13 Defensive Mapping Summary

F15 maps audit event kinds to F13 forbidden action kinds defensively:

- role assignment events -> role assignment
- role grant events -> role grant
- role revoke events -> role revoke
- permission grant/revoke events -> permission management
- access grant events -> access grant
- visibility grant events -> visibility grant
- external access events -> external access grant
- tenant-boundary override events -> cross-tenant visibility
- platform permission events -> platform visibility

This mapping is not a grant. If F13 explicitly forbids role-evidence-derived authority for the event category, F15 emits a blocked audit record candidate. If F13 has no explicit entry, F15 still emits only an audit record candidate and keeps separate authority required.

## Boundary Rules

Audit record creation is not role assignment.

Audit record creation is not permission grant.

Audit record creation is not permission revoke.

Audit record creation is not access grant.

Audit record creation is not visibility grant.

Audit record creation is not external access.

Audit record creation is not tenant-boundary override.

Audit record creation is not platform authority.

Audit evidence is not authorization.

Audit evidence is not policy satisfaction.

Audit evidence is not approval acceptance.

Audit evidence is not evidence satisfaction.

Audit record candidate is not persisted audit trail.

Audit record fingerprint is not approval.

Previous record fingerprint is not append authority.

Role ID in an audit record is not role membership.

Permission key in an audit record is not permission.

Actor reference in an audit record is not identity authority.

A blocked audit record does not retry the blocked action.

A rejected audit record does not close the issue.

## Unsafe Markers

The validator rejects applied-change and authority-shaped markers such as:

- `RoleAssigned = true`
- `RoleGranted = true`
- `PermissionGranted = true`
- `PermissionRevoked = true`
- `AccessGranted = true`
- `VisibilityGranted = true`
- `ExternalAccessGranted = true`
- `ChangeApplied = true`
- `UserUpdated = true`
- `GroupUpdated = true`
- `PrincipalUpdated = true`
- `AuthorizationSucceeded = true`
- `PermissionSatisfied = true`
- `CanAssignRole = true`
- `CanGrantPermission = true`
- `CanGrantAccess = true`
- `CanAuthorize = true`
- `CanMutate = true`
- `CanContinueWorkflow = true`
- `CanApprove = true`
- `CanSatisfyPolicy = true`
- `CanBypassRedaction = true`
- `CanViewSecrets = true`
- `CanViewCredentials = true`
- `CanViewRawPayload = true`
- `CanViewPrivateReasoning = true`

It also rejects semantic smells that try to turn audit records into permission grants, role assignment, access grants, authorization, approval acceptance, policy satisfaction, principal/user mutation, or redaction bypass.

## Test Summary

Focused F15 tests prove:

- safe audit requests validate
- unsafe authority/applied-change text is rejected and not echoed
- raw, secret, credential, and private-reasoning text is rejected
- unknown event, subject, outcome, and authority source fail closed
- required evidence references are required
- invalid F01 and F13 evidence fail closed
- requested role IDs and target role IDs must resolve through F01 when provided
- audit records are immutable, audit-only, and append-only contracts
- audit records do not persist anything
- audit records do not assign roles, grant/revoke roles, grant/revoke permissions, grant access, grant visibility, grant external access, override tenant boundary, or grant platform authority
- audit records do not accept approval, satisfy policy, satisfy evidence, create evidence, waive missing evidence, mutate source, continue workflow, merge, release, deploy, bypass redaction, or disclose secrets/credentials/raw/private material
- event kinds map defensively to F13 actions
- F13 forbidden entries produce blocked audit record candidates
- F13 omission does not become allow or applied change
- outcome and classification vocabulary avoids applied/granted/authorized/allowed/succeeded/completed language
- static scan proves no API, CLI, UI, OpenAPI, persistence, SQL, provider, authorization handler, permission resolver, access-control, role-assignment service, permission service, audit store, evidence writer, workflow, mutation, release, deploy, retry, rollback, or recovery surface was added

## Local Validation

Local validation on this branch:

- F15 focused: 41/41
- F14 + F15 compatibility: 118/118
- F13-F15 compatibility: 219/219
- F12-F15 compatibility: 262/262
- F11-F15 compatibility: 346/346
- F10-F15 compatibility: 444/444
- F10a + F15 compatibility: 121/121
- F09 + F15 compatibility: not run; F09 boundary tests remain intentionally deferred
- F09a + F15 compatibility: 134/134
- F08-F15 compatibility: 696/696
- F07-F15 compatibility: 769/769
- F06-F15 compatibility: 813/813
- F05-F15 compatibility: 850/850
- F04-F15 compatibility: 903/903
- F01-F15 compatibility: 1302/1302
- F02 matrix compatibility: 148/148
- F03 hard-stop regressions: 145/145
- exact E01-E18 corridor: 1630/1630
- C11 secret scan: 9/9
- `dotnet build IronDev.slnx --no-restore`: 0 errors / 4 warnings
- `git diff --check`: passed

`git diff --cached --check` must be run after staging the exact F15 files.

GitHub CI must not be claimed unless it runs and passes on the current head.

## Known Limitations

F15 does not implement role assignment.

F15 does not implement permission grant.

F15 does not implement audit persistence.

F15 also does not implement:

- role grant
- role revoke
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
- SQL storage
- read model projection
- runtime audit writer
- evidence creation
- evidence satisfaction
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
- API exposure
- CLI exposure
- UI exposure
- OpenAPI generation
- screen access
- endpoint invocation
- route guards
- GitHub sync

F09 boundary tests remain intentionally deferred.

## Stack

- Block: F15
- Branch: `governance/role-permission-change-audit-contract`
- Base: `governance/missing-evidence-visibility-by-role`
- Stack: F15 -> F14 -> F13 -> F12 -> F11 -> F10 -> F10a -> F09a -> F08 -> F07 -> F06 -> F05 -> F04 -> F03 -> F02 -> F01 -> Block E tip -> main roll-up later

## Review Line

Role/permission audit records are not role/permission authority.

## Killjoy Line

Writing down a power change is not performing it.
