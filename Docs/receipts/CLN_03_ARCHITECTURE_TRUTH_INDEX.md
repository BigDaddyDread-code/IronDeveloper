# CLN-03 Architecture Truth Index Receipt

**Date:** 12 July 2026

**Slice:** CLN-03

**Behavior change:** None

## Purpose

Record the creation and bounded verification of the canonical architecture authority map.

## Delivered

- Added `Docs/architecture/CANONICAL_ARCHITECTURE_INDEX.md`.
- Linked the index from `Docs/ARCHITECTURE.md`.
- Identified current canonical documents for authority, scope, workflow/run state, approval/continuation, apply safety, frontend IA, audit, memory, topology, and database truth.
- Classified architecture-bearing competitors as Canonical, Supporting, Historical, Superseded, ParkingLot, or DeleteCandidate.
- Recorded unresolved truth gaps without converting deferred designs into current architecture.

## Classification Scope

The index classifies 54 architecture-bearing documents or document groups:

| Classification | Count |
| --- | ---: |
| Canonical | 23 |
| Supporting | 15 |
| Historical | 10 |
| Superseded | 5 |
| ParkingLot | 1 |
| DeleteCandidate | 0 |

The accepted ADR-001 through ADR-007 pack and the Product UX v2 index plus four modules are counted as individual documents. The exhaustive repository documentation inventory remains CLN-04 work.

## Preserved Boundaries

- Runtime behavior and persisted contracts remain the proof of what exists.
- SQL is durable authority for authoritative backend product state, not a blanket claim over documentation or historical evidence.
- Accepted ADRs constrain implementation; supporting prose cannot override them.
- Receipts remain historical evidence and were not rewritten or classified as current architecture.
- No production deployment topology was invented for deferred hosted capability.
- No memory intelligence, write authority, promotion path, or retrieval authority changed.
- `DeleteCandidate` does not authorize deletion, and no candidate was forced into that class.

## Verification

```text
Markdown relative-link check: PASS (54 unique relative targets, 0 missing)
Classification vocabulary check: PASS (6 required classes present)
git diff --check: PASS
Required domain check: PASS (10 required domains present)
```

## Review Line

The repository now has one entry point for resolving current architecture authority by domain.

## Killjoy Line

The index records which story wins; it does not make a losing story safe to execute.
