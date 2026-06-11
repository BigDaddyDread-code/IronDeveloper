# Manual Critic API v1

## Purpose

Manual Critic API v1 exposes the existing stored manual IndependentCriticAgent path through HTTP.

This API creates or inspects critic review evidence only. It does not govern, approve, apply source changes, promote memory, execute tools, submit external reviews, or create execution permission.

## Endpoints

### POST `/api/v1/manual-critic/reviews`

Creates a manual critic review through the existing stored manual critic backend service.

Request fields:

| Field | Required | Meaning |
| --- | --- | --- |
| `projectId` | yes | Project scope for the stored audit evidence. |
| `subjectType` | yes | Existing critic subject type such as `Ticket`, `PullRequest`, `ArchitecturePlan`, `ExecutionAudit`, or `TestReport`. |
| `subjectId` | yes | Caller-visible subject identifier. |
| `summary` | yes | Short public review summary. |
| `content` | yes | Public critic finding content. |
| `evidenceRefs` | no | Caller-supplied evidence reference IDs. |
| `context` | no | Public context explaining why the finding matters. |
| `severityHint` | no | `Critical`, `High`, `Medium`, or `Low`. Defaults to `Medium`. |
| `correlationId` | no | Caller correlation ID. |

Rules:

- Unsupported fields are rejected.
- Hidden chain-of-thought, raw prompts, raw completions, scratchpad text, developer prompts, system prompts, and private reasoning are rejected.
- Approval, governance, execution, source-apply, memory-promotion, tool-execution, GitHub-submission, and pull-request-creation claims are rejected.
- Content is bounded.
- The endpoint may append the existing critic review audit envelope.
- The endpoint must not mutate source, promote memory, execute tools, create approvals, create governance decisions, or submit external reviews.

### GET `/api/v1/manual-critic/reviews/{agentRunId}?projectId={projectId}`

Inspects an existing manual critic audit record.

Rules:

- `projectId` is required.
- The endpoint is read-only.
- Cross-project access returns not found.
- Hidden/private audit text is redacted before response data is returned.
- The response remains advisory evidence, not approval or governance.

## Response envelope

Both endpoints return:

```json
{
  "status": "succeeded",
  "data": {},
  "runId": "manual-independent-critic-...",
  "reviewId": "critic-review-...",
  "evidenceId": "evidence-...",
  "boundary": {
    "criticIsGovernance": false,
    "criticIsApproval": false,
    "auditIsApproval": false,
    "proposalWasApplied": false,
    "sourceApplied": false,
    "memoryPromoted": false,
    "toolExecuted": false,
    "modelOutputIsAuthority": false,
    "endpointAccessIsExecutionPermission": false,
    "apiResponseStatusIsGovernance": false,
    "humanReviewRequiredForSourceApply": true,
    "humanReviewRequiredForMemoryPromotion": true
  },
  "mutationOccurred": false,
  "humanApprovalRequired": true,
  "warnings": [],
  "errors": []
}
```

For `POST`, `mutationOccurred` may be `true` only for the allowed creation of manual critic review audit/evidence records.

For `GET`, `mutationOccurred` is always `false`.

## Boundaries

- Critic review is advisory only.
- Critic review is not governance.
- Critic review is not approval.
- Audit evidence is not approval.
- API access is not execution permission.
- API response status is not governance.
- Model output is not authority.
- Human review remains required for source apply.
- Human review remains required for memory promotion.

## Non-goals

Manual Critic API v1 does not add:

- source apply
- memory promotion
- tool execution
- gate execution
- governance decision creation
- approval creation
- GitHub review submission
- proposal application
- automatic repair
- automatic test rerun
- hidden runner or scheduler
- CLI or UI exposure
- SQL schema or stored procedure shape changes
