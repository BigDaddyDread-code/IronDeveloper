# H12 Read Projection Backup and Rebuild Story

## Purpose

H12 defines the backup and rebuild story for read projections and derived read models.

H12 explains which data can be rebuilt, what source records are required, what order/replay boundaries matter, and what must never be treated as rebuildable authority.

H12 is a story, policy, and contract-test slice.

Projection rebuild plans restore read models. They do not recreate authority records.

A rebuilt projection is not the source of truth.

## Core Invariant

SQL is source of truth.

Authority records are durable source records.

Read models may be rebuildable.

Weaviate/vector indexes are rebuildable derived indexes.

A rebuilt projection is not the source of truth.

Authority records cannot be vibes.

## 1. Scope

This story covers derived read projections and read models, including:

- operation status summaries/projections
- operation timeline read models
- frontend readiness read models
- evidence metadata read models
- receipt metadata read models
- validation-result metadata read models
- patch-package metadata read models
- interrupted-run read models
- rollback-recovery read models
- worktree/base/head freshness read models
- status/error envelope read models
- read-side cache/index state
- Weaviate/vector indexes, if populated from safe summaries or references

Authority source records are not rebuildable projections.

H12 does not implement backup.

H12 does not implement rebuild.

H12 does not implement projection replay.

H12 does not alter storage.

## 2. Non-Rebuildable Authority Records

These are non-rebuildable authority/source records:

- governance events
- accepted approvals
- policy satisfaction records
- tool requests
- tool gate decisions
- approval decisions
- controlled dry-run receipts
- patch artifacts
- rollback support receipts
- source-apply dry-run receipts
- source-apply receipts
- rollback execution receipts
- workflow transition records
- release readiness decision records
- durable memory proposal decisions, where used as source records
- any append-only audit/event/receipt table intended as durable evidence

These records must be backed up and preserved. They must not be reconstructed from projections, vector indexes, UI state, receipt summaries, safe summaries, or read models.

## 3. Rebuildable Derived Surfaces

| Surface | Classification | Source records required | Ordering/cursor requirements | Tenant/project scope | Weaviate/vector involved | Rebuild risks | Authority boundary |
| --- | --- | --- | --- | --- | --- | --- | --- |
| operation status summaries/projections | `RebuildableProjection` | governance events, status projection events, receipt/evidence refs | append position, created/recorded UTC timestamp, event ID tie-breaker, schema version | tenant/project required where source records are scoped | no | stale or partial status if source chain incomplete | status projection is not authority |
| operation timeline read models | `RebuildableProjection` | operation timeline events and governance event refs | append position, recorded UTC timestamp, event ID tie-breaker | tenant/project required | no | ambiguous event order may hide a broken chain | timeline is not authority |
| frontend readiness read models | `RebuildableProjection` | operation status, evidence metadata, receipt metadata, validation metadata, patch package metadata | source projection versions and source timestamps | tenant/project required | no | fallback sources may look complete while canonical source is missing | frontend readiness is not authority |
| evidence metadata read models | `RebuildableProjection` | evidence metadata source records and evidence refs | evidence ref plus captured/observed UTC | tenant/project required | no | metadata may point to missing or redacted payloads | evidence metadata is not evidence validation |
| receipt metadata read models | `RebuildableProjection` | receipt metadata source records and receipt refs | receipt ref plus captured/observed UTC | tenant/project required | no | receipt summary can outlive receipt payload access | receipt metadata is not permission |
| validation-result metadata read models | `RebuildableProjection` | validation result metadata and validation receipts | observed UTC, expiry UTC, result ID | tenant/project required | no | stale validation may be projected as current if expiry is ignored | validation metadata is not approval |
| patch-package metadata read models | `RebuildableProjection` | patch package metadata, artifact refs, evidence refs | package ID, patch hash, captured UTC | tenant/project required | no | package metadata cannot recreate raw patch body safely | patch metadata is not source-apply authority |
| interrupted-run read models | `ManualReviewRequired` | checkpoints, receipts, diagnostic snapshots, recovery classifications | causation/correlation links and checkpoint order | tenant/project required | no | contradictory evidence must remain contaminated, not normal interrupted state | diagnosis is not recovery authority |
| rollback-recovery read models | `ManualReviewRequired` | rollback materials, apply receipts, target refs, recovery diagnostics | source-apply receipt order and rollback target refs | tenant/project required | no | missing apply receipt cannot be inferred from rollback text | rollback read model is not rollback authority |
| worktree/base/head freshness read models | `RebuildableProjection` | freshness observations, expected state refs, guard outputs | observation UTC and guard version | tenant/project required | no | fresh-looking projection can hide stale source observations | freshness projection is not mutation authority |
| status/error envelope read models | `RebuildableProjection` | status records, error envelopes, issue refs | source status version and recorded UTC | tenant/project required | no | user-facing envelope may simplify missing evidence | envelope is not authority |
| read-side cache/index state | `RebuildableCache` | canonical read-model source records | cache key plus source version | tenant/project required | no | cache may be stale or partial | cache is not source of truth |
| Weaviate/vector indexes | `RebuildableIndex` | safe summaries or approved redacted content only | source document/version cursor and index version | tenant/project required where indexed content is scoped | yes | vector recall can surface stale or incomplete material | vector recall is not authority |
| unknown read surface | `UnknownRequiresReview` | unknown | unknown | unknown | unknown | unknown surfaces may hide authority-shaped state | stop and review before rebuild |
| authority/source records | `NotRebuildableAuthorityRecord` | durable source records themselves | append-only/event order where applicable | tenant/project required where scoped | no | missing source records cannot be recreated from projections | preserve and back up, do not rebuild |

## 4. Source-Of-Truth Chain

```text
Durable source records -> deterministic projection/rebuild process -> read model / index / cache -> display/query surface
```

The chain does not work in reverse.

A read model must not recreate authority records.

A vector index must not recreate authority records.

A UI state must not recreate authority records.

A receipt summary must not recreate authority records.

## 5. Backup Story

### Authority / Source Records

Authority/source records must be backed up as durable records.

Backup must preserve:

- IDs
- tenant/project scope
- timestamps
- payload version
- hashes
- source references
- causation/correlation links
- append-only/event order where applicable
- receipt/artifact references
- schema version
- redaction/retention markers where applicable

### Read Projections

Read projections may be backed up for convenience, but backup is not required for correctness if they are rebuildable from source records.

Projection backups must be treated as cache snapshots, not authority.

### Weaviate / Vector Indexes

Weaviate/vector indexes may be backed up for operational convenience, but must remain rebuildable from safe source material.

Vector backup is not authority.

## 6. Rebuild Story

A future rebuild process must have:

- explicit target projection
- explicit source record set
- explicit tenant/project scope
- source schema/version compatibility check
- deterministic ordering rule
- checkpoint/cursor handling
- idempotency rule
- stale projection clearing rule
- dry-run mode
- verification step
- receipt/evidence of rebuild attempt
- failure classification
- no authority mutation
- no source-record mutation
- no approval/policy/source-apply/release/deploy grant

H12 defines these requirements only.

H12 does not build the process.

## 7. Ordering And Cursor Requirements

Possible source ordering inputs:

- append position
- created/recorded UTC timestamp
- event ID tie-breaker
- schema version
- causation/correlation link
- projection version

Ordering is for deterministic reconstruction of read models only.

Projection ordering is not authority order.

## 8. Tenant / Project Isolation

Future rebuilds must not:

- rebuild all tenants by default
- mix tenant data
- rebuild Tenant A projection from Tenant B source records
- allow missing TenantId to mean all tenants
- rely on project-only scope where TenantId is required

Tenant-scoped rebuild is still not authority.

## 9. Verification Requirements

A future rebuild implementation must verify:

- source records exist
- source record count/hash matches expected scope
- projection row count/hash matches expected output, where practical
- tenant/project isolation is preserved
- stale projection rows are handled
- no source records were mutated
- no authority records were created
- no release/deploy/workflow/action authority was granted

H12 does not implement verification.

## 10. Failure Story

Future failure classes:

- `SourceRecordsMissing`
- `SourceRecordsCorrupt`
- `SchemaVersionUnsupported`
- `ProjectionOrderingAmbiguous`
- `TenantScopeMismatch`
- `ProjectionWriteFailed`
- `VerificationFailed`
- `PartialRebuildDetected`
- `VectorIndexRebuildFailed`
- `ManualReviewRequired`

Failures must become explainable states, not silent partial reads.

## 11. Recovery Story

- If authority/source records are missing, do not rebuild from projection.
- If projection rebuild fails, keep source records untouched.
- If partial projection exists, mark projection stale/partial in a future implementation.
- If vector rebuild fails, degrade recall/search, not authority.
- If tenant scope is ambiguous, stop and require manual review.
- If schema version is unsupported, stop and require compatibility review.

## 12. Weaviate / Vector Rebuild Boundary

H12 defines the boundary for vector rebuilds.

H13 owns Weaviate rebuild command hardening.

- vector indexes are rebuildable derived indexes
- vector recall is not authority
- vector content must come from safe summaries or approved redacted content
- deleting/rebuilding vector indexes does not delete or recreate source records
- vector rebuild failure must not block authority records from existing
- vector rebuild success does not approve anything

H12 does not implement Weaviate behavior.

## 13. Backup / Rebuild Non-Authority Boundary

Backup is not authority.

Rebuild is not authority.

A backup story is not backup execution.

A rebuild story is not rebuild execution.

A projection rebuild is not authority.

A rebuilt projection is not approval.

A rebuilt projection is not policy satisfaction.

A rebuilt projection is not source-apply authority.

A rebuilt projection is not workflow continuation authority.

A rebuilt projection is not merge readiness.

A rebuilt projection is not release readiness.

A rebuilt projection is not deployment readiness.

A rebuilt projection is not rollback authority.

A rebuilt projection is not retry authority.

A rebuilt projection is not mutation authority.

A rebuilt projection does not prove source records are true.

A rebuilt projection does not recreate missing authority records.

A vector rebuild is not authority.

A rebuilt projection is not the source of truth.

## 14. Explicit Non-Implementation

H12 defines story/policy only.

H12 does not implement backup jobs.

H12 does not implement rebuild commands.

H12 does not implement projection replay.

H12 does not add a SQL migration.

H12 does not alter tables.

H12 does not add indexes.

H12 does not alter stored procedures.

H12 does not alter triggers.

H12 does not change permissions.

H12 does not change API/CLI/UI behavior.

H12 does not change Weaviate behavior.

H12 does not change workflow/source-apply/rollback/release/deployment authority.

H12 does not add data migration.

H12 does not add projection backfill.

H12 does not add read projection storage mutation.

H12 does not add artifact retention/deletion implementation.

H12 does not add migration runner or DbUp work.

## 15. Next Slice

H13 - Weaviate rebuild command hardening.

Review line: Weaviate rebuild restores recall. It does not restore authority.

Killjoy: A rebuilt vector index is still just an index.
