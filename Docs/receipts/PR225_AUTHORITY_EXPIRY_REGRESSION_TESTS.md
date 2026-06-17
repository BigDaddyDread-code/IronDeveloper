# PR225 - Authority Expiry Regression Tests

Review line:

> PR225 locks the expiry clock. It does not renew the authority.

## Purpose

PR225 adds focused regression coverage for stale-authority expiry behaviour.

This is a tests-and-receipt slice only. It does not change production behaviour.

## Boundary

Expired authority evidence remains stale evidence.

Expired authority evidence does not:

- approve release
- approve deployment
- approve merge
- execute release
- execute source apply
- execute rollback
- continue workflow
- mutate workflow state
- run git
- refresh authority
- reissue evidence
- extend expiry
- satisfy approval
- satisfy policy
- create release readiness
- dispatch agents
- call models
- invoke tools
- promote memory
- activate retrieval

Human review remains required.

## Regression coverage

The test pack pins expiry handling for:

- `AcceptedApproval`
- `PolicySatisfaction`
- `SourceApplyRequest`
- `SourceApplyReceipt`
- `RollbackExecutionReceipt`
- `RollbackExecutionAudit`
- `WorkflowContinuationGate`
- `WorkflowTransitionRecord`
- `ReleaseReadinessReport`
- `ReleaseReadinessDecisionRecord`
- `GovernedReleaseGateResult`

The tests prove:

- evidence expiring before evaluation is expired
- evidence expiring exactly at evaluation time is expired
- future expiry remains current
- missing expiry remains current
- future-created evidence is rejected even when expiry is future
- one expired evidence item makes the whole detection stale
- mixed current and expired evidence reports an `EvidenceExpired` finding
- all-current evidence remains current
- expired approval/policy/source/workflow/release evidence blocks release-readiness chains
- expired evidence does not refresh authority or reissue evidence
- expired evidence does not execute or mutate anything
- unsafe expired evidence is not echoed into result output
- authority-claiming expired evidence does not become approval
- the stale-authority detector does not gain renewal, execution, runtime, persistence, API, CLI, agent, model, tool, memory, or retrieval paths

## Changed files

- `IronDev.IntegrationTests/Governance/AuthorityExpiryRegressionTests.cs`
- `Docs/receipts/PR225_AUTHORITY_EXPIRY_REGRESSION_TESTS.md`

## Validation

- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "AuthorityExpiryRegression" --no-restore --logger "console;verbosity=minimal"`: passed, `28/28`.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "AuthorityExpiryRegression|StaleAuthorityDetection|ReleaseReadinessRegression|ReleaseReadinessCliRegression|GovernedDogfoodCampaign|GovernedReleaseGate|GovernedReleaseGateCli|ReleaseReadinessGateEvaluator|ReleaseReadinessDecisionRecordStore|ReleaseReadinessDecisionRecord|ReleaseReadinessReport" --no-restore --logger "console;verbosity=minimal"`: passed, `230/230`.
- `dotnet test IronDev.IntegrationTests.Api\IronDev.IntegrationTests.Api.csproj --filter "ReleaseReadinessApiRegression|GovernedReleaseGateApi|ReleaseReadinessDecisionRecordReadApi|WorkflowContinuationApiRegression|WorkflowTransitionRecordReadApi" --no-restore --logger "console;verbosity=minimal"`: passed, `53/53`.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "WorkflowContinuationRegression|WorkflowContinuationCliRegression|GovernedWorkflowContinuation|WorkflowTransitionRecordStore|WorkflowTransitionRecord|WorkflowContinuationGate|RollbackFailureRegression|RollbackReceiptWriteIntegration|RollbackExecutionAudit|RollbackExecutionReceiptStore|ControlledRollbackExecutor|RollbackRegression|SourceApplyRegression|SourceApplyNarrowRealApply|SourceApplyReceipt|PatchArtifactRegression|ApiCliContract|ApiCliReleaseGate" --no-restore --logger "console;verbosity=minimal"`: `282/283` passed, with the known unrelated `WorkflowContinuationCliRegression_CliRejectsForbiddenAuthorityOptions` mismatch expecting `--release-approved` while the current CLI rejects `--approve-release`.
- `dotnet build IronDev.slnx --no-restore -v:minimal`: passed, `0` errors / `2` warnings.
- `git diff --check`: passed.

## Non-goals

PR225 does not add or change:

- production code
- Core behaviour
- SQL schema
- stored procedures
- API
- CLI
- UI
- runtime worker
- scheduler
- workflow continuation
- release readiness creation
- approval satisfaction
- policy satisfaction
- source apply
- rollback execution
- release execution
- git or GitHub actions
- agent/model/tool execution
- memory promotion
- retrieval activation
