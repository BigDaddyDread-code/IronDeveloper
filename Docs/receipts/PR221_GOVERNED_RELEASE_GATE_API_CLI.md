# PR221 - Governed Release Gate API/CLI

## Review line

PR221 runs the readiness evidence gate. It does not release the product.

## Summary

PR221 adds a governed API and CLI path for release-readiness gate evaluation.

The path accepts a `ReleaseReadinessReport`, validates it, evaluates it with the existing `ReleaseReadinessGateEvaluator`, persists the resulting `ReleaseReadinessDecisionRecord` through the existing append-only release-readiness decision record store, reads the stored record back, and returns that stored record as evidence.

## Boundary

This PR runs an evidence gate and stores a decision record only.

A successful response means:

- release-readiness evidence was evaluated
- a `ReleaseReadinessDecisionRecord` was stored
- the stored decision record was read back

A successful response does not mean:

- release approval
- deployment approval
- merge approval
- release execution
- source apply
- rollback execution
- workflow continuation
- workflow mutation
- git commit, push, tag, branch, merge, or pull request creation
- agent dispatch
- model invocation
- tool execution
- memory promotion
- retrieval activation
- UI action

Human review remains required for release approval, deployment, and merge.

## API

Adds:

```text
POST /api/v1/projects/{projectId}/release-readiness/gate/governed
```

The API depends on `IGovernedReleaseGateService` only.

The API does not expose release execution, deployment, merge, source apply, rollback, workflow continuation, git, agent, model, tool, memory, retrieval, scheduler, or runtime methods.

## CLI

Adds:

```text
irondev release gate governed --request-file <path>
irondev release readiness gate governed --request-file <path>
```

The CLI calls the governed release gate API only.

The CLI rejects release/deployment/merge/source/rollback/workflow/git/agent/model/tool/memory/retrieval authority flags before API submission.

## Storage

No SQL migration is added.

No new store is added.

This PR uses the existing `IReleaseReadinessDecisionRecordStore` and existing release-readiness decision record persistence.

Successful governed release gate results require the stored decision record to be readable. If the record cannot be saved or read back, the governed result is not successful.

## Safety receipts

The PR keeps these flags false in result and API boundary material:

- `ReleaseApproved`
- `DeploymentApproved`
- `MergeApproved`
- `ReleaseExecuted`
- `SourceApplyExecuted`
- `RollbackExecuted`
- `WorkflowContinued`
- `WorkflowMutated`
- `GitOperationExecuted`

The PR keeps these flags true:

- `HumanReviewRequiredForReleaseApproval`
- `HumanReviewRequiredForDeployment`
- `HumanReviewRequiredForMerge`

## Validation

Focused validation run:

```powershell
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "GovernedReleaseGate|GovernedReleaseGateCli|ReleaseReadinessGateEvaluator|ReleaseReadinessDecisionRecordStore|ReleaseReadinessDecisionRecord|ReleaseReadinessReport" --no-restore --logger "console;verbosity=minimal"
# Passed: 121/121

dotnet test IronDev.IntegrationTests.Api\IronDev.IntegrationTests.Api.csproj --filter "GovernedReleaseGateApi|ReleaseReadinessDecisionRecordReadApi|WorkflowContinuationApiRegression|WorkflowTransitionRecordReadApi" --no-restore --logger "console;verbosity=minimal"
# Passed: 47/47

dotnet build IronDev.slnx --no-restore -v:minimal
# Passed: 0 errors, 2 warnings

git diff --check
# Passed with LF/CRLF warnings only
```

Additional direct PR221 checks:

```powershell
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "GovernedReleaseGateCli" --no-restore --logger "console;verbosity=minimal"
# Passed: 5/5

dotnet test IronDev.IntegrationTests.Api\IronDev.IntegrationTests.Api.csproj --filter "GovernedReleaseGateApi" --no-restore --logger "console;verbosity=minimal"
# Passed: 5/5
```

Known broad-band caveat:

```powershell
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "WorkflowContinuationRegression|WorkflowContinuationCliRegression|GovernedWorkflowContinuation|WorkflowTransitionRecordStore|WorkflowTransitionRecord|WorkflowContinuationGate|RollbackFailureRegression|RollbackReceiptWriteIntegration|RollbackExecutionAudit|RollbackExecutionReceiptStore|ControlledRollbackExecutor|RollbackRegression|SourceApplyRegression|SourceApplyNarrowRealApply|SourceApplyReceipt|PatchArtifactRegression|ApiCliContract|ApiCliReleaseGate" --no-restore --logger "console;verbosity=minimal"
# Failed: 1, Passed: 276, Total: 277
```

The failure is `WorkflowContinuationCliRegression_CliRejectsForbiddenAuthorityOptions`, which expects `--release-approved` in the existing workflow-continuation CLI forbidden option list. PR221 does not touch `CliWorkflowContinuation.cs`; the failure is a pre-existing neighboring regression lane, not a governed release gate API/CLI failure.

## Non-goals

PR221 does not add release execution, deployment, merge, git operations, source apply, rollback execution, workflow continuation, workflow mutation, workflow scheduling, runtime dispatch, API/CLI/UI release approval, agent dispatch, model invocation, tool execution, memory promotion, retrieval activation, SQL schema changes, or stored procedure changes.
