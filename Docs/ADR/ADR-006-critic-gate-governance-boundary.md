# ADR-006: Critic, Gate, and Governance Boundary

Status: Accepted.

## Context

Critic agents, model-backed manual agents, gates, and governance policy all participate in review flows. The backend must keep advice, gate decisions, and governance authority separate.

## Decision

Critic reviews and advises.

Gate blocks/allows paths according to explicit rules.

Governance remains explicit backend policy and contract logic.

Model output is advisory only.

Critic does not own governance.

Critic is not governance.

Gate is not executor.

## Explicit rejections

- Critic as governor is rejected.
- Gate as executor is rejected.
- Model confidence as approval is rejected.
- Advisory text as permission is rejected.

## Consequences

Critic findings can inform human review and proposal quality. They cannot approve, govern, mutate source, run tools, or block execution as authority.

Gates can decide whether a path is allowed to continue. They do not execute the path.

## Related docs

- [Backend Architecture](../BACKEND_ARCHITECTURE.md)
- [Backend Naming Inventory](../BACKEND_NAMING_INVENTORY.md)
- [Backend SQL Inventory](../BACKEND_SQL_INVENTORY.md)
- [Backend Entity/Table Inventory](../BACKEND_ENTITY_TABLE_INVENTORY.md)
- [Backend Test Fixture Inventory](../BACKEND_TEST_FIXTURE_INVENTORY.md)
