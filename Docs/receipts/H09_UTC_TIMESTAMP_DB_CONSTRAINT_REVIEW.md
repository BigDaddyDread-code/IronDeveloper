# H09 UTC Timestamp DB Constraint Review Receipt

## Purpose

H09 reviews current database timestamp columns, defaults, check constraints, and stored-procedure timestamp write patterns.

H09 is a SQL metadata review and contract-test slice.

UTC timestamps make time comparable only.

A correctly timed lie is still a lie.

## Files Changed

- `Docs/reviews/H09_UTC_TIMESTAMP_DB_CONSTRAINT_REVIEW.md`
- `Docs/receipts/H09_UTC_TIMESTAMP_DB_CONSTRAINT_REVIEW.md`
- `IronDev.IntegrationTests/Governance/UtcTimestampDbConstraintReviewTests.cs`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`
- `Docs/testing/SLOW_TEST_QUARANTINE_REGISTER.md`
- `IronDev.IntegrationTests/Governance/SlowQuarantineCategoryContractTests.cs`

The category inventory/register and slow/quarantine contract changed only because H09 adds one SQL-backed `RequiresRealDatabase` / `LongRunning` metadata review test class.

## What Was Reviewed

H09 reviewed:

- timestamp-like database columns discovered through SQL metadata
- timestamp default expressions
- timestamp-adjacent check constraints
- stored procedure timestamp write patterns
- UTC naming versus UTC defaults versus UTC enforcement
- non-authority boundary wording

H09 focused on current app/governance schemas: `dbo`, `governance`, `workflow`, `a2a`, `agent`, and `memory`.

H09 focused on DB metadata. H09 is DB metadata review only.

Repository UTC standard preserved:

- Persist UTC.
- Transmit UTC.
- Display UTC-aware dates.

## What Was Not Reviewed

H09 does not review API/client timestamp formatting.

H09 does not review UI timestamp display.

H09 does not review retention/redaction policy beyond recording H10 as the next intended slice.

H09 does not review tenant SQL constraints.

H09 does not review Weaviate rebuild/auth/config.

H09 does not review release/deployment gates.

H09 does not measure runtime timestamp-write performance.

## Timestamp Columns Found

Timestamp-like columns found: 135.

Classification summary:

- UtcEnforced: 0
- UtcDefaulted: 65
- UtcNamedOnly: 0
- UtcParameterOrProcedureDependent: 35
- LegacyAssumedUtc: 5
- Ambiguous: 0
- NonUtcOrLocalRisk: 0
- NotApplicable: 30

The full column inventory is recorded in `Docs/reviews/H09_UTC_TIMESTAMP_DB_CONSTRAINT_REVIEW.md`.

NotApplicable rows are intentional. The broad discovery rule catches non-timestamp fields such as `CreatedByActorId`, `ObservedBranch`, `StepCompleted`, `NextStepStarted`, and `IndexedFileCount`.

## Default Constraint Summary

Default expressions discovered:

- `SYSUTCDATETIME()`: 65
- `GETUTCDATE()`: 0
- `SYSDATETIMEOFFSET()`: 0
- `SYSDATETIME()`: 0
- `GETDATE()`: 0
- literal date/time: 0
- no default / procedure-dependent candidates: 70

H09 does not add default constraints.

H09 does not alter default constraints.

## Check Constraint Summary

No UTC-enforcing timestamp check constraints were found.

32 timestamp-like columns have check constraints, but those checks are expiry/order/shape/authority checks rather than UTC-offset enforcement.

H09 does not add check constraints.

H09 does not alter check constraints.

## Stored Procedure Timestamp Write Summary

Procedure candidates reviewed: 123.

- 11 procedures use `SYSUTCDATETIME()`.
- 119 procedures have UTC-named parameters or UTC-shaped fields.
- 0 procedures use `GETUTCDATE()`.
- 0 procedures use `GETDATE()`.
- 0 procedures use `CURRENT_TIMESTAMP`.
- 0 procedures use `SYSDATETIME()`.
- 0 procedures use `SYSDATETIMEOFFSET()`.

Representative procedures reviewed:

- `governance.usp_AcceptedApproval_Save`
- `governance.usp_PolicySatisfaction_Save`
- `governance.usp_SourceApplyReceipt_Save`
- `governance.usp_RollbackExecutionReceipt_Save`
- `governance.usp_WorkflowTransitionRecord_Save`
- `governance.usp_ReleaseReadinessDecisionRecord_Save`
- `workflow.usp_WorkflowRun_Create`
- `workflow.usp_WorkflowStep_Create`

H09 does not alter stored procedures.

## Findings Summary

- H09-INFO-001: 65 timestamp-like columns use UTC-shaped `SYSUTCDATETIME()` defaults.
- H09-LOW-001: 35 UTC-named timestamp columns are procedure/application dependent rather than DB-enforced.
- H09-LOW-002: 5 legacy date columns lack explicit UTC suffix and rely on project convention.
- H09-MEDIUM-001: no UTC-offset enforcing database check constraints were found.

All gaps are findings only.

## Boundary Rules

UTC timestamp shape is not approval.

UTC timestamp shape is not policy satisfaction.

UTC timestamp shape is not source-apply authority.

UTC timestamp shape is not workflow continuation authority.

UTC timestamp shape is not merge readiness.

UTC timestamp shape is not release readiness.

UTC timestamp shape is not deployment readiness.

UTC timestamp shape is not rollback authority.

UTC timestamp shape is not retry authority.

UTC timestamp shape is not mutation authority.

UTC timestamp shape does not prove the payload is true.

UTC timestamp shape does not prove the actor was authorized.

UTC timestamp shape does not prove the next action is safe.

UTC timestamps make time comparable only.

A correctly timed lie is still a lie.

## What Was Intentionally Not Built

H09 does not add a SQL migration.

H09 does not alter tables.

H09 does not alter timestamp columns.

H09 does not rename timestamp columns.

H09 does not add default constraints.

H09 does not alter default constraints.

H09 does not add check constraints.

H09 does not alter check constraints.

H09 does not alter stored procedures.

H09 does not alter triggers.

H09 does not change permissions.

H09 does not change API/CLI/UI behavior.

H09 does not change workflow/source-apply/rollback/release/deployment authority.

H09 does not change Weaviate behavior.

H09 does not change approval, policy, source-apply, release, or deployment decisions.

H09 does not change authority profiles.

H09 does not rebuild, replay, or backfill projections.

H09 does not adopt a migration runner or DbUp.

## Tests Added

`UtcTimestampDbConstraintReviewTests` adds focused H09 coverage:

- `UtcTimestampReview_DocumentsTimestampColumns`
- `UtcTimestampReview_ClassifiesDefaultsAndConstraints`
- `UtcTimestampReview_RecordsProcedureTimestampWritePatterns`
- `UtcTimestampReview_DoesNotMutateDatabaseSchema`
- `UtcTimestampReview_DoesNotAddUtcConstraints`
- `UtcTimestampReview_PreservesUtcStandardWithoutExpandingScope`
- `UtcTimestampReview_DoesNotTreatCorrectTimeAsAuthority`
- `Receipt_RecordsReviewScopeAndLimitations`

Suggested categories applied:

- `Governance`
- `Database`
- `UtcTimestamp`
- `StorageReview`
- `RequiresRealDatabase`
- `LongRunning`
- `Boundary`
- `Contract`

The tests use SQL metadata reads after normal integration setup:

- `sys.schemas`
- `sys.tables`
- `sys.columns`
- `sys.types`
- `sys.default_constraints`
- `sys.check_constraints`
- `sys.procedures`
- `sys.sql_modules`
- `sys.triggers`

The tests do not insert rows.

The tests do not update rows.

The tests do not delete rows.

The tests do not create constraints.

The tests do not drop constraints.

The tests do not alter schema.

The tests do not alter procedures.

## Commands Run

- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --filter FullyQualifiedName~UtcTimestampDbConstraintReviewTests --logger "trx;LogFileName=h09-utc-timestamp-review.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~UtcTimestampDbConstraintReviewTests --logger "trx;LogFileName=h09-utc-timestamp-review.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~IntegrationTestCategoryContractTests --logger "trx;LogFileName=h09-category-contract.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~SlowQuarantineCategoryContractTests --logger "trx;LogFileName=h09-slow-quarantine-contract.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~ReceiptTableIndexReviewTests|FullyQualifiedName~EvidenceTableIndexReviewTests|FullyQualifiedName~OperationStatusProjectionIndexReviewTests|FullyQualifiedName~TenantEnforcementReadModelTests|FullyQualifiedName~UtcTimestampDbConstraintReviewTests" --logger "trx;LogFileName=h05-h09-storage-readmodel-corridor.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests --logger "trx;LogFileName=h09-c11-secret-scan.trx"`
- `dotnet build IronDev.slnx --no-restore`
- `git diff --check`
- `git diff --cached --check`

## Validation Results

- Initial H09 focused test run: 7/8 passed, 1 receipt wording failure for missing explicit UTC standard wording.
- H09 focused tests after receipt fix: 8/8 passed.
- G13 category contract: 7/7 passed.
- G14 slow/quarantine category contract: 10/10 passed.
- H05-H09 storage/read-model corridor: 37/37 passed.
- C11 secret scan: 9/9 passed.
- Solution build: 0 errors / 4 existing warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed after staging the exact H09 files.

## Known Limitations

H09 focused tests prepare the local integration database with existing migration files when the current test database has not already been prepared. H09 does not add or edit those migration files.

H09 does not prove privileged SQL identities cannot alter timestamp storage.

H09 does not prove every application-supplied UTC timestamp is truly UTC.

H09 does not implement UTC-offset enforcing constraints.

H09 does not implement timestamp data migration.

H09 does not rename legacy timestamp columns.

H09 does not prove API/client/UI timestamp display behavior.

Existing unrelated migration debt remains outside H09 validation.

## Next Intended Slice

H10 - Raw payload redaction/retention policy.

Review line: Redaction policy limits exposure. It does not make retained payloads safe.

Killjoy: A retained secret is still a secret.
