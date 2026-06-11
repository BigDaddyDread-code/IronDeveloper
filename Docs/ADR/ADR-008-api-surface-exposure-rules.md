# ADR-008: API Surface Exposure Rules

## Status

Accepted for Block F.

## Context

Block F exposes backend capability through API and later CLI surfaces after the Backend Contract Freeze Report.

Primary references:

- [Docs/BACKEND_CONTRACT_FREEZE_REPORT.md](../BACKEND_CONTRACT_FREEZE_REPORT.md)
- [Docs/BACKEND_ARCHITECTURE.md](../BACKEND_ARCHITECTURE.md)
- [Docs/L4_L5_OPERATIONAL_DEBUGGING.md](../L4_L5_OPERATIONAL_DEBUGGING.md)
- [Docs/ADR/README.md](README.md)
- [ADR-001 SQL source of truth](ADR-001-SQL-source-of-truth.md)
- [ADR-002 Retrieval match is not memory candidate](ADR-002-retrieval-match-not-memory-candidate.md)
- [ADR-003 Candidate, proposal, and promotion boundary](ADR-003-memory-candidate-proposal-promotion-boundary.md)
- [ADR-004 Proposal, review, and apply boundary](ADR-004-proposal-review-apply-boundary.md)
- [ADR-005 Tool request, audit, and execution boundary](ADR-005-tool-request-audit-execution-boundary.md)
- [ADR-006 Critic, gate, and governance boundary](ADR-006-critic-gate-governance-boundary.md)
- [ADR-007 Human review remains required](ADR-007-human-review-required-for-apply-and-promotion.md)
- [Docs/BACKEND_SQL_INVENTORY.md](../BACKEND_SQL_INVENTORY.md)
- [Docs/BACKEND_ENTITY_TABLE_INVENTORY.md](../BACKEND_ENTITY_TABLE_INVENTORY.md)
- [Docs/BACKEND_NAMING_INVENTORY.md](../BACKEND_NAMING_INVENTORY.md)

The backend contract is frozen with exceptions. API exposure must consume that contract state. It must not redefine authority, add execution paths, add approval paths, or make evidence more powerful than the backend contract says it is.

## Decision

Block F API endpoints are transport wrappers over frozen backend contracts only.

API endpoints are not authority sources.

API request validation is not human approval.

API response status is not governance.

API route naming must preserve backend boundary names.

API must not turn advisory output into permission.

API must not add new persistence semantics.

API must not create hidden execution, promotion, or apply paths.

API must not hide PR 56 freeze exceptions. If an endpoint touches a known exception, the endpoint contract must name that limitation or block exposure until the exception is resolved.

## Exposure rules

API may expose:

- Read-only inspection over existing audit, evidence, run, gate, proposal, and report contracts.
- Request creation for existing manual backend flows where request creation does not imply execution permission.
- Gate evaluation or gate result reporting backed by existing gate behavior.
- Dogfood loop request or receipt surfaces only when backed by existing manual/dogfood capability.

API must not expose in Block F:

- Source apply endpoint.
- Memory promotion endpoint.
- Automatic tool execution endpoint.
- Model-output approval endpoint.
- Critic-governance endpoint.
- Audit-approval endpoint.
- Vector-as-truth endpoint.
- Hidden workflow or autonomous runner endpoint.
- Endpoint that combines request, approval, and execution.
- Endpoint that mutates source from proposal without explicit human approval design.

## Human approval boundary

API authentication is not human approval.

Authorization to call an endpoint is not approval to mutate source.

Creating a request is not approval.

Passing validation is not approval.

Critic output is not approval.

Gate result is not execution.

Audit record is not approval.

Human approval for source apply and memory promotion remains a separate explicit backend contract.

Block F must not expose source apply or memory promotion unless a later contract-change PR defines the approval model.

## Naming boundary

API route and DTO names must preserve backend naming from PR 48 and PR 56.

Required names:

- RetrievalMatch, not retrieval candidate.
- MemoryCandidate only for actual memory candidate concepts.
- MemoryProposal only for reviewable proposed memory changes.
- Promotion only for accepted persistence steps.
- Proposal, not apply.
- Apply only for actual source mutation paths.
- Audit, not approval.
- GateDecision, not execution.
- CriticReview, not governance.
- ToolRequest, not tool execution permission.

Generic names such as AgentAction, AgentDecision, AgentCommand, MemoryItem, ExecutionResult, and ApprovalResult are not allowed unless the boundary is obvious and documented.

## Explicit rejections

API call as approval is rejected.

CLI command as approval is rejected.

Endpoint access as execution permission is rejected.

Audit as approval is rejected.

Gate as executor is rejected.

Critic as governor is rejected.

Proposal-as-apply is rejected.

Retrieval match becoming candidate memory automatically is rejected.

Memory safety result acting as approval is rejected.

Model output as permission is rejected.

Vector index as authority is rejected.

Automatic source apply is rejected.

Automatic memory promotion is rejected.

## Consequences

API and CLI work can begin by exposing frozen backend contracts.

API and CLI work must not repair or redefine backend contracts in endpoint code.

If an endpoint needs authority that the backend contract does not already define, the work must stop and create a backend contract-change PR first.

If a PR exposes a known PR 56 exception, it must name the exception in the endpoint contract or block the endpoint until the exception is resolved.

## Non-goals

This ADR adds no controllers, no endpoints, no runtime DTOs, no CLI commands, no service registrations, no runtime behavior, no SQL/schema/proc changes, no persistence semantics, no source apply logic, no memory promotion logic, no approval logic, no tool execution logic, no vector/index behavior, no UI, no workflow runtime, and no agent capability.
