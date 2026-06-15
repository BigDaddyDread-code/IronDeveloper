# PR140 - Apply Dry-run Store

## Verdict

Apply dry-run records can now be stored and retrieved as durable, non-authoritative review evidence.

This is a receipt store. It is not a dry-run runner.

## Boundary

PR140 stores the dry-run receipt. It does not run the dry-run.

The store does not:

- perform a dry-run
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
- expose API, CLI, or UI

## Added contract

- `IApplyDryRunStore`
- `ApplyDryRunCreateRequest`
- `ApplyDryRunRecord`
- `ApplyDryRunSummary`
- `ApplyDryRunStoreValidator`
- `SqlApplyDryRunStore`
- `workflow.ApplyDryRunRecord`
- `workflow.usp_ApplyDryRun_Create`
- `workflow.usp_ApplyDryRun_Get`
- `workflow.usp_ApplyDryRun_ListByWorkflowRun`
- `workflow.usp_ApplyDryRun_ListByControlledApplyPlan`

## Safety invariants

- SQL remains the source of truth.
- Stored rows are append-only.
- Runtime role can execute approved procedures and read records only.
- Direct runtime insert, update, and delete are denied.
- C# validation and SQL procedures both reject unsafe text.
- SQL trigger rejects unsafe direct table inserts.
- Authority/action flags must stay false.
- Stored receipt status is evidence only.

## Review line

PR140 stores the dry-run receipt. It does not run the dry-run.
