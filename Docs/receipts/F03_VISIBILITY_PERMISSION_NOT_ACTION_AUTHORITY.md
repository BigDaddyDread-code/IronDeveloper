# F03 - Visibility Permission Is Not Action Authority

## Purpose

F03 adds regression-only hard-stop tests proving that visibility permission evidence, visibility matrix entries, visibility hints, read-model eligibility, and redacted-summary visibility cannot become action authority.

Visibility permission is not action authority.

Being allowed to see a thing is not being allowed to do the thing.

## Boundary

F03 is a regression-only hard-stop. It adds no permission system, access-control system, redaction engine, identity resolver, role assignment store, approval system, policy engine, executor wiring, persistence, API, CLI, UI, worker, OpenAPI, Git, GitHub, provider, merge, release, deployment, or workflow-continuation path.

F03 explicitly denies:

- access grant as action authority
- permission grant as action authority
- approval
- approval profile satisfaction
- policy satisfaction
- validation freshness
- source safety
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
- automation autonomy

## Regression Rules

Visibility permission evidence may describe a future read path for bounded, redacted, summary, metadata, reference, or presence-only views.

It must never imply that an actor may perform an action, approve work, satisfy policy, refresh validation, prove source safety, mutate source, commit, push, create or update a pull request, mark ready for review, merge, release, deploy, roll back, retry, recover, continue workflow, promote memory, bypass redaction, disclose secrets, or disclose private reasoning.

F03 uses local test-only fixtures. Those fixtures are not runtime contracts.

## Validation Evidence

Local validation run for this slice:

```powershell
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "FullyQualifiedName~BlockF03VisibilityPermissionNotActionAuthorityTests" --no-restore --logger "console;verbosity=minimal"
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "FullyQualifiedName~BlockF02RoleVisibilityMatrixContractTests|FullyQualifiedName~BlockF03VisibilityPermissionNotActionAuthorityTests" --no-restore --logger "console;verbosity=minimal"
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "FullyQualifiedName~BlockF01RoleCatalogContractTests|FullyQualifiedName~BlockF02RoleVisibilityMatrixContractTests|FullyQualifiedName~BlockF03VisibilityPermissionNotActionAuthorityTests" --no-restore --logger "console;verbosity=minimal"
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "FullyQualifiedName~BlockE0|FullyQualifiedName~BlockE1" --no-restore --logger "console;verbosity=minimal"
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "FullyQualifiedName~BlockC11SecretScanningRegressionTests" --no-restore --logger "console;verbosity=minimal"
dotnet build IronDev.slnx --no-restore -v:minimal
git diff --check
git diff --cached --check
```

Results:

- Focused F03: 145/145 passed
- F02 + F03 compatibility: 293/293 passed
- F01 + F02 + F03 compatibility: 399/399 passed
- E01-E18 corridor: 1630/1630 passed
- C11 secret scan: 9/9 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

Validation evidence is evidence only. It is not access grant, approval, policy satisfaction, execution permission, merge readiness, release readiness, deployment readiness, or workflow continuation.

## Review Traps

Reject F03 if it:

- creates a permission model in Core
- creates an access-control service
- creates a redaction engine
- creates role assignment
- introduces identity or principal concepts
- treats visibility permission as access grant
- treats access grant as action authority
- treats approval package visibility as approval
- treats policy visibility as policy satisfaction
- treats validation visibility as validation freshness
- treats pull request visibility as pull request action authority
- treats readiness visibility as merge, release, or deployment authority
- treats workflow visibility as workflow continuation
- treats redacted details as raw detail access
- exposes raw payloads, credentials, private reasoning, scratchpad material, raw policy, raw approval, raw command, or raw provider response
- bypasses redaction
- adds API, CLI, UI, persistence, worker, or OpenAPI surface
- calls Git, GitHub, provider, process, SQL, DB, or filesystem mutation APIs

## Killjoy

Being allowed to see a thing is not being allowed to do the thing.
