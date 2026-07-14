# CLN-21 Upgrade Migration Proof Receipt

**Recorded:** 14 July 2026

## Purpose

Prove that the current controlled migration sequence upgrades the last supported database baseline without losing durable product rows or supported semantic metadata.

## Starting Schema

The supported baseline is `main` commit `7f0e1058`, the last commit before CLN-19 moved request-path schema creation into the migration manifest.

The proof uses:

- `Database/baselines/7f0e1058_rebuild_db.sql`, an immutable snapshot of that commit's base schema;
- the ordered migration manifest through `2026-07-cln-12-user-mutation-attribution`; and
- `Database/baselines/cln_21_pre_cln_19_runtime_fixture.sql`, which recreates the runtime-owned table shapes present before CLN-19 and inserts fixed preservation rows.

## Migration Sequence

`Database/verify-upgrade-database.ps1`:

1. creates a uniquely named, isolated test database;
2. applies the pinned `7f0e1058` base schema;
3. applies the manifest through the CLN-12 baseline migration;
4. creates the pre-CLN-19 runtime-owned schema and preservation fixture;
5. applies the complete current migration manifest;
6. runs `Database/verify-migrations.ps1`; and
7. runs exact data-preservation assertions before removing the database.

## Data-Preservation Assertions

The proof preserves fixed IDs, scope, payloads, hashes, status, timestamps, scores, counts, and binary vector bytes for:

- tenant and project rows;
- project context documents;
- ticket compatibility fields;
- artifact source references;
- runs and run events;
- semantic artefacts and chunks;
- embedding jobs;
- semantic search traces and results;
- semantic embedding metadata; and
- semantic index runs.

The supported pre-CLN-19 search-trace shape uses GUID identities and is preserved. CLN-19's documented replacement of the unsupported pre-GUID derived diagnostic shape remains outside the durable-data preservation promise.

## Rollback and Recovery

There is no automated production down-migration in this slice.

Before a production-like upgrade:

1. stop application writes;
2. take and verify a restorable database backup;
3. record the starting commit, manifest identity, and database fingerprint; and
4. rehearse the upgrade and verification against a restored copy.

If apply, schema verification, or preservation verification fails:

1. keep application writes stopped;
2. retain the failed-attempt logs and database copy for diagnosis;
3. do not mark the migration verified and do not hand-edit rows to force a pass;
4. restore the verified backup to a replacement database under the owning DBA/operator procedure; and
5. correct and rehearse the migration before another controlled attempt.

The test script's database deletion is test cleanup only. It is not rollback authority and must not be reused against a production-like database.

## CI Ownership

`Scripts/ci/run-full-sql-integration-ci.ps1` executes the supported database upgrade proof as its own timed lane after the isolated platform baseline.

## Behaviour Preserved

- The migration manifest remains the ordering source of truth.
- Existing migration scripts remain idempotent.
- Durable SQL rows are not rewritten to satisfy verification.
- Migration evidence is not release, deployment, rollback, workflow, apply, or memory authority.

## Tests Executed

- `Database/verify-upgrade-database.ps1` — passed against a newly created LocalDB database; baseline migration, current migration, schema verification, all preservation assertions, and guarded cleanup passed.
- PowerShell parsing for `Database/apply-migrations.ps1` and `Database/verify-upgrade-database.ps1` — passed.
- Unknown `-ThroughMigrationId` — refused before a database connection was opened.
- `IronDev.IntegrationTests` focused build — passed with pre-existing package, nullability, and analyzer warnings.
- `UpgradeMigrationProofContractTests`, `ApplyMigrationsScriptContractTests`, and `RetrospectiveSqlInventoryTests` — passed, 16 tests.
- `Database/check-sql-inventory.ps1` — passed with 97 entries and all 62 top-level database SQL files covered.
