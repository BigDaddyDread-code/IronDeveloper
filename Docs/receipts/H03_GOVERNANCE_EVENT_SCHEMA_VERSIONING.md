# H03 Governance Event Schema Versioning Receipt

## Purpose

H03 adds a bounded schema-versioning contract for governance events so future readers, writers, stores, receipts, and replay/diagnostic tools can distinguish current, legacy, deprecated-readable, unknown future, unsupported, and invalid event payload shapes.

Schema versioning is parser evidence only.

A readable event is not an authoritative event.

Knowing how to read an event does not mean the event is true.

## Files Changed

- `IronDev.Core/Governance/GovernanceEventSchemaVersioning.cs`
- `IronDev.Core/Governance/GovernanceEventModels.cs`
- `IronDev.IntegrationTests/Governance/GovernanceEventSchemaVersioningTests.cs`
- `Docs/receipts/H03_GOVERNANCE_EVENT_SCHEMA_VERSIONING.md`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`

## Schema-Version Rules

- `GovernanceEventSchemaVersions.LegacyUnversioned = 0`
- `GovernanceEventSchemaVersions.Current = 1`

New governance events must use the explicit current version.

Legacy unversioned events are represented as version `0` for diagnostic classification only.

Unknown future versions are metadata only and must not be interpreted as current payloads.

## Classification Rules

H03 defines:

- `Current`
- `LegacyUnversioned`
- `DeprecatedReadable`
- `UnknownFuture`
- `Unsupported`
- `Invalid`

`Current` can be written, read for diagnostics, and interpreted by current code.

`LegacyUnversioned` can be read for diagnostics only.

`UnknownFuture` can be surfaced as metadata only.

`Unsupported` and `Invalid` fail closed.

`CanSatisfyAuthorityChecks` is always false in H03.

## Writer Behavior

Writer validation rejects missing, zero, negative, unsupported, and future schema versions for new governance events.

The existing `GovernanceEventValidator.ValidateAppend(...)` path now requires the explicit current version. It does not silently default old or missing versions to current.

## Reader Behavior

Reader-side classification can identify current, legacy unversioned, unknown future, unsupported, and invalid versions.

Reader classification does not authorize payload use, approval, policy satisfaction, source apply, workflow continuation, release readiness, deployment readiness, replay, backfill, or event mutation.

## Legacy / Unversioned Behavior

Legacy unversioned governance events are diagnostic only.

H03 does not rewrite legacy events.

H03 does not backfill legacy events.

H03 does not silently upgrade legacy events to current.

## Unknown Future Behavior

Unknown future versions can be surfaced as metadata.

Unknown future versions cannot be written by current writers.

Unknown future versions cannot be interpreted as current payloads.

Unknown future versions cannot satisfy authority checks.

## Boundary Rules

A governance-event schema version is not approval.

A governance-event schema version is not policy satisfaction.

A governance-event schema version is not source-apply authority.

A governance-event schema version is not workflow continuation authority.

A governance-event schema version is not merge readiness.

A governance-event schema version is not release readiness.

A governance-event schema version is not deployment readiness.

A governance-event schema version is not event replay permission.

A governance-event schema version is not permission to rewrite old events.

A governance-event schema version is not permission to infer missing authority.

A governance-event schema version is parser evidence only.

## What Was Intentionally Not Built

H03 does not add a SQL migration.

H03 does not alter the governance-event table.

H03 does not replay events.

H03 does not backfill old events.

H03 does not mutate existing governance events.

H03 does not add API/CLI/UI behavior.

H03 does not change workflow/source-apply/rollback/release/deployment authority.

H03 does not add database migration runner behavior.

H03 does not adopt DbUp.

H03 does not change `Database/migrations.json`.

H03 does not change `Database/apply-migrations.ps1`.

H03 does not change `Database/verify-migrations.ps1`.

## Tests Added

- `CurrentSchemaVersion_IsExplicitAndWritable`
- `MissingOrInvalidSchemaVersion_IsRejectedForNewEvents`
- `LegacyUnversionedEvents_AreDiagnosticOnly`
- `UnknownFutureSchemaVersion_FailsClosedForPayloadInterpretation`
- `SchemaVersionClassification_DoesNotGrantAuthority`
- `SchemaVersioning_DoesNotIntroduceSqlMigrationReplayOrBackfill`
- `Receipt_RecordsBoundaryAndLimitations`

The test class uses `Governance`, `GovernanceEvent`, `StaticBoundary`, and `Contract` categories.

## Commands Run

- `dotnet build IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-restore --verbosity minimal`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~GovernanceEventSchemaVersioningTests --verbosity minimal`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~IntegrationTestCategoryContractTests --verbosity minimal`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests --verbosity minimal`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~GovernanceEventStoreTests --verbosity minimal`
- `dotnet restore IronDev.slnx`
- `dotnet build IronDev.slnx --no-restore --verbosity minimal`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~MigrationStateTrackingDecisionTests|FullyQualifiedName~MigrationRunnerSpikeDecisionTests|FullyQualifiedName~GovernanceEventSchemaVersioningTests" --verbosity minimal`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~DatabaseMigrationApplicationReceiptTests --verbosity minimal`

## Validation Results

- Integration test project build: passed, 0 errors / 2 existing NU1510 warnings.
- H03 focused tests: 7/7 passed.
- Integration test category contract: 7/7 passed.
- C11 secret scan: 9/9 passed.
- Governance event store tests: 11/11 passed.
- Solution restore: passed with existing NU1510 warnings.
- Solution build: passed, 0 errors / 4 existing warnings.
- H01/H02/H03 corridor: 19/19 passed.
- `DatabaseMigrationApplicationReceiptTests`: failed on existing migration debt outside H03. `MigrationManifest_ListsCurrentBlockGMigrationsInOrderAndFilesExist` still expects 13 manifest entries while the current manifest has 23, and `ApplyMigrations_IsIdempotentAndVerifierPassesAgainstConfiguredTestDatabase` still cannot drop schema `workflow` because `usp_WorkflowGovernedContinuation_Transition` references it.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## Known Limitations

H03 does not tighten the SQL constraint on `governance.GovernanceEvent.PayloadVersion`; the existing database still has a positive-version constraint only. H03 is intentionally Core writer/classifier behavior plus tests, not a schema migration.

H03 does not add a read-model diagnostic endpoint.

H03 does not deserialize, migrate, replay, or backfill existing events.

H03 does not define deprecated readable versions beyond reserving the classification vocabulary.

## Next Intended Slice

H04 - Governance event append-only DB constraint tests.

Review line: Append-only storage preserves evidence. It does not validate authority.

Killjoy: An immutable lie is still a lie.

## Killjoy Line

Knowing how to read an event does not mean the event is true.
