# Block AU - Controlled Ready-for-Review Executor

## Review Line

Block AU consumes an eligible AT ready-for-review package, verifies current PR state, marks the expected draft PR ready for review, and writes an execution receipt. It does not request reviewers, resolve review threads, approve, merge, release, deploy, tag, publish, promote memory, or continue workflow.

## Purpose

AU is the first Phase 2 block allowed to perform the ready-for-review transition:

```text
draft PR -> ready-for-review PR
```

That transition is one narrow mutation. It is not reviewer request, approval, merge readiness, release readiness, deployment, memory promotion, or workflow continuation.

## Boundary

Ready-for-review package is not ready-for-review execution.

Ready-for-review execution is not reviewer request.

Reviewer request is not approval.

Approval is not merge.

Merge is not release.

Release is not deployment.

Validation evidence is not approval.

AU marks only the expected draft PR ready for review.

AU does not request reviewers.

AU does not resolve review threads.

AU does not approve.

AU does not merge.

AU does not release.

AU does not deploy.

AU does not continue workflow.

## AU1 - Eligible AT Package Required

AU requires a `ReadyForReviewEligibilityPackage` with:

```text
Verdict = EligibleForReadyExecutor
CanMarkReadyForReview = true
BlockReasons = []
Boundary.EvidenceOnly = true
Target.PullRequestState = open
Target.PullRequestDraft = true
Target.ExpectedHeadSha == Target.ObservedHeadSha
```

Blocked, incomplete, rejected, stale, or unbound AT packages cannot trigger the ready transition.

## AU2 - Pre-Mutation PR Observation

AU must re-observe current PR state immediately before mutation.

The observed PR must match repository, PR number, open state, draft state, head branch, head SHA, base branch, and base SHA where available.

If any observed value does not match the package and execution request, AU blocks before mutation.

## AU3 - Controlled Ready Transition

AU may perform exactly one mutation:

```text
mark expected draft PR ready for review
```

The implementation may use `gh pr ready` or an equivalent GitHub ready-for-review mutation.

It must not request reviewers, resolve review threads, approve, merge, release, deploy, tag, publish, promote memory, commit, push, mutate source, or continue workflow.

## AU4 - Post-Mutation Verification

After mutation, AU must re-observe the PR.

Success requires:

```text
PR state = open
PR draft = false
PR number unchanged
head branch unchanged
head SHA unchanged
base branch unchanged
base SHA unchanged where available
```

If the mutation is accepted but post-state verification fails, AU reports `Failed` with `PostReadyVerificationFailed`; it does not report success.

## AU5 - Execution Receipt

AU writes:

```text
ready-for-review-execution-receipt.json
ready-for-review-execution-receipt.md
```

The receipt records:

```text
ReadyTransitionAttempted
ReadyTransitionAccepted
PostStateVerified
ExecutionVerdict
FailureClassification
Issues
```

Attempted, accepted, and verified are separate facts.

## AU6 - Authority Bypass Tests

AU bypass tests prove the ready execution receipt cannot become reviewer request authority, approval authority, merge authority, release authority, deployment authority, memory-promotion authority, or continuation authority.

## CLI Boundary

Allowed commands:

```text
irondev ready execute
irondev ready execution-status
irondev ready execution-records
```

Forbidden authority-shaped commands:

```text
irondev ready execute-request-reviewers
irondev ready request-reviewers
irondev ready resolve-comments
irondev ready approve
irondev ready review
irondev ready merge
irondev ready auto-merge
irondev ready release
irondev ready deploy
irondev ready tag
irondev ready publish
irondev ready promote-memory
irondev ready continue
```

## Killjoy

Marking ready is not asking anyone to review it. It only stops hiding the draft behind a draft flag.
