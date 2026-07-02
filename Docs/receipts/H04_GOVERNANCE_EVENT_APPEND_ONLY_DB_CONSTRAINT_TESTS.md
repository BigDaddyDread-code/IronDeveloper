# H04 Governance Event Append-Only DB Constraint Tests Receipt

## Purpose

H04 proves the current governance-event storage contract is append-only at the supported write/read surface.

Append-only storage preserves evidence. It does not validate authority.

An immutable lie is still a lie.

## Files Changed

- `IronDev.IntegrationTests/Governance/GovernanceEventAppendOnlyDatabaseConstraintTests.cs`
- `Docs/receipts/H04_GOVERNANCE_EVENT_APPEND_ONLY_DB_CONSTRAINT_TESTS.md`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`
- `Docs/testing/SLOW_TEST_QUARANTINE_REGISTER.md`
- `IronDev.IntegrationTests/Governance/SlowQuarantineCategoryContractTests.cs`

The slow/quarantine register and contract test changed only because H04 is a new SQL-backed `RequiresRealDatabase` / `LongRunning` test class and the existing G14 category contract requires those tracked classes to remain registered.

## What Append-Only Means In H04

H04 proves the supported governance-event store and stored-procedure surface can append and read governance events.

H04 proves the supported governance-event store and stored-procedure surface does not expose sanctioned update, delete, replay, backfill, repair, or mutation paths for governance events.

H04 proves read paths do not mutate an appended governance-event row.

H04 does not prove the payload is true.

H04 does not prove the actor was authorized.

H04 does not turn an append-only event into approval, policy satisfaction, source-apply authority, workflow continuation authority, merge readiness, release readiness, or deployment readiness.

## What Was Tested

- Appending a unique governance event through `SqlGovernanceEventStore.AppendAsync(...)`.
- Reading the event through `GetAsync(...)`.
- Listing the event through project, correlation, subject, and causation read paths.
- Verifying the stored row is unchanged after read/list calls.
- Verifying `IGovernanceEventStore` exposes append/read/list methods only.
- Verifying the governance-event stored-procedure surface exposes the current append/read/list procedures only.
- Verifying read stored procedures do not mutate `governance.GovernanceEvent`.
- Verifying the append procedure inserts a row and does not update, delete, or merge existing governance-event rows.
- Probing direct table `UPDATE` and `DELETE` inside explicit transactions and rolling back in `finally`.
- Verifying H03 remains intact by appending new test events with `GovernanceEventSchemaVersions.Current`.

## What Was Not Tested

H04 does not test every possible SQL identity.

H04 does not test production database permissions.

H04 does not test whether a privileged SQL identity can disable triggers, alter permissions, alter stored procedures, or alter the table.

H04 does not test replay, backfill, migration, or repair tooling.

H04 does not test API, CLI, UI, workflow, source-apply, rollback, memory, release, or deployment behavior.

## DB Direct-DML Truth Boundary

H04 observed direct UPDATE and DELETE blocked by the configured test database trigger path.

This is database-level direct-DML evidence for the configured test identity, not proof that every privileged SQL identity is unable to disable or bypass database protections.

H04 proves the supported governance-event write/read surface is append-only.

H04 does not prove privileged direct-table DML is impossible.

A later schema/permission/trigger hardening slice is required if true database-level immutability is required.

## Boundary Rules

Append-only storage is not approval.

Append-only storage is not policy satisfaction.

Append-only storage is not source-apply authority.

Append-only storage is not workflow continuation authority.

Append-only storage is not merge readiness.

Append-only storage is not release readiness.

Append-only storage is not deployment readiness.

Append-only storage is not proof the payload is true.

Append-only storage is not proof the actor was authorized.

Append-only storage is not permission to replay events.

Append-only storage is not permission to backfill events.

Append-only storage is not permission to mutate old events.

Append-only storage preserves evidence only.

An append-only event is not necessarily a true event.

An immutable lie is still a lie.

## What Was Intentionally Not Built

H04 does not add a SQL migration.

H04 does not alter the governance-event table.

H04 does not alter stored procedures.

H04 does not add triggers.

H04 does not change permissions.

H04 does not replay events.

H04 does not backfill events.

H04 does not mutate existing governance events.

H04 does not add API/CLI/UI behavior.

H04 does not change workflow/source-apply/rollback/release/deployment authority.

H04 does not add event repair tooling.

H04 does not add DbUp or a migration runner package.

## Tests Added

- `GovernanceEventStore_AppendCreatesImmutableEventThroughSupportedSurface`
- `GovernanceEventReadPaths_DoNotMutateStoredEvent`
- `GovernanceEventStore_DoesNotExposeUpdateOrDeleteMethods`
- `GovernanceEventSqlSurface_DoesNotExposeUpdateOrDeleteProcedures`
- `GovernanceEventAppendProcedure_DoesNotUpdateOrDeleteExistingEvents`
- `GovernanceEventDirectDmlBoundary_IsRecordedHonestly`
- `Receipt_RecordsAppendOnlyBoundaryAndLimitations`

The test class uses `Governance`, `GovernanceEvent`, `Store`, `RequiresRealDatabase`, `LongRunning`, `Boundary`, and `Contract` categories.

## Commands Run

- `dotnet build IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-restore --verbosity minimal`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~GovernanceEventAppendOnlyDatabaseConstraintTests --verbosity minimal`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~IntegrationTestCategoryContractTests|FullyQualifiedName~SlowQuarantineCategoryContractTests" --verbosity minimal`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests --verbosity minimal`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~GovernanceEventStoreTests --verbosity minimal`
- `dotnet restore IronDev.slnx`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~GovernanceEventSchemaVersioningTests|FullyQualifiedName~GovernanceEventAppendOnlyDatabaseConstraintTests" --verbosity minimal`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~DatabaseMigrationApplicationReceiptTests --verbosity minimal`
- `dotnet build IronDev.slnx --no-restore --verbosity minimal`

## Validation Results

- Integration test project build: passed, 0 errors with existing warnings.
- H04 focused tests: 7/7 passed.
- Integration category and slow/quarantine contracts: 17/17 passed.
- C11 secret scan: 9/9 passed.
- Existing governance-event store tests: 11/11 passed.
- Solution restore: passed with existing NU1510 warnings.
- H03/H04 governance-event corridor: 14/14 passed.
- Solution build: passed, 0 errors / 4 existing warnings.
- `DatabaseMigrationApplicationReceiptTests`: failed on existing migration debt outside H04. `MigrationManifest_ListsCurrentBlockGMigrationsInOrderAndFilesExist` still expects 13 manifest entries while the current manifest has 23, and `ApplyMigrations_IsIdempotentAndVerifierPassesAgainstConfiguredTestDatabase` still cannot drop schema `workflow` because `usp_WorkflowGovernedContinuation_Transition` references it.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## Known Limitations

H04 is a SQL-backed integration test slice. It leaves one normal appended test event per append-path test, consistent with existing SQL integration test residue.

H04 does not change the `governance.GovernanceEvent` schema or permissions to make direct-DML behavior stronger.

H04 does not claim append-only storage makes event payloads authoritative.

Existing unrelated migration receipt debt remains outside H04 validation.

## Next Intended Slice

H05 - Receipt table/index review.

Review line: Receipt indexes improve lookup. They do not make receipts authoritative.

Killjoy: A fast receipt lookup is still just evidence.

## Killjoy Line

An immutable lie is still a lie.
