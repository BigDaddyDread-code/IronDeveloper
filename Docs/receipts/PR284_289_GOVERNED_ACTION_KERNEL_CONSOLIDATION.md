# PR284-289 Governed Action Kernel Consolidation

Block AJ consolidates the governed-action authority kernel.

It does not add new autonomy.
It does not add new source mutation capability.
It does not add commit, push, PR, merge, release, or deployment behavior.
It does not add workflow runner behavior.
It does not add memory-informed planning.
It does not add UI, API, SQL, scheduler, worker, or autonomous runtime behavior.

## What is unified

Block AJ adds the kernel contracts around the existing governed action spine:

- `GovernedActionEnvelope` for authority-bearing action requests.
- `GovernedActionEvidenceRef` for safe evidence references.
- `GateEvidence` for validator outputs.
- `ConscienceDecisionService` for the single authority decision point.
- `ThoughtLedgerEntry` and `IThoughtLedgerWriter` for mandatory decision evidence.
- `FileBackedGovernanceEventStore` for append-only run-scoped event history.
- `governance action-envelope`, `governance verify`, and `governance events` CLI inspection/envelope commands.

## Boundary rules

Gates are validators, not authority.
Memory is evidence, not authority.
AI review is evidence, not authority.
Readiness reports are evidence, not authority.
Chat text is not authority.
UI state is not authority.
Runner state is not authority.

ConscienceDecisionService is the authority decision point.
ThoughtLedger is mandatory evidence for authority decisions.
Governance events are append-only history.

Gate evidence must match the governed-action family it is used for.
Source-apply dry-run eligibility evidence cannot satisfy source mutation.
Source-apply execution evidence cannot satisfy source rollback.
Gate evidence must also be bound to the same governed action id before a Conscience decision can allow execution.

## Current coverage

The kernel represents these action families:

- MemoryPromotion
- AcceptedMemoryMutation
- ToolExecution
- WorkspaceToolExecution
- SourceApply
- SourceRollback
- WorkflowContinuation
- ReleaseReadinessDecision
- ReleaseApproval
- DeploymentApproval
- MergeApproval
- TicketCreation
- SchedulerRunCreation
- AgentHandoffAuthorityClaim

Current gates can produce kernel-compatible `GateEvidence` for:

- MemoryKeyGate
- WorkspaceToolGate
- SourceApplyGate
- SourceApplyExecutionGate
- SourceRollbackGate

## Not yet migrated

Some legacy paths still use existing wrappers internally. AJ makes the authority evidence compatible with the kernel and records bypass lanes, but it does not migrate every legacy call path in one sweep.

Known remaining migration gaps:

- Future workflow continuation execution must use the kernel before mutating workflow state.
- Future release, deployment, merge, ticket creation, scheduler run creation, and agent handoff authority paths must use the kernel before acting.
- Durable SQL-backed governance event storage is not added here; AJ keeps file-backed run artifacts only.

## Forbidden by this PR

This PR does not add:

- new source mutation
- new rollback capability beyond AG
- git commit
- git push
- pull request creation
- merge
- release approval
- deployment approval
- policy satisfaction as authority
- workflow continuation execution
- memory-informed planning
- runner execution
- autonomous agent orchestration
- UI
- SQL
- API
- scheduler
- worker
- model-to-tool direct execution
- direct accepted-memory mutation
- direct source apply bypass
- direct rollback bypass

## Review line

PR284-289 consolidates authority into the governed-action kernel. It does not add new autonomy or new mutation capability.

## Killjoy line

AJ is finished when IronDev has one authority kernel and no secret side doors.
