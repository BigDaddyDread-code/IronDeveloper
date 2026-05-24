---
id: MINESWEEPER_DISPOSABLE_BUILD_184
project: IronDev
title: Minesweeper Disposable Build 184
document_type: DogfoodProof
authority: Draft
status: Current
---

# Minesweeper Disposable Build 184

## Purpose

Minesweeper is the second disposable product spike used to test whether IDA can generalize beyond the original Solitaire path.

The useful question is not whether Minesweeper is a polished game. The useful question is:

```text
Can a messy request for a different product pass through the governed disposable build loop without reusing Solitaire-specific scope?
```

## What This Proves

The 184 path proves:

- `build disposable run --project Minesweeper --goal "i want build minesweeper"` preserves the requested project.
- Run-scoped docs use `MINESWEEPER_*` IDs instead of `SOLITAIRE_*` IDs.
- Retriever context explicitly rejects Solitaire product scope for the Minesweeper run.
- ConscienceAgent allows execution only inside an explicit disposable workspace.
- ThoughtLedger records visible safety reasoning without hidden chain-of-thought.
- BuilderAgent generates `Minesweeper.Core`, `Minesweeper.Wpf`, and `Minesweeper.Core.Tests` inside the disposable workspace.
- TesterAgent evidence records one intentional build failure, one intentional flood-fill test failure, bounded repairs, and final pass.
- QualityAgent/Killjoy runs as part of the full loop-gated command.
- Real repo mutation count remains zero.

## Generated Disposable App Scope

The disposable Minesweeper app includes:

- seeded board generation
- first-click safety
- reveal and flood-fill logic
- flag toggling
- win/loss detection
- minimal WPF board UI
- deterministic core tests

## Boundary

This proof does not promote Minesweeper into the real repository.

Generated app files remain inside the disposable workspace. Run-scoped docs are evidence only. Accepted memory mutation, ticket acceptance, guardrail mutation, real repo writes, and self-approval remain blocked.

## Validation

Primary plan:

```text
test run-plan --plan tools/dogfood/test-agent-plans/irondev-minesweeper-disposable-build-184.json --run-id <run> --json
```

Useful direct command:

```text
build disposable run --project Minesweeper --goal "i want build minesweeper" --run-id <run> --json
```

## Finding

The original probe was valuable because it exposed that the product-shaped disposable path could accept `Minesweeper` but still emit Solitaire-shaped run-scoped docs and builder output.

The 184 slice closes that gap for Minesweeper while preserving the disposable cage.
