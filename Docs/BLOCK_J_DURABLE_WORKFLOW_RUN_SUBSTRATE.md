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


## PR102 - Failure and Retry State Model

PR102 adds bounded workflow failure and retry state models.

Failure state records safe failure facts.

Retry state records reviewable retry facts and recommendations.

### Boundary

Failure state does not retry workflow.

Retry state does not retry workflow.

Retry recommendation does not grant retry permission.

Retry eligibility does not execute retry.

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

Failure/retry statuses are stored facts, not runtime actions.

### Source-of-truth rule

PR102 is Core contract only. It adds no SQL schema, runtime store, retry runner, retry scheduler, API, CLI, UI, runner, scheduler, orchestrator, dispatcher, tool execution, model call, source apply, memory promotion, release approval, or approval-satisfaction path.

PR102 writes the failure/retry note. It does not press retry.


## PR103 Workflow Read-only API

PR103 adds read-only workflow inspection endpoints.

The API exposes durable workflow run, step, checkpoint, evidence, and grounding facts.

The API is read-only.

The API does not create workflow records.

The API does not update workflow records.

The API does not delete workflow records.

The API does not execute workflow.

The API does not continue workflow.

The API does not resume workflow.

The API does not retry workflow.

The API does not dispatch agents.

The API does not call tools.

The API does not call models.

The API does not mutate source.

The API does not promote memory.

The API does not create accepted memory.

The API does not approve release.

The API does not satisfy approval requirements.

Statuses returned by the API are stored facts, not runtime actions.

PR103 lets clients read the workflow clipboard. It does not add any buttons.


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

## PR104 Workflow Inspection CLI Commands

PR104 adds read-only workflow inspection CLI commands.

The CLI exposes durable workflow run, step, checkpoint, evidence, and grounding facts.

The CLI is read-only.

The CLI does not create workflow records.

The CLI does not update workflow records.

The CLI does not delete workflow records.

The CLI does not execute workflow.

The CLI does not continue workflow.

The CLI does not resume workflow.

The CLI does not retry workflow.

The CLI does not dispatch agents.

The CLI does not call tools.

The CLI does not call models.

The CLI does not mutate source.

The CLI does not promote memory.

The CLI does not create accepted memory.

The CLI does not approve release.

The CLI does not satisfy approval requirements.

Statuses printed by the CLI are stored facts, not runtime actions.

PR104 lets users inspect the workflow clipboard from CLI. It does not add any buttons.

## PR105 Workflow State Contract Tests

PR105 adds workflow state contract tests for Block J.

The tests prove workflow run, step, checkpoint, evidence, grounding, API inspection, and CLI inspection compose without creating runtime authority.

The tests do not add workflow state.

The tests do not add SQL storage.

The tests do not add API endpoints.

The tests do not add CLI commands.

The tests do not execute workflow.

The tests do not continue workflow.

The tests do not resume workflow.

The tests do not retry workflow.

The tests do not dispatch agents.

The tests do not call tools.

The tests do not call models.

The tests do not mutate source.

The tests do not promote memory.

The tests do not create accepted memory.

The tests do not approve release.

The tests do not satisfy approval requirements.

## PR106 - Block J Workflow State Receipt

PR106 closes Block J as a workflow state substrate receipt.
Block J is complete as durable workflow state and inspection infrastructure.
Block J does not add workflow runtime.
Block J does not execute workflow.
Block J does not continue workflow.
Block J does not resume workflow.
Block J does not retry workflow.
Block J does not dispatch agents.
Block J does not call tools.
Block J does not call models.
Block J does not mutate source.
Block J does not promote memory.
Block J does not create accepted memory.
Block J does not satisfy approval.
Block J does not approve release.

See `Docs/receipts/BLOCK_J_WORKFLOW_STATE_RECEIPT.md` for the final Block J receipt.
