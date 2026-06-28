# F13 — Forbidden Action Catalog by Role

## Purpose

F13 adds a Core-only denial catalog that records which action categories must not be inferred from role evidence.

## Files Changed

- `IronDev.Core/Governance/ForbiddenActionCatalogModels.cs`
- `IronDev.Core/Governance/ForbiddenActionCatalogService.cs`
- `IronDev.Core/Governance/ForbiddenActionCatalogValidator.cs`
- `IronDev.IntegrationTests/BlockF13ForbiddenActionCatalogByRoleTests.cs`
- `Docs/receipts/F13_FORBIDDEN_ACTION_CATALOG_BY_ROLE.md`

## Catalog Semantics

Forbidden action metadata is not authorization.

The catalog may say that role evidence is forbidden as authority for an action.

The catalog must never say that a role may perform an action, that an action is allowed because it is not listed as forbidden, or that a role grants permission.

Absence from the forbidden catalog is not permission.

## Model Summary

F13 adds:

- `RoleForbiddenActionKind`
- `ForbiddenActionReasonKind`
- `ForbiddenActionAuthoritySourceKind`
- `ForbiddenActionLookupClassification`
- `ForbiddenActionCatalogEntry`
- `ForbiddenActionCatalog`
- `ForbiddenActionLookupRequest`
- `ForbiddenActionLookupDecision`
- `ForbiddenActionCatalogValidationResult`

Every catalog entry is denial-only:

- `AppliesWhenAuthoritySourceIsRoleEvidence = true`
- `IsForbidden = true`
- `IsAllowed = false`
- `GrantsAuthority = false`
- `GrantsPermission = false`
- `SatisfiesPolicy = false`
- `AllowsExecution = false`
- `AllowsMutation = false`
- `AllowsWorkflowContinuation = false`
- `AllowsRelease = false`
- `AllowsDeployment = false`
- `BypassesRedaction = false`
- `DisclosesSecrets = false`
- `DisclosesCredentials = false`
- `DisclosesRawPayload = false`
- `DisclosesPrivateReasoning = false`

Every lookup decision preserves false authority/action/disclosure flags and `RequiresSeparateAuthority = true`.

F13 uses `RoleForbiddenActionKind` because `ForbiddenActionKind` already exists in the D08 forbidden-action resolver contract.

## Service Behavior Summary

`ForbiddenActionCatalogService` can:

- build the default catalog from the existing F01 role catalog
- validate role/catalog/request evidence
- return `Forbidden` for explicit role/action denials
- return `NoCatalogGrantSeparateAuthorityRequired` for unlisted role/action combinations
- fail closed for unknown role, unknown action, unknown authority source, unsafe evidence, and missing evidence refs

It never returns allowed, permitted, authorized, granted, or can-execute classifications.

## Role Coverage Summary

The default catalog includes broad high-risk forbidden actions for every F01 role.

It also includes role-specific denials for:

- `ExternalViewer`
- `TenantAdministrator`
- `SystemAccountabilityOwner`
- `AutomationAgent`
- `ApproverCandidate`
- operations/support-like roles
- viewer/read-only roles

## Boundary Rules

Forbidden action metadata is not authorization.

A blacklist is not a permission system.

F13 does not implement authorization, permission resolution, access control, role assignment, role grant/revoke, identity, user/group/principal model, runtime action blocking, API exposure, CLI exposure, UI exposure, OpenAPI generation, screen access, endpoint invocation, route guards, client-side permission decisions, approval acceptance, policy satisfaction, validation refresh, source safety proof, diagnostic execution, retry execution, rollback execution, recovery execution, workflow continuation, source mutation, source apply, commit, push, PR mutation, ready-for-review, merge, release, deployment, redaction bypass, secret disclosure, credential disclosure, raw payload disclosure, private reasoning disclosure, persistence, SQL storage, read model projection, or GitHub sync.

Role evidence is not action authority.

Role catalog metadata is not permission.

Visibility matrix metadata is not permission.

Screen metadata is not permission.

Endpoint metadata is not permission.

Forbidden action lookup is not an access decision.

## Unsafe Markers

F13 rejects allow-shaped markers such as permission granted, action allowed, allowed by omission, not forbidden means allowed, role grants action, role grants endpoint access, role grants UI access, role grants mutation, and role grants release.

It also rejects raw, secret, credential, provider response, source, log, patch, diff, private reasoning, chain-of-thought, and scratchpad markers.

## Test Summary

Focused F13 tests prove:

- default catalog validity
- every F01 role is covered
- every entry references known F01 roles
- entries are denial-only
- every decision keeps `IsAllowed = false`
- unlisted actions require separate authority rather than becoming allowed
- role-specific denials for external viewer, tenant admin, system owner, automation, approver candidate, and support roles
- fail-closed lookup behavior for unknown/missing/unsafe inputs
- no API/CLI/UI/OpenAPI/persistence/provider/authorization/permission/workflow/mutation/release/deploy/redaction-bypass surface

## Reported Validation

Local validation on this branch:

- F13 focused tests: 101/101
- F12 + F13 compatibility: 144/144
- F11-F13 compatibility: 228/228
- F10-F13 compatibility: 326/326
- F10a + F13 compatibility: 181/181
- F09 + F13 compatibility: not run; F09 boundary tests remain intentionally deferred
- F09a + F13 compatibility: 194/194
- F08-F13 compatibility: 578/578
- F07-F13 compatibility: 651/651
- F06-F13 compatibility: 695/695
- F05-F13 compatibility: 732/732
- F04-F13 compatibility: 785/785
- F01-F13 compatibility: 1184/1184
- F02 matrix compatibility: 148/148
- F03 hard-stop regressions: 145/145
- Exact E01-E18 corridor: 1630/1630
- C11 secret scan: 9/9
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Known Limitations

F13 is Core-only and intentionally does not implement runtime enforcement.

F09 boundary tests remain intentionally deferred.

GitHub CI must be treated as absent until Actions runs on the current head.

## Stack

- Base branch: `api/screen-contract-metadata-endpoint`
- Head branch: `governance/forbidden-action-catalog-by-role`

## Killjoy

A blacklist is not a permission system.
