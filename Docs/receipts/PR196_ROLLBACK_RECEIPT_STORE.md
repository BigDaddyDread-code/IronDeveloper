# PR196 Rollback Receipt Store

## Purpose

PR196 adds the Rollback Receipt Store.

This PR stores rollback-support receipts.

This PR is data/store/test/receipt only.

## Boundary

This PR does not execute rollback.
This PR does not prove rollback execution succeeded.
This PR does not apply source.
This PR does not mutate source.
This PR does not inspect branch.
This PR does not inspect worktree.
This PR does not run git.
This PR does not create source-apply requests.
This PR does not create source-apply receipts.
This PR does not continue workflow.
This PR does not approve release.
This PR does not add API.
This PR does not add CLI.
This PR does not add UI.

Rollback support receipt is not rollback execution.
Rollback support receipt is not rollback success.
Rollback support receipt is not source apply.
Rollback support receipt is not workflow continuation.
Rollback support receipt is not release readiness.
Rollback support receipt does not authorize source mutation by itself.
Rollback support receipt records that rollback support existed for a patch artifact.
Real source apply must still pass the source-apply gate before mutation.

## Stored evidence

Rollback support receipt binds rollback plan hash, rollback gate evaluation hash, patch artifact id, patch hash, change-set hash, dry-run audit hash, dry-run receipt hash, policy satisfaction hash, subject hash, source snapshot reference, source baseline hash, workspace boundary hash, expected branch, expected clean worktree hash, receipt hash, evidence references, and boundary maxims.

## Authority chain

accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate

PR196 adds durable rollback-support evidence after rollback gate evaluation and before source apply.

## Next target

The next Block U target is Rollback Read API.

Suggested next PR:

PR197 - Rollback Read API

## Review line

PR196 puts the escape-hatch certificate in the vault. It does not pull the lever.
