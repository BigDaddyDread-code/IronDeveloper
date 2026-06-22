# BY Controlled Draft Pull Request Executor

## Purpose

This PR adds controlled draft PR creation/update.

It can create or update exactly one draft PR only after authority and remote-state checks pass.

## Boundary

Push authority is not PR authority.

Push receipt is not PR authority.

Commit authority is not PR authority.

Commit receipt is not PR authority.

Source apply authority is not PR authority.

Patch package evidence is not PR authority.

Draft PR is not ready-for-review authority.

Draft PR receipt is not ready-for-review authority.

PR URL is not release candidate ref.

PR URL is not release readiness.

PR URL is not deployment authority.

It does not mark ready for review.

It does not request reviewers.

It does not merge.

It does not release.

It does not deploy.

It does not promote memory.

It does not continue workflow.

It does not create approvals.

It does not satisfy policy.

## Execution Shape

The executor validates the request envelope, controlled push receipt, explicit draft PR authority, and draft PR text package before observing remote PR state.

The pre-mutation observation must prove the repository is reachable, the head branch exists, the base branch exists, the observed head commit matches, and any existing PR number is the same draft PR requested for update.

The gateway request always sets draft-only execution and disables ready-for-review, reviewer requests, and merge.

Completion requires a controlled draft PR receipt and post-mutation observation proving the PR is still draft and still bound to the same repository, head branch, base branch, and head commit.

## Receipt Boundary

A controlled draft PR receipt explains draft PR container mutation. It does not authorize ready-for-review, reviewer request, merge, release, deployment, memory promotion, policy satisfaction, or continuation.

## Review Line

Push authority is not PR authority.

## Killjoy

A pushed branch can be reviewed. It cannot create its own review authority.
