# ADR-003: Memory Candidate, Proposal, and Promotion Boundary

Status: Accepted.

## Context

The memory system separates local memory, candidate patterns, improvement proposals, evidence aggregation, safety classification, and collective memory promotion. The separation prevents memory from becoming hidden authority.

## Decision

Memory candidate means a possible memory item.

Memory proposal means a reviewable proposed memory change.

Memory safety result means advisory classification.

Memory promotion means an explicit accepted persistence step.

SQL-backed promoted memory is the durable source of truth.

Memory safe is not approval.

## Explicit rejections

- Automatic memory promotion is rejected.
- Safety result acting as approval is rejected.
- Audit record acting as promotion is rejected.
- Model output directly becoming memory is rejected.
- Vector retrieval directly becoming memory is rejected.

## Consequences

Memory may influence decisions only through explicit influence evidence and governed contracts. It cannot quietly become approval, promotion, policy clearance, or action authority.

Human review remains required for memory promotion.

## Related docs

- [Backend Architecture](../BACKEND_ARCHITECTURE.md)
- [Backend SQL Inventory](../BACKEND_SQL_INVENTORY.md)
- [Backend Entity/Table Inventory](../BACKEND_ENTITY_TABLE_INVENTORY.md)
- [Backend Naming Inventory](../BACKEND_NAMING_INVENTORY.md)
- [Backend Test Fixture Inventory](../BACKEND_TEST_FIXTURE_INVENTORY.md)
