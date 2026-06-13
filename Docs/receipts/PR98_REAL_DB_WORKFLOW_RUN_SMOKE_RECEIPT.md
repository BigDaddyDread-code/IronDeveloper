# PR98 Real DB Workflow Run Smoke Receipt

This receipt documents the real database smoke path for PR98.

PR98 adds durable workflow run storage only. A workflow run row means evidence about a governed workflow was recorded. It does not start a workflow, continue a workflow, dispatch an agent, execute a tool, grant approval, satisfy policy, apply source, approve release, transfer authority, or promote memory.

## Databases

Run the same sequence against both configured real databases when available:

- IronDeveloper
- IronDeveloper_Test

Run database-backed smoke commands sequentially. The shared SQL test databases are not safe for parallel destructive reset bands.

## Commands

```powershell
.\Database\apply-migrations.ps1 -ConnectionString "<connection-string>"
.\Database\verify-migrations.ps1 -ConnectionString "<connection-string>"
.\Database\smoke-workflow-run.ps1 -ConnectionString "<connection-string>"
```

## Expected proof

The smoke script verifies:

- `workflow.WorkflowRun` exists.
- `workflow.usp_WorkflowRun_Create` exists.
- `workflow.usp_WorkflowRun_Get` exists.
- `workflow.usp_WorkflowRun_ListByCorrelation` exists.
- `workflow.usp_WorkflowRun_ListBySubject` exists.
- A workflow run can be recorded with steps, evidence references, and grounding references.
- The record can be read by ID, correlation, and subject.
- No approval decision is created.
- No policy decision event is created.
- No dogfood receipt is created.
- No tool request or tool gate decision is created.
- No A2A handoff is created.
- No source apply state is created.
- No memory promotion state is created.

## Boundary

Workflow run storage is evidence storage only.

- Workflow run is not workflow execution.
- Workflow run is not workflow continuation.
- Workflow run is not agent dispatch.
- Workflow run is not tool execution.
- Workflow run is not approval.
- Workflow run is not policy satisfaction.
- Workflow run is not release approval.
- Workflow run is not source apply.
- Workflow run is not memory promotion.
- Workflow run is not authority transfer.

This receipt is intentionally boring. It proves the filing cabinet works; it does not install a conveyor belt.