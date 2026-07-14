# CLN-27 Memory Index Lifecycle Receipt

**Status:** Historical receipt

**Date:** 15 July 2026

## Delivered

- Added a manifest-owned append-only index lifecycle event ledger.
- Added all required source, embedding, stale, reindex, archive, delete, and rebuild events.
- Added serialised transition validation and current-state projection.
- Bound events to the active source version, required providers for derived events, enforced monotonic timestamps, and refused reindex completion without a matching request.
- Added source/project retrieval indexes and migration verification.
- Preserved SQL authority and rebuildable semantic/vector projections.

## Boundary

This slice provides the durable recording and validation boundary. It does not migrate existing provider call sites to emit events, grant reindex capability to ordinary users, make provider state authoritative, or automatically inject memory.
