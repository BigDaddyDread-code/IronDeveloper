# PR226 - Failed Apply Recovery Campaign

Review line:

> PR226 rehearses failed-apply recovery. It does not retry the apply or declare recovery complete.

## Purpose

PR226 adds failed apply recovery campaign evidence inspection only.

The campaign runner consumes explicitly supplied failed/partial source apply evidence, rollback recovery evidence, rollback audit evidence, stale-authority evidence when supplied, and follow-up release-readiness evidence when supplied.

It reports whether the supplied recovery evidence is complete, missing, failed, stale, or unsafe.

## Boundary

PR226 inspects supplied source apply failure evidence.
PR226 inspects supplied rollback recovery evidence.
PR226 inspects supplied rollback audit evidence.
PR226 inspects supplied stale-authority detection evidence when supplied.
PR226 inspects supplied follow-up release-readiness evidence when supplied.

PR226 does not retry source apply.
PR226 does not execute source apply.
PR226 does not execute rollback.
PR226 does not run rollback audit.
PR226 does not continue workflow.
PR226 does not mutate workflow state.
PR226 does not approve release.
PR226 does not approve deployment.
PR226 does not approve merge.
PR226 does not execute release.
PR226 does not run git.
PR226 does not tag.
PR226 does not create pull requests.
PR226 does not refresh authority.
PR226 does not reissue evidence.
PR226 does not add SQL.
PR226 does not add API.
PR226 does not add CLI.
PR226 does not add UI.
PR226 does not add runtime execution.
PR226 does not add scheduler or worker behavior.
PR226 does not call agents, models, or tools.
PR226 does not promote memory.
PR226 does not activate retrieval.

`RecoveryEvidenceSatisfied` means supplied recovery evidence appears complete.
`RecoveryEvidenceSatisfied` does not mean recovery was executed by PR226.
`RecoveryEvidenceSatisfied` does not mean workflow can continue.
`RecoveryEvidenceSatisfied` does not mean source apply can be retried.
`RecoveryEvidenceSatisfied` does not mean release approved.

Human review remains required.

## Changed files

- `IronDev.Core/Governance/FailedApplyRecoveryCampaign.cs`
- `IronDev.Infrastructure/Governance/FailedApplyRecoveryCampaignRunner.cs`
- `IronDev.IntegrationTests/Governance/FailedApplyRecoveryCampaignTests.cs`
- `Docs/receipts/PR226_FAILED_APPLY_RECOVERY_CAMPAIGN.md`

## Coverage

The tests prove:

- null or malformed campaign requests are rejected
- missing source apply failure evidence is rejected
- successful source apply cannot enter failed-apply recovery
- source apply evidence must prove failed or partial apply
- invalid hashes are rejected
- failed/applied paths are required
- missing rollback evidence produces `RecoveryEvidenceMissing`
- failed, partial, or not-executed rollback evidence produces `RecoveryEvidenceFailed`
- missing rollback audit evidence produces `RecoveryEvidenceMissing`
- inconsistent rollback audit evidence produces `RecoveryEvidenceFailed`
- complete rollback and audit evidence produces `RecoveryEvidenceSatisfied`
- stale authority evidence blocks recovery evidence satisfaction
- omitted stale-authority evidence is warning-only for PR226
- current stale-authority evidence allows recovery evidence evaluation
- follow-up ready evidence does not approve release
- follow-up readiness approval or execution claims fail recovery
- unsafe raw/private material is rejected and not echoed
- authority claims are rejected
- the runner has no source apply, rollback, workflow, release, git, SQL, API, CLI, agent, model, tool, memory, retrieval, scheduler, or runtime dependencies
- the runner does not rerun stale-authority detection

## Validation

- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "FailedApplyRecoveryCampaign" --no-restore --logger "console;verbosity=minimal"`: passed, `32/32`.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "FailedApplyRecoveryCampaign|AuthorityExpiryRegression|StaleAuthorityDetection|ReleaseReadinessRegression|ReleaseReadinessCliRegression|GovernedDogfoodCampaign|GovernedReleaseGate|GovernedReleaseGateCli|ReleaseReadinessGateEvaluator|ReleaseReadinessDecisionRecordStore|ReleaseReadinessDecisionRecord|ReleaseReadinessReport" --no-restore --logger "console;verbosity=minimal"`: passed, `262/262`.
- `dotnet test IronDev.IntegrationTests.Api\IronDev.IntegrationTests.Api.csproj --filter "ReleaseReadinessApiRegression|GovernedReleaseGateApi|ReleaseReadinessDecisionRecordReadApi|WorkflowContinuationApiRegression|WorkflowTransitionRecordReadApi" --no-restore --logger "console;verbosity=minimal"`: passed, `53/53`.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "WorkflowContinuationRegression|WorkflowContinuationCliRegression|GovernedWorkflowContinuation|WorkflowTransitionRecordStore|WorkflowTransitionRecord|WorkflowContinuationGate|RollbackFailureRegression|RollbackReceiptWriteIntegration|RollbackExecutionAudit|RollbackExecutionReceiptStore|ControlledRollbackExecutor|RollbackRegression|SourceApplyRegression|SourceApplyNarrowRealApply|SourceApplyReceipt|PatchArtifactRegression|ApiCliContract|ApiCliReleaseGate" --no-restore --logger "console;verbosity=minimal"`: `282/283` passed, with the known unrelated `WorkflowContinuationCliRegression_CliRejectsForbiddenAuthorityOptions` mismatch expecting `--release-approved` while the current CLI rejects `--approve-release`.
- `dotnet build IronDev.slnx --no-restore -v:minimal`: passed, `0` errors / `2` warnings.
- `git diff --check`: passed.
