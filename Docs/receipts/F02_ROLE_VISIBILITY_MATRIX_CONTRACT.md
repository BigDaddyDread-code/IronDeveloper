# F02 - Role Visibility Matrix Contract

## Purpose

F02 adds a Core-only governance role-to-visibility matrix contract.

The matrix maps F01 role catalog entries to bounded visibility categories for future read models, frontend summaries, approval packages, receipts, status views, audit summaries, and sensitive evidence summaries.

Visibility hints are not access grants.

Seeing the map is not permission to enter the room.

## Boundary

F02 is catalog-only. It does not assign actors to roles, resolve identity, grant access, grant permissions, bypass redaction, expose secrets, expose private reasoning, approve work, satisfy policy, authorize execution, authorize mutation, or continue workflow.

F02 adds no identity resolver, role assignment store, permission system, access-control system, approval system, policy engine, redaction engine, executor wiring, persistence, API, CLI, UI, worker, OpenAPI, Git, GitHub, provider, merge, release, deployment, or workflow-continuation path.

F02 explicitly denies:

- identity assignment
- user assignment
- group assignment
- role assignment
- access grant
- permission grant
- read authorization
- approval
- policy satisfaction
- validation satisfaction
- execution authority
- mutation authority
- source apply authority
- commit authority
- push authority
- pull request authority
- ready-for-review authority
- merge authority
- release authority
- deployment authority
- rollback authority
- retry authority
- recovery authority
- workflow continuation
- memory promotion authority
- redaction bypass
- secret disclosure authority
- credential disclosure authority
- private reasoning disclosure authority
- raw payload disclosure authority

## Matrix Rules

The matrix may describe:

- role id
- role kind
- role scope kind
- visibility surface
- visibility material kind
- visibility level
- sensitivity kind
- redaction requirement
- reason
- boundary statement
- catalog version

The matrix must not answer whether an actor has a role, may view data, may approve, may execute, may merge, may release, may satisfy policy, or may receive raw sensitive material.

Every matrix entry preserves these requirements:

- separate role assignment evidence is required
- separate visibility decision evidence is required
- separate policy/redaction enforcement is required for sensitive entries
- secret-like, credential-like, private-reasoning, and raw-payload material stays not visible

## Validation Evidence

Local validation run for this slice:

```powershell
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "FullyQualifiedName~BlockF02RoleVisibilityMatrixContractTests" --no-restore --logger "console;verbosity=minimal"
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "FullyQualifiedName~BlockF01RoleCatalogContractTests|FullyQualifiedName~BlockF02RoleVisibilityMatrixContractTests" --no-restore --logger "console;verbosity=minimal"
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "FullyQualifiedName~BlockE0|FullyQualifiedName~BlockE1" --no-restore --logger "console;verbosity=minimal"
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "FullyQualifiedName~BlockC11SecretScanningRegressionTests" --no-restore --logger "console;verbosity=minimal"
dotnet build IronDev.slnx --no-restore -v:minimal
git diff --check
git diff --cached --check
```

Results:

- Focused F02: 148/148 passed
- F01 + F02 compatibility: 254/254 passed
- E01-E18 corridor: 1630/1630 passed
- C11 secret scan: 9/9 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

Validation evidence is evidence only. It is not access grant, approval, policy satisfaction, execution permission, merge readiness, release readiness, deployment readiness, or workflow continuation.

## Review Traps

Reject F02 if it:

- introduces user-role assignment
- introduces identity resolution
- introduces permissions
- introduces access control
- treats visibility lookup as access grant
- treats visibility lookup as permission grant
- treats visibility lookup as approval
- treats visibility lookup as policy satisfaction
- treats candidate roles as authority
- treats automation role as autonomy
- exposes raw payloads, credentials, private reasoning, scratchpad material, raw policy, raw approval, raw command, or raw provider response
- bypasses redaction
- adds API, CLI, UI, persistence, worker, or OpenAPI surface
- calls Git, GitHub, provider, process, SQL, DB, or filesystem mutation APIs
- expands into approval profiles, policy rules, role assignment, redaction engine, or access-control enforcement

## Killjoy

Seeing the map is not permission to enter the room.
