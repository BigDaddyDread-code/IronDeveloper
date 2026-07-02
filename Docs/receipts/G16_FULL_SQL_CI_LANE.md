# G16 Full SQL CI Lane Receipt

## Purpose

G16 adds an explicit full SQL-backed CI lane that executes the real database / SQL-backed integration coverage made visible by G14.

Full CI is not release approval.

SQL green means the database lane ran, not that the product is safe.

## Files changed

- `.github/workflows/full-sql-integration-ci.yml`
- `Scripts/ci/run-full-sql-integration-ci.ps1`
- `Docs/receipts/G16_FULL_SQL_CI_LANE.md`

G16 does not change production code, Core behavior, Infrastructure behavior, API behavior, CLI behavior, SQL migration behavior, UI behavior, test bodies, test assertions, setup/cleanup logic, existing CI workflows, or project/package references.

## Old SQL lane shape

Existing narrow SQL lane remains unchanged:

- Workflow: `.github/workflows/sql-integration-ci.yml`
- Script: `Scripts/ci/run-sql-integration-ci.ps1`
- Artifact root: `artifacts/ci/sql-integration`
- Executes SQL readiness smoke plus the existing named SQL-backed governance store filters.

## New SQL lane shape

G16 adds a separate workflow:

- Workflow: `.github/workflows/full-sql-integration-ci.yml`
- Script: `Scripts/ci/run-full-sql-integration-ci.ps1`
- Artifact root: `artifacts/ci/full-sql-integration`

The new workflow restores and builds the solution, lists the G14 SQL/slow categories, executes SQL readiness, executes the old SQL-backed store lane, executes `TestCategory=RequiresRealDatabase`, executes G13/G14 category contracts, executes C11 secret scan compatibility, scans artifacts, and uploads bounded evidence.

## Workflow trigger changes

The new workflow runs on:

- `pull_request` to `main`
- `pull_request` to `governance/block-f-rollup-to-main`
- `pull_request` to `ci/fast-unit-lane-under-five-minutes` while G16 is stacked on G15
- `workflow_dispatch`

The workflow uses `permissions: contents: read` and cancels superseded runs for the same ref.

## SQL environment

- SQL Server service image: `mcr.microsoft.com/mssql/server:2022-latest`
- Isolated database: `IronDev_CI_${{ github.run_id }}_${{ github.run_attempt }}`
- CI-only SA password is generated from the GitHub run id / attempt and is not written as a committed connection string.
- The script builds `ConnectionStrings__IronDeveloperDb` at runtime and does not print it.
- The script writes the test output `appsettings.Test.json` only in the build output directory.

## Lane groups

1. Restore solution.
2. Build solution.
3. RequiresRealDatabase selection proof.
4. LongRunning selection proof.
5. RealDatabase category selection proof.
6. Store category selection proof.
7. SQL Server connectivity smoke with readiness retry.
8. SQL-backed governance stores.
9. RequiresRealDatabase execution.
10. Category safety contracts: G13 and G14.
11. C11 secret scan compatibility.

The lane intentionally executes `TestCategory=RequiresRealDatabase` once and records whether it also covers the `LongRunning` selection by overlap count. It does not duplicate the same 380-test class set unless a later split proves that duplication is useful.

## Filters used

- `FullyQualifiedName~BlockC02SqlServerConnectivitySmokeTests`
- Existing SQL store filter: `AcceptedApprovalSqlStoreTests`, `PolicySatisfactionSqlStoreTests`, `ApplyDryRunStoreTests`, `DryRunReceiptStoreTests`, `PatchArtifactStoreTests`, `WorkflowTransitionRecordStoreTests`, `ToolRequestStoreTests`
- `TestCategory=RequiresRealDatabase`
- `TestCategory=LongRunning`
- `TestCategory~RealDatabase`
- `TestCategory~Store`
- `FullyQualifiedName~IntegrationTestCategoryContractTests|FullyQualifiedName~SlowQuarantineCategoryContractTests`
- `FullyQualifiedName~BlockC11SecretScanningRegressionTests`

## Selection vs Execution

Selection proof means a filter lists tests.

Execution proof means the selected tests ran and passed.

This PR does not treat selection proof as execution proof.

## Selected counts

Local selection proof:

- `TestCategory=RequiresRealDatabase`: 365 discovered tests.
- `TestCategory=LongRunning`: 365 discovered tests.
- `TestCategory~RealDatabase`: 365 discovered tests.
- `TestCategory~Store`: 385 discovered tests.

Expected source-counted baseline from G14:

- `RequiresRealDatabase`: 380 source-counted test methods.
- `LongRunning`: 380 source-counted test methods.
- `ManualLocal`: 1 existing ignored manual-local task.

The difference between source-counted methods and `dotnet --list-tests` discovery is recorded as a count-shape difference, not a failure and not an execution result.

## Executed counts

Pending current-head GitHub run.

The new lane must record executed counts in `artifacts/ci/full-sql-integration/sql-lane-summary.md` and `test-count-summary.json`.

## Duration per lane

Pending current-head GitHub run.

The script writes `artifacts/ci/full-sql-integration/timing-summary.md`.

## Artifact names

Expected GitHub artifact name:

- `full-sql-integration-evidence-${{ github.run_id }}-${{ github.run_attempt }}`

Expected artifact root:

- `artifacts/ci/full-sql-integration`

Expected files:

- `evidence-summary.md`
- `sql-lane-summary.md`
- `selection-count-summary.md`
- `timing-summary.md`
- `test-count-summary.json`
- `execution-gap-summary.md`
- `test-results/*.trx`
- `selection/*.txt`

## Artifact safety scan result

Pending current-head GitHub run.

The workflow runs `Scripts/ci/test-ci-evidence-artifact-safety.ps1` before upload.

## Local validation

- `dotnet restore IronDev.slnx`: passed with existing NU1510 warnings.
- `dotnet build IronDev.slnx --no-restore --verbosity minimal`: passed with existing warnings.
- `dotnet build IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-restore --verbosity minimal`: passed with existing NU1510 warnings.
- `dotnet build IronDev.IntegrationTests.Api/IronDev.IntegrationTests.Api.csproj --no-restore --verbosity minimal`: passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~IntegrationTestCategoryContractTests`: 7/7 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~SlowQuarantineCategoryContractTests`: 10/10 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests`: 9/9 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --list-tests --filter TestCategory=RequiresRealDatabase`: 365 tests listed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --list-tests --filter TestCategory=LongRunning`: 365 tests listed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --list-tests --filter TestCategory~RealDatabase`: 365 tests listed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --list-tests --filter TestCategory~Store`: 385 tests listed.
- PowerShell parse check for `Scripts/ci/run-full-sql-integration-ci.ps1`: passed.
- G16 forbidden-marker scan for failure-hiding workflow settings, shell tricks, and committed password-bearing connection strings: passed.
- `git diff --check`: pending final diff check.
- `git diff --cached --check`: pending exact-file staging.

Local full SQL execution was not run because this workspace does not have the GitHub SQL Server service container. Current-head GitHub SQL workflow proof is required.

## GitHub SQL evidence

Pending current-head GitHub run:

- GitHub SQL run ID
- GitHub SQL job ID
- GitHub head SHA
- total duration
- selected counts
- executed counts
- artifact ID / name
- artifact safety scan result

## Execution gaps

Pending current-head GitHub run.

ManualLocal remains existing ignored manual-local debt and is not executed by G16.

## Selection-only gaps

Pending current-head GitHub run.

Any selection-only rows must remain explicitly recorded. No selected lane is called passed unless it executed.

## What full SQL CI proves

Full SQL CI proves only that the configured SQL-backed CI lanes ran against the configured SQL Server environment and passed on this head.

## What full SQL CI does not prove

Full SQL CI does not prove release approval, merge approval, deployment readiness, source apply authority, rollback authority, workflow continuation authority, memory promotion authority, frontend behavior, full product safety, or human acceptance.

## Known limitations

- G16 proves only configured SQL CI lane execution.
- G16 does not prove every possible database scenario.
- G16 does not prove frontend behavior.
- G16 does not prove API/CLI behavior unless those lanes are explicitly included.
- G16 does not prove release readiness.
- G16 does not grant authority.
- G16 does not replace human review.

## Next intended slice

Block H or the next roadmap block after G16, depending whether Block G is complete.

## Killjoy

SQL green means the database lane ran, not that the product is safe.
