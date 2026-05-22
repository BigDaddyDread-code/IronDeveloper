---
id: MEMORY_REINDEX_FRESHNESS_GATE
project: IronDev
title: Memory Reindex Freshness Gate
document_type: ArchitectureCheckpoint
authority: Accepted
status: Current
source: C:\Users\bob\source\repos\AIDeveloper\Docs\MEMORY_REINDEX_FRESHNESS_GATE.md
created_utc: 2026-05-23T09:30:00Z
primary_retrieval_questions:
  - Does IronDev memory reindexing keep current accepted memory fresh?
  - Does reindexing create duplicate memory chunks or artefacts?
  - Does stale memory remain visible as demoted evidence?
  - Does reindexing preserve IronDev and BookSeller project scope?
boundary: This proves memory reindex freshness only. It does not apply patches, create disposable workspaces, or change builder behaviour.
---

# Memory Reindex Freshness Gate

## Purpose

Before moving deeper into disposable workspace apply and weighted context, IronDev must prove memory reindexing keeps accepted project memory fresh, project-scoped, and idempotent.

This gate exists because stale or duplicated memory would poison later builder context even if the disposable workspace cage is safe.

## Required Proof

The reindex freshness proof must show:

- A current accepted document version beats an older stale version after reindex.
- The stale version remains visible as stale/demoted evidence.
- Repeated reindexing does not create duplicate active chunks, duplicate source records, or duplicate indexed candidates.
- BookSeller and IronDev remain project-scoped after reindex.
- Exact accepted title queries still promote the exact current document even when raw vector rank is weak.
- Compact evidence includes project, document title, old/new version ids, raw rank, final rank, stale penalty, duplicate counts, wrong-project rejection, and semantic trace id.

## Command

```text
memory reindex-freshness-smoke --project IronDev --bleed-project BookSeller --query "current reindex freshness rules"
```

## Test Agent Plan

```text
tools/dogfood/test-agent-plans/irondev-memory-reindex-freshness-smoke.json
```

## Boundary

No patch apply.

No disposable workspace creation.

No builder behaviour change.

No agent autonomy change.

No ranking semantics change unless a separate bug is found and documented.
