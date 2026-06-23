# C12 - LocalTest Safety Regression

## Summary

C12 expands LocalTest safety regression coverage and tightens the startup guard so LocalTest cannot quietly use obvious real resources.

LocalTest now requires a clear isolated test marker for database, workspace root, and logs root, and it still rejects `LocalTest:DangerRealRepoWritesEnabled=true`.

## Boundary

LocalTest safety guards prevent test runs from using obvious real resources. They do not grant authority, approval, policy satisfaction, execution permission, release readiness, deployment readiness, or workflow continuation.

A LocalTest run may only be destructive inside clearly isolated test resources.

C12 is a startup guard and regression proof only. Passing LocalTest safety does not authorize source mutation, repository writes, SQL ownership changes, commit, push, PR creation, merge, release, deploy, memory promotion, or workflow continuation.

## LocalTest Safety Scope

C12 covers LocalTest startup safety for:

- database name
- workspace root
- logs root
- dangerous real-repo-write flag

The safety check applies only when the API environment is `LocalTest`. Non-LocalTest environments do not use LocalTest-only safety rules.

## Accepted LocalTest Shape

The accepted shape is an explicitly isolated LocalTest setup, for example:

- database: `IronDeveloper_Test`
- workspace root: a path containing an explicit `Test` segment or `IronDevTestWorkspaces`
- logs root: a path containing an explicit `Test` segment or `IronDevTestLogs`
- `LocalTest:DangerRealRepoWritesEnabled=false`

## Rejected Unsafe Shapes

C12 rejects:

- missing database name
- production-looking database names such as `IronDeveloper`, `IronDeveloper_Prod`, `IronDeveloper_Live`, and `IronDeveloper_Accept`
- ambiguous accidental test substrings such as `Contest`, `Latest`, `Testament`, `ProdTest`, and `ProductionTestBackup`
- missing workspace root
- workspace roots without a clear test marker
- production-looking workspace roots
- missing logs root
- logs roots without a clear test marker
- production-looking logs roots
- `LocalTest:DangerRealRepoWritesEnabled=true`

## CI Lane

C12 is wired into the existing governance-boundary CI script through the explicit security boundary lane:

- `FullyQualifiedName~BlockC11SecretScanningRegressionTests`
- `FullyQualifiedName~BlockC12LocalTestSafetyRegressionTests`

C12 does not add a workflow, external service, artifact upload, GitHub write permission, SQL service, or frontend contract lane.

## Forbidden Mutation Paths

- no SQL migration
- no SQL store/procedure change
- no source-apply change
- no commit executor change
- no push executor change
- no PR executor change
- no rollback implementation change
- no memory write or promotion
- no governance authority change
- no frontend/Tauri runtime change
- no OpenAPI/generated-client change
- no release
- no deployment
- no workflow continuation

## Validation

- Focused C12 static tests: 9/9 passed.
- LocalTest API environment safety tests: 25/25 passed.
- C06/C07/C08/C09/C10/C11/C12 security boundary tests: 75/75 passed.
- API boundary lane: 38/38 passed.
- CLI boundary lane: 41/41 passed.
- Governance-boundary CI script: passed.
  - B-series profile boundary tests: 133/133 passed.
  - BQ-BU compatibility boundary tests: 80/80 passed.
  - Security boundary tests: 18/18 passed.
  - API boundary tests: 38/38 passed.
  - CLI boundary tests: 41/41 passed.
- Build: 0 errors / 2 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## Review Traps

Reject C12 if:

- LocalTest can use a production-looking database
- LocalTest can use a non-test workspace root
- LocalTest can use a non-test logs root
- LocalTest can enable dangerous real repo writes
- ambiguous strings like `Contest`, `Latest`, or `Testament` satisfy the safety check
- validation only exists as static source checks and never exercises startup behavior
- exception messages expose configured paths, database names, or secret-like values unnecessarily
- non-LocalTest environments are accidentally blocked by LocalTest-only rules
- JWT, CORS, Weaviate, or environment endpoint behavior changes outside proof
- SQL, governance, memory, source-apply, release, or deploy paths are touched
- C12 becomes broad environment management or deployment provisioning

## Killjoy

A test environment pointed at real resources is not a test environment; it is an accident waiting for automation.
