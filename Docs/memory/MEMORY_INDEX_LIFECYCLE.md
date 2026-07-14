# Memory Index Lifecycle

**Status:** Canonical memory index contract

**Last reviewed:** 15 July 2026

**Programme slice:** CLN-27

## Lifecycle

The durable SQL event sequence is:

`SourceCreated` / `SourceUpdated` -> `EmbeddingQueued` -> `EmbeddingCompleted` -> optional `StaleDetected` -> `ReindexRequested` -> `EmbeddingQueued` -> `EmbeddingCompleted` -> `ReindexCompleted`.

Source retirement appends `SourceArchived`; only then may the derived projection append `DerivedIndexDeleted`. A completed reindex may append `DerivedIndexRebuilt` when a provider rebuild is explicitly recorded.

## Enforcement

- Events are append-only and ordered per tenant/project/source under a serialised transaction.
- Invalid transitions are refused, not normalised.
- Every event carries source identity/version, correlation, timestamp, optional actor, content hash, and JSON detail. Derived-index events require a provider.
- Non-update events must match the active source version; `SourceUpdated` must introduce a different version.
- Event timestamps are monotonic, and `ReindexCompleted` requires an unmatched `ReindexRequested` for that version.
- The current lifecycle view is the latest SQL event per source.
- Source archive is a SQL lifecycle fact. Deleting a vector does not archive the source.
- SQL source records and lifecycle events are authoritative.
- Embeddings, semantic chunks, provider collections, and vector data are derived and rebuildable.

## Recovery

A provider loss is recovered by reading authoritative source records plus lifecycle state, queueing embeddings, completing them, and recording reindex completion/rebuild. Restoring a vector snapshot cannot manufacture source truth.

## Operational Integration Status

This slice installs the durable ledger, validated write procedure, current-state view, and verification contract. Existing semantic/vector provider call sites are not migrated to emit these events by this slice. Therefore, an absent lifecycle event is absent evidence; it must not be inferred from provider state, logs, or a surviving vector.

## Killjoy Line

A vector that still exists after source archive is stale debris, not durable memory.
