# PR224 - Stale Authority Detection

## Review line

PR224 spots stale authority. It does not refresh or approve it.

## Summary

PR224 adds stale authority detection only.

The detector inspects supplied governance evidence snapshots and reports whether they are expired, superseded, subject-mismatched, workflow-mismatched, hash-invalid, unsafe, or authority-claiming.

## Hard boundary

PR224 detects expired evidence.

PR224 detects superseded evidence.

PR224 detects subject-binding mismatches.

PR224 detects workflow-binding mismatches.

PR224 detects unsafe/private/raw material.

PR224 detects authority claims.

PR224 does not refresh authority.

PR224 does not reissue evidence.

PR224 does not approve release.

PR224 does not approve deployment.

PR224 does not approve merge.

PR224 does not execute release.

PR224 does not execute source apply.

PR224 does not execute rollback.

PR224 does not continue workflow.

PR224 does not mutate workflow state.

PR224 does not run git.

PR224 does not tag.

PR224 does not create pull requests.

PR224 does not add SQL.

PR224 does not add API.

PR224 does not add CLI.

PR224 does not add UI.

PR224 does not add runtime execution.

PR224 does not add scheduler or worker behavior.

PR224 does not call agents, models, or tools.

PR224 does not promote memory.

PR224 does not activate retrieval.

## Locked semantics

IsCurrent means supplied evidence was not detected as stale.

IsCurrent does not mean release approved.

IsCurrent does not mean deployment approved.

IsCurrent does not mean merge approved.

IsCurrent does not mean release ready.

IsCurrent does not execute release.

Human review remains required for release approval, deployment, and merge.

## Validation

Validation run:

```powershell
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "StaleAuthorityDetection" --no-restore --logger "console;verbosity=minimal"
# Passed: 31/31

dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "StaleAuthorityDetection|ReleaseReadinessRegression|ReleaseReadinessCliRegression|GovernedDogfoodCampaign|GovernedReleaseGate|GovernedReleaseGateCli|ReleaseReadinessGateEvaluator|ReleaseReadinessDecisionRecordStore|ReleaseReadinessDecisionRecord|ReleaseReadinessReport" --no-restore --logger "console;verbosity=minimal"
# Passed: 202/202

dotnet test IronDev.IntegrationTests.Api\IronDev.IntegrationTests.Api.csproj --filter "ReleaseReadinessApiRegression|GovernedReleaseGateApi|ReleaseReadinessDecisionRecordReadApi|WorkflowContinuationApiRegression|WorkflowTransitionRecordReadApi" --no-restore --logger "console;verbosity=minimal"
# Passed: 53/53

dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "WorkflowContinuationRegression|WorkflowContinuationCliRegression|GovernedWorkflowContinuation|WorkflowTransitionRecordStore|WorkflowTransitionRecord|WorkflowContinuationGate|RollbackFailureRegression|RollbackReceiptWriteIntegration|RollbackExecutionAudit|RollbackExecutionReceiptStore|ControlledRollbackExecutor|RollbackRegression|SourceApplyRegression|SourceApplyNarrowRealApply|SourceApplyReceipt|PatchArtifactRegression|ApiCliContract|ApiCliReleaseGate" --no-restore --logger "console;verbosity=minimal"
# Passed: 277/278
# Known neighboring failure: WorkflowContinuationCliRegression_CliRejectsForbiddenAuthorityOptions expects --release-approved while the existing workflow-continuation CLI uses --approve-release.

dotnet build IronDev.slnx --no-restore -v:minimal
# Passed: 0 errors, 2 warnings

git diff --check
# Passed
```

The known neighboring `WorkflowContinuationCliRegression_CliRejectsForbiddenAuthorityOptions` mismatch is documented as the existing `--release-approved` versus `--approve-release` workflow-continuation CLI expectation. It is not a PR224 stale authority detection behavior change.
