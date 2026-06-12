# Tool Request API v1

## Purpose

Tool Request API v1 exposes the frozen `AgentToolRequest` contract through HTTP.

Tool Request API v1 creates or inspects tool request evidence only. A tool request is not approval, execution permission, tool execution, source apply, memory promotion, or governance.

This API validates request shape and stores a durable SQL-backed tool request record. It does not execute the requested tool and does not create any approval, gate, source-apply, memory-promotion, workflow, CLI, UI, or runtime capability.

Tool request records are durable SQL-backed backend records and are linked to the governance event spine.

## Endpoints

### POST `/api/v1/tool-requests`

Creates a typed tool request from caller-supplied metadata.

Request fields:

| Field | Required | Meaning |
| --- | --- | --- |
| `projectId` | yes | Project scope for the request record. |
| `requestedTool` | yes | Tool kind, such as `workspace.diff`, `test.run`, `build.run`, or `patch.proposal`. |
| `requestKind` | yes | Request type, such as `readOnlyInspection`, `testExecutionRequest`, or `patchProposalRequest`. |
| `summary` | yes | Public purpose for requesting the tool. |
| `payload` | yes | Public request payload metadata. |
| `evidenceRefs` | no | Caller-supplied evidence reference IDs. |
| `correlationId` | no | Caller correlation ID. |
| `reason` | no | Public reason for the request. |
| `requestedByAgentRunId` | no | Existing agent-run reference if this request came from a prior run. |

Rules:

- Unsupported fields are rejected.
- Unsupported tool kinds are rejected.
- Unsupported request kinds are rejected.
- Hidden chain-of-thought, raw prompts, raw completions, scratchpad text, system prompts, developer prompts, and private reasoning are rejected.
- Secret-like request material is rejected.
- Approval, execution, source apply, memory promotion, gate execution, audit approval, model authority, and external submission claims are rejected.
- Payload content is bounded.
- The endpoint may create a durable SQL-backed tool request record.
- The record is durable across process restart.
- The record is SQL-backed and linked to durable governance event history.
- The endpoint must not run the tool, approve the request, execute a gate, apply source, promote memory, or create governance authority.

### GET `/api/v1/tool-requests/{toolRequestId}?projectId={projectId}`

Inspects an existing durable SQL-backed tool request record.

Rules:

- `projectId` is required.
- The endpoint is read-only.
- Cross-project access returns not found.
- Hidden/private request text is redacted before response data is returned.
- The response remains request-only evidence, not approval, permission, execution, source apply, memory promotion, or governance.

## Response shape

Both endpoints return:

```json
{
  "status": "succeeded",
  "data": {},
  "toolRequestId": "tool-request-...",
  "runId": "tool-request-api-v1-...",
  "evidenceId": "evidence-tool-request-...",
  "boundary": {
    "toolRequestIsExecutionPermission": false,
    "durable": true,
    "toolExecuted": false,
    "requestApproved": false,
    "auditIsApproval": false,
    "gateIsExecutor": false,
    "sourceApplied": false,
    "memoryPromoted": false,
    "modelOutputIsAuthority": false,
    "endpointAccessIsExecutionPermission": false,
    "apiResponseStatusIsGovernance": false,
    "humanReviewRequiredForSourceApply": true,
    "humanReviewRequiredForMemoryPromotion": true
  },
  "mutationOccurred": false,
  "humanApprovalRequired": false,
  "warnings": [],
  "errors": []
}
```

For `POST`, `mutationOccurred` may be `true` only for creating the durable SQL-backed tool request record. It does not mean a tool ran.

For `GET`, `mutationOccurred` is always `false`.

## Durability boundary

Tool Request API v1 uses a durable SQL-backed tool request store.

The inspection record is:

- durable
- SQL-backed
- visible across app instances when backed by the same SQL database
- durable across process restart
- linked to durable governance event history
- not approval
- not execution permission
- not governance authority

Durable SQL source-of-truth storage for tool requests is now present; gate decisions and execution audit remain separate concepts.

## Error model

Errors use explicit categories:

- `validation_error`
- `not_found`
- `unsupported_field`
- `unsupported_tool`
- `unsupported_request_kind`
- `content_too_large`
- `backend_contract_exception`

Validation failure is not a gate rejection. Missing approval is not generic success. Unsupported tool requests do not fall back to another tool.

## Authority boundaries

- Tool request is a request form, not execution permission.
- Tool request is not approval.
- Tool request is not tool execution.
- Tool request is not source apply.
- Tool request is not memory promotion.
- Audit evidence is not approval.
- Gate is not executor.
- Endpoint access is not execution permission.
- API response status is not governance.
- Model output is not authority.
- Human review remains required for source apply.
- Human review remains required for memory promotion.

## Hidden reasoning boundary

The API must not accept or expose hidden chain-of-thought, private reasoning, scratchpad content, raw prompts, raw completions, system prompts, or developer prompts.

Stored unsafe request text is redacted on readback.

## Examples

### Create a read-only workspace diff request

```http
POST /api/v1/tool-requests
Content-Type: application/json

{
  "projectId": 42,
  "requestedTool": "workspace.diff",
  "requestKind": "readOnlyInspection",
  "summary": "Request workspace diff inspection",
  "payload": {
    "workspacePath": "workspace-42",
    "runId": "run-42"
  },
  "evidenceRefs": ["source-report-42"],
  "correlationId": "diff-42"
}
```

### Inspect a request

```http
GET /api/v1/tool-requests/tool-request-42-workspacediff-diff-42?projectId=42
```

## Non-goals

Tool Request API v1 does not add:

- tool execution
- approval
- gate execution
- source apply
- memory promotion
- hidden workflow or scheduler
- autonomous runner
- GitHub submission
- CLI or UI exposure
- SQL schema or stored procedure shape changes
- vector/index behavior changes
