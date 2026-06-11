# API Surface Exposure Rules

## Purpose

This document defines the API exposure rules for Block F before adding new API endpoints.

This is API contract documentation, not API implementation.

Block F exposes frozen backend contracts only. API and CLI must not redefine backend authority.

## Block F scope

Block F may add API and CLI surfaces over existing backend contracts after PR 56.

Block F must not add new backend capability, new authority, new automation, hidden execution paths, source apply paths, memory promotion paths, or runtime autonomy.

The intended Block F API sequence is documented later in this file.

## Frozen backend contracts

The API surface must preserve the contract state recorded in:

- Docs/BACKEND_CONTRACT_FREEZE_REPORT.md
- Docs/BACKEND_ARCHITECTURE.md
- Docs/L4_L5_OPERATIONAL_DEBUGGING.md
- Docs/ADR/README.md
- Docs/ADR/ADR-001-SQL-source-of-truth.md
- Docs/ADR/ADR-002-retrieval-match-not-memory-candidate.md
- Docs/ADR/ADR-003-memory-candidate-proposal-promotion-boundary.md
- Docs/ADR/ADR-004-proposal-review-apply-boundary.md
- Docs/ADR/ADR-005-tool-request-audit-execution-boundary.md
- Docs/ADR/ADR-006-critic-gate-governance-boundary.md
- Docs/ADR/ADR-007-human-review-required-for-apply-and-promotion.md
- Docs/ADR/ADR-008-api-surface-exposure-rules.md

Core invariants:

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

## API exposure principles

API endpoints are transport wrappers, not authority sources.

API call is not human approval.

CLI command is not human approval.

Endpoint access is not execution permission.

API request validation is not human approval.

API response status is not governance.

API route naming must preserve backend boundary names.

API must not turn advisory output into permission.

API must not add new persistence semantics.

API must not create hidden execution, promotion, or apply paths.

API must surface known boundary exceptions where they affect a response.

## Endpoint Classification

### Read-only inspection endpoints

Allowed for:

- agent run inspection
- audit/evidence inspection
- dogfood receipt inspection
- proposal inspection
- gate result inspection
- memory improvement suggestion/proposal inspection

Rules:

- must not mutate source
- must not promote memory
- must not approve anything
- must not execute tools
- must not infer authority from missing data
- must make boundary status visible where available

### Request creation endpoints

Allowed for:

- manual critic request
- manual memory improvement request
- tool request creation
- dogfood loop request, only if it maps to existing manual/dogfood backend capability

Rules:

- create request/evidence only
- do not imply execution permission
- do not imply approval
- do not bypass gates
- response must identify created request/evidence records
- response must say whether human approval is required for any later mutating step

### Gate evaluation endpoints

Allowed only if backed by existing gate service behavior.

Rules:

- gate evaluates or reports decision
- gate does not execute
- gate does not approve
- gate result must include reason/evidence where available
- gate result must not mutate source or memory
- gate result must not be named as execution result

### Forbidden endpoints in Block F

Blocked:

- source apply endpoint
- memory promotion endpoint
- automatic tool execution endpoint
- model-output approval endpoint
- critic-governance endpoint
- audit-approval endpoint
- vector-as-truth endpoint
- hidden workflow/autonomous runner endpoint
- endpoint that combines request + approval + execution
- endpoint that mutates source from proposal without explicit human approval design

## Request/response envelope rules

Every new API response should make authority boundaries visible.

Conceptual fields:

```text
status
id
runId
evidenceId
boundary
mutationOccurred
humanApprovalRequired
warnings
errors
```

Not every endpoint must use identical DTOs, but every endpoint must expose:

- operation status
- stable identifier, if created/read
- evidence reference, if applicable
- boundary status
- whether mutation occurred
- whether human approval is required
- warnings/exceptions
- correlation/run id, if applicable

Responses must not use approval-like language for request creation.

Responses must not say applied for proposal-only operations.

Responses must not say promoted for candidate/proposal-only operations.

## Error model rules

API errors must be explicit and boring.

Required categories:

- validation error
- not found
- conflict/stale state
- forbidden by policy/gate
- missing human approval
- backend contract exception
- unsupported in Block F
- internal error

Error responses must not hide governance failures behind generic 500 responses where the boundary reason is known.

Forbidden:

- returning success when operation was blocked
- returning approval-like language for request creation
- returning applied for proposal-only operations
- returning promoted for candidate/proposal-only operations

## Authentication/authorization posture

Authentication proves caller identity for the API.

Authorization allows an authenticated caller to access an endpoint.

Authentication is not human approval.

Authorization is not approval to mutate source.

Authorization is not approval to promote memory.

Endpoint access is not execution permission.

Human approval remains a separate backend contract for source apply and memory promotion.

## Audit/evidence exposure rules

API may expose audit and evidence for inspection.

Rules:

- audit records may be read
- evidence references may be returned
- audit cannot be submitted as approval
- evidence cannot be treated as permission
- API must not let clients forge backend audit records
- client-supplied evidence must be clearly distinguished from backend-recorded audit/evidence
- response DTOs must avoid names that imply audit equals approval

## Pagination/filtering rules for read APIs

Read APIs must use explicit paging or bounded result sizes.

Filters must preserve tenant/project/run scope.

Missing filters must not cause cross-project or cross-run leakage.

Sort order must be deterministic where the result is a report, audit list, or evidence list.

Pagination metadata must not imply completeness when the result is capped.

## Human approval rules

API authentication is not human approval.

Authorization to call endpoint is not approval to mutate source.

Creating a request is not approval.

Passing validation is not approval.

Critic output is not approval.

Gate result is not execution.

Audit record is not approval.

Human approval for source apply and memory promotion remains a separate explicit backend contract.

Block F must not expose source apply or memory promotion unless a later explicit contract-change PR defines the approval model.

## Forbidden endpoint patterns

Reject Block F endpoints that:

- expose source apply
- expose memory promotion
- combine request, approval, and execution
- treat API call as approval
- treat CLI command as approval
- treat audit as approval
- treat critic review as governance
- treat gate decision as execution
- treat proposal as apply
- treat memory safety as approval
- treat model output as permission
- treat vector/index retrieval as truth
- create hidden execution through DI or service registration
- mutate source from proposal without explicit human approval design
- create a new agent capability
- create new persistence semantics

## API naming rules

Required naming:

- RetrievalMatch, not retrieval candidate
- MemoryCandidate only for actual memory candidate concepts
- MemoryProposal only for reviewable proposed memory changes
- Promotion only for accepted persistence steps
- Proposal, not apply
- Apply only for actual source mutation paths
- Audit, not approval
- GateDecision, not execution
- CriticReview, not governance
- ToolRequest, not tool execution permission

Avoid generic names unless the boundary is obvious and documented:

- AgentAction
- AgentDecision
- AgentCommand
- MemoryItem
- ExecutionResult
- ApprovalResult

## Block F PR sequence

| PR | Intended capability exposed | Mutation level | Authority boundary | Must not do |
| -- | --------------------------- | -------------- | ------------------ | ----------- |
| PR 58 - Read-only Agent Run API v1 | Inspect durable agent run audit envelopes and safe projections | Read-only | Audit is evidence, not approval | Must not append audit, execute agents, or infer approval |
| PR 59 - Manual Critic API v1 | Request or inspect manual critic review over existing backend contracts | Request/evidence only | CriticReview is advice, not governance | Must not block execution, approve, mutate source, or submit GitHub reviews |
| PR 60 - Manual Memory Improvement API v1 | Request or inspect memory improvement proposal output | Request/evidence only | MemoryProposal is reviewable proposal, not promotion | Must not persist promotion, create CollectiveMemory, or write Weaviate authority |
| PR 61 - Tool Request API v1 | Create typed tool requests | Request only | ToolRequest is form, not execution permission | Must not execute tools or mark request approved |
| PR 62 - Tool Gate API v1 | Evaluate or inspect gate decisions | Decision/report only | GateDecision is not executor | Must not execute tools or mutate source/memory |
| PR 63 - Dogfood Loop API v1 | Request or inspect existing manual dogfood loop receipts | Request/evidence only | Dogfood receipt is evidence, not release approval | Must not run autonomous workflow, apply patches, or claim release readiness |

## Known freeze exceptions that affect API exposure

Block F must not hide PR 56 exceptions.

Current exceptions:

- API chat freeform response wording assertion at EndpointContractTests.cs:189.
- Existing governance/agent runner approval assertions.
- Existing WPF/source boundary scan failures.
- Existing local-clock usage scan failures.
- Existing chat context effective-work-text expectations.
- Existing agent-memory boundary harness references to old CollectiveMemoryRetrievalCandidate naming.
- Existing L4 release gate failures dependent on the memory boundary harness.
- Existing static boundary scans for manual/model/boxed agent files.
- Legacy runtime DDL/bootstrap ownership exceptions from PR 51.
- Uncertain package references from PR 55.
- Uncertain config keys from PR 55.
- Ugly names intentionally left from prior inventories.

Each API PR must either avoid the affected area, name the limitation, or resolve the exception in a separate cleanup PR.

## Non-goals

This document adds no controllers, no endpoints, no runtime DTOs, no CLI commands, no service registrations, no runtime behavior, no SQL/schema/proc changes, no persistence semantics, no source apply logic, no memory promotion logic, no approval logic, no tool execution logic, no vector/index behavior, no UI, no workflow runtime, and no agent capability.
