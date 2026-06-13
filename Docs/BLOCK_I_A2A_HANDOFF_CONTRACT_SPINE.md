# Block I A2A Handoff Contract Spine

## Status

PR90 begins Block I with the Agent Handoff contract.

PR90 is contract/model/tests/docs only.

PR90 adds no SQL, API, CLI, runtime wiring, workflow runner, A2A runtime, LangGraph runtime, source apply, memory promotion, release approval, execution engine, transport, message bus, inbox, outbox, dispatcher, receiver, scheduler, orchestrator, model call, approval lookup, approval recording, approval satisfaction checker, policy decision recording, repository, stored procedure, durable handoff store, or UI.

## Core rule

Block I begins with the Agent Handoff contract.

A handoff transfers context and evidence only.

A handoff does not transfer approval, execution permission, memory ownership, workflow authority, source apply authority, memory promotion authority, or release authority.

A handoff may cite approval decisions only as evidence.

A handoff may cite gate decisions, dogfood receipts, critic output, validation output, ThoughtLedger references, and governance events only as evidence.

A handoff does not send itself.

A handoff does not create A2A runtime messages.

PR90 does not add API/CLI/SQL/runtime wiring.

## Main claim

A handoff transfers context and evidence.

It does not transfer authority.

Planner handing context to Builder does not mean Builder may execute.

Critic handing review evidence to Builder does not mean Builder may apply source changes.

Memory handing candidate context to Conscience does not mean memory has been promoted.

Gate evidence handed to another agent does not mean workflow may continue.

## Handoff can carry

- request intent
- subject context
- evidence references
- governance references
- policy and approval requirement references
- constraints
- correlation IDs
- causation IDs
- source agent identity
- target agent identity
- safe metadata

## Handoff must not carry

- approval authority
- execution permission
- memory ownership
- source apply authority
- workflow continuation authority
- release approval
- policy satisfaction
- gate pass authority
- approval satisfaction
- hidden reasoning
- raw chain-of-thought
- runtime rights

## Status vocabulary

Allowed status values:

- Draft
- ReadyForReview
- Offered
- Received
- Rejected
- Cancelled
- Expired
- Superseded

ReadyForReview means structurally ready to review. It does not mean approved.

Offered means the source agent prepared a handoff for a target agent. It does not mean accepted, approved, executable, or workflow continued.

Received means the target side received the handoff record or context. It does not mean approved, executable, or workflow continued.

Forbidden status meanings:

- Approved
- Authorized
- AcceptedAsApproval
- ExecutionAllowed
- PolicySatisfied
- WorkflowContinued
- SourceApplyAllowed
- MemoryPromotionAllowed
- ReleaseApproved
- CanExecute
- CanShip

## Handoff type vocabulary

Allowed handoff type values:

- TaskContext
- ReviewRequest
- EvidenceTransfer
- RequirementTransfer
- DebugContext
- ImplementationContext
- ValidationContext
- MemoryCandidateContext
- SourceApplyContext
- ReleaseEvidenceContext

MemoryCandidateContext is not memory promotion.

SourceApplyContext is not source apply permission.

ReleaseEvidenceContext is not release approval.

RequirementTransfer is not approval satisfaction.

ReviewRequest is not approval.

Forbidden type meanings:

- ApprovalTransfer
- ExecutionTransfer
- AuthorityTransfer
- MemoryOwnershipTransfer
- SourceApplyPermission
- ReleaseApproval
- WorkflowContinuation
- PolicySatisfied

## Evidence reference boundary

## PR91 Handoff allowedUse and Evidence Reference Model

Evidence references are evidence only.

PR91 adds bounded allowed-use semantics to each evidence reference.

Evidence references must declare allowed uses.

Allowed use is not authority.

Allowed use bounds how evidence may support context, review, debugging, validation, requirement evaluation, traceability, audit, policy input, handoff explanation, or human decision support.

Allowed use cannot be approval, execution permission, policy satisfaction, workflow continuation, source apply permission, memory promotion permission, release approval, or authority transfer.

ApprovalDecision may be cited only as evidence.

ToolGateDecision is not approval.

DogfoodReceipt is not approval.

CriticReview is not approval.

ApprovalPackage is not approval.

Approval decisions may be cited only as evidence.

Gate decisions may be cited only as evidence.

Dogfood receipts may be cited only as evidence.

Critic/model/retrieval output may be cited only as advisory evidence.

Safe allowed-use values:

- Context
- Review
- Debugging
- Validation
- Traceability
- RequirementEvaluation
- HumanDecisionSupport
- AuditReference
- PolicyInput
- HandoffExplanation

PolicyInput means evidence may be considered during policy or approval requirement evaluation. It does not satisfy policy.

HumanDecisionSupport means evidence may support a human decision. It does not make that decision.

AuditReference means evidence may support later audit. Audit is not approval.

Forbidden evidence meanings:

- ExecutionPermission
- TransferredApproval
- PolicySatisfied
- SourceApplyAllowed
- MemoryPromotionAllowed
- ReleaseApproved
- WorkflowContinuationAllowed

## Constraint boundary

Constraints describe what is still required.

Allowed constraints include:

- RequiresHumanReview
- RequiresApprovalDecision
- RequiresPolicyEvaluation
- RequiresValidation
- RequiresDogfoodReceipt
- RequiresSourceApplyApproval
- RequiresMemoryPromotionApproval
- EvidenceOnly
- DoNotExecute
- DoNotMutateSource
- DoNotPromoteMemory
- DoNotContinueWorkflow

Forbidden constraint meanings:

- ApprovalGranted
- ExecutionGranted
- SourceApplyGranted
- MemoryPromotionGranted
- WorkflowContinuationGranted
- ReleaseApproved

## Metadata boundary

Metadata must be safe, small, valid JSON, versioned, and factual.

Metadata may describe evidence and context.

Metadata must not contain hidden reasoning, raw chain-of-thought, scratchpad text, raw prompts, raw completions, raw tool output, whole patches, or authority-granting fields.

Metadata flags such as grantsApproval, grantsExecution, mutatesSource, promotesMemory, startsWorkflow, satisfiesPolicy, and transfersAuthority must remain false if present.

## PR92 No Authority Transfer Validator

PR92 adds a pure validator for authority-transfer attempts.

The validator rejects handoffs that attempt to transfer approval, execution permission, workflow authority, source apply authority, memory promotion authority, memory ownership, policy satisfaction, or release approval.

The validator does not send handoffs.

The validator does not receive handoffs.

The validator does not execute anything.

The validator does not write SQL.

The validator does not add API/CLI/runtime wiring.

It only proves the handoff is structurally non-authoritative.

## PR93 Durable Agent Handoff Store

PR93 adds durable SQL-backed handoff recording only.

The durable handoff store records handoff evidence and constraints; it does not deliver, route, execute, approve, satisfy policy, continue workflow, mutate source, promote memory, or release anything.

The store validates the handoff contract before persistence.

The store validates no-authority-transfer before persistence.

The store writes append-only a2a handoff records and child evidence/allowed-use/constraint rows.

The store writes a governance event for traceability.

The governance event is evidence only.

The durable handoff store is not:

- A2A transport
- A2A inbox
- A2A outbox
- workflow state
- workflow continuation
- approval satisfaction
- policy satisfaction
- execution permission
- source apply permission
- memory promotion permission
- release approval

Durable handoff records may be read for audit, traceability, review, debugging, validation, policy input, or human decision support.

Reading a durable handoff record does not grant authority.

## PR94 ThoughtLedger Handoff Entries

PR94 adds safe ThoughtLedger handoff entries.

ThoughtLedger may record that a handoff exists.

ThoughtLedger may summarize handoff context, evidence, allowed uses, and constraints.

ThoughtLedger does not approve handoffs.

ThoughtLedger does not send or receive handoffs.

ThoughtLedger does not execute handoffs.

ThoughtLedger does not continue workflow.

ThoughtLedger does not mutate source.

ThoughtLedger does not promote memory.

ThoughtLedger does not approve release.

ThoughtLedger entries must not contain hidden/private reasoning.

A ThoughtLedger handoff entry is evidence only.

It is not target-agent receipt.

It is not A2A delivery confirmation.

It is not policy satisfaction.

It is not memory ownership transfer.

It may help a human understand what durable handoff record exists, who prepared it, who it names as target, which subject it concerns, which evidence it cites, which allowed uses bound that evidence, and which constraints remain open.

It must keep all authority flags false.

## PR95 Grounding Evidence Reference Contract

PR95 adds a shared grounding evidence reference contract.

Grounding references support traceability and claim support.

Grounding references do not approve claims.

Grounding references do not execute tools.

Grounding references do not satisfy policy.

Grounding references do not continue workflow.

Grounding references do not mutate source.

Grounding references do not promote memory.

Grounding references do not approve release.

Grounding references do not create accepted memory.

Grounding references must not contain hidden/private reasoning.

Grounding gives a claim a receipt trail.

It does not make the claim true, approved, executable, promoted, or released.

## PR96 A2A Contract Validation Test Pack

PR96 adds a contract validation test pack for the Block I A2A spine.

The pack proves that handoff, allowed-use evidence, no-authority-transfer validation, durable handoff storage, ThoughtLedger handoff entries, and grounding evidence references compose without creating approval, execution permission, workflow continuation, source mutation, memory promotion, accepted memory, release approval, policy satisfaction, or authority transfer.

The pack adds no A2A runtime, transport, API, CLI, workflow runner, LangGraph, source apply, memory promotion, accepted memory, release approval, approval satisfaction, or execution path.

## PR97 Block I A2A Spine Receipt

PR97 closes Block I with a receipt.

The receipt states that Block I delivered the A2A Handoff Contract Spine, not A2A runtime.

The receipt preserves the evidence-only and no-authority-transfer boundaries.

The receipt explicitly refuses runtime, transport, workflow, source apply, memory promotion, accepted memory, release approval, approval satisfaction, and execution claims.

## Non-goals

PR90 does not deliver:

- A2A runtime
- handoff storage
- handoff transport
- handoff inbox/outbox
- API/CLI surface
- SQL migration
- workflow state
- workflow runner
- LangGraph
- source apply
- memory promotion
- release approval
- approval satisfaction
- approval decision recording
- execution engine
- UI
- L4 agents

PR93 does not deliver:

- A2A runtime
- handoff transport
- handoff delivery
- handoff inbox/outbox
- API/CLI surface
- workflow state
- workflow runner
- LangGraph
- source apply
- memory promotion
- release approval
- approval satisfaction
- execution engine
- UI

PR94 does not deliver:

- A2A runtime
- handoff transport
- handoff delivery
- handoff dispatch
- target-agent receipt
- API/CLI surface
- workflow state
- workflow runner
- LangGraph
- source apply
- memory promotion
- release approval
- approval satisfaction
- policy satisfaction
- execution engine
- UI

## Final statement

PR90 defines the envelope.

It does not send it, receive it, store it, route it, execute it, approve it, or make it powerful.

PR94 lets ThoughtLedger say the envelope exists.

It does not deliver the envelope.
