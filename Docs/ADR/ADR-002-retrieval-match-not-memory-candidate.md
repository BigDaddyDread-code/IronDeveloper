# ADR-002: Retrieval Match Is Not Memory Candidate

Status: Accepted.

## Context

PR 48 normalized retrieval output language from candidate to match. This prevents lookup results from being confused with memory candidates.

## Decision

Retrieval output is a `Match`.

Memory candidate means a reviewable possible memory item. Candidate is not memory. Retrieval match is not memory candidate.

Retrieval match must not auto-create candidate memory. Memory promotion requires explicit accepted persistence.

## Explicit rejections

- Retrieval match becoming candidate memory automatically is rejected.
- Retrieval confidence becoming memory authority is rejected.
- Retrieval output becoming persisted memory is rejected.
- Retrieval output bypassing proposal or promotion review is rejected.

## Consequences

Search and retrieval can surface useful context. They cannot create memory, promote memory, approve actions, or satisfy governance.

The word candidate remains reserved for possible memory content under review, not ranked retrieval rows.

## Related docs

- [Backend Architecture](../BACKEND_ARCHITECTURE.md)
- [Backend Naming Inventory](../BACKEND_NAMING_INVENTORY.md)
- [Backend SQL Inventory](../BACKEND_SQL_INVENTORY.md)
- [Backend Entity/Table Inventory](../BACKEND_ENTITY_TABLE_INVENTORY.md)
- [Backend Test Fixture Inventory](../BACKEND_TEST_FIXTURE_INVENTORY.md)
