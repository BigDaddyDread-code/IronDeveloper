# F01 - Role Catalog Contract

## Purpose

F01 adds a Core-only governance role catalog contract.

The catalog defines canonical role IDs, role kinds, scope kinds, governance surfaces, validation rules, and read-only lookup/listing behavior so later profile, approval, and policy slices can refer to roles consistently.

A role catalog names responsibility types. It does not grant authority.

A role name is not permission.

## Boundary

F01 is catalog-only. It does not assign actors to roles, resolve identity, grant permissions, approve work, satisfy policy, authorize execution, authorize mutation, or continue workflow.

F01 adds no identity resolver, role assignment store, permission system, approval system, policy engine, executor wiring, persistence, API, CLI, UI, worker, OpenAPI, Git, GitHub, provider, merge, release, deployment, or workflow-continuation path.

F01 explicitly denies:

- identity assignment
- user assignment
- group assignment
- permission grant
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

## Catalog Rules

The default catalog includes these responsibility types:

- Requester
- Planner
- Reviewer
- ApproverCandidate
- PolicyOwnerCandidate
- SecurityReviewer
- ReleaseReviewer
- OperationsReviewer
- ExecutorOperatorCandidate
- RollbackReviewer
- RecoveryReviewer
- Auditor
- Observer
- AutomationAgent
- SystemReadOnly

Candidate roles are intentionally candidate roles:

- ApproverCandidate is not approval.
- PolicyOwnerCandidate is not policy satisfaction.
- ExecutorOperatorCandidate is not execution authority.
- AutomationAgent is not autonomy.
- SystemReadOnly is not backend authority.

## Validation

The validator fails closed on malformed role IDs, missing catalog version, unknown role kinds, unknown scope kinds, missing role text, empty surfaces, unknown surfaces, missing authority-denying boundary statements, duplicate role IDs, duplicate display names, unsafe role text, invalid replacement role IDs, and deprecated roles without replacement or terminal reason.

Unsafe text handling rejects authority claims and raw/private/credential-shaped text without rejecting valid governance-domain phrases such as approval profile, policy review, release readiness, workflow continuation review, source apply review, merge readiness review, and deployment readiness review.

## Validation Evidence

Local validation run for this slice:

```powershell
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "FullyQualifiedName~BlockF01RoleCatalogContractTests" --no-restore --logger "console;verbosity=minimal"
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "FullyQualifiedName~BlockE06IdempotencyKeyContractTests|FullyQualifiedName~BlockE18DownstreamAuthorityAttemptDetectionTests" --no-restore --logger "console;verbosity=minimal"
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "FullyQualifiedName~BlockE0|FullyQualifiedName~BlockE1" --no-restore --logger "console;verbosity=minimal"
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "FullyQualifiedName~BlockC11SecretScanningRegressionTests" --no-restore --logger "console;verbosity=minimal"
dotnet build IronDev.slnx --no-restore -v:minimal
git diff --check
git diff --cached --check
```

Results:

- Focused F01: 106/106 passed
- E06 + E18 boundary sanity: 252/252 passed
- E01-E18 corridor: 1630/1630 passed
- C11 secret scan: 9/9 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

Validation evidence is evidence only. It is not approval, policy satisfaction, execution permission, merge readiness, release readiness, deployment readiness, or workflow continuation.

## Review Traps

Reject F01 if it:

- introduces role assignment
- introduces identity resolution
- introduces permissions
- introduces authorization checks
- adds authority-shaped fields
- treats role lookup as approval
- treats role lookup as policy satisfaction
- treats candidate roles as authority
- treats automation role as autonomy
- adds API, CLI, UI, persistence, worker, or OpenAPI surface
- calls Git, GitHub, provider, process, SQL, DB, or filesystem mutation APIs
- expands into approval profiles, policy rules, or role assignment

## Killjoy

A role name is not permission.
