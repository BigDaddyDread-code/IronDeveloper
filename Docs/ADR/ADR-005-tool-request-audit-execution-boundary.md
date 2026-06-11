# ADR-005: Tool Request, Audit, and Execution Boundary

Status: Accepted.

## Context

The backend now has typed tool requests, gate decisions, manual execution paths, and append-only tool execution audit storage. These concepts are adjacent, but they are not interchangeable.

## Decision

Tool request is request form.

Audit records evidence.

Execution requires a governed execution path.

Audit store is evidence locker, not robot arm.

Audit does not approve or execute.

Gate is not executor.

## Explicit rejections

- Request as permission is rejected.
- Audit as approval is rejected.
- Audit as executor is rejected.
- Gate as executor is rejected.

## Consequences

The audit store can record supported manual execution results. It cannot cause execution, approve execution, or replace a gate.

Future API/CLI work must expose request and audit records without implying that either is an execution button.

## Related docs

- [Backend Architecture](../BACKEND_ARCHITECTURE.md)
- [Backend SQL Inventory](../BACKEND_SQL_INVENTORY.md)
- [Backend Entity/Table Inventory](../BACKEND_ENTITY_TABLE_INVENTORY.md)
- [Backend Naming Inventory](../BACKEND_NAMING_INVENTORY.md)
- [Backend Test Fixture Inventory](../BACKEND_TEST_FIXTURE_INVENTORY.md)
