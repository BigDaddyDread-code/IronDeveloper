# Dogfood Loop API v1

## Purpose

Dogfood Loop API v1 creates or inspects dogfood loop receipts only. A dogfood receipt is not release approval, autonomous workflow execution, tool execution, source apply, memory promotion, or governance authority.

This is API exposure for manual dogfood evidence. It is not a workflow runner.

## Endpoint list

POST `/api/v1/dogfood-loops`

GET `/api/v1/dogfood-loops/{dogfoodLoopId}?projectId={projectId}`

There is no run, approve, apply, promote, execute, or workflow endpoint in v1.

## Request parameters

`POST /api/v1/dogfood-loops` accepts:

- `projectId`
- `summary`
- `goal`
- `agentRunIds`
- `criticReviewRunIds`
- `memoryImprovementRunIds`
- `toolRequestIds`
- `toolGateDecisionIds`
- `evidenceRefs`
- `observations`
- `blockedReasons`
- `correlationId`

The API rejects unsupported fields and values that imply release approval, execution, source apply, memory promotion, accepted memory, vector authority, audit approval, model authority, or hidden workflow execution.

## Response shape

Responses use the standard v1 exposure envelope:

- `status`
- `data`
- `dogfoodLoopId`
- `runId`
- `receiptId`
- `evidenceId`
- `boundary`
- `mutationOccurred`
- `humanApprovalRequired`
- `warnings`
- `errors`

Allowed statuses are:

- `receipt_created`
- `receipt_found`
- `validation_error`
- `not_found`

The API does not return `release_approved`, `ready_to_ship`, `approved`, `executed`, `applied`, `promoted`, or `workflow_completed`.

## Boundary status

Every response includes explicit boundary flags:

```json
{
  "dogfoodReceiptIsReleaseApproval": false,
  "dogfoodLoopIsAutonomousWorkflow": false,
  "toolExecuted": false,
  "requestApproved": false,
  "gateExecuted": false,
  "gateIsExecutor": false,
  "sourceApplied": false,
  "memoryPromoted": false,
  "collectiveMemoryWritten": false,
  "vectorAuthorityWritten": false,
  "auditIsApproval": false,
  "modelOutputIsAuthority": false,
  "endpointAccessIsExecutionPermission": false,
  "apiResponseStatusIsGovernance": false,
  "durable": true,
  "containsNonDurableReferences": true,
  "humanReviewRequiredForSourceApply": true,
  "humanReviewRequiredForMemoryPromotion": true
}
```

## Receipt-only guarantee

Dogfood receipt is not release approval.

Dogfood receipt is evidence, not release approval.

Dogfood loop is not autonomous workflow.

The API does not execute tools, run tests, run builds, invoke critic or memory-improvement services, evaluate a gate as an executor, apply source, promote memory, write accepted memory, submit GitHub reviews, create pull requests, or create governance authority.

## Authority boundaries

- API call is not approval.
- Endpoint access is not execution permission.
- API response status is not governance.
- Audit evidence is not approval.
- Gate is not executor.
- Gate pass is not human approval.
- Tool request is request form, not execution permission.
- Dogfood receipt is not release readiness.
- Model output is advisory only.
- Human review remains required for source apply.
- Human review remains required for memory promotion.

## Durability boundary

This API stores durable SQL-backed dogfood receipt evidence linked to the governance event spine. Durable SQL storage does not make the receipt release approval. The receipt is still evidence only; it is not release approval, policy satisfaction, execution permission, source apply, memory promotion, or workflow continuation.

`durable` is `true` for the dogfood receipt record in v1.

Tool request records, PR75 gate decision records, and PR78 dogfood receipt records are durable SQL-backed data. Caller-supplied references are still labelled separately and must not be treated as authority by themselves.

References that are caller-supplied or not backed by their own backend records are labelled non-durable and must not be treated as approval, execution permission, release evidence, source apply, or memory promotion by themselves.

## Hidden reasoning boundary

The API rejects request text containing hidden chain-of-thought, private reasoning, raw prompt, raw completion, scratchpad, system prompt, developer prompt, or secret-like material.

Readback sanitises stored receipt text that is marked as containing private reasoning. The API exposes safe summaries, evidence references, observations, blocked reasons, and boundary status only.

## Error model

Errors use explicit categories:

- `validation_error`
- `unsupported_field`
- `content_too_large`
- `not_found`

Missing evidence is not hidden. Non-durable references are not silently treated as durable. Validation failure is not dogfood failure. Dogfood warning is not release rejection or release approval.

## Known PR56, PR61, and PR62 exceptions

PR56 froze the backend contract with known broad-lane exceptions.

PR61 Tool Request API v1 uses durable SQL-backed tool request records once the durable Tool Request Store has landed.

PR62/75 Tool Gate API v1 records durable SQL-backed gate decision evidence.

PR78 removes the dogfood-loop durability limitation by storing dogfood receipts in SQL and linking them to governance events. PR63 endpoint semantics remain receipt-only and non-authoritative.

## Examples

Create a receipt:

```http
POST /api/v1/dogfood-loops
```

```json
{
  "projectId": 123,
  "summary": "Manual dogfood loop receipt for tool request review.",
  "goal": "Collect evidence for human review.",
  "agentRunIds": ["agent-run-123"],
  "criticReviewRunIds": ["critic-run-123"],
  "memoryImprovementRunIds": ["memory-run-123"],
  "toolRequestIds": ["tool-request-123"],
  "toolGateDecisionIds": ["tool-gate-123"],
  "evidenceRefs": [
    {
      "refType": "source_report",
      "refId": "source-report-123",
      "summary": "Caller-supplied source report reference.",
      "source": "caller"
    }
  ],
  "observations": ["Human review is still required."],
  "blockedReasons": [],
  "correlationId": "dogfood-123"
}
```

Inspect a receipt:

```http
GET /api/v1/dogfood-loops/dogfood-loop-123-dogfood-123?projectId=123
```

The response remains receipt-only and non-authoritative, even though the receipt is durable SQL-backed evidence.
