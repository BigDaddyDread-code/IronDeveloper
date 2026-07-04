# Integration Test Categories

This inventory is maintained for G13 integration test category cleanup.

Test categories are not test quality.

A label does not make a slow test safe.

## Scope

- Source roots scanned: `IronDev.IntegrationTests`, `IronDev.IntegrationTests.Api`.
- Excludes generated `bin` and `obj` folders.
- Counts are source-derived and intended for lane visibility, not coverage scoring.

## Totals

- Source files scanned: 616
- Test classes found: 610
- Test methods found: 9740
- Category names found: 229

## G13 Category Changes

- Added broad `StaticBoundary` metadata to static-boundary test classes that lacked a boundary-style category.
- Added broad `Receipt` metadata to receipt test classes that lacked a receipt-style category.
- Added broad `Store` metadata to store test classes that lacked a store-style category.
- Added broad `Governance`, `Contract`, and `Boundary` metadata to the G13 category contract test.
- No categories were renamed.
- No categories were removed.

## G14 Slow / Quarantine Split

- new categories added: `RequiresRealDatabase`, `LongRunning`, `ManualLocal`.
- categories not added: `Slow`, `Quarantined`, `RequiresExternalDependency`, `RequiresLocalTooling`.
- counts by category:
  - `RequiresRealDatabase`: 40 test classes, 420 test methods, 40 files.
  - `LongRunning`: 40 test classes, 420 test methods, 40 files.
  - `ManualLocal`: 1 test class, 1 test method, 1 file.
- test classes affected: 40 store/real-database-shaped integration classes plus 1 manual local legacy class.
- test methods affected if source-countable: 420 `RequiresRealDatabase`/`LongRunning` methods and 1 `ManualLocal` method.
- tests moved into explicit slow/quarantine visibility: store and real-database-shaped classes are now explicitly visible through `RequiresRealDatabase` and `LongRunning`; the existing manual local ignored task is visible through `ManualLocal`.
- tests remain in default lanes: no CI filters were changed, no tests were deleted, and no default lane exclusion was added.
- selection-only pending execution proof: most `RequiresRealDatabase`/`LongRunning` rows remain `SelectionOnlyPendingExecution` until a slow/SQL lane executes them.
- real execution proof: none was added by G14; existing SQL CI class membership remains documented, but this PR does not claim fresh SQL execution.
- Selection proof is not execution proof.

## H08 Tenant Enforcement Read Model Tests

- Added focused `TenantIsolation` and `ReadModel` metadata to the H08 cross-surface tenant enforcement contract test.
- Added broad `Governance`, `Contract`, and `Boundary` metadata to the H08 tenant enforcement contract test.
- H08 does not add `RequiresRealDatabase` or `LongRunning`; the tested surfaces are in-memory/read-adapter contracts.
- H08 adds no SQL migration, schema, table, column, index, stored procedure, trigger, permission, API, CLI, UI, Weaviate, workflow, source-apply, rollback, release, deployment, replay, rebuild, or backfill behavior.
- Tenant filters protect read scope only.

## H09 UTC Timestamp DB Constraint Review

- Added focused `Database` and `UtcTimestamp` metadata to the H09 SQL metadata review test.
- Added broad `Governance`, `StorageReview`, `RequiresRealDatabase`, `LongRunning`, `Contract`, and `Boundary` metadata to the H09 timestamp review test.
- H09 uses existing migration setup and SQL metadata reads only after integration setup.
- H09 does not add a SQL migration, alter tables, alter timestamp columns, rename timestamp columns, add default constraints, alter default constraints, add check constraints, alter check constraints, alter stored procedures, alter triggers, change permissions, change API/CLI/UI behavior, change Weaviate behavior, or change workflow/source-apply/rollback/release/deployment authority.
- UTC timestamps make time comparable only.

## H10 Raw Payload Redaction and Retention Policy

- Added focused `PayloadSafety`, `Redaction`, `Retention`, and `Policy` metadata to the H10 policy contract test.
- Added broad `Governance`, `Contract`, and `Boundary` metadata to the H10 policy contract test.
- H10 does not add `RequiresRealDatabase` or `LongRunning`; the test reads policy, receipt, category, and static repository path metadata only.
- H10 does not add a SQL migration, alter tables, add indexes, alter stored procedures, alter triggers, change permissions, change API/CLI/UI behavior, change Weaviate behavior, change workflow/source-apply/rollback/release/deployment authority, implement redaction, implement retention deletion, implement artifact deletion, run replay, run backfill, or rebuild projections.
- Redaction policy limits exposure. It does not make retained payloads safe.

## H11 Evidence Artifact Retention Policy

- Added focused `ArtifactRetention` and `EvidenceArtifact` metadata to the H11 policy contract test.
- Added broad `Governance`, `Retention`, `Policy`, `Contract`, and `Boundary` metadata to the H11 policy contract test.
- H11 does not add `RequiresRealDatabase` or `LongRunning`; the test reads policy, receipt, category, and static repository path metadata only.
- H11 does not add a SQL migration, alter tables, add indexes, alter stored procedures, alter triggers, change permissions, change API/CLI/UI behavior, change Weaviate behavior, change workflow/source-apply/rollback/release/deployment authority, implement artifact deletion, implement artifact expiry, implement retention deletion, implement cleanup commands, run replay, run backfill, or rebuild projections.
- Artifact retention policy controls lifecycle. It does not make artifacts safe.

## H12 Read Projection Backup and Rebuild Story

- Added focused `ReadProjection`, `ProjectionRebuild`, and `Backup` metadata to the H12 story contract test.
- Added broad `Governance`, `Policy`, `Contract`, and `Boundary` metadata to the H12 story contract test.
- H12 does not add `RequiresRealDatabase` or `LongRunning`; the test reads architecture, receipt, category, and static repository path metadata only.
- H12 does not add a SQL migration, alter tables, add indexes, alter stored procedures, alter triggers, change permissions, change API/CLI/UI behavior, change Weaviate behavior, change workflow/source-apply/rollback/release/deployment authority, implement backup jobs, implement rebuild commands, run replay, run backfill, or rebuild projections.
- Projection rebuild plans restore read models. They do not recreate authority records.

## H13 Weaviate Rebuild Command Hardening

- Added focused `Weaviate`, `SemanticMemory`, and `ProjectionRebuild` metadata to the H13 rebuild hardening contract test.
- Added broad `Governance`, `Contract`, and `Boundary` metadata to the H13 rebuild hardening contract test.
- H13 does not add `RequiresRealDatabase` or `LongRunning`; the test reads contract, receipt, category, and static repository path metadata only.
- H13 does not add a SQL migration, alter tables, add indexes, alter stored procedures, alter triggers, change permissions, change API/CLI/UI behavior, change Docker compose behavior, change Weaviate auth/prod config, implement raw payload redaction, implement artifact retention, implement source-apply/rollback/workflow/release/deploy behavior, or add migration runner/DbUp work.
- Weaviate rebuild restores recall. It does not restore authority.

## H14 Weaviate Auth / Production Config Tests

- Added focused `Auth`, `ProductionConfig`, `SecretSafety`, and `Weaviate` metadata to the H14 auth/prod config boundary test.
- Added broad `Governance`, `Contract`, and `Boundary` metadata to the H14 auth/prod config boundary test.
- H14 does not add `RequiresRealDatabase` or `LongRunning`; the test reads contract, receipt, category, config, environment endpoint, auth validator, and rebuild guard metadata only.
- H14 does not add a SQL migration, alter tables, add indexes, alter stored procedures, alter triggers, change permissions, change API/CLI/UI behavior, change Docker compose behavior, change deployment config, change Weaviate rebuild behavior, require live Weaviate, require Docker, implement raw payload redaction, implement artifact retention, implement source-apply/rollback/workflow/release/deploy behavior, or add migration runner/DbUp work.
- Weaviate auth protects the index. It does not make index content authoritative.

## J01 Remove Hardcoded Machine SQL Config

- Added focused `ConfigBoundary` metadata to the J01 committed-config hygiene regression test.
- J01 does not add `RequiresRealDatabase` or `LongRunning`; the test reads tracked config files, `.gitignore`, receipt text, and static repository metadata only.
- J01 does not add SQL bootstrap, SQL rebuild, Weaviate bootstrap, schema changes, API/CLI/UI behavior, workflow/source-apply/rollback/release/deployment authority, or production runtime behavior.
- Local SQL configuration is developer convenience. It is not authority, not evidence, and not a shared runtime contract.

## J02 Development Local Override

- Added focused `ConfigBoundary` metadata to the J02 development local override regression test.
- J02 does not add `RequiresRealDatabase` or `LongRunning`; the test reads tracked config/docs/example/receipt metadata and constructs in-memory API configuration builders only.
- J02 verifies Development-only optional local override loading, ignored/untracked local file protection, environment-variable precedence, and placeholder-only documentation.
- J02 does not add SQL bootstrap, SQL rebuild, Weaviate bootstrap, schema changes, API/CLI/UI behavior, workflow/source-apply/rollback/release/deployment authority, or production runtime behavior.
- Local override configuration is developer convenience. It is not shared configuration, not evidence, not authority, and not a runtime contract.

## J03 Validate No Local Machine Names In Committed Config

- Added focused `ConfigBoundary` metadata to the J03 tracked-file repository hygiene regression test.
- J03 does not add `RequiresRealDatabase` or `LongRunning`; the test reads tracked text-like files, docs, `.gitignore`, and receipt text only.
- J03 excludes generated/build output, generated `tools/dogfood/proofs/` artifacts, and the generated `tools/dogfood/knowledge/` mirror; it does not hide `Docs/`, scripts, source files, workflows, or shared config.
- J03 does not add runtime behavior, bootstrap behavior, schema changes, SQL migrations, SQL rebuild, Weaviate bootstrap/rebuild, API/CLI/UI behavior, workflow/source-apply/rollback/release/deployment authority, or production runtime behavior.
- Local machine names, local paths, and local SQL instances are developer-local facts. They are not shared configuration, not evidence, not authority, and not a runtime contract.

## J08 Fail-Safe Config Summary With Secret Redaction

- Added focused `ConfigBoundary` metadata to the J08 redacted config summary regression test.
- J08 does not add `RequiresRealDatabase` or `LongRunning`; the test constructs in-memory config summary inputs and reads Core source, docs, and receipt metadata only.
- J08 verifies raw connection strings are never emitted, sensitive keys are redacted, user-local paths are redacted, local override presence is reported without contents, environment-variable precedence can be represented without values, root safety is `NotEvaluated` without J10, and no bootstrap/mutation surface is added.
- J08 does not add SQL connectivity checks, SQL bootstrap, SQL rebuild, Weaviate bootstrap/rebuild, schema changes, API/CLI/UI behavior, workflow/source-apply/rollback/release/deployment authority, startup logging, or production runtime behavior.
- Config summaries help humans debug setup. They do not bless the setup.

## J04 Local Bootstrap Script

- Added focused `ConfigBoundary` and `LocalBootstrap` metadata to the J04 local bootstrap script regression test.
- J04 does not add `RequiresRealDatabase` or `LongRunning`; the test executes the script against a disposable fake repository and reads script, docs, receipt, and static repository metadata only.
- J04 verifies default check-only behavior, explicit local override copy behavior, no overwrite behavior, output redaction, no SQL/Weaviate/product-flow bootstrap behavior, and non-authority boundary wording.
- J04 does not add SQL connectivity checks, SQL bootstrap, SQL rebuild, Weaviate bootstrap/rebuild, schema changes, API/CLI/UI behavior, workflow/source-apply/rollback/release/deployment authority, startup logging, or production runtime behavior.
- Local bootstrap helps a developer stand up. It does not bless where they stand.

## J05 Local SQL Bootstrap/Rebuild Command

- Added focused `ConfigBoundary` and `LocalSql` metadata to the J05 local SQL command regression test.
- J05 does not add `RequiresRealDatabase` or `LongRunning`; the test executes the script against a disposable fake repository with a fake `sqlcmd` command and reads script, docs, receipt, and static repository metadata only.
- J05 verifies default check-only behavior, local-only SQL target classification, safe database-name classification, exact rebuild confirmation, setup-script path guarding, redacted output, no J04 automatic invocation, no SQL username/password/connection-string parameter, and non-authority boundary wording.
- J05 does not add SQL migrations, runtime SQL store behavior, Weaviate bootstrap/rebuild, Docker orchestration, schema changes, API/CLI/UI behavior, workflow/source-apply/rollback/release/deployment authority, startup logging, or production runtime behavior.
- Local SQL bootstrap prepares a disposable developer database. It does not prove product readiness.

## J06 Local Weaviate Bootstrap/Rebuild Command

- Added focused `ConfigBoundary`, `Weaviate`, `Boundary`, and `Contract` metadata to the J06 local Weaviate command regression test.
- J06 does not add `RequiresRealDatabase` or `LongRunning`; the test executes the script against a disposable fake repository and reads script, docs, receipt, and static repository metadata only.
- J06 verifies default check-only behavior, loopback-only endpoint classification, local collection-name classification, exact rebuild confirmation, schema-path guarding, no J04/J05 automatic invocation, no credential/service/demo/smoke parameter surface, and non-authority boundary wording.
- J06 does not start Docker or Weaviate, load demo vectors, load BookSeller data, run alpha smoke, write evidence, change source/SQL/runtime authority records, add API/CLI/UI behavior, or claim alpha/merge/release/deployment readiness.
- Local Weaviate state is a disposable derived index. Rebuilding it is setup convenience, not authority, approval, evidence, or readiness.

## J07 Developer Environment Doctor

- Added focused `ConfigBoundary`, `LocalBootstrap`, `Boundary`, and `Contract` metadata to the J07 developer environment doctor regression test.
- J07 does not add `RequiresRealDatabase` or `LongRunning`; the test executes the script against a disposable fake repository and fake local commands, then reads script, docs, receipt, and static repository metadata only.
- J07 verifies default diagnostic-only behavior, unsafe switch rejection, JSON/Markdown output, J05/J06 check-only delegation, SQL and Weaviate unsafe-target blocking, local override safety, LocalTest safety, redaction, GET-only probes, next-safe-action selection, and non-authority boundary wording.
- J07 does not create local files, start services, create or rebuild SQL, ensure or rebuild Weaviate, reset LocalTest data, run smoke, write evidence, change source/runtime authority records, add API/CLI/UI behavior, or claim alpha/merge/release/deployment readiness.
- The developer doctor reports local readiness blockers. It does not make the machine safe.

## P3-3 Builder Contract-Bound Patch Package

- Added focused `Builder` metadata to the P3-3 builder patch package contract regression test.
- Added broad `Governance` and `Contract` metadata to the P3-3 builder contract-bound patch package test.
- P3-3 does not add `RequiresRealDatabase` or `LongRunning`; the test validates Core builder package models, validators, receipt wording, and static production source boundaries only.
- P3-3 does not add UI, API, database schema, source apply, approval, critic execution, test authoring, workflow continuation, release, deployment, memory, or chat/channel behavior.
- A builder patch package is an implementation attempt against a confirmed contract. It is not approval, test proof, critic review, source apply permission, or workflow continuation.

## P3-1 Orchestrator/BA Contract Boundary

- Added focused `Orchestrator` metadata to the P3-1 Orchestrator/BA agent and contract boundary regression tests.
- Added broad `Governance`, `Contract`, and `Boundary` metadata to the P3-1 boundary tests.
- P3-1 does not add `RequiresRealDatabase` or `LongRunning`; the tests validate Core agent definitions, Core contract models, Core validators, receipt wording, and static source boundaries only.
- P3-1 does not add the durable Orchestrator loop, run start/continue/apply behavior, source mutation, test authoring, critic execution, approval, policy satisfaction, API, UI, SQL schema, release/deployment, memory promotion, or channel/chat behavior.
- The Orchestrator writes the contract. It does not judge the result.

## M02 Project Channels Schema

- Added broad `Governance`, `Database`, `RequiresRealDatabase`, `LongRunning`, `Contract`, and `Boundary` metadata to the M02 project channels schema test.
- M02 adds `Database/migrate_project_channels.sql` and Core channel contract models only.
- M02 does not add API endpoints, UI, message services, Ask IronDev behavior, reactions, sockets, notifications, workflow commands, approval from chat, release/deployment from chat, memory promotion from chat, or existing ProjectChatSessions migration.
- Channel schema stores collaboration state only.

## Inventory

| Category | Test classes | Test methods selected by class category | Explicit method category attributes | Files |
| --- | ---: | ---: | ---: | ---: |
| `A2aAuthoritySeparation` | 2 | 31 | 0 | 2 |
| `A2aContractComposition` | 1 | 15 | 0 | 1 |
| `A2aContractValidation` | 1 | 15 | 0 | 1 |
| `A2aEvidenceOnlySemantics` | 2 | 31 | 0 | 2 |
| `A2aStaticNoRuntimeBoundary` | 1 | 16 | 0 | 1 |
| `AcceptedApprovalReceiptRegression` | 1 | 20 | 0 | 1 |
| `AcceptedApprovalRecordContract` | 1 | 13 | 0 | 1 |
| `AcceptedApprovalSqlStore` | 1 | 15 | 0 | 1 |
| `AgentHandoff` | 3 | 53 | 0 | 3 |
| `AgentHandoffStore` | 1 | 8 | 0 | 1 |
| `ApiCliContract` | 3 | 24 | 0 | 3 |
| `ApiCliReleaseGate` | 1 | 10 | 0 | 1 |
| `ApplyDryRunAuthorityBoundary` | 1 | 4 | 0 | 1 |
| `ApplyDryRunStaticBoundary` | 1 | 4 | 0 | 1 |
| `ApplyDryRunStore` | 3 | 15 | 0 | 3 |
| `ApplyPreview` | 2 | 13 | 0 | 2 |
| `ArtifactRetention` | 1 | 8 | 0 | 1 |
| `Auth` | 1 | 11 | 0 | 1 |
| `Backup` | 1 | 9 | 0 | 1 |
| `ApprovalAuthorityBoundary` | 2 | 20 | 0 | 2 |
| `ApprovalAuthorityStaticBoundary` | 1 | 2 | 0 | 1 |
| `ApprovalAuthorityWording` | 0 | 1 | 1 | 1 |
| `ApprovalGateDogfoodCorrelationReport` | 2 | 14 | 0 | 2 |
| `ApprovalRequirementEvaluator` | 1 | 42 | 0 | 1 |
| `ApprovalSatisfactionEvaluator` | 1 | 20 | 0 | 1 |
| `AuthorityExpiryRegression` | 1 | 28 | 0 | 1 |
| `BlockGGovernanceSubstrateReceipt` | 1 | 9 | 0 | 1 |
| `BlockHPolicyModelReceipt` | 1 | 15 | 0 | 1 |
| `BlockIA2aSpineReceipt` | 1 | 16 | 0 | 1 |
| `BlockJWorkflowStateReceipt` | 1 | 48 | 0 | 1 |
| `BlockKMemoryL2L3Receipt` | 1 | 1 | 0 | 1 |
| `BlockNControlledApplyPreparation` | 3 | 11 | 0 | 3 |
| `BlockP0AuthorityValidationBaseline` | 1 | 10 | 0 | 1 |
| `BlockPThinUiReceipt` | 1 | 7 | 0 | 1 |
| `Boundary` | 18 | 166 | 0 | 18 |
| `Builder` | 1 | 9 | 0 | 1 |
| `BoxedLangGraphRoutingAdapter` | 3 | 32 | 0 | 3 |
| `CodeIndexFiles` | 1 | 4 | 0 | 1 |
| `ConfigBoundary` | 8 | 94 | 0 | 8 |
| `Contract` | 20 | 182 | 0 | 20 |
| `ControlledDryRunRequestContract` | 1 | 20 | 0 | 1 |
| `ControlledRollbackExecutor` | 3 | 31 | 0 | 3 |
| `CrossRunMemoryPatternDetection` | 1 | 14 | 0 | 1 |
| `Database` | 2 | 18 | 0 | 2 |
| `DatabaseMigrationReceipt` | 1 | 7 | 0 | 1 |
| `DatabaseMigration` | 2 | 12 | 0 | 2 |
| `Decision` | 2 | 12 | 0 | 2 |
| `DisposableWorkspaceDryRunBoundaryReceipt` | 1 | 20 | 0 | 1 |
| `DisposableWorkspaceDryRunExecutor` | 1 | 20 | 0 | 1 |
| `DogfoodReceiptStore` | 1 | 8 | 0 | 1 |
| `DryRunExecutionAuditContract` | 1 | 22 | 0 | 1 |
| `DryRunFailureRegression` | 1 | 24 | 0 | 1 |
| `DryRunReceiptStore` | 1 | 21 | 0 | 1 |
| `DryRunReceiptWriteIntegration` | 1 | 20 | 0 | 1 |
| `EndToEndGovernedDogfoodCampaign` | 1 | 10 | 0 | 1 |
| `Evidence` | 1 | 8 | 0 | 1 |
| `EvidenceArtifact` | 1 | 8 | 0 | 1 |
| `FailedApplyRecoveryCampaign` | 1 | 32 | 0 | 1 |
| `FailedContinuationRecoveryCampaign` | 1 | 33 | 0 | 1 |
| `FailedWorkflowDiagnosisReport` | 2 | 14 | 0 | 2 |
| `Governance` | 20 | 158 | 0 | 20 |
| `GovernanceEvent` | 2 | 14 | 0 | 2 |
| `GovernanceEventStore` | 1 | 11 | 0 | 1 |
| `GovernanceSubstrateAuthorityBoundary` | 1 | 10 | 0 | 1 |
| `GovernanceSubstrateContract` | 1 | 10 | 0 | 1 |
| `GovernanceSubstrateStaticBoundary` | 1 | 10 | 0 | 1 |
| `GovernanceTraceExplorer` | 2 | 13 | 0 | 2 |
| `GovernedDogfoodCampaign` | 1 | 22 | 0 | 1 |
| `GovernedReleaseGate` | 1 | 9 | 0 | 1 |
| `GovernedReleaseGateApi` | 1 | 5 | 0 | 1 |
| `GovernedReleaseGateCli` | 1 | 5 | 0 | 1 |
| `GovernedWorkflowContinuation` | 1 | 8 | 0 | 1 |
| `GroundingEvidenceReference` | 1 | 26 | 0 | 1 |
| `HumanApprovedApply` | 3 | 15 | 0 | 3 |
| `L4BackendReadinessReport` | 1 | 10 | 0 | 1 |
| `L4CapabilityMatrix` | 1 | 10 | 0 | 1 |
| `L4FailureModeReport` | 1 | 8 | 0 | 1 |
| `L4InvariantRegression` | 1 | 14 | 0 | 1 |
| `L4ReleaseGateReceipt` | 1 | 9 | 0 | 1 |
| `LocalBootstrap` | 2 | 30 | 0 | 2 |
| `LocalSql` | 1 | 16 | 0 | 1 |
| `LongRunning` | 40 | 420 | 0 | 40 |
| `ManualLocal` | 1 | 1 | 0 | 1 |
| `MemoryCannotPromoteItself` | 1 | 67 | 0 | 1 |
| `MemoryPromotionRequestPackage` | 1 | 7 | 0 | 1 |
| `MemoryProposalConflictDetection` | 1 | 14 | 0 | 1 |
| `MemoryProposalDuplicateDetection` | 1 | 14 | 0 | 1 |
| `MemoryProposalEvidencePackage` | 1 | 11 | 0 | 1 |
| `MemoryProposalStagingStore` | 1 | 10 | 0 | 1 |
| `MemoryProposalStaleDetection` | 1 | 13 | 0 | 1 |
| `MissingPolicyFailsClosed` | 1 | 49 | 0 | 1 |
| `MissingPolicyStaticBoundary` | 0 | 12 | 12 | 1 |
| `MissingPolicyWording` | 0 | 5 | 5 | 1 |
| `NoAuthorityTransferValidator` | 1 | 13 | 0 | 1 |
| `OverallMemorySystemDiscussion` | 1 | 10 | 0 | 1 |
| `OperationStatus` | 1 | 5 | 0 | 1 |
| `Orchestrator` | 2 | 14 | 0 | 2 |
| `PatchArtifactApiRegression` | 1 | 4 | 0 | 1 |
| `PatchArtifactContract` | 1 | 25 | 0 | 1 |
| `PatchArtifactCreation` | 1 | 21 | 0 | 1 |
| `PatchArtifactRegression` | 1 | 10 | 0 | 1 |
| `PatchArtifactStore` | 1 | 22 | 0 | 1 |
| `PatchBaseHashValidation` | 1 | 27 | 0 | 1 |
| `PayloadSafety` | 1 | 8 | 0 | 1 |
| `Policy` | 3 | 25 | 0 | 3 |
| `PolicyRequirementSatisfactionEvaluator` | 1 | 22 | 0 | 1 |
| `PolicySatisfactionReceiptRegression` | 1 | 24 | 0 | 1 |
| `PolicySatisfactionRecordContract` | 1 | 19 | 0 | 1 |
| `PolicySatisfactionSqlStore` | 1 | 15 | 0 | 1 |
| `ProjectionRebuild` | 2 | 18 | 0 | 2 |
| `ProductionConfig` | 1 | 11 | 0 | 1 |
| `PR204` | 2 | 10 | 0 | 2 |
| `PR205` | 1 | 18 | 0 | 1 |
| `PR206` | 1 | 14 | 0 | 1 |
| `PR207` | 1 | 15 | 0 | 1 |
| `PR208` | 1 | 7 | 0 | 1 |
| `PR209` | 1 | 15 | 0 | 1 |
| `PR210` | 1 | 24 | 0 | 1 |
| `PR211` | 1 | 25 | 0 | 1 |
| `PR214` | 1 | 8 | 0 | 1 |
| `PR215` | 3 | 11 | 0 | 3 |
| `PR216` | 1 | 23 | 0 | 1 |
| `PR219` | 1 | 30 | 0 | 1 |
| `PR221` | 3 | 19 | 0 | 3 |
| `PR222` | 3 | 24 | 0 | 3 |
| `PR223` | 1 | 22 | 0 | 1 |
| `PR224` | 1 | 31 | 0 | 1 |
| `PR225` | 1 | 28 | 0 | 1 |
| `PR226` | 1 | 32 | 0 | 1 |
| `PR227` | 1 | 33 | 0 | 1 |
| `PR228` | 1 | 30 | 0 | 1 |
| `ProjectApprovalRule` | 1 | 53 | 0 | 1 |
| `ProjectAutonomyPolicy` | 1 | 13 | 0 | 1 |
| `ProjectPolicyProfile` | 1 | 15 | 0 | 1 |
| `ReadOnlyApprovalPackageReviewUi` | 1 | 12 | 0 | 1 |
| `ReadOnlyDogfoodReceiptViewerUi` | 1 | 13 | 0 | 1 |
| `ReadOnlyGovernanceTimelineUi` | 1 | 10 | 0 | 1 |
| `ReadOnlyMemoryProposalReviewUi` | 1 | 13 | 0 | 1 |
| `ReadOnlyToolGateDecisionUi` | 1 | 12 | 0 | 1 |
| `ReadOnlyWorkflowRunStepViewerUi` | 1 | 12 | 0 | 1 |
| `ReadProjection` | 1 | 9 | 0 | 1 |
| `ReadModel` | 1 | 9 | 0 | 1 |
| `RealDatabaseAgentHandoffSmoke` | 1 | 8 | 0 | 1 |
| `RealDatabaseApplyDryRunStoreSmoke` | 1 | 7 | 0 | 1 |
| `RealDatabaseDogfoodReceiptSmoke` | 1 | 4 | 0 | 1 |
| `RealDatabaseDryRunReceiptStoreSmoke` | 1 | 21 | 0 | 1 |
| `RealDatabaseMemoryProposalStagingSmoke` | 1 | 10 | 0 | 1 |
| `RealDatabasePatchArtifactStoreSmoke` | 1 | 22 | 0 | 1 |
| `RealDatabaseReleaseReadinessDecisionRecordStoreSmoke` | 1 | 28 | 0 | 1 |
| `RealDatabaseRollbackSupportReceiptStoreSmoke` | 1 | 23 | 0 | 1 |
| `RealDatabaseSourceApplyDryRunReceiptStoreSmoke` | 1 | 11 | 0 | 1 |
| `RealDatabaseThoughtLedgerGovernanceReferenceSmoke` | 1 | 4 | 0 | 1 |
| `RealDatabaseToolGateDecisionSmoke` | 1 | 4 | 0 | 1 |
| `RealDatabaseToolRequestSmoke` | 1 | 5 | 0 | 1 |
| `RealDatabaseWorkflowCheckpointSmoke` | 1 | 9 | 0 | 1 |
| `RealDatabaseWorkflowRunSmoke` | 2 | 14 | 0 | 2 |
| `RealDatabaseWorkflowStepSmoke` | 1 | 8 | 0 | 1 |
| `RealDatabaseWorkflowTransitionRecordStoreSmoke` | 1 | 24 | 0 | 1 |
| `Redaction` | 1 | 8 | 0 | 1 |
| `Receipt` | 20 | 407 | 0 | 20 |
| `ReleaseGateNegativeCampaign` | 1 | 30 | 0 | 1 |
| `ReleaseReadinessApiRegression` | 1 | 6 | 0 | 1 |
| `ReleaseReadinessCliRegression` | 1 | 4 | 0 | 1 |
| `ReleaseReadinessDecisionRecordReadApi` | 1 | 17 | 0 | 1 |
| `ReleaseReadinessDecisionRecordStore` | 1 | 28 | 0 | 1 |
| `ReleaseReadinessGateEvaluator` | 1 | 30 | 0 | 1 |
| `ReleaseReadinessRegression` | 1 | 14 | 0 | 1 |
| `ReleaseReadinessReport` | 1 | 23 | 0 | 1 |
| `RequiresRealDatabase` | 40 | 420 | 0 | 40 |
| `Retention` | 2 | 16 | 0 | 2 |
| `RollbackExecutionAudit` | 1 | 15 | 0 | 1 |
| `RollbackExecutionReceipt` | 1 | 9 | 0 | 1 |
| `RollbackExecutionReceiptStore` | 3 | 27 | 0 | 3 |
| `RollbackFailureRegression` | 1 | 15 | 0 | 1 |
| `RollbackGateEvaluator` | 1 | 22 | 0 | 1 |
| `RollbackPlanContract` | 1 | 26 | 0 | 1 |
| `RollbackReceiptWriteIntegration` | 1 | 7 | 0 | 1 |
| `RollbackRegression` | 2 | 19 | 0 | 2 |
| `RollbackSupportReceiptReadApi` | 1 | 5 | 0 | 1 |
| `RollbackSupportReceiptStore` | 1 | 23 | 0 | 1 |
| `SecretSafety` | 1 | 11 | 0 | 1 |
| `SemanticMemory` | 1 | 9 | 0 | 1 |
| `SourceApplyDryRunExecutor` | 1 | 17 | 0 | 1 |
| `SourceApplyDryRunReceiptStore` | 1 | 11 | 0 | 1 |
| `SourceApplyDryRunReceiptValidation` | 1 | 11 | 0 | 1 |
| `SourceApplyGateEvaluator` | 1 | 17 | 0 | 1 |
| `SourceApplyNarrowRealApply` | 2 | 10 | 0 | 2 |
| `SourceApplyReceipt` | 1 | 3 | 0 | 1 |
| `SourceApplyReceiptStore` | 2 | 7 | 0 | 2 |
| `SourceApplyRegression` | 1 | 18 | 0 | 1 |
| `SourceApplyRequestValidation` | 1 | 15 | 0 | 1 |
| `SourceApplyThreatBoundary` | 1 | 11 | 0 | 1 |
| `SqlInventory` | 1 | 8 | 0 | 1 |
| `Spike` | 1 | 6 | 0 | 1 |
| `StaleAuthorityDetection` | 1 | 31 | 0 | 1 |
| `StaticBoundary` | 39 | 294 | 0 | 39 |
| `SkeletonRun` | 1 | 9 | 0 | 1 |
| `StorageReview` | 4 | 28 | 0 | 4 |
| `Store` | 12 | 130 | 0 | 12 |
| `TenantIsolation` | 1 | 9 | 0 | 1 |
| `TenantUsersAdmin` | 1 | 12 | 0 | 1 |
| `ThoughtLedgerGovernanceReference` | 2 | 14 | 0 | 2 |
| `ThoughtLedgerHandoffEntry` | 1 | 18 | 0 | 1 |
| `ToolRequestStore` | 1 | 11 | 0 | 1 |
| `UiCannotOwnBackendAuthority` | 1 | 8 | 0 | 1 |
| `UtcTimestamp` | 1 | 8 | 0 | 1 |
| `WorkflowA2aHandoff` | 2 | 31 | 0 | 2 |
| `WorkflowApprovalHalt` | 3 | 14 | 0 | 3 |
| `WorkflowAuthoritySubstitution` | 1 | 8 | 0 | 1 |
| `WorkflowCannotGrantAuthority` | 2 | 18 | 0 | 2 |
| `WorkflowCheckpointStore` | 1 | 9 | 0 | 1 |
| `WorkflowContinuationApiRegression` | 1 | 3 | 0 | 1 |
| `WorkflowContinuationCliRegression` | 1 | 2 | 0 | 1 |
| `WorkflowContinuationGate` | 1 | 24 | 0 | 1 |
| `WorkflowContinuationRegression` | 3 | 11 | 0 | 3 |
| `WorkflowDryRun` | 3 | 29 | 0 | 3 |
| `WorkflowFailureRetryState` | 1 | 12 | 0 | 1 |
| `WorkflowRunnerSkeleton` | 1 | 34 | 0 | 1 |
| `WorkflowRunnerSkeletonA2a` | 1 | 7 | 0 | 1 |
| `WorkflowRunnerSkeletonApprovalHalt` | 1 | 5 | 0 | 1 |
| `WorkflowRunnerSkeletonPolicyPreflight` | 1 | 5 | 0 | 1 |
| `WorkflowRunStore` | 1 | 9 | 0 | 1 |
| `WorkflowStateContract` | 1 | 76 | 0 | 1 |
| `WorkflowStepContract` | 1 | 21 | 0 | 1 |
| `WorkflowStepInputOutputReference` | 1 | 16 | 0 | 1 |
| `WorkflowStepPolicyPreflight` | 1 | 25 | 0 | 1 |
| `WorkflowStepPolicyPreflightA2a` | 1 | 7 | 0 | 1 |
| `WorkflowStepStore` | 1 | 8 | 0 | 1 |
| `WorkflowStepThoughtLedger` | 1 | 9 | 0 | 1 |
| `WorkflowTransitionRecord` | 1 | 25 | 0 | 1 |
| `WorkflowTransitionRecordStore` | 1 | 24 | 0 | 1 |
| `Weaviate` | 3 | 36 | 0 | 3 |

## CI-Facing Category Counts

- `ApiCliContract`: 3 test classes, 24 test methods selected by class category, 3 files.
- `ApiCliReleaseGate`: 1 test classes, 10 test methods selected by class category, 1 files.

## Boundary

This inventory does not approve tests, certify release readiness, hide slow tests, quarantine tests, weaken CI, or grant authority.
