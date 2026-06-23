# B04 - AskBeforeMutation Run Profile

## Summary

AskBeforeMutation is now a supported run-profile kind.

AskBeforeMutation supports only the source-apply lane.

ProposalOnly behavior did not widen.

BoundedRunAuthority remains unsupported for run-profile validation.

BoundedRunAuthority remains unsupported.

## Boundary

AskBeforeMutation is not approval.

AskBeforeMutation is not accepted approval.

AskBeforeMutation is not policy satisfaction.

AskBeforeMutation is not source apply execution.

AskBeforeMutation is not rollback authority.

AskBeforeMutation is not commit authority.

AskBeforeMutation is not push authority.

AskBeforeMutation is not PR authority.

AskBeforeMutation is not merge, release, or deploy authority.

AskBeforeMutation is not memory promotion.

AskBeforeMutation is not workflow continuation.

AskBeforeMutation profile allowance is not approval.

AskBeforeMutation profile allowance is not policy satisfaction.

AskBeforeMutation profile allowance is not execution authority.

AskBeforeMutation profile allowance is not source apply execution.

Profile allowance remains necessary but not sufficient.

Status eligibility remains not execution.

Executor re-check remains future and separate.

## Supported Shape

AskBeforeMutation allowed operations are:

- RepoInspect
- TaskInterpretation
- DisposableWorkspaceCreate
- DisposableWorkspaceModify
- DisposableWorkspaceValidate
- PatchProposal
- PatchPackageWrite
- ValidationResultPackageWrite
- GovernedStatusInspect
- SourceApply
- DurableSourceMutation

AskBeforeMutation does not allow:

- Rollback
- Commit
- Push
- DraftPullRequest
- ReadyForReview
- Merge
- Release
- Deployment
- MemoryPromotion
- WorkflowContinuation
- ApprovalRequestCreate
- PolicySatisfaction
- ProviderMutation
- PackagePublication
- DurableEventWrite

AskBeforeMutation may describe the source-apply lane.

It must not create approval requests.

It must not satisfy policy.

It must not execute source apply.

It must not commit, push, create PRs, mark ready, merge, release, deploy, promote memory, publish packages, or continue workflow.

## Status Alignment

Source apply still requires accepted apply approval evidence.

Eligible status is still not execution.

Accepted apply approval evidence may satisfy the AskBeforeMutation status gate, but it still does not execute anything.

The executor must independently re-check profile, grant, scope, patch hash, validation, mutation budget, and worktree state later.

## Non-Authority Proof

No executor path was added.

No mutation implementation was added.

No source apply executor was added.

No rollback, commit, push, PR, merge, release, deploy, memory, workflow, approval, policy, provider mutation, package publication, UI, API, CLI, SQL, durable store, or generated client path was added.

## Validation

- Focused B04: 12/12 passed
- B03 compatibility: 12/12 passed
- B01 compatibility: 11/11 passed
- BQ/BS/BT compatibility: 44/44 passed
- Stable governance/status corridor: 1502/1502 passed
- Build: 0 errors / 4 warnings
- git diff --check: passed
- git diff --cached --check: passed

## Review Traps

Reject this PR if:

- ProposalOnly behavior changes.
- BoundedRunAuthority becomes valid.
- AskBeforeMutation validates with arbitrary allowed operations.
- AskBeforeMutation allows commit, push, PR, ready-for-review, merge, release, deploy, memory promotion, workflow continuation, policy satisfaction, provider mutation, package publication, or approval request creation.
- AskBeforeMutation creates approval requests.
- AskBeforeMutation satisfies policy.
- AskBeforeMutation executes source apply.
- Eligible status becomes execution permission.
- Any executor or mutation implementation is touched.

## Killjoy

AskBeforeMutation means ask before mutation, not mutate because asked.
