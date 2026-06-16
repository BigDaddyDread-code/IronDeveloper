# PR200 - Source Apply Request Contract

## Summary

PR200 adds the Core source apply request contract and validation only.

The request records a proposed future controlled source-apply request and binds it to a satisfied source-apply gate evaluation, accepted approval, policy satisfaction, dry-run evidence, patch artifact, rollback support receipt, source baseline, workspace boundary, expected branch, and expected clean worktree hash.

## Boundary

PR200 does not add source apply.
PR200 does not mutate source.
PR200 does not execute requests.
PR200 does not create source-apply receipts.
PR200 does not write files.
PR200 does not apply patches.
PR200 does not inspect branch.
PR200 does not inspect worktree.
PR200 does not call git.
PR200 does not run processes.
PR200 does not execute rollback.
PR200 does not continue workflow.
PR200 does not approve release.
PR200 does not infer release readiness.
PR200 does not add SQL.
PR200 does not add API.
PR200 does not add CLI.
PR200 does not add UI.
PR200 does not add runtime execution.
PR200 does not call agents, models, or tools.
PR200 does not promote memory.
PR200 does not activate retrieval.

SourceApplyRequest is not source apply.
SourceApplyRequest is not source mutation.
SourceApplyRequest is not executor approval.
SourceApplyRequest is not workflow continuation.
SourceApplyRequest is not release readiness.
SourceApplyRequest only records a proposed request to apply a patch artifact later under controlled execution.

## Gate relationship

PR200 requires `SourceApplyGateSatisfied = true`.

That flag is only a prerequisite for a valid request shape. It is not source apply, source mutation, executor approval, workflow continuation, release readiness, git execution, file writing, patch application, rollback execution, or release approval.

## Review line

PR200 writes the launch request form. It does not launch.
