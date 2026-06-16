# PR194 - Rollback Plan Contract

PR194 adds the Rollback Plan contract.

This PR is contract/test/receipt only.
This PR defines rollback plan shape.
This PR does not execute rollback.
This PR does not apply source.
This PR does not mutate source.
This PR does not create source-apply requests.
This PR does not create source-apply receipts.
This PR does not continue workflow.
This PR does not approve release.
This PR does not add SQL.
This PR does not add API.
This PR does not add CLI.
This PR does not add UI.

## Boundary

Rollback plan is not rollback execution.
Rollback plan is not rollback success.
Rollback plan is not source apply.
Rollback plan is not workflow continuation.
Rollback plan is not release readiness.
Rollback plan does not authorize source mutation by itself.
Rollback plan defines the intended escape hatch only.
Real source apply must require rollback support before mutation.

Rollback plan is a proposed recovery plan prepared before source apply.
Rollback execution is a future governed mutation that actually restores or reverses source changes.
Rollback receipt is future durable evidence that rollback execution happened.
Source apply is future governed mutation of source.

## Binding

Rollback plan binds patch artifact, patch hash, change-set hash, dry-run receipt hash, dry-run audit hash, policy satisfaction hash, subject hash, source baseline hash, workspace boundary hash, expected branch, expected clean worktree hash, rollback plan hash, file actions, evidence references, and boundary maxims.

Rollback plan hash is required by this contract. Deterministic rollback plan hash generation may be added by a later PR.

## Authority chain

accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate

PR194 stops at rollback plan contract definition.

## Next target

The next Block U target is Rollback Gate Evaluator.

Suggested next PR: PR195 - Rollback Gate Evaluator

## Review line

PR194 defines the escape hatch. It does not open it.
