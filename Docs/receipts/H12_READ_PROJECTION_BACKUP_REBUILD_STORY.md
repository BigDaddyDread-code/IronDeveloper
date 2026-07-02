# H12 Read Projection Backup and Rebuild Story Receipt

## Purpose

H12 defines the backup and rebuild story for read projections and derived read models.

H12 explains which data can be rebuilt, what source records are required, what order/replay boundaries matter, and what must never be treated as rebuildable authority.

H12 defines story/policy only.

Projection rebuild plans restore read models. They do not recreate authority records.

A rebuilt projection is not the source of truth.

## Files Changed

- `Docs/architecture/H12_READ_PROJECTION_BACKUP_REBUILD_STORY.md`
- `Docs/receipts/H12_READ_PROJECTION_BACKUP_REBUILD_STORY.md`
- `IronDev.IntegrationTests/Governance/ReadProjectionBackupRebuildStoryTests.cs`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`

H12 does not update `Docs/testing/SLOW_TEST_QUARANTINE_REGISTER.md` because H12 tests do not connect to SQL or use real external resources.

## Source-Of-Truth Chain

H12 records this chain:

```text
Durable source records -> deterministic projection/rebuild process -> read model / index / cache -> display/query surface
```

SQL is source of truth.

Authority records are durable source records.

Read models may be rebuildable.

Weaviate/vector indexes are rebuildable derived indexes.

The chain does not work in reverse.

A read model must not recreate authority records.

A vector index must not recreate authority records.

A UI state must not recreate authority records.

A receipt summary must not recreate authority records.

## Rebuildable / Derived Surfaces

H12 classifies these read-side surfaces:

- operation status summaries/projections
- operation timeline read models
- frontend readiness read models
- evidence metadata read models
- receipt metadata read models
- validation-result metadata read models
- patch-package metadata read models
- interrupted-run read models
- rollback-recovery read models
- worktree/base/head freshness read models
- status/error envelope read models
- read-side cache/index state
- Weaviate/vector indexes
- unknown read surface
- authority/source records

H12 defines these classifications:

- `RebuildableProjection`
- `RebuildableCache`
- `RebuildableIndex`
- `ManualReviewRequired`
- `UnknownRequiresReview`
- `NotRebuildableAuthorityRecord`

## Non-Rebuildable Authority Records

These are non-rebuildable authority/source records:

- governance events
- accepted approvals
- policy satisfaction records
- tool requests
- tool gate decisions
- approval decisions
- controlled dry-run receipts
- patch artifacts
- rollback support receipts
- source-apply dry-run receipts
- source-apply receipts
- rollback execution receipts
- workflow transition records
- release readiness decision records
- durable memory proposal decisions, where used as source records
- any append-only audit/event/receipt table intended as durable evidence

These records must be backed up and preserved.

They must not be reconstructed from projections, vector indexes, UI state, receipt summaries, safe summaries, or read models.

## Backup Story

Authority/source records must be backed up as durable records.

Backup must preserve:

- IDs
- tenant/project scope
- timestamps
- payload version
- hashes
- source references
- causation/correlation links
- append-only/event order where applicable
- receipt/artifact references
- schema version
- redaction/retention markers where applicable

Read projections may be backed up for convenience, but backup is not required for correctness if they are rebuildable from source records.

Projection backups must be treated as cache snapshots, not authority.

Vector backup is not authority.

## Rebuild Story

A future rebuild process must have:

- explicit target projection
- explicit source record set
- explicit tenant/project scope
- source schema/version compatibility check
- deterministic ordering rule
- checkpoint/cursor handling
- idempotency rule
- stale projection clearing rule
- dry-run mode
- verification step
- receipt/evidence of rebuild attempt
- failure classification
- no authority mutation
- no source-record mutation
- no approval/policy/source-apply/release/deploy grant

H12 defines these requirements only.

H12 does not build the process.

## Tenant / Project Boundary

Future rebuilds must not:

- rebuild all tenants by default
- mix tenant data
- rebuild Tenant A projection from Tenant B source records
- allow missing TenantId to mean all tenants
- rely on project-only scope where TenantId is required

Tenant-scoped rebuild is still not authority.

## Failure Classes

Future failure classes:

- `SourceRecordsMissing`
- `SourceRecordsCorrupt`
- `SchemaVersionUnsupported`
- `ProjectionOrderingAmbiguous`
- `TenantScopeMismatch`
- `ProjectionWriteFailed`
- `VerificationFailed`
- `PartialRebuildDetected`
- `VectorIndexRebuildFailed`
- `ManualReviewRequired`

Failures must become explainable states, not silent partial reads.

## Weaviate / H13 Boundary

H13 owns Weaviate rebuild command hardening.

H12 records:

- vector indexes are rebuildable derived indexes
- vector recall is not authority
- vector content must come from safe summaries or approved redacted content
- deleting/rebuilding vector indexes does not delete or recreate source records
- vector rebuild failure must not block authority records from existing
- vector rebuild success does not approve anything

H12 does not implement Weaviate behavior.

## What Was Intentionally Not Built

H12 does not implement backup.

H12 does not implement backup jobs.

H12 does not implement rebuild.

H12 does not implement rebuild commands.

H12 does not implement projection replay.

H12 does not add a SQL migration.

H12 does not alter tables.

H12 does not add indexes.

H12 does not alter stored procedures.

H12 does not alter triggers.

H12 does not change permissions.

H12 does not change API/CLI/UI behavior.

H12 does not change Weaviate behavior.

H12 does not change workflow/source-apply/rollback/release/deployment authority.

H12 does not add data migration.

H12 does not add projection backfill.

H12 does not add read projection storage mutation.

H12 does not add artifact retention/deletion implementation.

H12 does not add migration runner or DbUp work.

## Non-Authority Boundary

Backup is not authority.

Rebuild is not authority.

A rebuilt projection is not approval.

A rebuilt projection is not policy satisfaction.

A rebuilt projection is not source-apply authority.

A rebuilt projection is not workflow continuation authority.

A rebuilt projection is not merge readiness.

A rebuilt projection is not release readiness.

A rebuilt projection is not deployment readiness.

A rebuilt projection is not rollback authority.

A rebuilt projection is not retry authority.

A rebuilt projection is not mutation authority.

A rebuilt projection does not prove source records are true.

A rebuilt projection does not prove the actor was authorized.

A rebuilt projection does not prove the next action is safe.

A rebuilt projection does not recreate missing authority records.

A rebuilt projection is not the source of truth.

Vector rebuild is not approval.

Vector rebuild is not release readiness.

Vector rebuild is not source-apply authority.

## Tests Added

H12 adds `ReadProjectionBackupRebuildStoryTests`.

The tests prove:

- the story and receipt define the source-of-truth chain
- authority/source records are classified as non-rebuildable
- derived read surfaces are classified as rebuildable, manual-review, unknown-review, or source records
- future rebuild requirements are explicit
- tenant/project and failure boundaries are explicit
- Weaviate command hardening is deferred to H13
- backup/rebuild/vector rebuild are not authority
- H12 did not add SQL, schema, runtime, backup, rebuild, replay, API, CLI, UI, Weaviate, workflow, source-apply, rollback, release, deployment, artifact lifecycle, migration runner, or DbUp implementation
- the receipt records scope and limitations

## Commands Run

- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --filter FullyQualifiedName~ReadProjectionBackupRebuildStoryTests --logger "trx;LogFileName=h12-read-projection-backup-rebuild-story.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~ReadProjectionBackupRebuildStoryTests --logger "trx;LogFileName=h12-read-projection-backup-rebuild-story.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~IntegrationTestCategoryContractTests --logger "trx;LogFileName=h12-category-contract.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~RawPayloadRedactionRetentionPolicyTests|FullyQualifiedName~EvidenceArtifactRetentionPolicyTests|FullyQualifiedName~ReadProjectionBackupRebuildStoryTests" --logger "trx;LogFileName=h10-h12-policy-corridor.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests --logger "trx;LogFileName=h12-c11-secret-scan.trx"`
- `dotnet build IronDev.slnx --no-restore`
- `git diff --check`
- `git diff --cached --check`

## Validation Results

- H12 focused tests: 9/9 passed.
- G13 category contract: 7/7 passed.
- H10-H12 policy corridor: 25/25 passed.
- C11 secret scan: 9/9 passed.
- Solution build: 0 errors / 4 existing warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed after staging the exact H12 files.

## Known Limitations

H12 does not implement backup.

H12 does not implement rebuild.

H12 does not verify existing backups.

H12 does not verify existing projections are rebuildable.

H12 does not prove existing source records are complete.

H12 does not prove historical projection rows are correct.

H12 does not define exact backup retention windows.

H12 does not implement lifecycle operations.

H12 does not implement H13.

## Next Intended Slice

H13 - Weaviate rebuild command hardening.

Review line: Weaviate rebuild restores recall. It does not restore authority.

Killjoy: A rebuilt vector index is still just an index.
