# G03 - Status Mapper Unit Tests

## Purpose

Seed fast unit coverage for pure status mapper behavior in `IronDev.UnitTests`.

Review line:

> Status mapper unit tests are not status projection proof.

Killjoy line:

> Mapping truth is not storing truth.

## Files Changed

- `IronDev.UnitTests/Status/AuthorityProfileStatusMapperUnitTests.cs`
- `IronDev.UnitTests/Status/OperationStatusMapperUnitTests.cs`
- `IronDev.UnitTests/Status/StatusMapperTestFixtures.cs`
- `Docs/receipts/G03_STATUS_MAPPER_UNIT_TESTS.md`

## Mappers Covered

- `AuthorityProfileStatusMapper`
- `PatchProposalGovernedOperationStatusMapper`
- `ControlledSourceApplyGovernedOperationStatusMapper`

## Tests Moved Or Duplicated

No integration tests were moved or deleted.

G03 uses the duplicate-first strategy. Existing integration coverage remains intact; these tests seed cheaper fast-lane coverage for deterministic mapper behavior only.

## Why The Tests Are Pure

The G03 unit tests:

- use only `IronDev.Core` models and mappers
- use fixed in-memory request/status inputs
- call mapper methods directly
- assert deterministic status, evidence, issue, red-flag, and forbidden-action output
- use a fixed `DateTimeOffset`
- do not use SQL, repositories, projections, API host/controller behavior, provider clients, GitHub clients, environment variables, current time, or integration helpers

## Dependencies Excluded

The fast unit project remains scoped to:

- `IronDev.Core`
- MSTest package references

G03 does not add references to API, CLI, Infrastructure, IntegrationTests, SQL, workers, provider projects, test-host packages, or CI helpers.

## Boundary Proofs

G03 proves the selected mappers keep conservative status semantics:

- `ProposalOnly` mutation maps to blocked.
- `AskBeforeMutation` source apply requires explicit approval evidence.
- bounded-run eligible status remains non-executable.
- expired grant evidence maps to expired, not eligible.
- blocked eligibility carries missing evidence and forbidden actions.
- ready patch proposal maps to review-ready/completed status without source apply authority.
- blocked patch proposal preserves missing evidence and does not recommend controlled source apply.
- source-apply eligible status requires policy satisfaction evidence.
- completed source apply requires a `source-apply-receipt:` reference.
- source-apply receipt text cannot authorize push, commit, PR, rollback, or workflow continuation.

## Commands Run

- `dotnet restore IronDev.slnx`
- `dotnet build IronDev.slnx --no-restore`
- `dotnet test IronDev.UnitTests/IronDev.UnitTests.csproj --no-build --logger "console;verbosity=minimal"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~BlockC11SecretScanningRegressionTests" --logger "console;verbosity=minimal"`
- `git diff --check`
- `git diff --cached --check`

## Reported Validation

- Restore: passed with existing integration package-prune warnings.
- Build: passed with 0 errors and existing warnings.
- Fast unit tests: 43/43 passed.
- C11 secret scan: 9/9 passed after rerunning with a longer local timeout.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## GitHub CI

`fast-unit-ci` passed on the first pushed G03 head `af753f0ca63e114f8580dab70a1d4f2b3aad14a6`.

- Run: `28333548965`
- Job: `83935763466`

Current-head GitHub CI evidence is tracked on the PR checks and PR body after the final push.

## Known Limitations

G03 does not prove durable status projection.
G03 does not test operation read repositories.
G03 does not test API envelopes.
G03 does not test SQL persistence.
G03 does not test tenant isolation.
G03 does not change production status semantics.
G03 does not replace integration tests.
G03 does not prove release readiness.

## Next Intended Migration Area

G04 should seed fast unit tests for pure authority profile evaluator behavior.
