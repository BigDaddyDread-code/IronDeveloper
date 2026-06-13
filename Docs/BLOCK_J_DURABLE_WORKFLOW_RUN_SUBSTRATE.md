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