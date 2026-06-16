# PR197 - Rollback Read API

## Summary

PR197 adds a read-only API for rollback-support receipts.

This PR exposes project-scoped GET-only endpoints for persisted RollbackSupportReceipt records. It lets reviewers and future gates inspect durable rollback-support evidence recorded by PR196.

## Boundary

Rollback read API is not rollback execution.
Rollback read API is not rollback success.
Rollback read API is not source apply.
Rollback read API is not workflow continuation.
Rollback read API is not release readiness.
Rollback read API does not authorize source mutation by itself.

A rollback-support receipt means rollback support was recorded for review/gating.
It does not mean rollback was performed.
It does not mean source apply is allowed.
Real source apply must still pass the source-apply gate before mutation.

## What this PR does

- Adds project-scoped GET-only endpoints for rollback-support receipts.
- Adds a read model and read-only query service backed by IRollbackSupportReceiptStore.
- Preserves evidence references, boundary maxims, and boundary text.
- Keeps RollbackGateSatisfied as recorded evidence only.
- Adds API tests for read path, project scoping, read-only routes, and authority-boundary backstops.

## What this PR does not do

- This PR does not create rollback-support receipts.
- This PR does not execute rollback.
- This PR does not prove rollback execution succeeded.
- This PR does not apply source.
- This PR does not mutate source.
- This PR does not create source-apply requests.
- This PR does not create source-apply receipts.
- This PR does not continue workflow.
- This PR does not approve release.
- This PR does not infer release readiness.
- This PR does not add CLI.
- This PR does not add UI.
- This PR does not add a runtime worker, scheduler, runner, git runner, process runner, agent dispatch, model call, tool execution, memory promotion, or retrieval activation path.

## Contract chain

accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate

PR197 opens the vault window. It does not hand out the keys.
