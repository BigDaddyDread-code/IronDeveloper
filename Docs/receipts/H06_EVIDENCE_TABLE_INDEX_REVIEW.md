# H06 Evidence Table / Index Review Receipt

## Purpose

H06 reviews the current SQL evidence-reference tables, indexes, and lookup/query surfaces without changing schema.

Evidence indexes improve retrieval. They do not make evidence authoritative.

Fast evidence is still just evidence.

## Files Changed

- `Docs/reviews/H06_EVIDENCE_TABLE_INDEX_REVIEW.md`
- `Docs/receipts/H06_EVIDENCE_TABLE_INDEX_REVIEW.md`
- `IronDev.IntegrationTests/Governance/EvidenceTableIndexReviewTests.cs`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`
- `Docs/testing/SLOW_TEST_QUARANTINE_REGISTER.md`
- `IronDev.IntegrationTests/Governance/SlowQuarantineCategoryContractTests.cs`

The category inventory/register and slow/quarantine contract changed only because H06 adds one SQL-backed `RequiresRealDatabase` / `LongRunning` metadata review test class.

## What Was Reviewed

- Dedicated evidence-reference SQL tables discovered by table names and current evidence-reference store/procedure evidence.
- Evidence table columns, primary keys, indexes, filters, uniqueness, constraints, and triggers.
- Current stored procedures and store methods that write/read evidence references.
- Current lookup paths by parent memory item, handoff, workflow run, workflow checkpoint, memory proposal, allowed use, and evidence ID.
- Project/tenant scoping shape.
- UTC-oriented timestamp columns.
- Evidence metadata retention and artifact-retention risks.

## What Was Not Reviewed

H06 does not review receipt table indexes.

H06 does not review operation-status projection indexes.

H06 does not review Weaviate rebuild implementation.

H06 does not review production SQL permissions beyond the evidence review notes.

H06 does not measure runtime performance.

H06 does not prove every future lookup path is indexed.

## Evidence Tables Found

- `agent.AgentLocalMemoryEvidenceRef`
- `a2a.AgentHandoffEvidenceReference`
- `a2a.AgentHandoffEvidenceAllowedUse`
- `workflow.WorkflowRunEvidenceReference`
- `workflow.WorkflowCheckpointEvidenceReference`
- `memory.MemoryProposalEvidenceReference`

## Index Review Summary

Current indexes appear to support the current parent-scoped evidence-reference materialization paths for memory items, handoffs, workflow runs, and memory proposals.

Workflow checkpoint evidence has primary-key support but no direct nonclustered checkpoint-parent evidence index in the current metadata.

H06 does not claim runtime performance improvement. It only records metadata shape.

## Findings Summary

- H06-INFO-001: Current evidence indexes align with current parent-scoped evidence materialization paths.
- H06-LOW-001: Evidence-reference tables do not carry direct non-null tenant columns; local memory evidence inherits scope through the parent memory item.
- H06-LOW-002: Direct evidence-ID diagnostic lookup is not a current dedicated lookup path.
- H06-LOW-003: `workflow.WorkflowCheckpointEvidenceReference` has no direct nonclustered checkpoint-parent evidence index.
- H06-MEDIUM-001: Evidence metadata and artifact references need later retention/redaction/artifact-retention review.

## Boundary Rules

An evidence row is not approval.

An evidence row is not policy satisfaction.

An evidence row is not source-apply authority.

An evidence row is not workflow continuation authority.

An evidence row is not merge readiness.

An evidence row is not release readiness.

An evidence row is not deployment readiness.

An evidence index is not authority.

Fast evidence retrieval is not authority.

Evidence existence is not evidence truth.

Evidence retrieval is not evidence validation.

An evidence table review is not schema hardening.

An evidence table review is not retention policy.

An evidence table review is not redaction policy.

An evidence table review is evidence about storage shape only.

SQL remains source of truth.

Weaviate is rebuildable.

Read models may be rebuildable.

Authority records cannot be vibes.

Evidence indexes improve retrieval only.

Evidence indexes improve retrieval. They do not make evidence authoritative.

Fast evidence is still just evidence.

## What Was Intentionally Not Built

H06 does not add a SQL migration.

H06 does not alter evidence tables.

H06 does not add indexes.

H06 does not remove indexes.

H06 does not alter stored procedures.

H06 does not change permissions.

H06 does not add API/CLI/UI behavior.

H06 does not change workflow/source-apply/rollback/release/deployment authority.

H06 does not implement retention or redaction.

H06 does not implement evidence artifact retention.

H06 does not change Weaviate behavior.

H06 does not replay evidence.

H06 does not backfill evidence.

H06 does not repair evidence.

## Tests Added

- `EvidenceStorageReview_DocumentsEvidenceTablesAndIndexes`
- `EvidenceStorageReview_DocumentsKnownLookupPaths`
- `EvidenceStorageReview_RecordsIndexSupportFindingsWithoutChangingSchema`
- `EvidenceStorageReview_DoesNotMutateEvidenceStorage`
- `EvidenceStorageReview_DoesNotTreatEvidenceOrIndexesAsAuthority`
- `EvidenceStorageReview_PreservesSqlSourceOfTruthAndRebuildableIndexBoundary`
- `EvidenceStorageReview_RecordsPayloadRetentionAndArtifactRetentionAsLaterWork`
- `Receipt_RecordsReviewScopeAndLimitations`

The test class uses `Governance`, `Evidence`, `Store`, `RequiresRealDatabase`, `LongRunning`, `StorageReview`, `Boundary`, and `Contract` categories.

## Commands Run

- `dotnet build IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-restore --verbosity minimal`
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~EvidenceTableIndexReviewTests --verbosity minimal`
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~IntegrationTestCategoryContractTests|FullyQualifiedName~SlowQuarantineCategoryContractTests" --verbosity minimal`
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests --verbosity minimal`
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~GovernanceEventAppendOnlyDatabaseConstraintTests|FullyQualifiedName~ReceiptTableIndexReviewTests|FullyQualifiedName~EvidenceTableIndexReviewTests" --verbosity minimal`
- `dotnet build IronDev.slnx --no-restore --verbosity minimal`

## Validation Results

- Integration test project build: passed with existing warnings.
- H06 focused evidence table/index review tests: 8/8 passed.
- G13/G14 category/register contract tests: 17/17 passed.
- C11 secret scanning regression tests: 9/9 passed.
- H04/H05/H06 focused DB corridor: 22/22 passed.
- Solution build: passed with 0 errors and 4 existing warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## Known Limitations

H06 focused tests prepare the local integration database with existing migration files when the current test database has not already been prepared. H06 does not add or edit those migration files.

H06 does not prove runtime performance.

H06 does not prove privileged SQL identities cannot alter evidence storage.

H06 does not implement missing-index fixes.

H06 does not implement table-level tenant columns.

H06 does not implement evidence artifact retention.

Existing unrelated migration receipt debt remains outside H06 validation.

## Next Intended Slice

H07 - Operation status projection indexes.

Review line: Status indexes improve display and investigation. They do not make status authoritative.

Killjoy: Fast status is still not permission.

## Killjoy Line

Fast evidence is still just evidence.
