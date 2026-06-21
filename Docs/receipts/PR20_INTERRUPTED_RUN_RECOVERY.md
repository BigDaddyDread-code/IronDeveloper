# PR20 Interrupted Run Recovery

## Purpose

PR20 adds backend interrupted-run recovery diagnosis.

A half-run must become an explainable state, not a mystery.

Recovery diagnosis explains the wreckage. It does not drive the tow truck.

## Handled States

- Workspace created but no patch.
- Patch created but no validation.
- Validation failed.
- Source apply started but not completed.
- Commit package created but no commit.
- Commit created but no push.
- Push completed but no pull request.
- Contradictory evidence, including non-passing validation followed by downstream evidence.
- Incomplete downstream receipt evidence, including commit receipt without commit hash, push receipt without remote branch evidence, and draft pull request receipt without completed push evidence.

## Authority Boundary

Recovery diagnosis is read-only.

It may inspect known evidence and receipts.

It may classify the interrupted state.

It may explain missing evidence.

It may recommend next safe actions.

It must not execute the next action.

## Non-Authority

Workspace created is not patch created.

Patch created is not validation.

Validation failed is not approval.

Failed, inconclusive, or unknown validation followed by downstream evidence is chain contamination, not recovery state.

Apply started is not apply completed.

Apply completed is not commit authority.

Commit package is not commit.

Commit is not push.

Push is not pull request.

Pull request creation is not merge readiness.

Recovery diagnosis is not workflow continuation.

Recovery diagnosis is not rollback execution.

Recovery diagnosis is not authority refresh.

## Explicit Non-Mutation Proof

The recovery boundary allows only:

- CanExplainState
- CanInspectEvidence

The recovery boundary does not allow:

- resume run
- retry step
- apply source
- rollback source
- create commit
- push
- create pull request
- continue workflow
- promote memory
- satisfy policy
- accept approval

This PR adds no executor, provider gateway, source mutation, rollback execution, commit, push, PR creation, merge, release, deployment, memory promotion, workflow continuation, frontend behavior, or approval/policy acceptance.

## Validation

- Focused PR20: 31/31.
- CA rollback executor focused lane: 16/16.
- BJ through CA plus PR20 boundary corridor: 473/473.
- Build: 0 errors / 4 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed with normal LF/CRLF warnings.
- `git diff --check HEAD~1 HEAD`: passed.

## Killjoy

Recovery diagnosis explains the wreckage. It does not drive the tow truck.
