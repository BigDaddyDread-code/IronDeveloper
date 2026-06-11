# ADR-001: SQL Source of Truth

Status: Accepted.

## Context

The backend has SQL-backed tables, append-only events, constraints, triggers, and stored procedure boundaries for governed memory, audit, proposals, approvals, promotions, and execution evidence. Retrieval systems and model outputs can assist review, but they are not durable authority.

## Decision

SQL is source of truth for authoritative backend state.

Runtime memory, proposals, audit records, approvals, promotions, and source apply evidence must resolve to SQL-backed authoritative records or explicitly governed file-backed workspace evidence where that contract already exists.

Weaviate, vector, index, and retrieval systems are retrieval only. Retrieval may assist discovery, but cannot approve, promote, apply, govern, or create authority.

## Explicit rejections

- Vector index as authority is rejected.
- Model output as authority is rejected.
- Audit trail as approval is rejected.
- Retrieval match as memory is rejected.

## Consequences

Read models, indexes, summaries, and model output may reference authoritative records. They must not replace them.

API/CLI work after PR 56 must preserve SQL-backed authority and must not treat retrieval, model output, or audit records as approval.

## Related docs

- [Backend Architecture](../BACKEND_ARCHITECTURE.md)
- [Backend SQL Inventory](../BACKEND_SQL_INVENTORY.md)
- [Backend Inline SQL Inventory](../BACKEND_INLINE_SQL_INVENTORY.md)
- [Backend Entity/Table Inventory](../BACKEND_ENTITY_TABLE_INVENTORY.md)
- [Backend Naming Inventory](../BACKEND_NAMING_INVENTORY.md)
