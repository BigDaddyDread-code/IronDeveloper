# AW Controlled Reviewer Request Executor

## Review Line

Block AW consumes an eligible AV reviewer request package, verifies current PR state, requests only the package-declared reviewers and teams, verifies the resulting reviewer-request state, and writes an execution receipt. It does not resolve review threads, approve, submit reviews, merge, release, deploy, tag, publish, promote memory, commit, push, mutate source, or continue workflow.

## Purpose

AW is the first Phase 2 block allowed to perform the reviewer-request mutation.

The mutation is narrow:

```text
request named reviewers and/or teams on the expected ready PR
```

That must not become:

```text
request reviewers + approve
request reviewers + resolve comments
request reviewers + merge readiness
request reviewers + workflow continuation
```

Reviewer request only means someone has been asked to look. It does not mean they looked, agreed, approved, or that the PR is mergeable.

## Core Rule

Reviewer request package is not reviewer request execution.

Reviewer request execution is not review.

Review request is not approval.

Approval is not merge.

Merge is not release.

Release is not deployment.

Validation evidence is not approval.

No self-approval.

No hidden mutation.

## Scope

AW may:

- read an AV reviewer request package
- verify the package is executor-eligible
- re-observe current PR metadata immediately before mutation
- verify PR identity
- verify PR is open
- verify PR is not draft
- verify head branch and head SHA
- verify base branch and base SHA where available
- verify requested reviewers and teams are exactly those in the package
- verify no package-declared target has become stale or already requested
- verify no package-declared reviewer is the requester
- verify no package-declared reviewer is the PR author
- request only the package-declared reviewers and teams
- re-observe reviewer-request state after mutation
- verify expected reviewers and teams are now requested
- write reviewer request execution receipt
- write governance event

AW must not:

- mark ready for review
- remove reviewers
- resolve review threads
- reply to review comments
- approve
- submit review
- merge
- enable auto-merge
- release
- deploy
- tag
- publish
- promote memory
- commit
- push
- mutate source
- mutate workspace except receipt/artifact output
- continue workflow

## Authority Boundary

AW performs one controlled mutation:

```text
CanRequestReviewers = true
```

Only under an eligible AV package and matching live PR state.

Everything else remains false:

```text
CanMarkReadyForReview = false
CanRemoveReviewers = false
CanResolveReviewThreads = false
CanReplyToReviewThreads = false
CanApprove = false
CanSubmitReview = false
CanMerge = false
CanAutoMerge = false
CanRelease = false
CanDeploy = false
CanTag = false
CanPublish = false
CanPromoteMemory = false
CanContinueWorkflow = false
CanCommit = false
CanPush = false
CanMutateSource = false
CanMutateWorkspace = false
```

AW execution receipt must not become approval, merge, release, deployment, memory-promotion, or continuation authority.

## Required Lines

Reviewer request package is not reviewer request execution.

Reviewer request execution is not approval.

Reviewer request execution is not review completion.

Reviewer request execution is not merge readiness.

Reviewer request execution is not release readiness.

Reviewer request execution does not resolve review threads.

Approval is not merge.

Merge is not release.

Release is not deployment.

Validation evidence is not approval.

No self-approval.

No hidden mutation.

AW requests only package-declared reviewers and teams.

AW does not approve.

AW does not merge.

AW does not release.

AW does not deploy.

AW does not continue workflow.

## Execution Receipt

AW writes:

- `reviewer-request-execution-receipt.json`
- `reviewer-request-execution-receipt.md`

The receipt records:

- `ReviewerRequestAttempted`
- `ReviewerRequestAccepted`
- `PostStateVerified`

These fields must stay distinct. AW must not report success unless post-state verification proves the expected reviewers and teams are requested on the expected PR/head/base.

## Event

AW records `ReviewerRequestExecuted` only when actual reviewer request mutation occurred and post-state verified.

For blocked or failed attempts, event text must not imply execution succeeded.

## Killjoy

AW knocks on the reviewer's door. It does not speak for the reviewer, approve the work, merge the PR, or pretend attention has already been given.
