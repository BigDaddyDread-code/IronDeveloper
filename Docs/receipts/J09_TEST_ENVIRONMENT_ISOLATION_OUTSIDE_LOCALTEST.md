# J09 - Test Environment Isolation Outside LocalTest

## Summary

J09 adds startup validation and regression proof for test-shaped API environments that are not `LocalTest`.

Before J09, LocalTest had isolated resource checks and production-like environments had local/test-resource rejection, but Test was outside both. J09 gives Test/CI-shaped environments their own isolation guard without weakening C12 or C13.

## Boundary

J09 is startup validation and regression proof only. It does not provision test infrastructure, create databases, create directories, write evidence, grant approval, satisfy policy, continue workflows, apply source, release, or deploy.

A test environment isolation check is a startup guard. It is not approval, policy satisfaction, evidence validation, release readiness, deployment readiness, workflow continuation, or permission to mutate source.

Passing J09 means only that a non-LocalTest test-shaped API startup did not obviously point at real/shared resources.

## Non-LocalTest Test-Shaped Environments

The explicit J09-owned environments are:

- `Test`
- `CI`
- `IntegrationTest`
- `E2E`
- `AutomationTest`
- `SmokeTest`

`LocalTest` remains C12-owned. `Development` remains outside J09. Unknown/custom names remain production-like under C13.

## Accepted Test Resource Shape

Accepted database examples include:

- `IronDeveloper_Test`
- `IronDeveloper_CI`
- `IronDeveloper_IntegrationTest`
- `IronDeveloper_AutomationTest`
- `IronDeveloper_SmokeTest`

Configured roots are optional, but when present they must include an explicit test/CI/automation marker as a path segment or accepted bounded compound segment.

Accepted root examples include:

- `%TEMP%\IronDev\Test\workspaces`
- `%TEMP%\IronDev\CI\evidence`
- `C:\IronDevTestWorkspaces`
- `C:\IronDevTestLogs`
- `<ci-temp>/IronDev/ci/workspaces`

## Rejected Unsafe Shapes

J09 rejects:

- missing database names
- default/shared database names such as `IronDeveloper` and `IronDeveloper_Main`
- production-like database/root markers such as production, prod, live, accept, staging, UAT, demo, main, release, shared, and default
- development/local markers such as dev, development, and local
- ambiguous accidental markers such as `Contest`, `Latest`, `Testament`, `ProductionTestBackup`, and `ProdTest`
- configured workspace/log/evidence roots without clear test/CI/automation isolation markers
- dangerous real repo writes in test-shaped environments

Startup failures name categories only. They must not echo full connection strings, passwords, JWT keys, API keys, full user-local paths, full temp paths, full repo paths, or machine names.

## LocalTest / Production-Like Interaction

J09 preserves the existing ownership order:

1. `LocalTest` uses C12 validation.
2. Explicit non-LocalTest test-shaped environments use J09 validation.
3. Production-like and unknown/custom environments use C13 validation.
4. `Development` is not made test-like.

## CI Lane

The governance boundary script now includes:

- `BlockJ09TestEnvironmentIsolationTests` in the security/static proof lane.
- `TestEnvironmentIsolationSafetyTests` in the API boundary lane.

## Forbidden Mutation Paths

J09 does not create or modify:

- SQL databases, migrations, stores, or procedures
- filesystem roots, logs, evidence, or workspaces
- source apply, controlled apply, commit, push, PR, rollback, release, deployment, or workflow continuation surfaces
- approval or policy satisfaction state
- frontend/OpenAPI/generated client surfaces

## Validation

Local validation for the roll-up branch recorded:

- `dotnet build IronDev.slnx --no-restore`: passed with existing warnings
- focused API startup tests for `TestEnvironmentIsolationSafetyTests`: 32/32 passed
- focused static regression tests for `BlockJ09TestEnvironmentIsolationTests`: 9/9 passed
- Block J focused lane (`BlockJ01` through `BlockJ10`): 159/159 passed
- category contracts (`IntegrationTestCategoryContractTests|SlowQuarantineCategoryContractTests`): 17/17 passed
- governance boundary CI script: passed after adding the J09 static/API lanes
- diff checks: passed

## Review Traps

Block this PR if:

- `Test` can start against `IronDeveloper` or `IronDeveloper_Main`
- `Test` can start with `DangerRealRepoWritesEnabled=true`
- `Test` can use production/live/accept/staging/UAT/demo-shaped database names
- `Test` can use generic workspace/log/evidence roots without a test/CI marker
- ambiguous substrings like `Contest`, `Latest`, or `Testament` satisfy the marker check
- `LocalTest` C12 behavior changes unnecessarily
- production-like C13 behavior changes unnecessarily
- unknown/custom environments become test-like instead of production-like
- `Development` gets blocked by test-only rules without explicit review
- startup errors echo full connection strings, secrets, or full user-local paths
- J09 creates directories, databases, evidence, or local resources
- J09 is framed as alpha readiness or release readiness

## Review Line

J09 makes non-LocalTest test runs isolated enough to trust as tests. It does not make them evidence, approval, release readiness, or permission to mutate source.

## Killjoy

A test environment pointed at real resources is not test coverage. It is a loaded footgun.
