# Memory Index Lifecycle

**Status:** Canonical memory index contract

**Last reviewed:** 14 July 2026

**Programme slice:** CLN-27

## Lifecycle

The durable SQL event sequence is:

`SourceCreated` / `SourceUpdated` -> `EmbeddingQueued` -> `EmbeddingCompleted` -> optional `StaleDetected` -> `ReindexRequested` -> `EmbeddingQueued` -> `EmbeddingCompleted` -> `ReindexCompleted`.

Source retirement appends `SourceArchived`; the derived projection may then append `DerivedIndexDeleted`. A completed reindex may append `DerivedIndexRebuilt` when a provider rebuild is explicitly recorded.

## Enforcement

- Events are append-only and ordered per tenant/project/source under a serialised transaction.
- Invalid transitions are refused, not normalised.
- Every event carries source identity/version, correlation, timestamp, optional actor, provider, content hash, and JSON detail.
- The current lifecycle view is the latest SQL event per source.
- Source archive is a SQL lifecycle fact. Deleting a vector does not archive the source.
- SQL source records and lifecycle events are authoritative.
- Embeddings, semantic chunks, provider collections, and vector data are derived and rebuildable.

## Recovery

A provider loss is recovered by reading authoritative source records plus lifecycle state, queueing embeddings, completing them, and recording reindex completion/rebuild. Restoring a vector snapshot cannot manufacture source truth.

## Killjoy Line

A vector that still exists after source archive is stale debris, not durable memory.
