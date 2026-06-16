# PR195 Rollback Gate Evaluator Receipt

## Purpose

PR195 adds a pure Core rollback gate evaluator.

The evaluator checks whether a validated rollback plan supports a supplied patch artifact before any later source-apply path is allowed to rely on rollback support.

This is rollback-support evidence only.

## Boundary

Rollback gate evaluation is not rollback execution.
Rollback gate evaluation is not rollback success.
Rollback gate evaluation is not source apply.
Rollback gate evaluation is not workflow continuation.
Rollback gate evaluation is not release readiness.
Rollback gate evaluation does not authorize source mutation by itself.
Rollback gate satisfied means only that rollback support exists for the patch artifact.
Real source apply must still pass the source-apply gate before mutation.

## What changed

- Added `RollbackGateEvaluationRequest`.
- Added `RollbackGateEvaluationResult`.
- Added `RollbackGateEvaluationIssue`.
- Added `RollbackGateBoundaryText`.
- Added `RollbackGateEvaluator.Evaluate`.
- Added focused rollback gate evaluator tests.

## What is validated

The evaluator checks:

- request shape
- patch artifact validity
- rollback plan validity
- project binding
- patch artifact id binding
- patch hash binding
- change-set hash binding
- dry-run request binding
- dry-run audit binding
- dry-run receipt binding
- policy satisfaction binding
- subject binding
- source snapshot binding
- source baseline binding
- workspace boundary binding
- expected branch binding
- expected clean worktree binding
- rollback coverage for each patch file change
- rollback action kind mapping
- rollback action hash mapping
- duplicate rollback actions
- extra non-Noop rollback actions
- private/raw material rejection
- authority-claim rejection

## Non-goals

PR195 does not:

- execute rollback
- run a rollback dry-run
- apply source
- mutate source
- read source files
- inspect a real worktree
- call git
- invoke tools
- dispatch agents
- continue workflow
- approve release
- satisfy policy
- create source-apply requests
- create source-apply receipts
- create rollback receipts
- promote memory
- activate retrieval
- add SQL
- add API
- add CLI
- add UI
- add runtime workers

## Review line

PR195 checks that the escape hatch exists. It does not pull the lever.
