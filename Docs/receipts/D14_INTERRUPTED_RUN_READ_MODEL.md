# D14 Interrupted Run Read Model

## Purpose

D14 adds a read-only interrupted-run read model for governed operations.

The model consumes supplied checkpoint observations and an optional supplied diagnostic snapshot. It explains where an operation appears to have stopped, including workspace-without-patch, patch-without-validation, validation failure, apply-started-not-completed, commit-package-without-commit, commit-without-push, push-without-PR, explicit failed/cancelled/completed states, and ambiguous checkpoint evidence.

## Stack

Stack base while open:

```text
status/worktree-base-head-freshness-read-model
```

Suggested title:

```text
core(status): add interrupted run read model
```

## Files

```text
IronDev.Core/Governance/InterruptedRunReadModelModels.cs
IronDev.Core/Governance/InterruptedRunReadModelValidator.cs
IronDev.Core/Governance/InterruptedRunReadModelAssembler.cs
IronDev.IntegrationTests/BlockD14InterruptedRunReadModelIntegrationTests.cs
Docs/receipts/D14_INTERRUPTED_RUN_READ_MODEL.md
```

## Boundary

The interrupted-run read model explains where a governed operation appears to have stopped using supplied checkpoint metadata and supplied diagnostic summaries only. It does not retry, resume, recover, rollback, continue workflow, execute mutation, apply patches, commit, push, create PRs, merge, release, deploy, promote memory, choose next safe action, or grant authority.

D14 is metadata-only. It does not produce observations, fetch observations, inspect live source state, read raw source content, read raw patch content, read raw diff content, run validation, calculate missing evidence, resolve forbidden actions, resolve receipts, resolve evidence, resolve validation staleness, resolve patch/base freshness, resolve worktree/base/head freshness, or write stores.

Interrupted state is not retry permission.

Failed state is not recovery permission.

Cancelled state is not resume permission.

Apply-started-not-completed is not rollback authority.

Commit-created-no-push is not push authority.

Push-completed-no-PR is not PR creation authority.

No interruption observed is not action allowed.

Ambiguous checkpoint metadata never silently selects a winner.

## Validation

Recorded after implementation:

```text
Focused D14: 56/56 passed
D01-D14 stacked resolver/read-model lane: 1097/1097 passed
A02 + A05 read-adapter corridor: 61/61 passed
Governance/status corridor: 1277/1277 passed
Build: 0 errors / 4 warnings
git diff --check: passed
git diff --cached --check: passed
```

## Review Line

A half-run must become an explainable state, not a mystery.

## Killjoy

Knowing where the run stopped is not authority to restart it.
