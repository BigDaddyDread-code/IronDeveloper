# D15 Rollback Recovery Read Model

## Purpose

D15 adds a read-only rollback/recovery read model for governed operations.

The model consumes supplied rollback/recovery material observations and an optional supplied diagnostic snapshot. It explains rollback/recovery context such as missing plans, plan-without-evidence, evidence-without-receipt, observed execution, failed execution, combined rollback/recovery observation, interrupted operations without rollback/recovery material, and ambiguous material evidence.

## Stack

Stack base while open:

```text
status/interrupted-run-read-model
```

Suggested title:

```text
core(status): add rollback recovery read model
```

## Files

```text
IronDev.Core/Governance/RollbackRecoveryReadModelModels.cs
IronDev.Core/Governance/RollbackRecoveryReadModelValidator.cs
IronDev.Core/Governance/RollbackRecoveryReadModelAssembler.cs
IronDev.IntegrationTests/BlockD15RollbackRecoveryReadModelIntegrationTests.cs
Docs/receipts/D15_ROLLBACK_RECOVERY_READ_MODEL.md
```

## Boundary

The rollback/recovery read model explains supplied rollback and recovery material metadata only. It does not execute rollback, execute recovery, retry, resume, continue workflow, apply patches, commit, push, create PRs, merge, release, deploy, promote memory, choose next safe action, or grant authority.

D15 is metadata-only. It does not produce observations, fetch observations, inspect live source state, read raw source content, read raw patch content, read raw diff content, run validation, calculate missing evidence, resolve forbidden actions, resolve receipts, resolve evidence, resolve validation staleness, resolve patch/base freshness, resolve worktree/base/head freshness, resolve interrupted-run state, or write stores.

Rollback plan observed is not rollback authority.

Rollback evidence observed is not rollback execution proof.

Rollback receipt observed is not rollback permission.

Recovery plan observed is not recovery authority.

Recovery evidence observed is not recovery execution proof.

Recovery receipt observed is not recovery permission.

Rollback failed is not retry authority.

Recovery failed is not resume authority.

No missing material is not action allowed.

Ambiguous rollback/recovery material never silently selects a winner.

## Validation

Recorded after implementation:

```text
Focused D15: 59/59 passed
D01-D15 stacked resolver/read-model lane: 1156/1156 passed
A02 + A05 read-adapter corridor: 61/61 passed
Governance/status corridor: 1336/1336 passed
Build: 0 errors / 4 warnings
git diff --check: passed
git diff --cached --check: passed
```

## Review Line

Rollback/recovery state must be explainable before it can ever be trusted.

## Killjoy

A rollback plan is not rollback execution. A recovery plan is not recovery authority.
