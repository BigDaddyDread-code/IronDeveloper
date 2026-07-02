# H07 Operation Status Projection Index Review

## Purpose

H07 reviews whether an existing SQL-backed operation-status projection table or projection read-model storage exists that can safely receive narrow operation-status projection indexes.

Outcome selected: `DeferredNoExistingProjectionStorage`.

Operation status indexes improve projection lookup. They do not make status authoritative.

A fast status projection is still a projection.

No projection table means no projection index.

You cannot index a projection that does not exist.

## 1. Discovery Summary

H07 found no existing SQL-backed operation-status projection table safe to index.

No `Database` migration or verification file currently defines a dedicated operation-status projection table, operation-status projection stored procedure, or operation-status projection index target.

Current operation-status read/projection code is contract/read-model code over supplied records and events:

| Surface | Current shape | SQL-backed projection storage? | H07 index target? |
| --- | --- | --- | --- |
| `IGovernedOperationStatusReadRepository` | Read-only operation-status read contract keyed by operation ID. | No. | No. |
| `GovernedOperationStatusReadRepository` | Narrow adapter over supplied `GovernedOperationStatusReadRecord` instances; default constructor supplies an empty collection. | No. | No. |
| `OperationStatusFrontendReadinessBackendTruthSource` | Reads through the repository and exposes frontend-readiness operation status. | No. | No. |
| `OperationStatusProjector` / `OperationStatusProjectionModels` | Deterministic Core projector over supplied projection events. | No. | No. |
| `OperationStatusPaginator` / `OperationStatusPaginationModels` | Deterministic Core pagination/filtering over supplied operation-status summary rows. | No. | No. |
| `OperationStatusReadEnvelopeFactory` / validator | Safe read-envelope contract for supplied status read results. | No. | No. |

## 2. Current Lookup Paths

Current operation-status lookup paths are code-level/read-model paths, not SQL table paths:

| Lookup category | Current evidence | Current support |
| --- | --- | --- |
| By operation ID | `IGovernedOperationStatusReadRepository.GetByOperationId`; `GovernedOperationStatusReadRepository` scans supplied records by operation ID. | Supported in code over supplied records only. |
| By tenant scope | `GovernedOperationStatusReadRepository` validates tenant scope on supplied records. | Supported in code over supplied records only. |
| By project/status/timestamp pagination | `OperationStatusPaginator` filters and sorts supplied `OperationStatusSummaryRow` rows. | Supported in Core over supplied rows only. |
| By correlation/reference IDs | `OperationStatusPaginator` can filter supplied rows by supplied correlation/reference fields. | Supported in Core over supplied rows only. |
| By stale/expired/read-state markers | Frontend readiness read-state contracts classify supplied/read results. | Supported in code only. |

H07 cannot add indexes for these paths because no durable SQL projection table currently owns those rows.

## 3. Why Indexes Were Not Added

H07 must not create a projection table just to add indexes.

H07 must not index unrelated governance event, receipt, evidence, timeline, or Weaviate/vector storage under an operation-status label.

H07 must not turn supplied-row pagination or read-repository adapters into durable SQL storage.

The safe outcome is deferral until a future slice creates or wires durable operation-status projection storage explicitly.

## 4. Future Prerequisite

Physical operation-status projection indexes require a future durable projection-storage slice first.

That future prerequisite must define, review, and test the storage owner before any index work:

- exact SQL table/read-model storage name
- owner migration
- write/projection semantics
- read repository or stored procedure surface
- tenant/project scoping
- UTC timestamp behavior
- rebuild/backfill boundary
- non-authority boundary

Only after that exists can a later index slice add narrow indexes for real current lookup paths such as:

- `TenantId` + `ProjectId` + `OperationId`
- `ProjectId` + `OperationId`
- `ProjectId` + projected status kind/state
- `ProjectId` + last status changed/observed timestamp
- supported correlation/reference lookup
- supported stale/expired lookup
- supported project/time pagination

## 5. Non-Authority Boundary

An operation-status projection is not approval.

An operation-status projection is not policy satisfaction.

An operation-status projection is not source-apply authority.

An operation-status projection is not workflow continuation authority.

An operation-status projection is not merge readiness.

An operation-status projection is not release readiness.

An operation-status projection is not deployment readiness.

An operation-status projection is not rollback authority.

An operation-status projection is not retry authority.

An operation-status index is not authority.

Fast operation-status lookup is not authority.

Status projection does not choose next safe action.

Status projection does not prove underlying evidence is true.

Status projection does not replace governance events, receipts, or evidence records.

Operation status indexes improve lookup only.

Operation status is display/investigation projection only.

SQL remains source of truth.

Weaviate is rebuildable.

Read models may be rebuildable.

Authority records cannot be vibes.

## 6. What H07 Did Not Change

H07 does not add a SQL migration.

H07 does not create an operation-status projection table.

H07 does not add indexes.

H07 does not alter stored procedure behavior.

H07 does not alter triggers.

H07 does not change permissions.

H07 does not change production Core behavior.

H07 does not change operation-status projection semantics.

H07 does not change status mapper semantics.

H07 does not change next-safe-action decisions.

H07 does not add API/CLI/UI behavior.

H07 does not change workflow/source-apply/rollback/memory/release/deploy behavior.

H07 does not change authority profiles.

H07 does not change Weaviate behavior.

H07 does not change receipt/evidence storage.

H07 does not backfill or rebuild projections.

H07 does not add migration runner or DbUp behavior.

## 7. Next Intended Slice

H08 - TenantId enforcement tests on new read models.

Review line: Tenant filters protect read scope. They do not create authority.

Killjoy: A tenant-scoped lie is still a lie.
