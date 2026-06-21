# PR21 Repo State Freshness Guard

## Purpose

PR21 adds a read-only repository state freshness guard for governed mutation steps.

Stale evidence cannot authorize current mutation.

Yesterday's clean state is not today's authority.

## Handled Cases

- Dirty worktree.
- Unknown worktree.
- Base branch moved.
- Head branch moved.
- Patch no longer applies.
- Patch applicability unknown.
- Commit head changed.
- Remote branch or remote SHA changed.
- Expired validation.
- Validation base mismatch.
- Validation head mismatch.
- Validation patch hash mismatch.
- Multiple failures in the same evaluation.

## Authority Boundary

The freshness guard is read-only.

It may compare supplied expected evidence with supplied observed repository state.

It may explain stale or dirty state.

It may block mutation.

It may list next safe actions.

It must not perform the next action.

## Non-Authority

Clean worktree evidence is not durable mutation authority.

Old base SHA is not current base SHA.

Old head SHA is not current head SHA.

Patch applied yesterday does not prove patch applies today.

Validation passed against old state is not validation against current state.

Commit package is not commit authority.

Commit receipt is not push authority.

Push receipt is not pull request authority.

Freshness is not approval.

Freshness is not policy satisfaction.

Freshness is not dry-run.

Freshness is not source apply.

Freshness is not rollback.

Freshness is not commit.

Freshness is not push.

Freshness is not pull request creation.

Freshness guard is not evidence refresh.

Freshness guard is not revalidation.

Freshness guard is not workflow continuation.

## Explicit Non-Mutation Proof

The freshness boundary allows only:

- CanExplainFreshness
- CanInspectEvidence

The freshness boundary does not allow:

- refresh evidence
- revalidate
- regenerate patch
- apply source
- rollback source
- commit
- push
- create pull request
- continue workflow
- promote memory
- satisfy policy
- accept approval

This PR adds no executor, provider gateway, direct git execution, source mutation, rollback execution, commit, push, pull request creation, merge, release, deployment, memory promotion, workflow continuation, frontend behavior, or approval/policy acceptance.

## Validation

- Focused PR21: 32/32.
- PR20 interrupted recovery focused lane: 31/31.
- CA rollback executor focused lane: 16/16.
- BJ through PR21 authority corridor: 505/505.
- Build: 0 errors / 4 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed with normal LF/CRLF warnings.
- `git diff --check HEAD~1 HEAD`: passed.

## Killjoy

Yesterday's clean state is not today's authority.
