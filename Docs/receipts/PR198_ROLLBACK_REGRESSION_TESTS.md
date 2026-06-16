# PR198 - Rollback Regression Tests

## Summary

PR198 adds regression tests only.

This PR locks the rollback-support boundary across PR194, PR195, PR196, and PR197. It proves the rollback plan, rollback gate, rollback support receipt store, and rollback read API remain evidence-only.

## Boundary

Rollback plan is not rollback execution.
Rollback gate satisfaction is not rollback execution.
Rollback support receipt is not rollback execution.
Rollback read API is not rollback execution.
Rollback support evidence is not source apply authority.

RollbackPlan describes how rollback could be performed later.
RollbackPlan does not perform rollback.

Rollback gate satisfaction proves support coverage.
It does not prove rollback execution.

RollbackSupportReceipt proves rollback support existed.
It does not prove rollback was executed.

Rollback read API can display evidence.
It cannot grant authority.

## What this PR does

- Adds regression tests for PR194 rollback plan contract boundaries.
- Adds regression tests for PR195 rollback gate evaluator boundaries.
- Adds regression tests for PR196 rollback support receipt store boundaries.
- Adds regression tests for PR197 rollback read API boundaries.
- Adds cross-layer static backstops proving rollback support does not execute rollback, apply source, continue workflow, approve release, dispatch agents, call models, invoke tools, promote memory, activate retrieval, call git, run processes, or inspect worktrees.

## What this PR does not do

- PR198 does not add production code.
- PR198 does not add SQL.
- PR198 does not execute rollback.
- PR198 does not prove rollback execution succeeded.
- PR198 does not apply source.
- PR198 does not mutate source.
- PR198 does not create source-apply requests.
- PR198 does not create source-apply receipts.
- PR198 does not continue workflow.
- PR198 does not approve release.
- PR198 does not infer release readiness.
- PR198 does not add API.
- PR198 does not add CLI.
- PR198 does not add UI.
- PR198 does not add runtime execution.
- PR198 does not add a rollback executor, rollback execution audit, rollback execution receipt, rollback success marker, source apply executor, source apply decision, runtime worker, scheduler, process runner, git runner, agent dispatch, model call, tool execution, memory promotion, or retrieval activation.

## Contract chain

accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate

PR198 locks the escape-hatch paperwork. It does not build the parachute.
