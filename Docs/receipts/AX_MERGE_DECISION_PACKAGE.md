# AX Merge Decision Package

## Review Line

Block AX packages an explicit merge decision for a reviewed, current, non-draft PR using PR identity, current head evidence, review evidence, validation evidence, and phase authority evidence. It does not merge, enable auto-merge, approve, submit reviews, resolve review threads, release, deploy, tag, publish, promote memory, commit, push, mutate source, or continue workflow.

## Purpose

AX starts Phase 3 by creating the sealed merge decision package that a future merge executor may consume. It records that the PR is current, non-draft, reviewed, validation-backed, and explicitly selected for a future merge executor.

AX does not press the merge button.

## Authority Chain

Reviewer request execution is not review completion.
Review completion is not approval.
Approval is not merge decision.
Merge decision package is not merge execution.
Merge execution is not release.
Release is not deployment.
Validation evidence is not approval.
No self-approval.
No hidden mutation.

## Required Evidence

AX requires an executed AW reviewer-request execution receipt with post-state verification. It also requires observed current PR state, review evidence for the expected head SHA, validation evidence for the expected head SHA, and an explicit merge decision record.

The observed PR state must prove:

- repository and PR number match
- PR is open
- PR is not draft
- head branch and head SHA match
- base branch and base SHA match when supplied
- mergeability and conflict fields were explicitly supplied
- conflicts are absent

The review evidence must prove current required approvals, no requested changes, no stale approval reliance, and no unresolved blocking review threads.

The validation evidence must prove the required validation families passed for the expected head SHA:

- FocusedCurrentBlock
- ImpactedArea
- FastAuthorityInvariant
- Build
- DiffCheck
- PhaseAuthority
- MergeDecisionAuthority

The merge decision record must be explicit. It must include decision maker, rationale, expected repository, PR number, expected head SHA, expected base branch, and selected merge strategy. The decision maker must not be the PR author.

## Boundary

AX produces package evidence only.

```text
EvidenceOnly = true
CanMerge = false
CanAutoMerge = false
CanApprove = false
CanSubmitReview = false
CanRelease = false
CanDeploy = false
CanTag = false
CanPublish = false
CanPromoteMemory = false
CanCommit = false
CanPush = false
CanMutateSource = false
CanMutateWorkspace = false
CanContinueWorkflow = false
```

`CanMergeForExecutor = true` is a handoff flag for AY only. It is not merge authority for AX.

AX does not merge.
AX does not enable auto-merge.
AX does not approve.
AX does not submit reviews.
AX does not resolve review threads.
AX does not release.
AX does not deploy.
AX does not continue workflow.

## Outputs

AX writes:

- `merge-decision-package.json`
- `merge-decision-package-receipt.json`
- `merge-decision-summary.md`
- `merge-decision-evidence.jsonl`
- governance event `MergeDecisionPackageCreated`

## Rejection Traps

Reject AX if approval automatically creates merge eligibility.
Reject AX if green checks automatically create merge eligibility.
Reject AX if reviewer request execution automatically creates merge eligibility.
Reject AX if missing explicit merge decision is allowed.
Reject AX if stale review or validation evidence is accepted.
Reject AX if requested changes or unresolved blocking threads are ignored.
Reject AX if package creation executes merge, auto-merge, release, deployment, tag, publish, approval, review submission, review-thread resolution, commit, push, source mutation, memory promotion, or workflow continuation.

## Killjoy

Approval is not merge. Green checks are not merge. Reviewer request is not merge. AX writes the sealed merge decision package; it does not press the merge button.
