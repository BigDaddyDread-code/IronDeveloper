# PR141 Apply Preview API

This receipt documents the Apply Preview API boundary.

The API exposes read-only inspection of controlled apply evidence by assembling a preview from safe workflow identifiers and existing apply dry-run receipt summaries.

## Endpoint

`GET /api/workflow/apply-preview/{workflowRunId}/{workflowStepId}`

Query parameters:

- `controlledApplyPlanReferenceId`
- `takeDryRuns`
- `includeDryRunSummaries`

## Boundary

Apply preview is a review aid only.

It does not:

- perform an apply dry-run
- apply source
- apply a patch
- read source files
- mutate files
- run commands
- invoke tools
- run validation
- run rollback
- satisfy approval
- satisfy policy
- transition workflow state
- promote memory
- activate retrieval
- dispatch agents
- call models

Dry-run summaries are receipts only. API status is not governance. Endpoint access is not execution permission.

Human review remains required for source apply and memory promotion.

## Persistence

This PR adds no SQL schema, table, procedure, trigger, index, migration, or durable write path.

The preview service reads existing `IApplyDryRunStore` summaries only. It does not create, update, delete, or append any record.

## Expected response posture

Responses must keep every action and authority flag false:

- `CanExecuteDryRun = false`
- `IsDryRunExecution = false`
- `CanApplySource = false`
- `AppliesPatch = false`
- `ReadsSourceFiles = false`
- `MutatesFiles = false`
- `RunsCommand = false`
- `InvokesTool = false`
- `RunsValidation = false`
- `RunsRollback = false`
- `SatisfiesApproval = false`
- `SatisfiesPolicy = false`
- `TransitionsWorkflow = false`
- `PromotesMemory = false`
- `ActivatesRetrieval = false`
- `DispatchesAgent = false`
- `CallsModel = false`

Review line:

PR141 shows the dry-run receipts in the window. It does not open the door.
