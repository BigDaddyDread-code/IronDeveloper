# BZ Rollback Status Mapping

## Summary

This PR adds rollback status mapping only.

It maps rollback availability, rollback plan, rollback authority, rollback request, source apply receipt, worktree, and post-state evidence into canonical `GovernedOperationStatus`.

## Boundary

Rollback plan is not rollback execution.

Rollback request accepted is not rollback execution.

Rollback availability is not rollback authority.

Rollback status is not source mutation.

This PR does not execute rollback.

It does not mutate source.

It does not run git.

It does not commit.

It does not push.

It does not create or update PRs.

It does not merge.

It does not release.

It does not deploy.

It does not continue workflow.

## Status Rules

Wrong apply receipt blocks rollback.

Partial rollback risk blocks rollback.

Dirty worktree blocks rollback.

Post-state mismatch fails rollback status.

Eligible rollback status is not rollback execution.

Completed rollback status is not invented by this slice because no rollback execution receipt exists here.

## Stability Rule

Do not call the system stable until rollback is boring.
