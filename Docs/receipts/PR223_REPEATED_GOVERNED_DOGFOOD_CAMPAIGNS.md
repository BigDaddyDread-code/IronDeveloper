# PR223 - Repeated Governed Dogfood Campaigns

## Review line

PR223 repeats the governed dogfood drill. It does not become an autonomous operator.

## Summary

PR223 adds repeated governed dogfood campaign runner only.

PR223 repeats explicitly supplied governed release gate requests.

The campaign runner can produce a bounded campaign report from supplied release gate requests and existing governed release gate results.

## Hard boundary

PR223 does not create release-readiness reports.

PR223 does not invent evidence.

PR223 does not approve release.

PR223 does not approve deployment.

PR223 does not approve merge.

PR223 does not execute release.

PR223 does not tag.

PR223 does not git commit.

PR223 does not git push.

PR223 does not merge.

PR223 does not create pull requests.

PR223 does not execute source apply.

PR223 does not execute rollback.

PR223 does not continue workflow.

PR223 does not mutate workflow state.

PR223 does not add SQL.

PR223 does not add API.

PR223 does not add CLI.

PR223 does not add UI.

PR223 does not add runtime execution.

PR223 does not add scheduler or worker behavior.

PR223 does not call agents, models, or tools.

PR223 does not promote memory.

PR223 does not activate retrieval.

## Locked semantics

Repeated governed dogfood campaign is not autonomy.

Repeated governed dogfood campaign is not release approval.

Repeated governed dogfood campaign is not release execution.

Campaign Completed means bounded campaign loop completed.

Campaign Completed does not mean release approved.

ReadyEvidenceSatisfiedCount does not mean release approved.

BlockedDecisionCount does not mean campaign failure.

FailedIterationCount means at least one governed gate iteration failed.

Human review remains required for release approval, deployment, and merge.

## Validation

Validation run:

```powershell
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "GovernedDogfoodCampaign" --no-restore --logger "console;verbosity=minimal"
# Passed: 32/32

dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "GovernedDogfoodCampaign|ReleaseReadinessRegression|ReleaseReadinessCliRegression|GovernedReleaseGate|GovernedReleaseGateCli|ReleaseReadinessGateEvaluator|ReleaseReadinessDecisionRecordStore|ReleaseReadinessDecisionRecord|ReleaseReadinessReport" --no-restore --logger "console;verbosity=minimal"
# Passed: 171/171

dotnet test IronDev.IntegrationTests.Api\IronDev.IntegrationTests.Api.csproj --filter "ReleaseReadinessApiRegression|GovernedReleaseGateApi|ReleaseReadinessDecisionRecordReadApi|WorkflowContinuationApiRegression|WorkflowTransitionRecordReadApi" --no-restore --logger "console;verbosity=minimal"
# Passed: 53/53

dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "WorkflowContinuationRegression|WorkflowContinuationCliRegression|GovernedWorkflowContinuation|WorkflowTransitionRecordStore|WorkflowTransitionRecord|WorkflowContinuationGate|RollbackFailureRegression|RollbackReceiptWriteIntegration|RollbackExecutionAudit|RollbackExecutionReceiptStore|ControlledRollbackExecutor|RollbackRegression|SourceApplyRegression|SourceApplyNarrowRealApply|SourceApplyReceipt|PatchArtifactRegression|ApiCliContract|ApiCliReleaseGate" --no-restore --logger "console;verbosity=minimal"
# Passed: 276/277
# Known neighboring failure: WorkflowContinuationCliRegression_CliRejectsForbiddenAuthorityOptions expects --release-approved while the existing workflow-continuation CLI uses --approve-release.

dotnet build IronDev.slnx --no-restore -v:minimal
# Passed: 0 errors, 2 warnings

git diff --check
# Passed
```

The known neighboring `WorkflowContinuationCliRegression_CliRejectsForbiddenAuthorityOptions` mismatch is documented as the existing `--release-approved` versus `--approve-release` workflow-continuation CLI expectation. It is not a PR223 governed dogfood campaign behavior change.
