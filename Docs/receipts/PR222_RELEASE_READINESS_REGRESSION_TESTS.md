# PR222 - Release Readiness Regression Tests

## Review line

PR222 locks the release gate cage. It does not open the gate.

## Summary

PR222 adds release-readiness regression tests only.

The tests pin the PR216 through PR221 release-readiness chain:

- `ReleaseReadinessReport`
- `ReleaseReadinessDecisionRecord`
- `ReleaseReadinessDecisionRecordStore`
- `ReleaseReadinessGateEvaluator`
- `ReleaseReadinessDecisionRecordReadApi`
- `GovernedReleaseGateService`
- `GovernedReleaseGateController`
- `IronDevApiClient.CreateGovernedReleaseGateAsync`
- `irondev release gate governed`
- `irondev release readiness gate governed`

## Hard boundary

PR222 does not add release-readiness behavior.

PR222 does not change release-readiness report behavior.

PR222 does not change release-readiness decision record behavior.

PR222 does not change release-readiness store behavior.

PR222 does not change release-readiness gate evaluator behavior.

PR222 does not change governed release gate API behavior.

PR222 does not change governed release gate CLI behavior.

PR222 does not approve release.

PR222 does not approve deployment.

PR222 does not approve merge.

PR222 does not execute release.

PR222 does not tag.

PR222 does not git commit.

PR222 does not git push.

PR222 does not merge.

PR222 does not create pull requests.

PR222 does not execute source apply.

PR222 does not execute rollback.

PR222 does not continue workflow.

PR222 does not mutate workflow state.

PR222 does not add SQL.

PR222 does not add API.

PR222 does not add CLI.

PR222 does not add UI.

PR222 does not add runtime execution.

PR222 does not call agents, models, or tools.

PR222 does not promote memory.

PR222 does not activate retrieval.

## Locked semantics

Release readiness regression tests are not release readiness.

ReadyEvidenceSatisfied means evidence satisfied only.

ReadyEvidenceSatisfied does not mean release approved.

ReadyEvidenceSatisfied does not mean deployment approved.

ReadyEvidenceSatisfied does not mean merge approved.

Governed release gate success means the evidence gate completed and a decision record was stored/read back.

Governed release gate success does not mean release approval.

No stored ReleaseReadinessDecisionRecord means no successful governed release gate result.

Human review remains required for release approval, deployment, and merge.

## Regression coverage

The PR222 tests prove:

- a complete release-readiness report is not release-ready
- ReadyEvidenceSatisfied is not release approval
- governed release gate success is stored evidence only
- save failure means no successful governed result
- read-back failure means no successful governed result
- blocked decisions can be successful stored evaluations without approval
- release-readiness decision storage remains append-only
- authority flags remain rejected
- unsafe/private material cannot become release-readiness authority
- authority claims cannot create ready decisions
- report hashes and decision hashes keep their prefix/raw-hex boundary
- read API rejects prefixed decision-record hash lookup
- API controllers keep safe dependency boundaries
- CLI remains API-only and rejects release authority options

## Validation

Validation run:

```powershell
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "ReleaseReadinessRegression|ReleaseReadinessCliRegression" --no-restore --logger "console;verbosity=minimal"
# Passed: 18/18

dotnet test IronDev.IntegrationTests.Api\IronDev.IntegrationTests.Api.csproj --filter "ReleaseReadinessApiRegression" --no-restore --logger "console;verbosity=minimal"
# Passed: 6/6

dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "ReleaseReadinessRegression|ReleaseReadinessCliRegression|GovernedReleaseGate|GovernedReleaseGateCli|ReleaseReadinessGateEvaluator|ReleaseReadinessDecisionRecordStore|ReleaseReadinessDecisionRecord|ReleaseReadinessReport" --no-restore --logger "console;verbosity=minimal"
# Passed: 139/139

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

The known neighboring `WorkflowContinuationCliRegression_CliRejectsForbiddenAuthorityOptions` mismatch is documented as the existing `--release-approved` versus `--approve-release` workflow-continuation CLI expectation. It is not a PR222 release-readiness regression-test behavior change.
