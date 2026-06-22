# CA Controlled Rollback Executor

## Summary

This PR adds controlled rollback execution.

Rollback is source mutation.

Rollback does not get a free pass because it sounds safer.

## Boundary

Rollback plan is not rollback execution.

Rollback eligibility is not rollback execution.

Rollback executes only under explicit rollback authority or a narrow policy-approved rollback path.

Source-apply receipt evidence must exist, match the rollback request, be a source-apply receipt, and be accepted for rollback.

Rollback verifies source apply receipt, rollback target, expected files, pre-state, receipt, and post-state.

Partial rollback risk blocks rollback.

Partial rollback failure is not successful rollback.

Dirty worktree blocks rollback.

Post-state mismatch fails rollback execution.

Rollback receipt is not commit authority.

Rollback receipt is not push authority.

Rollback receipt is not PR authority.

Rollback receipt is not merge, release, deploy, or workflow authority.

## Non-Authority

This PR does not commit.

It does not push.

It does not create or update PRs.

It does not merge.

It does not release.

It does not deploy.

It does not promote memory.

It does not continue workflow.

## Killjoy

Undo is still a hand on the source tree. Treat it like a loaded tool.
