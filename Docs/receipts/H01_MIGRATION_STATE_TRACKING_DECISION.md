# H01 Migration State Tracking Decision Receipt

## Purpose

H01 defines the decision contract for migration-state tracking before any durable migration-state implementation exists.

Migration state is evidence, not database authority.

A recorded migration is not a safe database.

## Files Changed

- `Docs/decisions/ADR-017-migration-state-tracking.md`
- `Docs/receipts/H01_MIGRATION_STATE_TRACKING_DECISION.md`
- `IronDev.IntegrationTests/Governance/MigrationStateTrackingDecisionTests.cs`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`
- `Database/README.md`

## Decision Summary

IronDev will track migration state separately from migration execution.

The future migration-state tracker must be durable, append-only or append-preferred, inspectable, and bounded to evidence about migration attempts and observations.

The source-of-truth chain is:

1. Migration manifest defines expected migration identity and order.
2. Migration scripts define intended database changes.
3. Apply execution attempts to run scripts.
4. Database verification proves expected objects, constraints, and procedures exist.
5. Migration state records evidence about what happened.

State follows execution and verification. State does not replace execution or verification.

## Why This Is Docs / Static Contract Only

H01 is intentionally a decision and static-boundary slice. It defines vocabulary, boundaries, failure handling, drift states, writer restrictions, and future implementation direction before storage or runtime behavior exists.

The integration tests read text files and source files. They do not connect to SQL, execute PowerShell, run migrations, write files, call providers, or mutate the database.

## What Was Intentionally Not Built

H01 does not create a migration-state table.

H01 does not create a migration runner.

H01 does not change apply-migrations.ps1.

H01 does not change verify-migrations.ps1.

H01 does not execute migrations.

H01 does not create stored procedures.

H01 does not change Database/migrations.json.

H01 does not add API, CLI, UI, Core, Infrastructure, workflow, source apply, rollback, memory, or background-worker migration-state write paths.

H01 does not grant release approval.

H01 does not grant deployment approval.

H01 does not prove any database is safe.

## Migration-State Boundaries

A migration state record is not release approval, deployment approval, merge approval, schema verification by itself, runtime schema mutation permission, permission to skip verification, proof production is safe, authority to apply the next migration, authority to roll back, or authority to alter data.

Migration state is evidence only.

Allowed future writers are limited to migration CLI, migration CI script, controlled migration runner, and explicit administrative migration command.

Forbidden writers include API request handlers, agent runtime, workflow runner, tool execution path, source apply executor, rollback executor, memory promotion path, frontend, and background workers unless explicitly acting as migration runners.

## Failure / Drift States Documented

The ADR defines future statuses:

- `Pending`
- `Applying`
- `Applied`
- `Verified`
- `Failed`
- `RolledBackByManualIntervention`
- `Superseded`
- `Unknown`

Only `Verified` may be treated as evidence that both apply and verify completed. Even `Verified` is not release approval.

The ADR documents drift states:

- `StateMissingButObjectsExist`
- `StateExistsButObjectsMissing`
- `ScriptHashChangedAfterApply`
- `ManifestOrderMismatch`
- `DuplicateStateRecord`
- `UnknownStateRecord`
- `VerificationFailedAfterApply`
- `ManualInterventionDetected`

## Tests Added

- `MigrationStateDecision_DefinesStateAsEvidenceOnly`
- `MigrationStateDecision_SeparatesManifestApplyVerifyAndState`
- `MigrationStateDecision_ForbidsRuntimeSchemaMutationAuthority`
- `MigrationStateDecision_DefinesFailureAndDriftStates`
- `MigrationStateDecision_DoesNotIntroduceMigrationImplementation`
- `MigrationStateReceipt_RecordsDecisionScopeAndLimitations`

The test class uses `Governance`, `DatabaseMigration`, `StaticBoundary`, and `Decision` categories.

## Commands Run

- `dotnet restore IronDev.slnx`: passed with existing NU1510 warnings.
- `dotnet build IronDev.slnx --no-restore --verbosity minimal`: passed with existing warnings.
- `dotnet build IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-restore --verbosity minimal`: passed with existing warnings.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~MigrationStateTrackingDecisionTests --verbosity minimal`: 6/6 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~IntegrationTestCategoryContractTests --verbosity minimal`: 7/7 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests --verbosity minimal`: 9/9 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~DatabaseMigrationApplicationReceiptTests --verbosity minimal`: failed in existing migration receipt coverage. The test still expects 13 manifest entries while `Database/migrations.json` currently has 23, and its cleanup path cannot drop the `workflow` schema because newer workflow objects reference it. H01 does not change that test because H01 is decision/static-contract only.
- `git diff --check`: passed.
- `git diff --cached --check`: passed after exact-file staging.

## Known Limitations

H01 does not detect drift in SQL.

H01 does not enforce script hashes.

H01 does not persist migration state.

H01 does not expose migration state through API, CLI, UI, read models, or CI artifacts.

H01 does not decide the final schema shape beyond conceptual future fields.

H01 does not replace migration verification.

## Next Intended Slice

H02 - Migration state schema contract.

Review line: A migration-state table is not a migration runner.

Killjoy: Storing migration history does not make history true.

## Killjoy Line

A recorded migration is not a safe database.
