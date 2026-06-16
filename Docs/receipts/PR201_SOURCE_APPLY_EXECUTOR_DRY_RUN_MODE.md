# PR201 Source Apply Executor Dry-run Mode

PR201 adds source-apply executor dry-run mode only.

PR201 rehearses the launch sequence. It does not ignite the engines.

## Boundary

PR201 does not add real source apply.
PR201 does not mutate source.
PR201 does not write files.
PR201 does not apply patches.
PR201 does not inspect branch.
PR201 does not inspect worktree.
PR201 does not call git.
PR201 does not run processes.
PR201 does not create source-apply receipts.
PR201 does not add SQL.
PR201 does not add API.
PR201 does not add CLI.
PR201 does not add UI.
PR201 does not add runtime execution.
PR201 does not execute rollback.
PR201 does not continue workflow.
PR201 does not approve release.
PR201 does not infer release readiness.
PR201 does not call agents, models, or tools.
PR201 does not promote memory.
PR201 does not activate retrieval.

## Dry-run meaning

Source apply dry-run is not source apply.
Source apply dry-run is not source mutation.
Source apply dry-run is not patch application.
Source apply dry-run is not git execution.
Source apply dry-run is not workflow continuation.
Source apply dry-run is not release readiness.
Source apply dry-run only proves what the controlled source-apply executor would attempt later if separately authorized.

## Evidence contract

The dry-run executor consumes a valid SourceApplyRequest.
It preserves the source apply request hash, source apply gate evaluation, patch artifact evidence, rollback support evidence, source baseline hash, workspace boundary hash, expected branch, and expected clean worktree hash.

If workspace state is checked, the state must be supplied through SourceApplyDryRunWorkspaceSnapshot.
The snapshot is evidence input only. It is not real branch inspection, real worktree inspection, file reading, git execution, or process execution.

## Review line

PR201 rehearses the launch sequence. It does not ignite the engines.
