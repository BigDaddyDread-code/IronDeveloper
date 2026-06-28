# G01 - Fast Unit Test Project

## Purpose

G01 creates a dedicated fast unit test project for pure in-memory Core/domain tests.

Fast unit tests are not reduced validation.

A faster lane is not a weaker gate.

## Files Changed

- `IronDev.UnitTests/IronDev.UnitTests.csproj`
- `IronDev.UnitTests/GlobalUsings.cs`
- `IronDev.UnitTests/G01FastUnitTestProjectTests.cs`
- `.github/workflows/fast-unit-ci.yml`
- `Docs/receipts/G01_FAST_UNIT_TEST_PROJECT.md`
- `IronDev.slnx`

## Project

- Project name/path: `IronDev.UnitTests/IronDev.UnitTests.csproj`
- Target framework: `net10.0`
- Test framework: MSTest
- Project references: `IronDev.Core`
- CI workflow: `fast-unit-ci`

## Explicit Excluded Dependencies

The project does not reference:

- `IronDev.Api`
- `IronDev.Cli`
- `IronDev.IntegrationTests`
- `IronDev.Infrastructure`
- persistence or SQL projects
- workers
- GitHub/provider projects
- ASP.NET host projects

The project also avoids:

- SQL Server
- database providers
- ASP.NET test host
- network calls
- Docker/Testcontainers
- environment-variable dependency
- user secrets
- appsettings content copying
- filesystem mutation

## Fast CI

G01 adds a small dedicated `fast-unit-ci` workflow. It runs on pull requests to `main`, on pull requests to the stacked Block F roll-up branch, and on manual dispatch.

The workflow runs:

- `dotnet restore IronDev.slnx`
- `dotnet build IronDev.slnx --no-restore`
- `dotnet test IronDev.UnitTests/IronDev.UnitTests.csproj --no-build`

This does not replace governance-boundary, SQL integration, frontend contract, or existing integration corridors.

## Boundary Rules

A fast lane is not a replacement for integration tests.

A fast lane is not release readiness.

A unit test pass is not governance approval.

A unit test project must not hide integration dependencies.

A unit test project must not require SQL, API host, providers, or external services.

A fast test project is infrastructure, not product behavior.

## Test Summary

Focused G01 unit tests prove:

- the project is discoverable
- the project can reference a stable Core governance type
- the project is registered in `IronDev.slnx`
- the project references Core only
- the project uses MSTest packages only
- the project does not reference API, CLI, integration, infrastructure, persistence, provider, worker, SQL, ASP.NET host, Docker, GitHub, network, appsettings, user-secret, or environment-dependent tokens
- the project name makes the fast unit lane explicit

## Reported Validation

- `dotnet restore IronDev.slnx`: passed with existing restore warnings in `IronDev.IntegrationTests`
- `dotnet build IronDev.slnx --no-restore`: passed with existing warnings
- G01 focused unit tests: 7/7 passed
- `dotnet test IronDev.UnitTests/IronDev.UnitTests.csproj --no-build`: 7/7 passed
- C11 secret scan: 9/9 passed
- `git diff --check`: passed
- `git diff --cached --check`: passed

GitHub `fast-unit-ci` must not be claimed unless it runs and passes on the current head.

Because G01 is stacked on the Block F roll-up branch, the roll-up CI posture is tracked separately. At the time this receipt was updated, PR #624 had current-head GitHub success for:

- `governance-boundary-ci`
- `sql-integration-ci`
- `frontend-contract-ci`

## Known Limitations

G01 does not migrate existing tests.

G01 does not replace integration tests.

G01 does not prove release readiness.

G01 does not add production behavior.

G01 does not validate database/API/provider behavior.

## Next Intended Use

G02 can seed a small number of pure Core/domain unit tests into this project after G01 proves the project boundary is clean.

## Stack

- Block: G01
- Branch: `test/fast-unit-test-project`
- Base: `governance/block-f-rollup-to-main`
- Stack: G01 -> Block F roll-up -> main

## Review Line

Fast unit tests are not reduced validation.

## Killjoy Line

A faster lane is not a weaker gate.
