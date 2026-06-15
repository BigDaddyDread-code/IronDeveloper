# PR146 - Failed Workflow Diagnosis Report

PR146 adds a read-only failed workflow diagnosis report surface. This is the Block O failed-workflow diagnosis slice.

The report turns existing workflow and governance trace evidence into a safe operational diagnosis package. It helps humans inspect failed, blocked, halted, or incomplete workflow evidence without treating the report as root-cause proof or as permission to repair anything.

## Boundary

The Failed Workflow Diagnosis Report is read-only.

It does not:

- rerun workflow
- retry workflow
- resume workflow
- transition workflow state
- prove root cause
- repair anything
- create tickets
- invoke tools
- dispatch agents
- call models
- build prompts
- apply source
- apply patches
- promote memory
- activate retrieval
- satisfy approval
- satisfy policy
- approve release
- create, update, or delete governance events
- expose hidden/private reasoning, raw prompts, raw completions, raw tool output, source content, or patch payloads

It does not invoke tools. It does not apply source. It does not promote memory. It does not dispatch agents, call models, apply patches, activate retrieval, or move workflow state.

## API

`GET /api/v1/workflow/failures/{workflowRunId}/diagnosis-report`

Supported query parameters:

- `projectReferenceId`
- `workflowStepId`
- `correlationId`
- `includeTraceTimeline`
- `includeRecommendations`
- `takeTraceItems`

The route is GET-only. There are no repair, retry, resume, transition, approve, ticket creation, tool invocation, dispatch, source apply, patch apply, memory promotion, retrieval activation, or model execution routes.

## Output

The report includes:

- safe report identity and workflow references
- failure signals inferred from safe governance trace summaries
- diagnosis hypotheses marked as not root-cause proof
- missing evidence notes marked as not approval or policy satisfaction
- optional trace timeline as safe summaries only
- optional investigation recommendations marked as non-executable and non-mutating
- boundary warnings

## Validation

PR146 adds focused API, governance boundary, and static boundary tests for the report surface.

Review line:

PR146 reads the scorch marks. It does not restart the engine.
