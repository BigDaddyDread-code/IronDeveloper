# PR100 Real DB Workflow Checkpoint Smoke Receipt

## Verdict

Passed when `Database/smoke-workflow-checkpoint.ps1` completes against a database with PR98, PR99, and PR100 workflow migrations applied.

## What the smoke proves

- `workflow.usp_WorkflowRun_Create` can create the parent workflow run.
- `workflow.usp_WorkflowStep_Create` can create the parent workflow step.
- `workflow.usp_WorkflowCheckpoint_Create` can create a durable workflow checkpoint.
- Checkpoint evidence references persist.
- Checkpoint grounding references persist.
- The checkpoint record persists as evidence only.

## Boundary

The smoke does not execute, start, continue, resume, restore, dispatch, approve, mutate source, promote memory, satisfy policy, transfer authority, expose API/CLI/UI, or create runtime workflow behaviour.

Workflow checkpoint storage is a filing cabinet for safe checkpoint facts. It is not a workflow resume token.
