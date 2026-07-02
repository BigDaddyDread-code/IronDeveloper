# H05 Receipt Table / Index Review Receipt

## Purpose

H05 reviews the current SQL receipt storage tables, indexes, and lookup/query surfaces without changing schema.

Receipt indexes improve lookup. They do not make receipts authoritative.

A fast receipt lookup is still just evidence.

## Files Changed

- `Docs/reviews/H05_RECEIPT_TABLE_INDEX_REVIEW.md`
- `Docs/receipts/H05_RECEIPT_TABLE_INDEX_REVIEW.md`
- `IronDev.IntegrationTests/Governance/ReceiptTableIndexReviewTests.cs`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`
- `Docs/testing/SLOW_TEST_QUARANTINE_REGISTER.md`
- `IronDev.IntegrationTests/Governance/SlowQuarantineCategoryContractTests.cs`

The category inventory/register and slow/quarantine contract changed only because H05 adds one SQL-backed `RequiresRealDatabase` / `LongRunning` metadata review test class.

## What Was Reviewed

- Receipt-related SQL tables discovered by receipt table names and current receipt store/procedure evidence.
- Receipt table columns, primary keys, indexes, filters, uniqueness, and ordered index columns.
- Current receipt stored procedures and store methods.
- Current lookup paths by receipt ID, project, request/ref IDs, hash, subject, correlation, patch artifact, rollback refs, and diagnostics.
- Project/tenant scoping shape.
- UTC-oriented timestamp columns.
- JSON/text payload and raw-evidence risk.

## What Was Not Reviewed

H05 does not review evidence table indexes.

H05 does not review operation-status projection indexes.

H05 does not review Weaviate rebuild implementation.

H05 does not review production SQL permissions beyond the receipt review notes.

H05 does not measure runtime performance.

H05 does not prove every future lookup path is indexed.

## Receipt Tables Found

- `governance.ControlledDryRunReceipt`
- `governance.DogfoodReceipt`
- `governance.RollbackExecutionReceipt`
- `governance.RollbackSupportReceipt`
- `governance.SourceApplyDryRunReceipt`
- `governance.SourceApplyReceipt`

## Index Review Summary

Current indexes appear to support the current receipt store/procedure lookup paths for receipt IDs, project-scoped hashes, request/reference IDs, patch artifacts, rollback refs, subject/project dogfood receipt lists, correlation dogfood receipt lists, and chronological inspection where list procedures exist.

H05 does not claim runtime performance improvement. It only records metadata shape.

## Findings Summary

- H05-INFO-001: Current receipt indexes align with current receipt get/list methods.
- H05-LOW-001: Receipt tables use `ProjectId`; no discovered receipt table has `TenantId`.
- H05-LOW-002: `DogfoodReceipt.CausationId` exists, but no current causation list procedure/index was found.
- H05-LOW-003: Some indexes appear ahead of current public read surfaces.
- H05-MEDIUM-001: Receipt JSON/text payload columns need later retention/redaction review.

## Boundary Rules

A receipt row is not approval.

A receipt row is not policy satisfaction.

A receipt row is not source-apply authority.

A receipt row is not workflow continuation authority.

A receipt row is not merge readiness.

A receipt row is not release readiness.

A receipt row is not deployment readiness.

A receipt index is not authority.

A fast receipt lookup is not authority.

A receipt table review is not schema hardening.

A receipt table review is not retention policy.

A receipt table review is not redaction policy.

A receipt table review is evidence about storage shape only.

SQL remains source of truth.

Weaviate is rebuildable.

Read models may be rebuildable.

Authority records cannot be vibes.

Receipt indexes improve lookup only.

Receipt rows are evidence, not approval.

A fast receipt lookup is still just evidence.

## What Was Intentionally Not Built

H05 does not add a SQL migration.

H05 does not alter receipt tables.

H05 does not add indexes.

H05 does not remove indexes.

H05 does not alter stored procedures.

H05 does not change permissions.

H05 does not add API/CLI/UI behavior.

H05 does not change workflow/source-apply/rollback/release/deployment authority.

H05 does not implement retention or redaction.

H05 does not change Weaviate behavior.

H05 does not replay receipts.

H05 does not backfill receipts.

H05 does not repair receipts.

## Tests Added

- `ReceiptStorageReview_DocumentsReceiptTablesAndIndexes`
- `ReceiptStorageReview_DocumentsKnownLookupPaths`
- `ReceiptStorageReview_RecordsIndexSupportFindingsWithoutChangingSchema`
- `ReceiptStorageReview_DoesNotMutateReceiptStorage`
- `ReceiptStorageReview_DoesNotTreatReceiptsOrIndexesAsAuthority`
- `ReceiptStorageReview_PreservesSqlSourceOfTruthAndRebuildableIndexBoundary`
- `Receipt_RecordsReviewScopeAndLimitations`

The test class uses `Governance`, `Receipt`, `Store`, `RequiresRealDatabase`, `LongRunning`, `StorageReview`, `Boundary`, and `Contract` categories.

## Commands Run

- `dotnet build IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-restore --verbosity minimal`
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~ReceiptTableIndexReviewTests --verbosity minimal`
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~IntegrationTestCategoryContractTests|FullyQualifiedName~SlowQuarantineCategoryContractTests" --verbosity minimal`
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests --verbosity minimal`
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~GovernanceEventAppendOnlyDatabaseConstraintTests|FullyQualifiedName~ReceiptTableIndexReviewTests" --verbosity minimal`
- `dotnet build IronDev.slnx --no-restore --verbosity minimal`

## Validation Results

- Integration test project build: passed with existing warnings.
- H05 focused receipt table/index review tests: 7/7 passed.
- G13/G14 category/register contract tests: 17/17 passed.
- C11 secret scanning regression tests: 9/9 passed.
- H04/H05 focused DB corridor: 14/14 passed.
- Solution build: passed with 0 errors and 4 existing warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## Known Limitations

H05 focused tests prepare the local integration database with existing migration files when the current test database has not already been prepared. H05 does not add or edit those migration files.

H05 does not prove runtime performance.

H05 does not prove privileged SQL identities cannot alter receipt storage.

H05 does not implement missing-index fixes.

H05 does not implement table-level tenant columns.

Existing unrelated migration receipt debt remains outside H05 validation.

## Next Intended Slice

H06 - Evidence table/index review.

Review line: Evidence indexes improve retrieval. They do not make evidence authoritative.

Killjoy: Fast evidence is still just evidence.

## Killjoy Line

A fast receipt lookup is still just evidence.
