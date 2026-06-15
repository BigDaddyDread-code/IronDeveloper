# PR157 - Workflow Run/Step Viewer UI

## Summary

PR157 adds a read-only Tauri UI route for inspecting workflow runs, workflow steps, and safe workflow evidence references.

The UI route is:

- `/workflows/runs`

The backend read surface remains the existing workflow read-only API:

- `GET /api/v1/workflow/runs`
- `GET /api/v1/workflow/runs/by-correlation/{correlationId}`
- `GET /api/v1/workflow/runs/{workflowRunId}`
- `GET /api/v1/workflow/runs/{workflowRunId}/steps`
- `GET /api/v1/workflow/runs/{workflowRunId}/steps/{workflowRunStepId}`

## Boundary

This is a viewer only.

Workflow visibility is not workflow authority.

Workflow status is not transition permission.

Step status is not execution permission.

Refresh is not retry.

This UI cannot start, continue, transition, retry, repair, execute workflow, invoke tools, dispatch agents, apply source, or release software.

## What changed

- Added workflow run and workflow step read-only API client contracts.
- Added the `/workflows/runs` route mapping into the existing Governance shell area.
- Added `WorkflowRunStepViewerRoute` and view-model helpers.
- Added scoped viewer styling.
- Added Playwright coverage for the read-only UI behaviour.
- Added static backend guard tests for route, API, and boundary wording.

## What did not change

- No backend controller was added.
- No SQL migration was added.
- No stored procedure was added or changed.
- No workflow runner was added.
- No workflow transition behaviour was added.
- No workflow retry behaviour was added.
- No workflow repair behaviour was added.
- No tool invocation was added.
- No agent dispatch was added.
- No source apply path was added.
- No release approval path was added.
- No API write endpoint was added.
- No CLI command was added.

## Allowed UI actions

- Search
- Refresh
- Clear Filters
- Copy Workflow ID
- Copy Step ID
- Copy Correlation ID
- Open Run
- Open Step
- Open Trace
- Open Timeline
- Open Diagnosis
- Open Agent Health
- Open Tool Gate Ledger
- Open Dogfood Receipts
- Open Approval Packages

These actions inspect or navigate to read-only evidence. They do not move workflow state.

## Review line

PR157 shows the workflow path. It does not move the workflow forward.
