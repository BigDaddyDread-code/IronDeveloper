# CLN-04 Documentation Truth Inventory Receipt

**Date:** 13 July 2026

**Slice:** CLN-04

**Behavior change:** None

## Purpose

Record the complete authority inventory of tracked Markdown documentation under `Docs/`.

## Delivered

- Added `Docs/cleanup/DOCUMENTATION_TRUTH_INVENTORY.md`.
- Classified 603 documents, including this receipt and the inventory itself.
- Recorded title, area, status, verification truth, canonical replacement, required action, and owner for every document.
- Preserved all historical receipts without modification.
- Distinguished archive review from deletion authority.
- Added direct superseded-status banners to the old workflow-engine and semantic-memory designs.
- Added direct parking-lot boundaries to the old self-improvement discussion and Codex goal pack.

## Classification Result

| Status | Count |
| --- | ---: |
| Canonical | 31 |
| Supporting | 123 |
| HistoricalReceipt | 393 |
| Superseded | 5 |
| ParkingLot | 4 |
| ArchiveCandidate | 47 |
| DeleteCandidate | 0 |
| **Total** | **603** |

## Boundaries

- Inventory status does not change runtime behavior or authority.
- `HistoricalReceipt` files remain immutable evidence.
- `ArchiveCandidate` is a move-review signal, not permission to move or delete.
- `DeleteCandidate` requires separate dependency proof; none was asserted.
- “Not reverified by CLN-04” is retained where this documentation pass did not inspect owning code.
- Bounded structure changes remain CLN-05 work.

## Verification

```text
Tracked Docs Markdown coverage: PASS (603 expected, 603 unique rows, 0 missing, 0 extra)
Required columns: PASS (8 of 8 present)
Required status vocabulary: PASS (7 of 7 represented in the summary; no unproven DeleteCandidate rows)
Historical receipt mutations: PASS (0 existing receipts modified; 1 CLN-04 receipt added)
git diff --check: PASS
```

## Review Line

Every tracked document is now visible as current authority, bounded support, immutable history, replaced design, future discussion, or a separately reviewable archive candidate.

## Killjoy Line

An inventory that silently calls old prose current is tidier than confusion, but no more truthful.
