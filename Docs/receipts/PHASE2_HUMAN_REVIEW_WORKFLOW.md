# Phase 2 Human Review Workflow

## Review Line

Phase 2 moves a PR from internal draft-readiness evidence to controlled reviewer-request execution. It does not approve, submit reviews, resolve review threads, merge, release, deploy, tag, publish, promote memory, commit, push, mutate source, or continue workflow.

## Boundary

Phase 2 moves human-review workflow through four controlled authority slices:

- AT packages ready-for-review eligibility evidence.
- AU executes the ready-for-review transition for the expected draft PR.
- AV packages reviewer request intent for a ready PR.
- AW executes reviewer requests for only the package-declared reviewers and teams.

Authority chain:

```text
Ready-for-review package is not ready-for-review execution.
Ready-for-review execution is not reviewer request.
Reviewer request package is not reviewer request execution.
Reviewer request execution is not review completion.
Reviewer request execution is not approval.
Reviewer request execution is not merge readiness.
Reviewer request execution is not release readiness.
Reviewer request execution is not deployment readiness.
Approval is not merge.
Merge is not release.
Release is not deployment.
Validation evidence is not approval.
No self-approval.
No hidden mutation.
```

## Phase Map

### AT - Ready-for-Review Separation

AT packages ready-for-review eligibility evidence for an expected draft PR, expected head, expected base, validation posture, and phase authority posture.

AT evidence cannot mark a PR ready, request reviewers, resolve review threads, approve, submit reviews, merge, release, deploy, tag, publish, promote memory, mutate source, commit, push, or continue workflow.

### AU - Controlled Ready-for-Review Executor

AU consumes an eligible AT package, verifies current PR state, marks only the expected draft PR ready for review, verifies post-state, and writes an execution receipt.

AU execution cannot request reviewers, resolve review threads, approve, submit reviews, merge, release, deploy, tag, publish, promote memory, mutate source, commit, push, or continue workflow.

### AV - Controlled Reviewer Request Package

AV packages reviewer request intent for a ready PR using AU execution evidence, current PR state, requested reviewer/team targets, self-review checks, author-review checks, and already-requested checks.

AV evidence cannot request reviewers, remove reviewers, resolve review threads, reply to review threads, approve, submit reviews, merge, release, deploy, tag, publish, promote memory, mutate source, commit, push, or continue workflow.

### AW - Controlled Reviewer Request Executor

AW consumes an eligible AV package, re-observes current PR state, requests only the package-declared reviewers and teams, re-observes post-state, and writes an execution receipt.

AW execution cannot mark ready for review, remove reviewers, resolve review threads, reply to review threads, approve, submit reviews, merge, release, deploy, tag, publish, promote memory, mutate source, commit, push, or continue workflow.

## Review Traps

Reject Phase 2 if:

- AT package can mark a PR ready
- AU ready execution can request reviewers
- AV package can request reviewers
- AW reviewer request execution becomes review completion
- AW reviewer request execution becomes approval
- AW reviewer request execution becomes merge readiness
- AW reviewer request execution becomes release readiness
- AW reviewer request execution becomes deployment readiness
- any phase artifact grants approval, merge, release, deploy, memory promotion, or continuation authority
- validation evidence is treated as approval
- self-review is allowed
- already-requested reviewers are treated as new request work
- hidden mutation appears outside the declared executor boundaries

## Validation

The phase boundary is covered by `Phase2HumanReviewWorkflowAuthorityTests`.

The combined AT/AU/AV/AW boundary lane remains useful, but it is not a substitute for the explicit phase cross-boundary authority lane.

## Killjoy

Human review workflow asks humans to review. It does not impersonate their review, approve the work, merge the PR, ship the release, or continue workflow.
