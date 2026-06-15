# PR142 Apply Preview CLI

## Scope

PR142 adds a read-only CLI command for inspecting the PR141 Apply Preview API response:

```text
irondev workflow apply-preview --workflow-run <workflowRunId> --workflow-step <workflowStepId>
```

Optional filters:

```text
--controlled-apply-plan <id>
--take-dry-runs 10
--no-dry-runs
--json
```

## Boundary

This command is a preview reader only.

It does not perform an apply dry-run.
It does not create dry-run receipts.
It does not apply source.
It does not apply patches.
It does not read source files.
It does not mutate files.
It does not run commands.
It does not invoke tools.
It does not run validation.
It does not run rollback.
It does not satisfy approval.
It does not satisfy policy.
It does not transition workflow state.
It does not promote memory.
It does not activate retrieval.
It does not dispatch agents.
It does not call models.

## API route

The CLI calls only:

```text
GET /api/workflow/apply-preview/{workflowRunId}/{workflowStepId}
```

Allowed query parameters:

```text
controlledApplyPlanReferenceId
takeDryRuns
includeDryRunSummaries
```

## Output contract

Text output highlights:

- preview status
- workflow run and step IDs
- controlled apply plan reference when present
- safe summary lines
- dry-run receipt summaries when requested
- missing evidence
- gates
- risks
- warnings
- boundary reminders

JSON output wraps the API response in the standard CLI envelope and adds CLI boundary flags that remain false for source apply, dry-run execution, patch apply, approval satisfaction, policy satisfaction, workflow transition, memory promotion, retrieval activation, dispatch, and model calls.

## Review line

PR142 prints the apply-preview docket. It does not stamp it, execute it, or touch the source tree.
