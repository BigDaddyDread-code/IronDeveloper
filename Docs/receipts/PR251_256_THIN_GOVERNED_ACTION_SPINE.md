# PR251-256 - Thin Governed Action Spine Receipt

## Purpose

Block AB installs the first thin governed action spine under the manual patch proposal product.

The spine gives IronDev a shared action vocabulary before risky capabilities grow more hands.

## Action kinds added

Non-authority patch-loop actions:

- PatchProposalRunStarted
- DisposableWorkspaceCreated
- PatchArtifactExported
- ChangedFilesDetected
- WorkspaceTestsExecuted
- ReviewPackageCreated
- PatchRunStatusRead
- PatchRunListed
- PatchWorkspaceCleaned

Authority-bearing actions registered but not executable:

- ToolExecution
- MemoryPromotion
- AcceptedMemoryMutation
- SourceApply
- RollbackExecution
- WorkflowContinuation
- ReleaseReadinessDecision
- ReleaseApproval
- DeploymentApproval
- MergeApproval

Forbidden or unsupported direct paths:

- ProductionDeployment
- DirectGitCommitToSource
- DirectGitPush
- DirectAcceptedMemoryWrite
- UIApprovalCreation
- AgentSelfApproval
- WorkflowContinuationFromTextEvidence

## Classifications added

- NonAuthority
- AuthorityBearing
- ForbiddenOrUnsupported

Patch-loop events are NonAuthority.
High-risk future actions are AuthorityBearing.
Direct bypass paths are ForbiddenOrUnsupported.

## Patch-loop events recorded

Patch proposal runs now append local run-scoped JSONL governance events to:

```text
governance-events.jsonl
```

Events are recorded for:

- patch run start
- disposable workspace creation
- changed-file detection
- workspace test execution or explicit skip
- patch artifact export
- review package creation
- patch status inspection
- patch workspace cleanup

These events are local run artifacts. They are evidence that a patch-loop action happened. They are not approval.

## Inventory behavior

`irondev governance inventory` lists known action kinds and their classification.

`irondev governance classify --action <action-kind>` reports:

- classification
- current-block allowance
- Conscience requirement
- ThoughtLedger requirement
- implementation status
- executable in current block from the inventory allowance: true for patch-loop non-authority events, false for high-risk future paths

Authority-bearing actions are registered only. They are not executable in this block.

## Conscience decision contract

Block AB adds the Conscience decision shape:

- Allow
- Block
- RequiresHumanReview
- NotImplemented

The contract records risk, evidence refs, policy refs, block reasons, ThoughtLedger ref, expiry, and hash.

Authority-bearing actions that are not allowed in the current block fail closed with `ActionNotAllowedInCurrentBlock` before any supplied Conscience decision can make them executable.

This does not make IronDev autonomous.

## ThoughtLedger requirement

Authority-bearing actions require ThoughtLedger before future execution.

Missing ThoughtLedger fails closed for authority-bearing actions.

Patch-loop NonAuthority events do not require ThoughtLedger.

## Bypass lanes added

Bypass expectation lanes were added for:

- memory promotion without Conscience
- memory promotion without ThoughtLedger
- tool execution without gate
- source apply without accepted approval
- source apply without policy satisfaction
- source apply without patch artifact
- source apply without dry-run
- source apply without rollback plan
- workflow continuation from receipt text
- release readiness decision from report text
- UI approval creation
- agent self-approval
- direct git push from IronDev action path

If an implementation path does not exist yet, the lane asserts the action is registered, authority-bearing or forbidden, and not executable in the current block.

## Boundaries preserved

This block installs a thin governed action spine.

It does not apply source.
It does not execute rollback.
It does not promote memory.
It does not mutate accepted memory.
It does not execute tools through a new gate.
It does not dispatch agents.
It does not call models.
It does not continue workflow.
It does not approve release.
It does not approve deployment.
It does not approve merge.
It does not add API, SQL, UI, scheduler, worker, or autonomous runtime behavior.

Patch-loop events are recorded as non-authority events.
Authority-bearing actions are inventoried but not executable in this block.

## Known limitations

- This is not a durable SQL governance event store.
- This does not enforce every future high-risk path.
- Conscience decisions are modeled but not wired as universal runtime gates.
- ThoughtLedger requirements are modeled but not hydrated from a ledger store here.
- Bypass lanes prove expected future blocking posture; they do not implement the future paths.

## Validation

Validation run:

```powershell
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "BlockABThinGovernedActionSpine" --no-restore --logger "console;verbosity=minimal"
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "IronDevCliTests|BlockZManualPatchProposalProduct|BlockAAPatchLoopUsability|BlockABThinGovernedActionSpine" --no-restore --logger "console;verbosity=minimal"
dotnet build IronDev.slnx --no-restore -v:minimal
git diff --check
```

Results:

- Block AB thin governed action spine: 6/6 passed.
- CLI + Block Z + Block AA + Block AB regression band: 190/190 passed.
- Solution build: passed with 0 errors and 4 warnings.
- `git diff --check`: passed with LF/CRLF warnings only.

## Review line

PR251-256 installs the thin governed action spine. It records patch-loop actions but does not grant authority.

## Killjoy line

Block AB is finished when the worker has a spine, not when the spine has become a government.
