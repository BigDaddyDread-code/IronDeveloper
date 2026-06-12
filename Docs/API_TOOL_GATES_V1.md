# Tool Gate API v1

## Purpose

Tool Gate API v1 evaluates or inspects gate decisions only. A gate decision is not approval, execution permission, tool execution, source apply, memory promotion, or governance authority.

This API exposes the frozen `AgentToolExecutionGate` contract through HTTP. It does not execute the requested tool and does not create approval, source-apply, memory-promotion, workflow, CLI, UI, SQL, schema, or stored-procedure capability.

## Endpoint list

### POST `/api/v1/tool-gates/evaluations`

Evaluates a gate preview for a tool request currently available in the API-local PR61 request inspection cache.

Request fields:

| Field | Required | Meaning |
| --- | --- | --- |
| `projectId` | yes | Project scope for the request and gate preview. |
| `toolRequestId` | yes | Existing API-local tool request inspection ID. |
| `evidenceRefs` | no | Caller-supplied evidence references for the preview. |
| `correlationId` | no | Caller correlation ID. |
| `reason` | no | Public reason for evaluating the gate. |

Rules:

- Unsupported fields are rejected.
- Missing tool request records return not found.
- Hidden chain-of-thought, raw prompts, raw completions, scratchpad text, system prompts, developer prompts, and private reasoning are rejected.
- Secret-like request material is rejected.
- Approval, execution, source apply, memory promotion, gate execution, audit approval, model authority, and external submission claims are rejected.
- The endpoint may create a non-durable API-local gate preview record.
- The endpoint must not run the tool, approve the request, apply source, promote memory, append tool execution audit, or create governance authority.

### GET `/api/v1/tool-gates/evaluations/{gateDecisionId}?projectId={projectId}`

Inspects an existing non-durable API-local gate preview.

Rules:

- `projectId` is required.
- The endpoint is read-only.
- Cross-project access returns not found.
- Hidden/private gate or request text is redacted before response data is returned.
- The response remains gate preview evidence, not approval, permission, execution, source apply, memory promotion, or governance.

## Response shape

Both endpoints return:

```json
{
  "status": "succeeded",
  "data": {},
  "toolRequestId": "tool-request-...",
  "gateDecisionId": "tool-gate-tool-request-...",
  "runId": "run-...",
  "evidenceId": "evidence-tool-gate-tool-request-...",
  "boundary": {
    "gateIsExecutor": false,
    "gateDecisionIsApproval": false,
    "gatePassIsHumanApproval": false,
    "toolRequestIsExecutionPermission": false,
    "toolExecuted": false,
    "requestApproved": false,
    "auditIsApproval": false,
    "sourceApplied": false,
    "memoryPromoted": false,
    "modelOutputIsAuthority": false,
    "endpointAccessIsExecutionPermission": false,
    "apiResponseStatusIsGovernance": false,
    "durable": false,
    "requestDurable": true,
    "gateDecisionDurable": false,
    "humanReviewRequiredForSourceApply": true,
    "humanReviewRequiredForMemoryPromotion": true
  },
  "mutationOccurred": false,
  "humanApprovalRequired": false,
  "warnings": [],
  "errors": []
}
```

For `POST`, `mutationOccurred` may be `true` only for creating the non-durable API-local gate preview record. It does not mean a tool ran.

For `GET`, `mutationOccurred` is always `false`.

## Decision data

Decision names are restricted to:

- `allowed_by_gate`
- `blocked_by_gate`
- `requires_approval`
- `unsupported`

Decision data includes:

- `decision`
- `reasons`
- `blockedReasons`
- `requiredApprovals`
- `requiredEvidence`
- `requiresHumanApproval`
- `requiresPolicyApproval`
- `requiresDryRun`
- `requiresGovernanceGate`
- `requiresSeparateExecutor`
- `durable`
- `requestDurable`
- `gateDecisionDurable`

The `requiresSeparateExecutor` field means exactly that: a separate executor would still be required. It is not execution permission.

## Error model

Errors use explicit categories:

- `validation_error`
- `not_found`
- `unsupported_field`
- `missing_tool_request`
- `content_too_large`
- `backend_contract_exception`

Validation failure is not a gate rejection. Gate block is not an internal error. Missing approval is not generic success. Unsupported tool requests do not fall back to another tool. Errors must not expose secrets or hidden reasoning.

## Gate-only guarantee

- Gate is not executor.
- Gate decision is not approval.
- Gate pass is not human approval.
- Gate result is not tool execution.
- Tool request is a request form, not execution permission.
- Audit evidence is not approval.
- API call is not approval.
- Endpoint access is not execution permission.
- API response status is not governance.
- Model output is not authority.
- Human review remains required for source apply.
- Human review remains required for memory promotion.

## Durability boundary

This API reads durable SQL-backed tool request records and creates non-durable API-local gate previews. It does not yet provide durable SQL source-of-truth gate decisions.

The gate preview record is:

- non-durable
- not SQL-backed
- not visible across app instances
- not durable across process restart
- not part of durable evidence/audit history
- not approval
- not execution permission
- not governance authority

Durable SQL Tool Request plus Tool Gate Decision storage is required before gate decisions can become backend evidence.

## Hidden reasoning boundary

The API must not accept or expose hidden chain-of-thought, private reasoning, scratchpad content, raw prompts, raw completions, system prompts, or developer prompts.

Stored unsafe request or gate text is redacted on readback.

## Known PR56 / PR61 exceptions

Tool request records are durable SQL-backed backend records.

PR62 still provides a non-durable API-local gate preview over durable tool request records.

PR62 does not satisfy durable SQL source-of-truth storage for gate decisions.

## Examples

### Evaluate a gate preview

```http
POST /api/v1/tool-gates/evaluations
Content-Type: application/json

{
  "projectId": 42,
  "toolRequestId": "tool-request-42-workspacediff-diff-42",
  "evidenceRefs": ["source-report-42"],
  "correlationId": "gate-42",
  "reason": "Preview gate outcome for this request"
}
```

### Inspect a gate preview

```http
GET /api/v1/tool-gates/evaluations/tool-gate-tool-request-42-workspacediff-diff-42?projectId=42
```

## Non-goals

Tool Gate API v1 does not add:

- tool execution endpoint
- approval endpoint
- source apply
- memory promotion
- tool execution audit append
- tool execution
- durable SQL-backed gate decision store
- hidden workflow or scheduler
- autonomous runner
- GitHub submission
- CLI or UI exposure
- SQL schema or stored procedure shape changes
- vector/index behavior changes
