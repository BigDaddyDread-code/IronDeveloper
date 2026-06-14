# PR112 - Cross-run Memory Pattern Detection

## Purpose

PR112 adds deterministic cross-run pattern detection for staged memory proposals and workflow-linked evidence.

It notices repeated staged proposal themes across workflow runs so a human/governed reviewer can decide whether a later memory improvement path is worth considering.

## Boundary

A cross-run pattern candidate is not accepted memory.
A cross-run pattern candidate is not promoted memory.
A cross-run pattern candidate is not Portable Engineering Memory.
A cross-run pattern candidate is not retrieval authority.
A cross-run pattern candidate is not vector index content.
A cross-run pattern candidate is not an embedding.
A cross-run pattern candidate is not approval.
A cross-run pattern candidate is not policy satisfaction.
A cross-run pattern candidate is not workflow progress.
A cross-run pattern candidate is not source apply.

The detector produces review candidates only. Human/governed review remains required before any proposal acceptance, memory promotion, Portable Engineering Memory creation, retrieval activation, indexing, embedding, policy, approval, workflow, source apply, or memory-truth decision.

## What changed

- Added Core cross-run memory pattern candidate models.
- Added bounded status, pattern type, and recurrence-band vocabularies.
- Added a deterministic validator that rejects raw/private reasoning, authority language, memory acceptance, memory promotion, retrieval activation, vector/index/embedding language, and true authority flags.
- Added a deterministic detector that groups staged memory proposals across workflow runs by repeated proposal theme.
- Added evidence and review-note models that remain non-authoritative.
- Added focused tests for shape, validator, detection, and boundary guards.

## Explicit non-goals

- No SQL migration or store.
- No API, CLI, UI, scheduler, orchestrator, or runtime dispatch.
- No model call.
- No embedding creation.
- No vector or Weaviate write.
- No retrieval activation.
- No accepted-memory store.
- No memory promotion path.
- No source apply.
- No policy satisfaction.
- No approval satisfaction.
- No release approval.

## Review line

PR112 notices recurring sticky-note themes. It does not turn them into memory.
