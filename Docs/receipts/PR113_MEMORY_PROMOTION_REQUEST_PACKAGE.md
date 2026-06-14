# PR113 - Memory Promotion Request Package

## Purpose

PR113 packages staged memory proposals for promotion review.

A promotion request package gathers the staged proposal, its evidence package, duplicate/stale/conflict/cross-run pattern review signals, risk notes, sanitization notes, requested target scope, and approval requirement references into one reviewable package.

## Boundary

No package accepts memory.
No package rejects memory.
No package promotes memory.
No package creates accepted memory.
No package creates Portable Engineering Memory.
No package activates retrieval.
No package creates embeddings.
No package writes to vector storage.
No package writes to Weaviate.
No package satisfies approval.
No package satisfies policy.
No package transfers authority.
No package mutates source.
No package approves release.

The package requests promotion review. It does not approve the promotion.

## What changed

- Added bounded Memory Promotion Request Package models.
- Added evidence, grounding, signal, approval requirement, and review note reference models.
- Added target-scope, purpose, and status vocabularies that remain candidate/review-only.
- Added validator rejection for hidden/private reasoning, raw prompt/completion/tool output, whole patch payloads, authority language, acceptance language, promotion language, approval language, policy language, retrieval activation, embedding/vector language, and true authority flags.
- Added deterministic assembler from staged proposal, evidence package, duplicate candidates, stale candidates, conflict candidates, and cross-run pattern candidates.
- Added focused tests and static no-SQL/no-runtime boundary guards.

## Explicit non-goals

- No SQL migration or store.
- No API, CLI, UI, scheduler, orchestrator, or runtime dispatch.
- No model call.
- No accepted-memory store.
- No memory promotion path.
- No proposal status mutation.
- No retrieval activation.
- No embedding creation.
- No vector or Weaviate write.
- No approval decision recording.
- No policy decision recording.
- No source apply.
- No release approval.

## Review line

PR113 puts a staged memory proposal into the promotion review folder. It does not approve the promotion.
