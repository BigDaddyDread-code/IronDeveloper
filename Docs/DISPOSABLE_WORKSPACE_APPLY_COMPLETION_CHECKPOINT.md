---
id: DISPOSABLE_WORKSPACE_APPLY_COMPLETION_CHECKPOINT
project: IronDev
title: Disposable Workspace Apply Completion Checkpoint
document_type: ArchitectureCheckpoint
authority: Draft
status: Proposed
source: C:\Users\bob\source\repos\AIDeveloper\Docs\DISPOSABLE_WORKSPACE_APPLY_COMPLETION_CHECKPOINT.md
created_utc: 2026-05-23T09:00:00Z
primary_retrieval_questions:
  - What has IronDev proven about disposable workspace apply?
  - Is IronDev allowed to write to the real repo?
  - What comes after disposable workspace apply proof?
boundary: Writing is proven only inside the disposable cage. Real repository writes remain blocked.
---

# Disposable Workspace Apply Completion Checkpoint

## Current Completion Status

IronDev has completed the disposable workspace proof path through the first controlled safe apply cycle.

Completed:

- 104-115 disposable workspace proof path.
- External patch proposal input.
- Positive BookSeller disposable apply.
- Fail-closed unsafe proposal test.
- Build/test inside disposable workspace.
- Before/after evidence and comparison.
- Failure/success package.
- Human approval boundary.
- Final IDA memory/report document.

## Current Boundary

The important boundary still stands:

> This proves writing inside the disposable cage only. It does not permit real repo writes yet.

IronDev has crossed from preview-only into controlled disposable execution.

IronDev has not crossed into production/developer working tree mutation.

No agent currently has permission to apply patches to the real repository.

## What This Means

IronDev can now prove the safer build loop:

```text
Codex proposes patch
        ↓
IronDev/IDA applies patch inside disposable workspace only
        ↓
Build/test runs inside disposable workspace
        ↓
IDA compares before/after evidence
        ↓
Failure/success package is generated
        ↓
Human approval boundary remains intact
```

This is the first real “write inside a cage” milestone.

## What Is Still Not Proven

- Real repo write path.
- Controlled write path design.
- Long-running repair loop.
- Real BookSeller app generation beyond the fixture/proof path.
- Production provider LLM reasoning across the full loop.
- UI reliability.
- ResearchAgent or SentinelAgent behaviour.

## Recommended Next Work

Do not move directly to real repo writes.

Recommended next phase:

```text
116: Disposable apply regression pack
117: Reindex freshness and idempotency gate
118: Weighted context evidence hardening after apply
119: IDA/Codex code comparison scoring
120: Controlled write path safety analysis
```

## Blunt Assessment

This is a major milestone.

IronDev has proven it can write inside a disposable cage.

The next danger is assuming that means it can write to the real repo.

It cannot yet.

The correct next move is to harden, repeat, and inspect the cage before designing any controlled real write path.
