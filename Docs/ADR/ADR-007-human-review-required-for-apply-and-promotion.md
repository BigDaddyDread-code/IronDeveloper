# ADR-007: Human Review Required for Apply and Promotion

Status: Accepted.

## Context

The backend has increasing evidence quality: model-backed manual review, critic findings, gates, memory safety classifications, audits, source reports, and dogfood receipts. Better evidence must not become hidden approval.

## Decision

Human review remains required before source apply.

Human review remains required before memory promotion.

Model output, critic review, gate result, audit record, retrieval match, memory influence, and safety result are not substitutes for human approval.

API/CLI work must preserve this boundary.

Memory safe is not approval.

Model output is advisory only.

## Explicit rejections

- Automatic source apply is rejected.
- Automatic memory promotion is rejected.
- Hidden approval through model/tool output is rejected.
- Safe classification becoming approval is rejected.

## Consequences

The backend may assemble evidence packages and review reports. It may not silently convert them into permission to mutate source or promote memory.

Human review remains required for source apply and memory promotion.

## Related docs

- [Backend Architecture](../BACKEND_ARCHITECTURE.md)
- [Backend Naming Inventory](../BACKEND_NAMING_INVENTORY.md)
- [Backend SQL Inventory](../BACKEND_SQL_INVENTORY.md)
- [Backend Entity/Table Inventory](../BACKEND_ENTITY_TABLE_INVENTORY.md)
- [Backend Test Fixture Inventory](../BACKEND_TEST_FIXTURE_INVENTORY.md)
