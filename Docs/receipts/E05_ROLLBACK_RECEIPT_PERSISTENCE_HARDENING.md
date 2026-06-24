# E05 - Rollback Receipt Persistence Hardening

## Review Line

A rollback receipt is a witness. It is not rollback authority.

## Purpose

E05 hardens rollback receipt persistence as bounded reference-only witness metadata.

Rollback receipt persistence records bounded reference-only rollback receipt metadata as a durable witness. It does not execute rollback, plan rollback, approve work, satisfy policy, validate freshness, grant authority, retry, recover, resume, merge, release, deploy, promote memory, or continue workflow.

## Stack

Base: `main`

Branch: `rollback/rollback-receipt-persistence-hardening`

This follows E01, E02, E03, and E04 receipt persistence hardening.

## Files Changed

- `IronDev.Core/Governance/RollbackReceiptPersistenceModels.cs`
- `IronDev.Core/Governance/RollbackReceiptPersistenceValidator.cs`
- `IronDev.Core/Governance/IRollbackReceiptPersistenceStore.cs`
- `IronDev.Core/Governance/RollbackReceiptPersistenceService.cs`
- `IronDev.IntegrationTests/BlockE05RollbackReceiptPersistenceHardeningTests.cs`
- `Docs/receipts/E05_ROLLBACK_RECEIPT_PERSISTENCE_HARDENING.md`

## Persistence Behavior

E05 persists only safe rollback receipt metadata and references:

- tenant, project, operation, and correlation scope
- rollback receipt and rollback attempt IDs
- rollback plan, result, and target references
- target-kind specific receipt references
- commit SHA metadata when required by the target kind
- repository, branch, PR, worktree, and validation references
- outcome, timestamp, source, redaction, and deterministic fingerprint metadata

E05 does not persist raw rollback plans, commands, inverse patches, patches, diffs, source content, commit messages, push output, Git output, GitHub output, PR text, validation logs, receipt payloads, evidence payloads, prompt text, private reasoning, command text, transcripts, secrets, tokens, or connection strings.

## Idempotency And Conflict Behavior

- First valid persist returns `Persisted`.
- Repeating the same receipt with the same deterministic fingerprint returns `AlreadyPersisted`.
- Reusing a receipt ID with a different fingerprint returns `Conflict`.
- Reusing a rollback attempt ID with a conflicting terminal outcome returns `Conflict`.
- Reusing a rollback target ref under a conflicting tenant/project/operation/correlation scope returns `Conflict`.
- Reusing a rollback result ref under a conflicting tenant/project/operation/correlation scope returns `Conflict`.

E05 does not overwrite, delete, compact, or update terminal rollback receipt records in place.

## Boundary

Rollback receipt persistence is reference-only durable witness storage.

It is not:

- rollback execution
- rollback planning
- rollback approval
- retry, recovery, or resume authority
- workflow continuation
- source apply, commit, push, PR creation, ready-for-review, reviewer request, merge, release, or deploy authority
- approval or policy satisfaction
- validation freshness, patch freshness, source-state proof, execution proof, or source-safety proof
- merge readiness, release readiness, or deployment readiness

Persisted rollback receipt is not retry, recovery, resume, source-safety, merge, release, deployment, or workflow continuation authority.

## Validation

Local validation:

- E05 focused: 114/114 passed
- E04 focused: 80/80 passed
- E03 focused: 68/68 passed
- E02 focused: 61/61 passed
- E01 focused: 48/48 passed
- D10 + D16 focused: 194/194 passed
- D01-D20 resolver/read-model lane: 1439/1439 passed
- A02 + A05 corridor: 61/61 passed
- Governance/status corridor: 1871/1871 passed
- Governance boundary CI script: passed
  - B-series profile boundary tests: 133/133 passed
  - BQ-BU compatibility boundary tests: 80/80 passed
  - Security boundary tests: 66/66 passed
  - API boundary tests: 38/38 passed
  - CLI boundary tests: 41/41 passed
- SQL integration: not run; SQL files were not touched
- Targeted E05 secret-shaped text scan: passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

GitHub Actions:

- GitHub Actions results are tracked on the PR for the current head.
- Repository receipt text does not grant authority from CI and does not need transient run IDs to be treated as proof.

CI and validation are evidence only. They are not approval, policy satisfaction, merge readiness, release readiness, deployment readiness, execution permission, or workflow continuation.
