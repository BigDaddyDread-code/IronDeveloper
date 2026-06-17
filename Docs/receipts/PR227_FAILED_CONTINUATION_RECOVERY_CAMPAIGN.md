# PR227 Failed Continuation Recovery Campaign Receipt

PR227 adds failed continuation recovery campaign evidence inspection only.

PR227 inspects supplied workflow continuation failure evidence.
PR227 inspects supplied workflow transition recovery evidence.
PR227 inspects supplied stale-authority detection evidence when supplied.
PR227 inspects supplied follow-up release-readiness evidence when supplied.

PR227 does not retry workflow continuation.
PR227 does not continue workflow.
PR227 does not mutate workflow state.
PR227 does not create workflow transition records.
PR227 does not execute source apply.
PR227 does not execute rollback.
PR227 does not run rollback audit.
PR227 does not approve release.
PR227 does not approve deployment.
PR227 does not approve merge.
PR227 does not execute release.
PR227 does not run git.
PR227 does not tag.
PR227 does not create pull requests.
PR227 does not refresh authority.
PR227 does not reissue evidence.
PR227 does not add SQL.
PR227 does not add API.
PR227 does not add CLI.
PR227 does not add UI.
PR227 does not add runtime execution.
PR227 does not add scheduler or worker behavior.
PR227 does not call agents, models, or tools.
PR227 does not promote memory.
PR227 does not activate retrieval.

RecoveryEvidenceSatisfied means supplied failed-continuation recovery evidence appears complete.
RecoveryEvidenceSatisfied does not mean recovery was executed by PR227.
RecoveryEvidenceSatisfied does not mean workflow continued.
RecoveryEvidenceSatisfied does not mean workflow can continue.
RecoveryEvidenceSatisfied does not mean continuation retry is approved.
RecoveryEvidenceSatisfied does not mean release approved.
Human review remains required.

## Files

- `IronDev.Core/Governance/FailedContinuationRecoveryCampaign.cs`
- `IronDev.Infrastructure/Governance/FailedContinuationRecoveryCampaignRunner.cs`
- `IronDev.IntegrationTests/Governance/FailedContinuationRecoveryCampaignTests.cs`
- `Docs/receipts/PR227_FAILED_CONTINUATION_RECOVERY_CAMPAIGN.md`

## Validation

- `FailedContinuationRecoveryCampaign`: 33/33 passed.
- `FailedContinuationRecoveryCampaign|FailedApplyRecoveryCampaign|AuthorityExpiryRegression|StaleAuthorityDetection|ReleaseReadinessRegression|ReleaseReadinessCliRegression|GovernedDogfoodCampaign|GovernedReleaseGate|GovernedReleaseGateCli|ReleaseReadinessGateEvaluator|ReleaseReadinessDecisionRecordStore|ReleaseReadinessDecisionRecord|ReleaseReadinessReport`: 295/295 passed.
- API read-side band `ReleaseReadinessApiRegression|GovernedReleaseGateApi|ReleaseReadinessDecisionRecordReadApi|WorkflowContinuationApiRegression|WorkflowTransitionRecordReadApi`: 53/53 passed.
- Broad workflow/rollback/source-apply neighbour band `WorkflowContinuationRegression|WorkflowContinuationCliRegression|GovernedWorkflowContinuation|WorkflowTransitionRecordStore|WorkflowTransitionRecord|WorkflowContinuationGate|RollbackFailureRegression|RollbackReceiptWriteIntegration|RollbackExecutionAudit|RollbackExecutionReceiptStore|ControlledRollbackExecutor|RollbackRegression|SourceApplyRegression|SourceApplyNarrowRealApply|SourceApplyReceipt|PatchArtifactRegression|ApiCliContract|ApiCliReleaseGate`: 282/283 passed.
- `dotnet build IronDev.slnx --no-restore -v:minimal`: passed, 0 errors / 2 warnings.
- `git diff --check`: passed.

Known unrelated caveat:

- `WorkflowContinuationCliRegression_CliRejectsForbiddenAuthorityOptions` expects `--release-approved`, while the current CLI rejects `--approve-release`. PR227 does not touch CLI behavior.

PR227 rehearses failed-continuation recovery. It does not continue the workflow or declare recovery complete.
