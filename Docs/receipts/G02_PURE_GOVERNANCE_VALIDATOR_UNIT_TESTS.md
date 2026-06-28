# G02 - Pure Governance Validator Unit Tests

## Purpose

G02 seeds a small, clearly pure subset of governance validator coverage into the fast unit test lane.

Fast unit coverage is not integration coverage.

A quick test catches cheap lies, not expensive truths.

## Files Changed

- `IronDev.UnitTests/Governance/GovernanceValidatorTestFixtures.cs`
- `IronDev.UnitTests/Governance/RoleCatalogValidatorUnitTests.cs`
- `IronDev.UnitTests/Governance/RoleVisibilityMatrixValidatorUnitTests.cs`
- `IronDev.UnitTests/Governance/ForbiddenActionCatalogValidatorUnitTests.cs`
- `Docs/receipts/G02_PURE_GOVERNANCE_VALIDATOR_UNIT_TESTS.md`

## Validators Covered

- `RoleCatalogValidator`
- `RoleVisibilityMatrixValidator`
- `ForbiddenActionCatalogValidator`

## Tests Moved Or Duplicated

G02 uses the duplicate-first migration strategy.

No integration tests were deleted or moved.

The new fast tests duplicate narrow pure assertions from the existing F01, F02, and F13 integration coverage.

## Why These Tests Are Pure

The new tests:

- use only `IronDev.Core` governance models, services, and validators
- call validator/service methods directly
- build in-memory model fixtures
- do not use SQL
- do not use an API host
- do not use `WebApplicationFactory`, `TestServer`, or `HttpClient`
- do not use GitHub/provider clients
- do not mutate the filesystem
- do not depend on current time
- do not depend on environment variables
- do not use integration test helpers

The only repository file read is the unit-test project guard, which re-checks the G01 project boundary.

## Dependencies Excluded

`IronDev.UnitTests` remains limited to:

- MSTest packages
- `IronDev.Core`

It does not reference:

- `IronDev.Api`
- `IronDev.Cli`
- `IronDev.IntegrationTests`
- `IronDev.Infrastructure`
- persistence or SQL projects
- workers
- GitHub/provider projects
- ASP.NET host projects
- Docker/Testcontainers
- network dependencies

## Boundary Rules

A validator unit test is not API proof.

A validator unit test is not SQL proof.

A validator unit test is not read-model proof.

A validator unit test is not executor proof.

A fast pass is not release readiness.

A fast pass is not governance approval.

## Reported Validation

- `dotnet restore IronDev.slnx`: passed with existing restore warnings in `IronDev.IntegrationTests`
- `dotnet build IronDev.slnx --no-restore`: passed with existing warnings
- `dotnet test IronDev.UnitTests/IronDev.UnitTests.csproj --no-build`: 28/28 passed
- C11 secret scan: 9/9 passed
- `git diff --check`: passed
- `git diff --cached --check`: passed
- GitHub `fast-unit-ci` on the current G02 head: recorded in the PR body after the workflow runs

Do not claim GitHub CI unless it runs and passes on the current head.

## Known Limitations

G02 does not replace integration tests.

G02 does not migrate service/API/read-model/executor tests.

G02 does not prove database/API/provider behavior.

G02 does not change production validator behavior.

G02 does not weaken existing validation corridors.

## Next Intended Migration Area

G03 can seed pure status-mapper tests into the fast unit lane without touching operation projection, persistence, or API behavior.

## Review Line

Fast unit coverage is not integration coverage.

## Killjoy Line

A quick test catches cheap lies, not expensive truths.
