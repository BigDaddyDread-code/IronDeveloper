# H08 - TenantId Enforcement Tests On New Read Models

## Purpose

H08 adds focused tenant-boundary tests for newer governance read-model and read-repository surfaces so a tenant-scoped read cannot return cross-tenant records.

Tenant filters protect read scope. They do not create authority.

A tenant-scoped lie is still a lie.

## Files changed

- `IronDev.IntegrationTests/Governance/TenantEnforcementReadModelTests.cs`
- `Docs/receipts/H08_TENANT_ENFORCEMENT_READ_MODEL_TESTS.md`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`

## Read models reviewed

| Surface | Classification | Notes |
| --- | --- | --- |
| `GovernedOperationStatusReadRepository` | TenantEnforced | Uses `FrontendReadinessReadScope`, `IsTenantScoped`, and record `TenantId`; wrong or missing tenant fails closed. |
| `OperationStatusFrontendReadinessBackendTruthSource` | TenantEnforced | Propagates repository tenant mismatch as not-visible frontend-readiness state. |
| `OperationStatusReadEnvelopeFactory` | TenantNotApplicable | Formats already-scoped read state into an envelope; it is not a tenant lookup source. |
| `OperationStatusPaginator` | TenantEnforced | Existing D17 coverage validates tenant/project row isolation for supplied status rows. |
| `OperationTimelineReadRepository` | TenantEnforced | Uses `FrontendReadinessReadScope`, `IsTenantScoped`, and record `TenantId`; wrong or missing tenant fails closed. |
| `OperationTimelineFrontendReadinessBackendTruthSource` | TenantEnforced | Propagates repository tenant mismatch as not-visible frontend-readiness state. |
| `EvidenceMetadataReadRepository` | TenantEnforced | Uses `FrontendReadinessReadScope`, `IsTenantScoped`, and record `TenantId`; wrong or missing tenant fails closed. |
| `EvidenceMetadataFrontendReadinessBackendTruthSource` | TenantEnforced | Propagates repository tenant mismatch as not-visible frontend-readiness state. |
| `ReceiptMetadataReadRepository` | TenantEnforced | Uses `FrontendReadinessReadScope`, `IsTenantScoped`, and record `TenantId`; wrong or missing tenant fails closed. |
| `ReceiptMetadataFrontendReadinessBackendTruthSource` | TenantEnforced | Propagates repository tenant mismatch as not-visible frontend-readiness state. |
| `PatchPackageMetadataReadRepository` | TenantEnforced | Uses `FrontendReadinessReadScope`, `IsTenantScoped`, and record `TenantId`; wrong or missing tenant fails closed. |
| `PatchPackageMetadataFrontendReadinessBackendTruthSource` | TenantEnforced | Propagates repository tenant mismatch as not-visible frontend-readiness state. |
| `ValidationResultMetadataReadRepository` | TenantEnforced | Uses `FrontendReadinessReadScope`, `IsTenantScoped`, and record `TenantId`; wrong or missing tenant fails closed. |
| `ValidationResultMetadataFrontendReadinessBackendTruthSource` | TenantEnforced | Propagates repository tenant mismatch as not-visible frontend-readiness state. |
| `BackendFrontendReadinessReadApi` | TenantEnforced | Uses `ICurrentTenantContext` to build read scope and stops on not-visible canonical backend truth without falling through to fallback data. |
| `FrontendReadinessReadModels snapshot API` | TenantNotApplicable | The snapshot sanitizer has no tenant selector; it must be fed only after repository/source tenant selection. H08 records this limitation rather than pretending the snapshot is a tenant repository. |
| `InterruptedRunReadModelValidator` | TenantEnforced | Request, checkpoint, and diagnostic snapshot tenant mismatch fail closed. |
| `RollbackRecoveryReadModelValidator` | TenantEnforced | Request, material, and diagnostic snapshot tenant mismatch fail closed. |
| `WorktreeBaseHeadFreshnessReadModelValidator` | TenantEnforced | Request, rule, expectation, and observation tenant mismatch fail closed. |
| `PatchBaseFreshnessResolver` | TenantEnforced | Existing D12 resolver contract carries required tenant scope and mismatch checks. |
| `ValidationStalenessResolver` | TenantEnforced | Existing D11 resolver contract carries required tenant scope and mismatch checks. |
| `ReceiptReferenceResolver` | TenantEnforced | Existing D09 resolver contract carries required tenant scope and mismatch checks. |
| `EvidenceResolver` | TenantEnforced | Existing D10 resolver contract carries required tenant scope and mismatch checks. |

Classification summary:

- GovernedOperationStatusReadRepository: TenantEnforced
- OperationTimelineReadRepository: TenantEnforced
- EvidenceMetadataReadRepository: TenantEnforced
- ReceiptMetadataReadRepository: TenantEnforced
- PatchPackageMetadataReadRepository: TenantEnforced
- ValidationResultMetadataReadRepository: TenantEnforced
- BackendFrontendReadinessReadApi: TenantEnforced
- FrontendReadinessReadModels snapshot API: TenantNotApplicable
- InterruptedRunReadModelValidator: TenantEnforced
- RollbackRecoveryReadModelValidator: TenantEnforced
- WorktreeBaseHeadFreshnessReadModelValidator: TenantEnforced

## Tests added

`TenantEnforcementReadModelTests` adds focused H08 coverage:

- `OperationStatusReadModel_DoesNotReturnCrossTenantRecords`
- `OperationTimelineReadModel_DoesNotReturnCrossTenantEntries`
- `EvidenceMetadataReadModel_DoesNotReturnCrossTenantEvidence`
- `ReceiptMetadataReadModel_DoesNotReturnCrossTenantReceipts`
- `FrontendReadinessReadModel_DoesNotLeakTenantBackendTruth`
- `TenantScopedReadModels_FailClosedWhenTenantIdIsMissingOrMismatched`
- `NewDiagnosticReadModels_FailClosedOnTenantMismatch`
- `TenantScopedReadModels_DoNotGrantAuthority`
- `Receipt_RecordsTenantBoundaryAndLimitations`

## What passed

Local validation:

- `dotnet build IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-restore --verbosity minimal`: passed with existing warnings.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~TenantEnforcementReadModelTests --verbosity minimal`: 9/9 passed.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~IntegrationTestCategoryContractTests --verbosity minimal`: 7/7 passed.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests --verbosity minimal`: 9/9 passed.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~TenantEnforcementReadModelTests|FullyQualifiedName~BlockA12FrontendReadinessAuthorizationTenantScopeProofTests|FullyQualifiedName~BlockD17OperationStatusTenantIsolationTests" --verbosity minimal`: 154/154 passed.
- `dotnet build IronDev.slnx --no-restore --verbosity minimal`: passed with 0 errors and 4 existing warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## Gaps found

- No SQL-backed operation-status projection table exists yet; H07 recorded that deferral.
- `FrontendReadinessReadModels` snapshot sanitization is not a tenant lookup source. It is safe only after an upstream tenant-aware source or repository selected the visible data.
- H08 does not add tenant database columns, indexes, stored procedures, or schema constraints. Durable storage tenant hardening must remain a separate storage slice where needed.

## Boundary rules

Tenant filters protect read scope only.

Tenant filtering is not approval.

Tenant filtering is not policy satisfaction.

Tenant filtering is not source-apply authority.

Tenant filtering is not workflow continuation authority.

Tenant filtering is not merge readiness.

Tenant filtering is not release readiness.

Tenant filtering is not deployment readiness.

Tenant filtering is not rollback authority.

Tenant filtering is not retry authority.

Tenant filtering is not mutation authority.

A tenant-scoped read is not proof the record is true.

A tenant-scoped read is not proof the actor was authorized.

A tenant-scoped read is not proof the next action is safe.

A tenant-scoped lie is still a lie.

SQL remains source of truth where SQL-backed records exist.

Weaviate is rebuildable.

Read models may be rebuildable.

Authority records cannot be vibes.

## What was intentionally not built

H08 does not add a SQL migration.

H08 does not alter tables.

H08 does not add TenantId columns.

H08 does not add indexes.

H08 does not alter stored procedures.

H08 does not alter triggers.

H08 does not alter permissions.

H08 does not change API/CLI/UI behavior.

H08 does not change workflow/source-apply/rollback/release/deployment authority.

H08 does not change approval, policy, source-apply, rollback, release, or deployment decisions.

H08 does not change authority profiles.

H08 does not change Weaviate behavior.

H08 does not rebuild, replay, or backfill projections.

H08 does not create operation-status projection storage.

H08 does not create a tenant-policy engine.

## Commands run

- `dotnet build IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-restore --verbosity minimal`
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~TenantEnforcementReadModelTests --verbosity minimal`
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~IntegrationTestCategoryContractTests --verbosity minimal`
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests --verbosity minimal`
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~TenantEnforcementReadModelTests|FullyQualifiedName~BlockA12FrontendReadinessAuthorizationTenantScopeProofTests|FullyQualifiedName~BlockD17OperationStatusTenantIsolationTests" --verbosity minimal`
- `dotnet build IronDev.slnx --no-restore --verbosity minimal`
- `git diff --check`
- `git diff --cached --check`

## Validation results

- H08 focused tests: 9/9 passed.
- Integration category contract: 7/7 passed.
- C11 secret scan: 9/9 passed.
- H08 + A12 + D17 tenant/read-model corridor: 154/154 passed.
- Solution build: 0 errors / 4 existing warnings.
- Diff whitespace check: passed.
- Cached diff whitespace check: passed.

## Known limitations

H08 is a focused contract-test slice over current in-memory/read-adapter surfaces. It does not prove SQL tenant constraints, API authorization, controller routing, durable projection rebuild behavior, Weaviate rebuild behavior, or storage index behavior.

Existing A12 and D17 coverage remains the deeper frontend-readiness and operation-status pagination tenant isolation proof. H08 adds a cross-surface contract and names the current classifications.

## Next intended slice

H09 - UTC timestamp DB constraint review.

Review line: UTC timestamps make time comparable. They do not make records authoritative.

Killjoy: A correctly timed lie is still a lie.
