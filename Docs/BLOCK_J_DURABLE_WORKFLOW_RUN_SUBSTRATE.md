# Block J Durable Workflow Run Substrate

PR98 adds the first durable workflow run storage surface for Block J.

This is not workflow orchestration. It is the ledger that lets later orchestration work point at a stable, append-only workflow run record.

## What exists

- `WorkflowRun`
- `WorkflowRunStep`
- `WorkflowRunEvidenceReference`
- `WorkflowRunGroundingReference`
- `IWorkflowRunStore`
- `SqlWorkflowRunStore`
- `Database/migrate_workflow_run.sql`
- `Database/smoke-workflow-run.ps1`

## Boundary

A workflow run record means a governed workflow context was recorded.

It does not mean:

- a workflow started
- a workflow continued
- an agent was dispatched
- a handoff was received
- a tool executed
- approval was granted
- policy was satisfied
- source was applied
- release was approved
- memory was promoted
- authority transferred

## SQL ownership

SQL is source of truth for the durable workflow run record.

The `workflow` schema owns:

- `workflow.WorkflowRun`
- `workflow.WorkflowRunStep`
- `workflow.WorkflowRunEvidenceReference`
- `workflow.WorkflowRunGroundingReference`
- `workflow.usp_WorkflowRun_Create`
- `workflow.usp_WorkflowRun_Get`
- `workflow.usp_WorkflowRun_ListByProject`
- `workflow.usp_WorkflowRun_ListByCorrelation`
- `workflow.usp_WorkflowRun_ListBySubject`

The runtime store calls stored procedures only. It does not create schema at runtime.

## Status vocabulary

Workflow run status is a reporting state, not execution permission.

Allowed statuses:

- `Created`
- `ReadyForReview`
- `Blocked`
- `Completed`
- `Failed`
- `Cancelled`
- `Superseded`

No status grants execution, approval, policy satisfaction, source apply, release approval, authority transfer, or memory promotion.

## Evidence and grounding

Evidence references are pointers to existing proof surfaces. They are not copied raw payloads.

Grounding references let a workflow run point at grounding evidence. They do not create grounding authority and do not promote memory.

## PR98 non-goals

PR98 adds no:

- workflow runner
- LangGraph runtime
- orchestrator
- scheduler
- inbox or outbox processor
- message bus
- agent dispatch
- A2A transport
- tool executor
- source apply
- memory promotion
- approval satisfaction
- policy activation
- API endpoint
- CLI command
- UI surface

PR98 records the workflow envelope. It does not move it.
## PR99 - Durable Workflow Step Store

PR99 adds durable workflow step storage over the PR98 workflow schema. It reuses the existing `workflow.WorkflowRunStep`, `workflow.WorkflowRunEvidenceReference`, and `workflow.WorkflowRunGroundingReference` tables and adds the stored procedure/read-model surface needed to record individual workflow step facts.

### What changed

- Added `workflow.usp_WorkflowStep_Create`.
- Added `workflow.usp_WorkflowStep_Get`.
- Added `workflow.usp_WorkflowStep_ListByRun`.
- Added `workflow.usp_WorkflowStep_ListByCorrelation`.
- Added `workflow.usp_WorkflowStep_ListBySubject`.
- Added `workflow.WorkflowRunStep.SequenceNumber` for deterministic step ordering.
- Added `CK_WorkflowRunStep_SequenceNumber_Positive`.
- Extended the safe step-type vocabulary for policy/debug/review evidence steps.
- Added `IWorkflowStepStore` and `SqlWorkflowStepStore`.

### Boundary

A workflow step row is evidence about a workflow step. It is not execution.

PR99 does not:

- start a workflow
- continue a workflow
- dispatch a workflow
- execute a workflow step
- approve anything
- satisfy policy
- mutate source
- promote memory
- transfer authority
- create A2A transport
- expose API/CLI/UI/runtime wiring

A persisted step means a validated workflow step fact was recorded with evidence and grounding references. It does not mean the step was performed.

### Source-of-truth rule

SQL remains the source of truth. The runtime store uses stored procedures only and does not create or mutate schema at runtime.


## PR101 - Step Input/Output Reference Model

PR101 adds bounded workflow step input/output reference models.

Input references describe what a step record may refer to.

Output references describe what a step record may record as output.

### Boundary

Input references do not consume input.

Output references do not produce output.

The model does not execute workflow.

The model does not continue workflow.

The model does not resume workflow.

The model does not dispatch agents.

The model does not call tools.

The model does not call models.

The model does not mutate source.

The model does not promote memory.

The model does not create accepted memory.

The model does not approve release.

The model does not satisfy approval requirements.

Input/output statuses are stored facts, not runtime actions.

### Source-of-truth rule

PR101 is Core contract only. It adds no SQL schema, runtime store, API, CLI, UI, runner, scheduler, orchestrator, dispatcher, tool execution, model call, source apply, memory promotion, release approval, or approval-satisfaction path.

PR101 labels the step's input and output receipts. It does not consume the input or produce the output.


## PR100 - Durable Workflow Checkpoint Store

PR100 adds durable workflow checkpoint storage over the PR98/PR99 workflow substrate. Checkpoints record safe workflow state, evidence references, grounding references, and review support facts.

### What changed

- Added `workflow.WorkflowCheckpoint`.
- Added `workflow.WorkflowCheckpointEvidenceReference`.
- Added `workflow.WorkflowCheckpointGroundingReference`.
- Added `workflow.usp_WorkflowCheckpoint_Create`.
- Added `workflow.usp_WorkflowCheckpoint_Get`.
- Added `workflow.usp_WorkflowCheckpoint_ListByRun`.
- Added `workflow.usp_WorkflowCheckpoint_ListByStep`.
- Added `workflow.usp_WorkflowCheckpoint_ListByCorrelation`.
- Added `workflow.usp_WorkflowCheckpoint_ListBySubject`.
- Added `IWorkflowCheckpointStore` and `SqlWorkflowCheckpointStore`.

### Boundary

A workflow checkpoint row is a safe state/evidence snapshot. It is not a workflow resume token.

PR100 does not:

- start a workflow
- continue a workflow
- resume a workflow
- restore a workflow
- dispatch an agent
- execute a workflow step
- execute a tool
- approve anything
- satisfy policy
- mutate source
- promote memory
- transfer authority
- create A2A transport
- expose API/CLI/UI/runtime wiring

A persisted checkpoint means safe checkpoint facts were recorded with evidence and grounding references. It does not mean workflow execution can continue from that checkpoint.

### Source-of-truth rule

SQL remains the source of truth. The runtime store uses stored procedures only and does not create or mutate schema at runtime.
