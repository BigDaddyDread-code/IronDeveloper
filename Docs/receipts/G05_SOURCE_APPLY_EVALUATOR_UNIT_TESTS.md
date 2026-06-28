# G05 - Source Apply Evaluator Unit Tests

## Purpose

Seed fast unit coverage for pure source-apply execution gate behavior in `IronDev.UnitTests`.

Review line:

> Source apply evaluator tests are not source apply execution.

Killjoy line:

> A safe-to-apply decision is not an apply.

## Files Changed

- `IronDev.UnitTests/SourceApply/SourceApplyExecutionGateUnitTests.cs`
- `IronDev.UnitTests/SourceApply/SourceApplyExecutionGateTestFixtures.cs`
- `Docs/receipts/G05_SOURCE_APPLY_EVALUATOR_UNIT_TESTS.md`

## Evaluator/Gate Covered

- `SourceApplyExecutionGate.Evaluate(...)`

## Tests Moved Or Duplicated

No integration tests were moved or deleted.

G05 uses the duplicate-first strategy. Existing integration coverage remains intact; these tests seed cheaper fast-lane coverage for deterministic source-apply execution gate behavior only.

## Why The Tests Are Pure

The G05 unit tests:

- use only `IronDev.Core` source-apply and governance models
- use fixed in-memory source-apply request, approval, verification, readiness, dry-run, rollback, snapshot, conscience, and thought-ledger fixtures
- call `SourceApplyExecutionGate.Evaluate(...)` directly
- assert deterministic gate outcomes and blocker reasons
- pass a fixed `DateTimeOffset` for expiry-sensitive checks
- do not create files
- do not apply patches
- do not execute Git
- do not invoke `ControlledSourceApplyExecutor`
- do not persist receipts
- do not use SQL, repositories, API hosts, controllers, provider clients, environment variables, current time, or integration helpers

## Dependencies Excluded

The fast unit project remains scoped to:

- `IronDev.Core`
- MSTest package references

G05 does not add references to API, CLI, Infrastructure, IntegrationTests, SQL, workers, provider projects, test-host packages, or CI helpers.

## Boundary Proofs

G05 proves the selected gate paths keep conservative source-apply semantics:

- complete bounded evidence returns `AllowApplyToWorkingTree`
- allow decisions carry no block reasons
- allow decisions bind to request IDs and run ID
- source-apply request absence and run/request/repo/base/patch mismatches block
- missing or failed patch verification blocks
- missing approval evidence blocks
- approval evidence must match request, run, repo, base, patch, and changed files
- approval evidence requires a human reviewer
- overbroad approval text blocks commit, push, pull request, merge, release, and deployment authority claims
- readiness must be ready for future controlled apply
- dry-run evidence must be rehearsal-only and must not mutate the source repo
- rollback plan evidence is required but does not execute rollback
- pre-source snapshot must match the base commit and be clean
- conscience decision must allow the current source-apply subject and remain unexpired
- thought ledger reference is required and may be supplied explicitly
- `AllowApplyToWorkingTree` does not create a receipt
- `AllowApplyToWorkingTree` does not authorize commit, push, PR creation, merge, release, deployment, rollback execution, source mutation, or workflow continuation
- blocked decisions do not attempt fallback execution

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
- Unit test project build: passed with 0 warnings.
- Fast unit tests: 71/71 passed.
- C11 secret scan: 9/9 passed.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## GitHub CI

`fast-unit-ci` passed on the first pushed G05 head `40e8e60e899820190bd8d07e9d53acf3daa76966`.

- Run: `28336055045`
- Job: `83942352535`

Current-head GitHub CI evidence is tracked on the PR checks and PR body after the final push.

## Known Limitations

G05 does not apply source changes.
G05 does not test `ControlledSourceApplyExecutor`.
G05 does not test Git command execution.
G05 does not persist receipts.
G05 does not test rollback execution.
G05 does not test operation projection.
G05 does not test API envelopes.
G05 does not test SQL persistence.
G05 does not test provider behavior.
G05 does not change production source-apply semantics.
G05 does not replace integration tests.
G05 does not prove release readiness.

## Next Intended Migration Area

G06 should seed fast unit tests for pure release-readiness evaluator behavior.
