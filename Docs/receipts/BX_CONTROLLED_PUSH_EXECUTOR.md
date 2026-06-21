# BX Controlled Push Executor

## Purpose

Block BX adds a controlled push executor.

It can push exactly one expected commit to one expected remote branch only after push authority, commit receipt, pre-push remote state, receipt validation, and post-push observation checks pass.

## Boundary

Commit authority is not push authority.

Commit receipt is not push authority.

Commit package is not push authority.

Source apply authority is not push authority.

Patch proposal and patch package evidence are not push authority.

Validation evidence and clean diff evidence are not push authority.

It does not force push.

It does not push tags.

It does not create branches.

It does not create PRs.

It does not mark ready for review.

It does not request reviewers.

It does not merge.

It does not release.

It does not deploy.

It does not promote memory.

It does not continue workflow.

It does not create approvals.

It does not issue or store grants.

It does not satisfy policy.

It does not run validation.

## Execution Shape

The executor validates the request envelope, controlled commit receipt, and push authority before observing remote state.

The pre-push observation must prove the remote is reachable, the remote name and URL match, the remote branch matches, the remote head is still the expected head, the local head is the expected commit, exactly one expected local commit is unpushed, and there are no local uncommitted files.

The executor then calls only `IControlledPushGateway` with force push and tag push disabled.

After the gateway returns, the executor validates the controlled push receipt and then observes post-push remote state. Completion requires the observed remote head to equal the pushed commit and no remaining unpushed commits.

## Receipt Boundary

Push receipt is not PR, merge, release, deployment, or workflow authority.

A controlled push receipt explains remote mutation. It does not authorize review, merge, release, deployment, memory promotion, policy satisfaction, or continuation.

Ready-for-review and reviewer-request transitions are not represented as executable receipt outcomes because BX has no gateway request fields or execution path for them. They remain forbidden status actions.

## Review Line

Commit authority is not push authority.

## Killjoy

A commit receipt proves a commit exists. It does not prove it may leave the machine.
