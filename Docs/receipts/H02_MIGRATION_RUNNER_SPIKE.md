# H02 Migration Runner Spike Receipt

## Purpose

H02 evaluates whether DbUp, or an equivalent .NET migration runner, can fit IronDev's migration authority model without weakening the existing manifest, apply, verify, and migration-state boundaries.

This is a spike, not adoption.

A migration runner is not database authority.

Running scripts in order does not prove the database is safe.

## Files Changed

- `Docs/spikes/H02_MIGRATION_RUNNER_SPIKE.md`
- `Docs/receipts/H02_MIGRATION_RUNNER_SPIKE.md`
- `IronDev.IntegrationTests/Governance/MigrationRunnerSpikeDecisionTests.cs`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`

## Options Evaluated

- Option A - keep current PowerShell apply/verify path.
- Option B - adopt DbUp through a bounded future migration CLI or CI migration command.
- Option C - build a custom minimal runner.
- Option D - defer runner selection until stronger contracts exist.

## Recommendation

Recommendation: `RecommendDeferral`.

H02 does not adopt DbUp and does not reject DbUp permanently. DbUp remains a candidate for a future bounded runner only after manifest-order, script-hash, transaction, journal/state, and verification-evidence contracts are explicit.

The next intended slice is H03 - Migration script hash and manifest order contract.

Review line: A script hash is evidence, not safety.

Killjoy: A matching hash proves identity, not correctness.

## Why No Implementation Was Added

The missing problem is not migration execution machinery. The missing problem is a stronger evidence contract around script identity, manifest order, runner journal/state, failed apply, failed verify, and verification evidence.

Adding a runner before those contracts exist would create new execution authority before IronDev has enough evidence boundaries to contain it.

## Boundary Rules

A migration runner is not approval.

A migration runner is not verification.

A migration runner is not release readiness.

A migration runner is not deployment readiness.

A migration runner is not schema safety.

A migration runner is not authority to create databases.

A migration runner is not authority to alter data outside approved migration scripts.

A migration runner is not authority to skip manifest checks.

A migration runner is not authority to skip script hash checks.

A migration runner is not authority to skip verification.

A migration runner is not authority to self-record success without external verification evidence.

A migration runner is execution machinery only.

## Known Risks

DbUp journal/state could be mistaken for IronDev migration-state truth unless explicitly mapped as evidence only.

DbUp journal/state could be mistaken for verification unless existing `Database/verify-migrations.ps1`, or a successor verifier, remains mandatory after execution.

DbUp script discovery could bypass `Database/migrations.json` unless a future runner is manifest-fed only.

Database creation helpers could sneak in as convenience unless explicitly forbidden.

Transaction mode could become an unsafe default unless documented and tested.

Custom runner code could duplicate library behavior badly and create local execution-authority drift.

Keeping current scripts without state/hash contracts leaves drift and retry evidence under-specified.

## What Was Intentionally Not Built

H02 does not install DbUp.

H02 does not add package references.

H02 does not add a runner project.

H02 does not add a console app.

H02 does not add SQL schema changes.

H02 does not add a migration-state table.

H02 does not add a migration journal table.

H02 does not add stored procedures.

H02 does not change `Database/migrations.json`.

H02 does not change `Database/apply-migrations.ps1`.

H02 does not change `Database/verify-migrations.ps1`.

H02 does not change existing SQL migration scripts.

H02 does not add CI workflow migration execution.

H02 does not add production Core, Infrastructure, API, CLI, UI, agent, workflow, source-apply, rollback, or memory code.

H02 does not connect to SQL.

H02 does not invoke PowerShell.

H02 does not execute a migration.

## Tests Added

- `MigrationRunnerSpike_DefinesRunnerAsExecutionOnly`
- `MigrationRunnerSpike_ComparesCurrentScriptsDbUpCustomAndDeferral`
- `MigrationRunnerSpike_ForbidsRuntimeAndAgentExecutionPaths`
- `MigrationRunnerSpike_RequiresManifestOrderHashAndVerificationSeparation`
- `MigrationRunnerSpike_DoesNotInstallOrImplementRunner`
- `MigrationRunnerSpike_RecordsRecommendationWithoutGrantingAuthority`

The test class uses `Governance`, `DatabaseMigration`, `StaticBoundary`, `Decision`, and `Spike` categories.

## Commands Run

- `dotnet build IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-restore --verbosity minimal`: passed with existing warnings.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~MigrationRunnerSpikeDecisionTests --verbosity minimal`: 6/6 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~IntegrationTestCategoryContractTests --verbosity minimal`: 7/7 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests --verbosity minimal`: first attempt hit command timeout; rerun passed 9/9 in 2m25s.
- `dotnet restore IronDev.slnx`: passed with existing NU1510 warnings.
- `dotnet build IronDev.slnx --no-restore --verbosity minimal`: passed with 0 errors / 4 warnings.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~DatabaseMigrationApplicationReceiptTests --verbosity minimal`: failed in existing migration receipt coverage. The test still expects 13 manifest entries while `Database/migrations.json` currently has 23, and its cleanup path cannot drop the `workflow` schema because newer workflow objects reference it. H02 does not change that test because H02 is spike/static-contract only.
- `git diff --check`: passed.
- `git diff --cached --check`: passed after exact-file staging.

## Validation Results

H02 focused static-boundary tests passed.

Category inventory tests passed.

C11 secret scanning passed after rerun with a longer command timeout.

Restore and solution build passed with existing warnings.

Existing `DatabaseMigrationApplicationReceiptTests` local debt remains visible and is not counted as H02 validation.

## Next Intended Slice

H03 - Migration script hash and manifest order contract.

Review line: A script hash is evidence, not safety.

Killjoy: A matching hash proves identity, not correctness.

## Killjoy Line

Running scripts in order does not prove the database is safe.
