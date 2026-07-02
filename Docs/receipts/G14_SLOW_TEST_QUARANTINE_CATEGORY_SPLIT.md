# G14 Slow Test Quarantine / Category Split Receipt

## Purpose

G14 creates an explicit, auditable category split for slow, real-database, long-running, and manual-local integration tests.

Quarantine is not deletion.

A slow test in a new bucket is still a slow test.

## Files changed

- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`
- `Docs/testing/SLOW_TEST_QUARANTINE_REGISTER.md`
- `Docs/receipts/G14_SLOW_TEST_QUARANTINE_CATEGORY_SPLIT.md`
- `IronDev.IntegrationTests/Governance/SlowQuarantineCategoryContractTests.cs`
- Metadata-only `[TestCategory]` additions under `IronDev.IntegrationTests/**/*.cs`

No production, Core, Infrastructure, API behavior, CLI behavior, SQL behavior, UI, project, package, generated client, or CI filter file is changed by G14.

## Categories added

- `RequiresRealDatabase`
- `LongRunning`
- `ManualLocal`

## Categories not added

- Slow: not introduced.
- Quarantined: not introduced.
- RequiresExternalDependency: not introduced.
- RequiresLocalTooling: not introduced.

## Tests marked Slow

None.

## Tests marked LongRunning

35 store/real-database-shaped integration classes are marked `LongRunning`. The exact class list is the `RequiresRealDatabase; LongRunning` set in `Docs/testing/SLOW_TEST_QUARANTINE_REGISTER.md`.

## Tests marked RequiresRealDatabase

35 store/real-database-shaped integration classes are marked `RequiresRealDatabase`. The exact class list is recorded in `Docs/testing/SLOW_TEST_QUARANTINE_REGISTER.md`.

## Tests marked RequiresExternalDependency

None.

## Tests marked RequiresLocalTooling

None.

## Tests marked ManualLocal

- `IronDev.IntegrationTests/ManualIndexingTask.cs::ManualIndexingTask`

## Tests marked Quarantined

Quarantined: not introduced.

No failing or flaky test is placed in quarantine by G14.

## Before / after category counts

Baseline from G13:

- Source files scanned: 589
- Test classes found: 583
- Test methods found: 9495
- Category names found: 196
- Store selector listed 108 tests
- RealDatabase selector listed 202 tests
- Store / RealDatabase broad local execution timed out

After G14:

- Source files scanned: 590
- Test classes found: 584
- Test methods found: 9505
- Category names found: 199
- `RequiresRealDatabase`: 35 test classes, 380 test methods, 35 files
- `LongRunning`: 35 test classes, 380 test methods, 35 files
- `ManualLocal`: 1 test class, 1 test method, 1 file

## Before / after execution lane counts

Before G14:

- Store selector: 108 selected tests
- RealDatabase selector: 202 selected tests
- Store / RealDatabase broad execution: timed out locally

After G14 register rows:

- `SqlIntegration`: 8 rows
- `SelectionOnlyPendingExecution`: 27 rows
- `ManualLocal`: 1 row
- `SlowIntegration`: 0 rows
- `QuarantineLane`: 0 rows

No CI filter is changed, so no test stops running because of G14.

## Proof no tests were deleted

No `[TestMethod]`, test class, or test file deletion is part of this PR.

## Proof no Ignore attributes were added

The only `[Ignore]` remains the existing manual local indexing task in `IronDev.IntegrationTests/ManualIndexingTask.cs`.

## Proof no test body changed

G14 changes test metadata attributes and adds source-scanning contract tests/docs only. It does not change assertions, awaits, fixture setup, cleanup, database setup, timeout behavior, or test method names.

## Proof no CI filter weakened

G14 does not modify CI workflow files or CI scripts.

No `--filter TestCategory!=Slow`, `--filter TestCategory!=LongRunning`, or `--filter TestCategory!=Quarantined` exclusion is introduced.

## Selection vs Execution

Selection proof means a filter lists tests.

Execution proof means the selected tests ran and passed.

This PR does not treat selection proof as execution proof.

## Execution gaps

- `RequiresRealDatabase`: 380 tests listed by selection proof; execution not run in G14.
- `LongRunning`: 380 tests listed by selection proof; execution not run in G14.
- `ManualLocal`: 1 test listed by selection proof; execution not run because it is the existing ignored manual local task.
- `Slow`: not introduced.
- `Quarantined`: not introduced.
- GitHub `fast-unit-ci`: passed; exact final-head run is recorded in GitHub checks.
- GitHub `governance-boundary-ci`: not observed for this head.
- GitHub `sql-integration-ci`: not observed for this head.

## Selection-only gaps

Most `RequiresRealDatabase` / `LongRunning` rows are registered as `SelectionOnlyPendingExecution` because G13 established that broad Store / RealDatabase execution timed out locally.

This is a gap, not a pass.

## Commands run

- `dotnet restore IronDev.slnx`: passed with existing NU1510 warnings.
- `dotnet build IronDev.slnx --no-restore`: passed with existing warnings.
- `dotnet build IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-restore`: passed with existing warnings.
- `dotnet build IronDev.IntegrationTests.Api/IronDev.IntegrationTests.Api.csproj --no-restore`: passed with 0 warnings / 0 errors.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~SlowQuarantineCategoryContractTests`: 10/10 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~IntegrationTestCategoryContractTests`: 7/7 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests`: 9/9 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --list-tests --filter TestCategory=RequiresRealDatabase`: 380 tests listed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --list-tests --filter TestCategory=LongRunning`: 380 tests listed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --list-tests --filter TestCategory=ManualLocal`: 1 test listed.

The broad `RequiresRealDatabase` and `LongRunning` execution lanes were not run locally in G14. This is recorded as an execution gap.

## GitHub CI result

GitHub `fast-unit-ci` passed; exact final-head run is recorded in GitHub checks.

GitHub `governance-boundary-ci` and `sql-integration-ci` were not observed for this head at receipt update time.

Fast unit CI is not integration quarantine proof.

## Known limitations

- G14 makes slow/database/manual-local tests more visible; it does not make them faster.
- G14 does not fix flaky tests.
- G14 does not add a new slow CI lane.
- G14 does not prove SQL execution for every real-database-shaped test.
- Rows with `owner-required` still need real ownership assignment.

## What did not happen

No tests were deleted.
No [Ignore] attributes were added.
No test assertions changed.
No production behavior changed.
No CI lane was weakened without an explicit replacement lane.
No selection proof was treated as execution proof.

## Next intended slice

G15 - CI fast lane under 5 minutes.

Review line: A fast lane is not a complete lane.

Killjoy: Five minutes of green does not buy five minutes of trust.

## Killjoy

A slow test in a new bucket is still a slow test.
