# B05 - Bounded Run Authority Profile

## Summary

BoundedRunAuthority is now a supported run-profile kind.

BoundedRunAuthority is broader than AskBeforeMutation but still bounded.

BoundedRunAuthority supports source apply, rollback, commit, push, and draft PR profile lanes only under a matching bounded grant.

ProposalOnly behavior did not widen.

AskBeforeMutation behavior did not widen.

## Boundary

BoundedRunAuthority profile is not a bounded grant.

BoundedRunAuthority profile is not approval.

BoundedRunAuthority profile is not accepted approval.

BoundedRunAuthority profile is not policy satisfaction.

BoundedRunAuthority profile is not execution permission.

BoundedRunAuthority profile is not source apply execution.

BoundedRunAuthority profile is not rollback execution.

BoundedRunAuthority profile is not commit execution.

BoundedRunAuthority profile is not push execution.

BoundedRunAuthority profile is not PR creation execution.

BoundedRunAuthority profile is not ready-for-review authority.

BoundedRunAuthority profile is not merge, release, or deploy authority.

BoundedRunAuthority profile is not memory promotion.

BoundedRunAuthority profile is not workflow continuation.

BoundedRunAuthority profile allowance is not approval.

BoundedRunAuthority profile allowance is not policy satisfaction.

BoundedRunAuthority profile allowance is not execution authority.

BoundedRunAuthority profile allowance is not source apply, rollback, commit, push, or PR execution.

The grant narrows the profile.

The profile cannot widen the grant.

Operation eligibility remains necessary but not sufficient.

Eligible status remains not execution.

Executor re-check remains future and separate.

## Supported Shape

BoundedRunAuthority allowed operations are:

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
- Rollback
- Commit
- Push
- DraftPullRequest

BoundedRunAuthority does not support:

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

ReadyForReview stays forbidden in B05.

Draft PR creation is a controlled publication boundary.

Ready-for-review is a separate review-intent escalation.

## Grant Alignment

Bounded grant validation now aligns with BoundedRunAuthorityAllowedOperations and BoundedRunAuthorityForbiddenOperations.

A bounded grant may allow only operations inside the BoundedRunAuthority profile ceiling.

A bounded grant must not allow operations in the BoundedRunAuthority forbidden set.

Stop-before can narrow a grant.

Stop-before cannot widen a grant.

## Non-Authority Proof

No executor path was added.

No mutation implementation was added.

No source apply, rollback, commit, push, PR, ready-for-review, merge, release, deploy, memory promotion, workflow continuation, approval creation, policy satisfaction, provider mutation, package publication, UI, API, CLI, SQL, durable store, or generated client path was added.

## Validation

- Focused B05: 16/16 passed.
- B04/B03/B01 compatibility: 35/35 passed.
- BQ/BR/BS/BT compatibility: 59/59 passed.
- BU compatibility refresh: 21/21 passed.
- Stable governance/status corridor: 1518/1518 passed.
- Build: 0 errors / 4 warnings.
- git diff --check: passed with normal LF/CRLF warnings.
- git diff --cached --check: passed.

## Review Traps

Reject this PR if:

- ProposalOnly behavior changes.
- AskBeforeMutation behavior changes.
- BoundedRunAuthority validates arbitrary operation sets.
- BoundedRunAuthority allows ready-for-review, merge, release, deployment, memory promotion, workflow continuation, approval request creation, policy satisfaction, provider mutation, package publication, or durable event write.
- BoundedRunAuthority creates approval requests.
- BoundedRunAuthority satisfies policy.
- BoundedRunAuthority executes source apply, rollback, commit, push, or PR creation.
- Bounded grant validation allows operations outside BoundedRunAuthorityAllowedOperations.
- Status or eligibility executes.
- Any executor or mutation implementation is touched.

## Killjoy

A bounded profile names the lane. The grant, evidence, and executor still hold the keys.
