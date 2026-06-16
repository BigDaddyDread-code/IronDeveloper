# PR203 - Source Apply Dry-run Receipt Read API

PR203 adds a read-only API for source-apply dry-run receipts.

This PR exposes project-scoped GET-only endpoints for persisted `SourceApplyDryRunReceipt` records.

## Boundary

This PR does not create source-apply dry-run receipts.

This PR does not perform dry-runs.

This PR does not apply source.

This PR does not mutate source.

This PR does not write files.

This PR does not apply patches.

This PR does not call git.

This PR does not inspect worktrees.

This PR does not execute rollback.

This PR does not continue workflow.

This PR does not approve release.

This PR does not satisfy approval.

This PR does not satisfy policy.

This PR does not add CLI.

This PR does not add UI.

This PR does not add agents, models, tools, schedulers, workers, or runtime dispatch.

## Read model meaning

Source Apply Dry-run Receipt Read API is not source apply.

Source Apply Dry-run Receipt Read API is not dry-run execution.

Source Apply Dry-run Receipt Read API is not file mutation.

Source Apply Dry-run Receipt Read API is not patch application.

Source Apply Dry-run Receipt Read API is not workflow continuation.

Source Apply Dry-run Receipt Read API is not release readiness.

Source Apply Dry-run Receipt Read API does not authorize source mutation by itself.

A source-apply dry-run receipt means rehearsal evidence was recorded for review/gating.

It does not mean the dry-run was performed by this API.

It does not mean source apply is allowed.

Real source apply still requires accepted approval, policy satisfaction, source-apply gate success, and human review.

## Exposed routes

- `GET /api/v1/projects/{projectId}/source-apply-dry-run-receipts/{sourceApplyDryRunReceiptId}`
- `GET /api/v1/projects/{projectId}/source-apply-dry-run-receipts/by-hash/{sourceApplyDryRunReceiptHash}`
- `GET /api/v1/projects/{projectId}/source-apply-dry-run-receipts/by-source-apply-request/{sourceApplyRequestId}`
- `GET /api/v1/projects/{projectId}/source-apply-dry-run-receipts/by-source-apply-gate/{sourceApplyGateEvaluationId}`
- `GET /api/v1/projects/{projectId}/source-apply-dry-run-receipts/by-patch-artifact/{patchArtifactId}`
- `GET /api/v1/projects/{projectId}/source-apply-dry-run-receipts/by-rollback-support/{rollbackSupportReceiptId}`

## Position in source-apply path

`accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> source-apply gate -> source-apply dry-run receipt -> controlled source apply -> source-apply receipt -> rollback -> workflow continuation -> release readiness gate`

PR203 opens the rehearsal archive. It does not hand out launch codes.
