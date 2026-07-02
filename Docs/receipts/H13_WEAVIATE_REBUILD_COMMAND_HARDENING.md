# H13 Weaviate Rebuild Command Hardening Receipt

## Purpose

H13 hardens Weaviate rebuild command behavior.

Weaviate rebuild restores recall. It does not restore authority.

A rebuilt vector index is still just an index.

H13 preserves the Block H invariant:

- SQL remains source of truth.
- Weaviate remains a rebuildable derived index.
- Authority records cannot be rebuilt from Weaviate.

## Files Changed

- `IronDev.Core/KnowledgeCompiler/SemanticIndexRebuildModels.cs`
- `IronDev.Core/KnowledgeCompiler/ISemanticMemoryService.cs`
- `IronDev.Infrastructure/Services/SemanticMemory/WeaviateSemanticMemoryService.cs`
- `IronDev.Infrastructure/Services/SemanticMemory/InMemorySemanticMemoryService.cs`
- `IronDev.IntegrationTests/Governance/WeaviateRebuildCommandHardeningTests.cs`
- `Docs/receipts/H13_WEAVIATE_REBUILD_COMMAND_HARDENING.md`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`

H13 does not update `Docs/testing/SLOW_TEST_QUARANTINE_REGISTER.md` because H13 tests do not connect to SQL, Docker, Weaviate, or real external resources.

## Existing Rebuild Surface Reviewed

The existing semantic memory surface exposed:

- `ISemanticMemoryService.RebuildIndexAsync(int projectId, ...)`
- `ISemanticMemoryService.RebuildProjectAsync(int projectId, ...)`
- `WeaviateSemanticMemoryService.RebuildIndexAsync(int projectId, ...)`
- `InMemorySemanticMemoryService.RebuildIndexAsync(int projectId, ...)`

The existing Weaviate rebuild path was project-parameterized but deleted the configured collection during rebuild.

The configured collection is shared by prefix, not obviously per-project.

Project-scoped rebuild must not silently delete/reset a shared collection.

## Hardening Outcome

Hardening outcome selected: `SafeHardeningImplemented`.

H13 adds an explicit rebuild request/result contract and routes the existing project rebuild method through it.

H13 removes the project-scoped collection delete from the Weaviate rebuild path.

H13 does not implement a full collection reset.

H13 blocks collection reset requests with `UnsafeSharedCollectionReset`.

## Rebuild Request / Result Contract

H13 adds:

- `SemanticIndexRebuildRequest`
- `SemanticIndexRebuildMode`
- `SemanticIndexRebuildPlan`
- `SemanticIndexRebuildResult`
- `SemanticIndexRebuildStatus`
- `SemanticIndexRebuildBlockReason`
- `SemanticIndexRebuildGuard`

`SemanticIndexRebuildRequest` carries:

- `ProjectId`
- `Mode`
- `DryRun`
- `AllowCollectionReset`
- `RequestedBy`
- `RequestedAtUtc`
- `Reason`

`RequestedBy` is metadata, not authority.

`Reason` is metadata, not authority.

`AllowCollectionReset` defaults to false.

`AllowCollectionReset` must not be implied by project rebuild.

`SemanticIndexRebuildResult` always keeps authority flags false:

- `IsAuthorityGrant = false`
- `GrantsApproval = false`
- `GrantsPolicySatisfaction = false`
- `GrantsSourceApplyAuthority = false`
- `GrantsWorkflowContinuation = false`
- `GrantsReleaseReadiness = false`
- `GrantsDeploymentReadiness = false`

## Shared Collection Reset Handling

Project-scoped rebuild no longer calls `Collections.Delete(collectionName)`.

Project-scoped rebuild marks project semantic chunks stale, ensures the configured collection exists, and re-embeds active project documents.

The rebuild plan reports:

- `IsDestructive = false`
- `WillDeleteCollection = false`
- `WillMutateSqlSourceRecords = false`
- `WillMutateAuthorityRecords = false`

If `AllowCollectionReset = true`, H13 blocks with `UnsafeSharedCollectionReset`.

If `Mode = FullCollectionResetBlocked`, H13 blocks with `UnsafeSharedCollectionReset`.

Full collection reset belongs in a future explicit guarded slice if it is needed.

## Project Scope Behavior

Project rebuild requires explicit project scope.

Missing/default project ID fails closed with `MissingProjectId`.

No H13 path rebuilds all projects by default.

No H13 path treats missing project ID as all projects.

Tenant/project source-of-truth boundaries from H12 remain intact.

## Failure / Block Reasons

H13 defines these block/failure reasons:

- `MissingProjectId`
- `WeaviateDisabled`
- `WeaviateUnavailable`
- `UnsafeSharedCollectionReset`
- `UnsupportedGlobalRebuild`
- `UnsafeSourceContent`
- `SourceDocumentsUnavailable`
- `Cancelled`
- `Unknown`

Weaviate disabled returns an explainable blocked result in the request/result contract.

Weaviate unavailable returns an explainable failed result in the request/result contract.

H13 sanitizes secret-shaped provider error text before putting it in rebuild results.

## H10 / H11 Payload Boundary

H13 preserves H10/H11 boundaries:

- Weaviate/vector text must come from safe summaries or approved redacted content where applicable.
- Do not index raw secrets.
- Do not index raw artifact bodies.
- Do not index raw private payloads.
- Do not treat vector indexing as redaction.

Vector indexing is not redaction.

H13 does not implement raw payload redaction.

H13 does not implement artifact retention.

If existing source extraction is broader than desired, that remains a known limitation for a future redaction/source-extraction slice.

## H12 Source-Of-Truth Boundary

SQL remains source of truth.

Weaviate remains a rebuildable derived index.

H13 does not make Weaviate authoritative.

H13 does not restore authority.

H13 does not recreate authority records.

Authority records cannot be rebuilt from Weaviate.

## H14 Auth / Production Config Boundary

H13 does not change Weaviate auth/prod config.

H14 owns Weaviate auth/prod config tests.

H13 does not change API keys, TLS, Docker, production deployment configuration, or Weaviate authentication behavior.

## What Was Intentionally Not Built

H13 does not implement a full collection reset.

H13 does not add a new CLI rebuild command.

H13 does not add a new API endpoint.

H13 does not add a UI route or button.

H13 does not change Docker compose behavior.

H13 does not change Weaviate auth/prod config.

H13 does not add a SQL migration.

H13 does not alter tables.

H13 does not add indexes.

H13 does not alter stored procedures.

H13 does not alter triggers.

H13 does not change permissions.

H13 does not change API/CLI/UI behavior.

H13 does not implement raw payload redaction.

H13 does not implement artifact retention.

H13 does not implement source-apply/rollback/workflow/release/deploy behavior.

H13 does not change approval/policy/source-apply/release/deploy authority.

H13 does not add migration runner or DbUp work.

## Non-Authority Boundary

H13 does not grant approval.

H13 does not grant policy satisfaction.

H13 does not grant source-apply authority.

H13 does not grant workflow continuation authority.

H13 does not grant merge readiness.

H13 does not grant release readiness.

H13 does not grant deployment readiness.

H13 does not grant rollback authority.

H13 does not grant retry authority.

H13 does not grant mutation authority.

Vector recall is not authority.

Vector recall is not evidence validation.

Vector recall is not retention compliance.

Successful rebuild is recall/index success only.

## Tests Added

H13 adds `WeaviateRebuildCommandHardeningTests`.

The tests prove:

- default project rebuild plan is non-destructive and does not delete the shared collection
- missing project ID fails closed with `MissingProjectId`
- shared collection reset is blocked by default with `UnsafeSharedCollectionReset`
- disabled/unavailable Weaviate states are explainable
- rebuild results do not grant authority
- rebuild plans do not mutate SQL source records or authority records
- H10/H11 payload boundaries are preserved
- H13 does not add API, CLI, UI, Docker, config, SQL migration, or database behavior
- the receipt records scope and limitations

## Commands Run

- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --filter FullyQualifiedName~WeaviateRebuildCommandHardeningTests --logger "trx;LogFileName=h13-weaviate-rebuild-command-hardening.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~WeaviateRebuildCommandHardeningTests --logger "trx;LogFileName=h13-weaviate-rebuild-command-hardening.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~IntegrationTestCategoryContractTests --logger "trx;LogFileName=h13-category-contract.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~ReadProjectionBackupRebuildStoryTests|FullyQualifiedName~WeaviateRebuildCommandHardeningTests" --logger "trx;LogFileName=h12-h13-projection-rebuild-corridor.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC10WeaviateProductionAuthConfigTests --logger "trx;LogFileName=h13-c10-weaviate-auth-boundary.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests --logger "trx;LogFileName=h13-c11-secret-scan.trx"`
- `dotnet build IronDev.slnx --no-restore`
- `git diff --check`
- `git diff --cached --check`

## Validation Results

- Initial H13 focused test run: 7/9 passed. Failures were test wording/static-scan issues, not production behavior failures.
- H13 focused tests after test/receipt fixes: 9/9 passed.
- H13 focused tests no-build rerun: 9/9 passed.
- G13 category contract: 7/7 passed.
- H12-H13 projection rebuild corridor: 18/18 passed.
- C10 Weaviate auth boundary: 17/17 passed.
- C11 secret scan: 9/9 passed.
- Solution build: 0 errors / 4 existing warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed after staging the exact H13 files.

## Known Limitations

H13 does not implement per-project vector-object deletion.

H13 does not prove existing indexed text is redacted.

H13 does not change the source extraction policy used for project context documents.

H13 does not implement full collection reset authorization.

H13 does not require Docker or live Weaviate in tests.

H13 does not implement H14.

## Next Intended Slice

H14 - Weaviate auth/prod config tests.

Review line: Weaviate auth protects the index. It does not make index content authoritative.

Killjoy: An authenticated vector index is still just an index.
