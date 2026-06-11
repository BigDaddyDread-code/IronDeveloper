# Manual Memory Improvement API v1

## Purpose

Manual Memory Improvement API v1 exposes the existing stored manual MemoryImprovementAgent path through HTTP.

This API creates or inspects proposal-only memory-improvement evidence. It does not promote memory, create accepted memory, write CollectiveMemory, write vector/index authority, approve anything, execute tools, apply source changes, or create execution permission.

## Endpoints

### POST `/api/v1/manual-memory-improvements`

Creates a manual memory-improvement request through the existing stored manual memory-improvement backend service.

Request fields:

| Field | Required | Meaning |
| --- | --- | --- |
| `projectId` | yes | Project scope for stored audit evidence. |
| `sourceType` | yes | Public source type such as `AgentRunAuditEnvelope`, `FailurePackage`, `SourceReport`, or `Ticket`. |
| `sourceId` | yes | Caller-visible source identifier. |
| `summary` | yes | Short public proposal summary. |
| `content` | yes | Public pattern/proposal content. |
| `evidenceRefs` | no | Caller-supplied evidence reference IDs. |
| `context` | no | Public rationale explaining why the proposal may help. |
| `candidateType` | no | Existing `MemoryImprovementPatternType`. Defaults to `RepeatedManualCorrection`. |
| `correlationId` | no | Caller correlation ID. |

Rules:

- Unsupported fields are rejected.
- Hidden chain-of-thought, raw prompts, raw completions, scratchpad text, developer prompts, system prompts, and private reasoning are rejected.
- Approval, promotion, accepted-memory, CollectiveMemory, vector-authority, source-apply, tool-execution, GitHub-submission, and pull-request-creation claims are rejected.
- Content is bounded.
- The endpoint may append the existing manual memory-improvement audit envelope.
- The endpoint must not promote memory, create accepted memory, write CollectiveMemory, write vector/index authority, mutate source, execute tools, create approvals, create governance decisions, or submit external reviews.

### GET `/api/v1/manual-memory-improvements/{agentRunId}?projectId={projectId}`

Inspects an existing manual memory-improvement audit record.

Rules:

- `projectId` is required.
- The endpoint is read-only.
- Cross-project access returns not found.
- Hidden/private audit text is redacted before response data is returned.
- The response remains proposal-only evidence, not approval, promotion, or governance.
- The endpoint must not create memory candidates, memory proposals, CollectiveMemory records, vector/index writes, or additional audit records.

## Response envelope

Both endpoints return:

```json
{
  "status": "succeeded",
  "data": {},
  "runId": "manual-memory-improvement-...",
  "proposalId": "memory-proposal-draft-...",
  "evidenceId": "evidence-...",
  "boundary": {
    "memoryImprovementIsPromotion": false,
    "memoryProposalIsPromotion": false,
    "memorySafeIsApproval": false,
    "candidateIsMemory": false,
    "retrievalMatchIsMemoryCandidate": false,
    "auditIsApproval": false,
    "sourceApplied": false,
    "memoryPromoted": false,
    "collectiveMemoryWritten": false,
    "vectorAuthorityWritten": false,
    "toolExecuted": false,
    "modelOutputIsAuthority": false,
    "endpointAccessIsExecutionPermission": false,
    "apiResponseStatusIsGovernance": false,
    "humanReviewRequiredForMemoryPromotion": true,
    "humanReviewRequiredForSourceApply": true
  },
  "mutationOccurred": false,
  "humanApprovalRequired": true,
  "warnings": [],
  "errors": []
}
```

For `POST`, `mutationOccurred` may be `true` only for the allowed creation of manual memory-improvement audit/evidence/proposal records.

For `GET`, `mutationOccurred` is always `false`.

## Boundaries

- Memory-improvement output is proposal-only.
- Memory improvement is proposal-only.
- Memory improvement output is not promotion.
- Memory improvement is not promotion.
- Memory proposal is not promotion.
- Memory safety is not approval.
- Memory safe is not approval.
- Candidate is not memory.
- Retrieval match is not memory candidate.
- Audit evidence is not approval.
- API access is not execution permission.
- API response status is not governance.
- Model output is not authority.
- Human review remains required for memory promotion.
- Human review remains required for source apply.

## Non-goals

Manual Memory Improvement API v1 does not add:

- memory promotion
- accepted memory creation
- CollectiveMemory writes
- vector/index authority writes
- source apply
- tool execution
- gate execution
- governance decision creation
- approval creation
- GitHub review submission
- automatic repair
- automatic test rerun
- hidden runner or scheduler
- CLI or UI exposure
- SQL schema or stored procedure shape changes
