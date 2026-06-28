# G06 - Release Readiness Evaluator Unit Tests

## Purpose

Seed fast unit coverage for pure release-readiness evaluator behavior in `IronDev.UnitTests`.

Review line:

> Release readiness evaluator tests are not release approval.

Killjoy line:

> Ready to consider release is not released.

## Files Changed

- `IronDev.UnitTests/Release/ReleaseReadinessGateEvaluatorUnitTests.cs`
- `IronDev.UnitTests/Release/ReleaseReadinessGateEvaluatorTestFixtures.cs`
- `Docs/receipts/G06_RELEASE_READINESS_EVALUATOR_UNIT_TESTS.md`

## Evaluator Covered

- `ReleaseReadinessGateEvaluator.Evaluate(...)`

## Tests Moved Or Duplicated

No integration tests were moved or deleted.

G06 uses the duplicate-first strategy. Existing integration coverage remains intact; these tests seed cheaper fast-lane coverage for deterministic release-readiness evaluator behavior only.

## Why The Tests Are Pure

The G06 unit tests:

- use only `IronDev.Core` release-readiness models and evaluator types
- use fixed in-memory `ReleaseReadinessGateRequest` and `ReleaseReadinessReport` fixtures
- call `ReleaseReadinessGateEvaluator.Evaluate(...)` directly
- assert deterministic decision statuses, reasons, evidence references, boundary maxims, hashes, and authority flags
- use fixed `DateTimeOffset` values
- do not call API or CLI
- do not call release execution code
- do not execute deployment
- do not execute merge
- do not run Git
- do not use SQL, repositories, providers, GitHub clients, filesystem mutation, environment variables, current time, or integration helpers

## Dependencies Excluded

The fast unit project remains scoped to:

- `IronDev.Core`
- MSTest package references

G06 does not add references to API, CLI, Infrastructure, IntegrationTests, SQL, workers, provider projects, test-host packages, or CI helpers.

## Boundary Proofs

G06 proves the selected evaluator paths keep conservative release-readiness semantics:

- complete evidence maps to `ReadyEvidenceSatisfied`
- ready records carry human-review warnings
- ready records keep release, deployment, merge, source-apply, rollback, workflow, Git, and release execution flags false
- ready records include evidence references and boundary maxims
- decision record hashes are deterministic
- null request and missing report block by missing evidence
- missing gate IDs, project ID, requested timestamp, evidence references, boundary maxims, and boundary text block
- project mismatch blocks by failed evidence
- invalid report shape and report hash mismatch block
- missing/failed/unsupported report status maps to missing or failed evidence decisions
- blocking findings block
- missing approval and policy evidence block
- failed or partial source apply requires successful rollback recovery evidence
- failed, partial, or inconsistent rollback evidence blocks
- workflow continuation and transition evidence must be satisfied
- report authority and execution claims block
- private/raw material and authority-shaped text are rejected
- safe negative authority text is allowed
- release-readiness evidence satisfaction remains evidence for human review, not approval or execution

## Commands Run

- `dotnet restore IronDev.slnx`
- `dotnet build IronDev.slnx --no-restore`
- `dotnet build IronDev.UnitTests/IronDev.UnitTests.csproj --no-restore`
- `dotnet test IronDev.UnitTests/IronDev.UnitTests.csproj --no-build --logger "console;verbosity=minimal"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~BlockC11SecretScanningRegressionTests" --logger "console;verbosity=minimal"`
- `git diff --check`
- `git diff --cached --check`

## Reported Validation

- Restore: passed with existing integration package-prune warnings.
- Build: passed with 0 errors and existing warnings.
- Unit test project build: passed with 0 errors and existing warnings.
- Fast unit tests: 90/90 passed.
- C11 secret scan: 9/9 passed.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## GitHub CI

`fast-unit-ci`: passed on the first pushed G06 head `f2dfbc5a23f48937ba5839b2feb959a969056819`.

- Run: `28336757887`
- Job: `83944232569`

Current-head GitHub CI evidence is tracked on the PR checks and PR body after the final push.

## Known Limitations

G06 does not approve release.
G06 does not approve deployment.
G06 does not approve merge.
G06 does not execute release.
G06 does not test `GovernedReleaseGateService`.
G06 does not test API or CLI release gates.
G06 does not test SQL persistence.
G06 does not test operation projection.
G06 does not test provider behavior.
G06 does not change production release-readiness semantics.
G06 does not replace integration tests.
G06 does not prove release readiness for a real release.

## Next Intended Migration Area

G07 should seed fast unit tests for pure ConscienceAgent policy-decision behavior without calling agents, models, tools, providers, memory, retrieval, API, CLI, or executors.
