# AV Controlled Reviewer Request Package

## Review Line

Block AV packages reviewer request intent for a ready PR with explicit reviewers, PR identity, current head evidence, AU ready-for-review execution evidence, and request constraints. It does not request reviewers, resolve review threads, approve, merge, release, deploy, tag, publish, promote memory, or continue workflow.

## Purpose

AV creates a reviewer request package that AW may later execute.

AU can mark a draft PR ready for review. That still does not mean reviewers have been requested.

AV must not request reviewers itself.

## Core Rule

Ready-for-review execution is not reviewer request.

Reviewer request package is not reviewer request execution.

Reviewer request is not approval.

Approval is not merge.

Merge is not release.

Release is not deployment.

Validation evidence is not approval.

No self-approval.

No hidden mutation.

## Phase 2 Flow

```text
AT - Ready-for-Review Separation
AU - Controlled Ready-for-Review Executor
AV - Controlled Reviewer Request Package
AW - Controlled Reviewer Request Executor
```

AV sits exactly between AU and AW:

```text
AU ready execution -> AV reviewer request package -> AW reviewer request execution
```

Not:

```text
AU ready execution -> AV package + request reviewers
```

## Scope

AV may:

- read AU ready-for-review execution receipts
- read current PR metadata supplied as observed evidence
- read reviewer selection input
- verify PR identity
- verify PR is open
- verify PR is no longer draft
- verify head branch and head SHA
- verify base branch and base SHA where available
- verify AU execution receipt is executed and post-state verified
- verify requested reviewers are explicit
- verify requested teams are explicit
- detect duplicate reviewers or teams
- detect already-requested reviewers or teams when provided
- block self-review and self-request patterns
- record reviewer rationale
- create a reviewer request package
- write a reviewer request package receipt
- write a governance event

AV must not:

- request reviewers
- remove reviewers
- resolve review threads
- reply to review comments
- approve
- submit a review
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

AV produces package evidence only.

The package may set `CanRequestReviewersForExecutor = true` only when AW may later consume the package.

The AV boundary itself remains evidence-only:

```text
EvidenceOnly = true
CanMarkReadyForReview = false
CanRequestReviewers = false
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

Core boundary:

```text
Reviewer request package is not reviewer request execution.
```

## Required Input Evidence

AV requires a `ReadyForReviewExecutionReceipt`.

The AU receipt must have:

- `ExecutionVerdict = Executed`
- `FailureClassification = None`
- `ReadyTransitionAttempted = true`
- `ReadyTransitionAccepted = true`
- `PostStateVerified = true`
- `PostState.PullRequestState = open`
- `PostState.PullRequestDraft = false`
- `PostState.HeadSha == ExpectedHeadSha`

AV rejects AU receipts that are missing, invalid, blocked, failed, incomplete, not post-state verified, not tied to the target PR, not tied to the expected head branch, not tied to the expected head SHA, or not tied to the expected base branch/base SHA where available.

## Current PR State

AV requires observed current PR state and verifies:

- PR state is open
- PR draft is false
- PR number matches
- repository matches
- head branch matches
- head SHA matches
- base branch matches
- base SHA matches when provided

If any mismatch exists:

```text
PackageVerdict = PackageBlocked
CanRequestReviewersForExecutor = false
```

No stale ready execution.

No "ready probably still applies."

## Reviewer Selection

AV requires at least one explicit reviewer or team and a request rationale.

AV blocks:

- empty reviewer and team target lists
- missing rationale
- invalid reviewer login
- invalid team slug
- duplicate reviewer target
- duplicate team target
- requester requesting themselves
- PR author requested as reviewer

Already-requested reviewers and teams are recorded honestly as already satisfied and are not included as new request targets.

If every supplied target is already requested, the package is not ready for AW.

## Output Artifacts

AV writes:

- `reviewer-request-package.json`
- `reviewer-request-package-receipt.json`
- `reviewer-request-summary.md`
- `reviewer-request-targets.jsonl`

The receipt says:

Reviewer request package is not reviewer request execution.

Reviewer request is not approval.

Approval is not merge.

Merge is not release.

Release is not deployment.

Validation evidence is not approval.

No self-approval.

No hidden mutation.

## Event

AV records `ReviewerRequestPackageCreated`.

This event records package creation only.

It must not imply reviewer request execution.

## Killjoy

Ready for review is not asking anyone to review it. A reviewer request package is only a sealed envelope, not the knock on the reviewer's door.
