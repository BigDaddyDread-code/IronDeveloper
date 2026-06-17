# PR228 Release Gate Negative Campaigns Receipt

PR228 adds release gate negative campaign evidence testing only.

PR228 runs explicitly supplied negative governed release-gate cases.
PR228 proves expected negative cases remain rejected or blocked.
PR228 does not create release-readiness reports.
PR228 does not repair evidence.
PR228 does not refresh authority.
PR228 does not reissue evidence.
PR228 does not approve release.
PR228 does not approve deployment.
PR228 does not approve merge.
PR228 does not execute release.
PR228 does not execute source apply.
PR228 does not execute rollback.
PR228 does not continue workflow.
PR228 does not mutate workflow state.
PR228 does not run git.
PR228 does not tag.
PR228 does not create pull requests.
PR228 does not add SQL.
PR228 does not add API.
PR228 does not add CLI.
PR228 does not add UI.
PR228 does not add runtime execution.
PR228 does not add scheduler or worker behavior.
PR228 does not call agents, models, or tools.
PR228 does not promote memory.
PR228 does not activate retrieval.

Negative campaign success means expected negative cases stayed negative.
Negative campaign success does not mean release readiness.
Negative campaign success does not mean release approval.
Negative campaign success does not mean deployment approval.
Negative campaign success does not mean merge approval.
Negative campaign success does not mean release execution.
ReadyEvidenceSatisfied is never acceptable in a negative campaign.
Human review remains required.

## Files

- `IronDev.Core/Governance/ReleaseGateNegativeCampaign.cs`
- `IronDev.Infrastructure/Governance/ReleaseGateNegativeCampaignRunner.cs`
- `IronDev.IntegrationTests/Governance/ReleaseGateNegativeCampaignTests.cs`
- `Docs/receipts/PR228_RELEASE_GATE_NEGATIVE_CAMPAIGNS.md`

## Validation

Validation recorded:

- `ReleaseGateNegativeCampaign`: 30/30 passed.
- `ReleaseGateNegativeCampaign|FailedContinuationRecoveryCampaign|FailedApplyRecoveryCampaign|AuthorityExpiryRegression|StaleAuthorityDetection|ReleaseReadinessRegression|ReleaseReadinessCliRegression|GovernedDogfoodCampaign|GovernedReleaseGate|GovernedReleaseGateCli|ReleaseReadinessGateEvaluator|ReleaseReadinessDecisionRecordStore|ReleaseReadinessDecisionRecord|ReleaseReadinessReport`: 325/325 passed.
- `ReleaseReadinessApiRegression|GovernedReleaseGateApi|ReleaseReadinessDecisionRecordReadApi|WorkflowContinuationApiRegression|WorkflowTransitionRecordReadApi`: 53/53 passed.
- `WorkflowContinuationRegression|WorkflowContinuationCliRegression|GovernedWorkflowContinuation|WorkflowTransitionRecordStore|WorkflowTransitionRecord|WorkflowContinuationGate|RollbackFailureRegression|RollbackReceiptWriteIntegration|RollbackExecutionAudit|RollbackExecutionReceiptStore|ControlledRollbackExecutor|RollbackRegression|SourceApplyRegression|SourceApplyNarrowRealApply|SourceApplyReceipt|PatchArtifactRegression|ApiCliContract|ApiCliReleaseGate`: 282/283 passed.
- Known unrelated caveat: `WorkflowContinuationCliRegression_CliRejectsForbiddenAuthorityOptions` still expects `--release-approved`, while the current CLI rejects `--approve-release`; PR228 does not touch workflow continuation CLI behavior.
- `dotnet build IronDev.slnx --no-restore -v:minimal`: passed, 0 errors / 2 warnings.
- `git diff --check`: passed.

PR228 proves the release gate can say no. It does not teach it to say yes.
