# PR202 Source Apply Dry-run Receipt Store

Review line: PR202 files the rehearsal report. It does not claim the launch happened.

## Purpose

PR202 adds durable SQL storage for SourceApplyDryRunReceipt records.

This is a dry-run receipt store only. A stored receipt records the result of a controlled source-apply dry-run evaluation, including file-level dry-run outcomes, evidence references, gate references, patch artifact references, rollback support references, and boundary maxims.

## Boundary

The receipt is evidence only.

It does not perform source apply, mutate source, write files, apply patches, inspect git branches, inspect worktrees, run git commands, execute processes, continue workflow, satisfy approval, satisfy policy, approve release, activate retrieval, promote memory, dispatch agents, call models, expose API, expose CLI, or create UI behavior.

DryRunSatisfied = true means the recorded dry-run preconditions were satisfied for the supplied evidence. It is not source apply permission.

DryRunSatisfied = false is still a valid receipt. It records why the rehearsal did not satisfy preconditions.

## What changed

- Added SourceApplyDryRunReceipt and SourceApplyDryRunReceiptFileResult Core models.
- Added SourceApplyDryRunReceiptValidation.
- Added ISourceApplyDryRunReceiptStore with save/read/list methods only.
- Added SqlSourceApplyDryRunReceiptStore backed by stored procedures.
- Added governance.SourceApplyDryRunReceipt SQL migration, append-only triggers, insert validation trigger, stored procedures, indexes, and runtime role protections.
- Updated migration manifest, SQL inventory, and migration verifier.
- Added focused store, validation, SQL, and static boundary tests.

## Preserved invariants

- Dry-run receipt is not source apply.
- Dry-run receipt is not patch application.
- Dry-run receipt is not approval.
- Dry-run receipt is not policy satisfaction.
- Dry-run receipt is not workflow continuation.
- Dry-run receipt is not release readiness.
- Dry-run receipt is not rollback execution.
- Dry-run receipt is not memory promotion.
- SQL remains source of truth for durable receipts.

## Validation target

Focused validation should include SourceApplyDryRunReceiptStore, SourceApplyDryRunReceiptValidation, SourceApplyDryRunExecutor, SourceApplyRequestValidation, SourceApplyGateEvaluator, RollbackRegression, SourceApplyThreatBoundary, PatchArtifactRegression, RollbackSupportReceiptStore, neighboring read API bands, build, and diff-check.
