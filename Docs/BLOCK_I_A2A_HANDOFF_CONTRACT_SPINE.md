# Block I A2A Handoff Contract Spine

## Status

PR90 begins Block I with the Agent Handoff contract.

This is contract/model/tests/docs only.

No SQL, API, CLI, runtime wiring, workflow runner, A2A runtime, LangGraph runtime, source apply, memory promotion, release approval, execution engine, transport, message bus, inbox, outbox, dispatcher, receiver, scheduler, orchestrator, model call, approval lookup, approval recording, approval satisfaction checker, policy decision recording, repository, stored procedure, durable handoff store, or UI is added.

## Core rule

Block I begins with the Agent Handoff contract.

A handoff transfers context and evidence only.

A handoff does not transfer approval, execution permission, memory ownership, workflow authority, source apply authority, memory promotion authority, or release authority.

A handoff may cite approval decisions only as evidence.

A handoff may cite gate decisions, dogfood receipts, critic output, validation output, ThoughtLedger references, and governance events only as evidence.

A handoff does not send itself.

A handoff does not create A2A runtime messages.

A handoff does not add API/CLI/SQL/runtime wiring.

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

## Final statement

PR90 defines the envelope.

It does not send it, receive it, store it, route it, execute it, approve it, or make it powerful.
