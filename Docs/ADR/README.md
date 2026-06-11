# Backend Architecture Decision Records

This ADR pack is part of the pre-PR56 Backend Contract Freeze preparation.

These records document current backend decisions. They do not introduce new behavior, new schema, new runtime wiring, new API/CLI/UI surface, new persistence behavior, or new capability.

## Related backend contract docs

- [Backend Architecture](../BACKEND_ARCHITECTURE.md)
- [Backend Naming Inventory](../BACKEND_NAMING_INVENTORY.md)
- [Backend SQL Inventory](../BACKEND_SQL_INVENTORY.md)
- [Backend Inline SQL Inventory](../BACKEND_INLINE_SQL_INVENTORY.md)
- [Backend Entity/Table Inventory](../BACKEND_ENTITY_TABLE_INVENTORY.md)
- [Backend Test Fixture Inventory](../BACKEND_TEST_FIXTURE_INVENTORY.md)

## ADR index

| ADR | Decision | Boundary protected |
| --- | --- | --- |
| [ADR-001](ADR-001-SQL-source-of-truth.md) | SQL is the source of truth. | Retrieval, model output, and audit cannot become authority. |
| [ADR-002](ADR-002-retrieval-match-not-memory-candidate.md) | Retrieval match is not memory candidate. | Lookup output cannot become candidate memory. |
| [ADR-003](ADR-003-memory-candidate-proposal-promotion-boundary.md) | Candidate, proposal, safety, and promotion remain separate. | Memory safe is not approval or promotion. |
| [ADR-004](ADR-004-proposal-review-apply-boundary.md) | Proposal, review, and source apply remain separate. | Proposal is not apply. |
| [ADR-005](ADR-005-tool-request-audit-execution-boundary.md) | Tool request, audit, gate, and execution remain separate. | Audit is not approval; gate is not executor. |
| [ADR-006](ADR-006-critic-gate-governance-boundary.md) | Critic, gate, and governance remain separate. | Critic is not governance. |
| [ADR-007](ADR-007-human-review-required-for-apply-and-promotion.md) | Human review remains required for source apply and memory promotion. | Advisory or safety signals cannot replace human approval. |
| [ADR-008](ADR-008-api-surface-exposure-rules.md) | API exposes frozen backend contracts only. | API/CLI transport cannot create authority or hidden execution. |

## Core invariants

- SQL is source of truth.
- Vector/index/retrieval is retrieval only.
- Retrieval match is not memory candidate.
- Candidate is not memory.
- Proposal is not apply.
- Audit is not approval.
- Gate is not executor.
- Critic is not governance.
- Memory safe is not approval.
- Tool request is request form, not execution permission.
- Model output is advisory only.
- Human review remains required for source apply and memory promotion.
