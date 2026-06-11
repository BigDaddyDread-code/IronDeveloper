# ADR-004: Proposal, Review, and Apply Boundary

Status: Accepted.

## Context

Builder proposals, fix proposals, repair proposals, manual implementation proposals, critic reviews, and dogfood harness receipts all produce evidence. Evidence is not mutation.

## Decision

Proposal generation is advisory.

Review produces review evidence.

Source apply is a separate mutation path.

Proposal persistence does not mutate source.

Source apply requires explicit human approval.

Proposal is not apply.

## Covered surfaces

- Builder proposals.
- Fix proposals.
- Repair proposals.
- Manual implementation proposals.
- Dogfood harness receipts.

## Explicit rejections

- Proposal-as-apply is rejected.
- Critic-as-approval is rejected.
- Audit-as-approval is rejected.
- Model-output-as-source-mutation is rejected.

## Consequences

Any future API/CLI surface must keep proposal, review, approval, dry-run, apply, verify, and report steps distinct.

Human review remains required for source apply.

## Related docs

- [Backend Architecture](../BACKEND_ARCHITECTURE.md)
- [Backend Naming Inventory](../BACKEND_NAMING_INVENTORY.md)
- [Backend Entity/Table Inventory](../BACKEND_ENTITY_TABLE_INVENTORY.md)
- [Backend Test Fixture Inventory](../BACKEND_TEST_FIXTURE_INVENTORY.md)
- [Backend SQL Inventory](../BACKEND_SQL_INVENTORY.md)
