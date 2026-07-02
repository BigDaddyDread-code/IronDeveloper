# Integration Test Categories

This inventory is maintained for G13 integration test category cleanup.

Test categories are not test quality.

A label does not make a slow test safe.

## Scope

- Source roots scanned: `IronDev.IntegrationTests`, `IronDev.IntegrationTests.Api`.
- Excludes generated `bin` and `obj` folders.
- Counts are source-derived and intended for lane visibility, not coverage scoring.

## Totals

- Source files scanned: 592
- Test classes found: 586
- Test methods found: 9517
- Category names found: 202

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
  - `RequiresRealDatabase`: 35 test classes, 380 test methods, 35 files.
  - `LongRunning`: 35 test classes, 380 test methods, 35 files.
  - `ManualLocal`: 1 test class, 1 test method, 1 file.
- test classes affected: 35 store/real-database-shaped integration classes plus 1 manual local legacy class.
- test methods affected if source-countable: 380 `RequiresRealDatabase`/`LongRunning` methods and 1 `ManualLocal` method.
- tests moved into explicit slow/quarantine visibility: store and real-database-shaped classes are now explicitly visible through `RequiresRealDatabase` and `LongRunning`; the existing manual local ignored task is visible through `ManualLocal`.
- tests remain in default lanes: no CI filters were changed, no tests were deleted, and no default lane exclusion was added.
- selection-only pending execution proof: most `RequiresRealDatabase`/`LongRunning` rows remain `SelectionOnlyPendingExecution` until a slow/SQL lane executes them.
- real execution proof: none was added by G14; existing SQL CI class membership remains documented, but this PR does not claim fresh SQL execution.
- Selection proof is not execution proof.

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
| `Boundary` | 2 | 17 | 0 | 2 |
| `BoxedLangGraphRoutingAdapter` | 3 | 32 | 0 | 3 |
| `Contract` | 2 | 17 | 0 | 2 |
| `ControlledDryRunRequestContract` | 1 | 20 | 0 | 1 |
| `ControlledRollbackExecutor` | 3 | 31 | 0 | 3 |
| `CrossRunMemoryPatternDetection` | 1 | 14 | 0 | 1 |
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
| `FailedApplyRecoveryCampaign` | 1 | 32 | 0 | 1 |
| `FailedContinuationRecoveryCampaign` | 1 | 33 | 0 | 1 |
| `FailedWorkflowDiagnosisReport` | 2 | 14 | 0 | 2 |
| `Governance` | 4 | 29 | 0 | 4 |
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
| `LongRunning` | 35 | 380 | 0 | 35 |
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
| `PatchArtifactApiRegression` | 1 | 4 | 0 | 1 |
| `PatchArtifactContract` | 1 | 25 | 0 | 1 |
| `PatchArtifactCreation` | 1 | 21 | 0 | 1 |
| `PatchArtifactRegression` | 1 | 10 | 0 | 1 |
| `PatchArtifactStore` | 1 | 22 | 0 | 1 |
| `PatchBaseHashValidation` | 1 | 27 | 0 | 1 |
| `PolicyRequirementSatisfactionEvaluator` | 1 | 22 | 0 | 1 |
| `PolicySatisfactionReceiptRegression` | 1 | 24 | 0 | 1 |
| `PolicySatisfactionRecordContract` | 1 | 19 | 0 | 1 |
| `PolicySatisfactionSqlStore` | 1 | 15 | 0 | 1 |
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
| `Receipt` | 19 | 400 | 0 | 19 |
| `ReleaseGateNegativeCampaign` | 1 | 30 | 0 | 1 |
| `ReleaseReadinessApiRegression` | 1 | 6 | 0 | 1 |
| `ReleaseReadinessCliRegression` | 1 | 4 | 0 | 1 |
| `ReleaseReadinessDecisionRecordReadApi` | 1 | 17 | 0 | 1 |
| `ReleaseReadinessDecisionRecordStore` | 1 | 28 | 0 | 1 |
| `ReleaseReadinessGateEvaluator` | 1 | 30 | 0 | 1 |
| `ReleaseReadinessRegression` | 1 | 14 | 0 | 1 |
| `ReleaseReadinessReport` | 1 | 23 | 0 | 1 |
| `RequiresRealDatabase` | 35 | 380 | 0 | 35 |
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
| `StaticBoundary` | 38 | 287 | 0 | 38 |
| `Store` | 9 | 108 | 0 | 9 |
| `ThoughtLedgerGovernanceReference` | 2 | 14 | 0 | 2 |
| `ThoughtLedgerHandoffEntry` | 1 | 18 | 0 | 1 |
| `ToolRequestStore` | 1 | 11 | 0 | 1 |
| `UiCannotOwnBackendAuthority` | 1 | 8 | 0 | 1 |
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

## CI-Facing Category Counts

- `ApiCliContract`: 3 test classes, 24 test methods selected by class category, 3 files.
- `ApiCliReleaseGate`: 1 test classes, 10 test methods selected by class category, 1 files.

## Boundary

This inventory does not approve tests, certify release readiness, hide slow tests, quarantine tests, weaken CI, or grant authority.
