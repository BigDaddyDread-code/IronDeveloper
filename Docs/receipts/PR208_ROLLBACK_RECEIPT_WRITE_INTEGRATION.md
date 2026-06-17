# PR208 - Rollback Receipt Write Integration

PR208 adds rollback receipt write integration coverage.

PR208 proves ControlledRollbackExecutor can persist RollbackExecutionReceipt through SqlRollbackExecutionReceiptStore.

PR208 does not add rollback behaviour.
PR208 does not add rollback action kinds.
PR208 does not add API.
PR208 does not add CLI.
PR208 does not add UI.
PR208 does not add runtime execution.
PR208 does not continue workflow.
PR208 does not approve release.
PR208 does not infer release readiness.
PR208 does not declare rollback cleanup.
PR208 does not expand source apply.
PR208 does not call agents, models, or tools.
PR208 does not promote memory.
PR208 does not activate retrieval.
PR208 does not git commit.
PR208 does not git push.
PR208 does not merge.

Rollback receipt write integration is persistence proof only.
A persisted RollbackExecutionReceipt is mutation evidence.
A persisted RollbackExecutionReceipt is not workflow permission.
A persisted RollbackExecutionReceipt is not release readiness.
A persisted RollbackExecutionReceipt is not proof that the crash is cleaned up.
Human review remains required.

## Evidence covered

- Successful controlled rollback writes one durable receipt through the real SQL store.
- Rejected preflight writes no receipt.
- Partial rollback writes a durable partial receipt with the full preflight file plan.
- Persisted receipts roundtrip ids, hashes, mutation flags, file result hashes, issue codes, evidence references, boundary maxims, and boundary text.
- Persisted receipts contain hashes/results, not rollback source content or raw/private material.
- The SQL table remains append-only and rejects raw/private material plus authority claims.
- No API, CLI, UI, runtime, workflow continuation, release approval, git, agent/model/tool, memory promotion, or retrieval activation surface is added.

## Review line

PR208 proves the emergency-brake receipt reaches the vault. It does not pull the brake again.
