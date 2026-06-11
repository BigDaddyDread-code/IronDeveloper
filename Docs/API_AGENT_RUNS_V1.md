# Agent Run API v1

## Purpose

Agent Run API v1 exposes read-only inspection over existing agent run audit and evidence records.

Agent run API v1 is inspection-only. It does not start, execute, approve, apply, promote, or govern agent runs.

This API exposes frozen backend contracts only. It does not redefine backend authority.

## Endpoint list

```text
GET /api/v1/agent-runs
GET /api/v1/agent-runs/{agentRunId}
GET /api/v1/agent-runs/{agentRunId}/audit
```

All endpoints require authentication under the existing API auth convention.

## Request parameters

### GET /api/v1/agent-runs

Required:

- `projectId`

Allowed filters:

- `agentId`
- `agentKind`
- `status`
- `triggerType`
- `createdAfterUtc`
- `createdBeforeUtc`
- `runId`
- `correlationId`
- `take`
- `skip`

Paging is bounded by the existing read service. `take` must be between 1 and 200. `skip` must be greater than or equal to 0.

Unsupported filters are rejected. They must not silently broaden scope.

### GET /api/v1/agent-runs/{agentRunId}

Required:

- `projectId`
- `agentRunId`

### GET /api/v1/agent-runs/{agentRunId}/audit

Required:

- `projectId`
- `agentRunId`

## Response shape

Responses use an explicit inspection envelope:

```text
status
data
runId
evidenceId
boundary
mutationOccurred
humanApprovalRequired
warnings
errors
```

For these read-only endpoints:

- `mutationOccurred` is always `false`.
- `humanApprovalRequired` does not mean approval occurred.
- `status` describes the API read result, not governance authority.
- `boundary` states that audit is not approval, endpoint access is not execution permission, API response status is not governance, and model output is not authority.

## Error model

Error categories:

- validation error
- not found
- forbidden/unauthorized
- unsupported filter
- backend contract exception
- internal error

Rules:

- blocked or forbidden must not be returned as success
- missing data must not be treated as approval
- unsupported filters must not silently broaden scope
- internal errors must not expose secrets or hidden reasoning

## Read-only guarantee

The API uses the existing agent run audit query service only.

The API does not:

- create agent runs
- append audit records
- create evidence
- create tool requests
- execute tools
- run critics
- run gates
- apply source
- promote memory
- create memory candidates or proposals
- create approval
- mutate SQL state

## Authority boundaries

API call is not approval.

Endpoint access is not execution permission.

API response status is not governance.

Audit is not approval.

Gate is not executor.

Critic is not governance.

Proposal is not apply.

Memory safe is not approval.

Model output is advisory only.

Human review remains required for source apply and memory promotion.

## Audit and evidence exposure

Audit records may be inspected.

Evidence references may be returned.

Audit cannot be submitted as approval.

Evidence cannot be treated as permission.

Client-supplied evidence must remain distinct from backend-recorded audit/evidence.

## Hidden reasoning boundary

The API must not expose hidden chain-of-thought or sensitive internal reasoning.

If existing audit records contain unsafe text markers, the existing audit projection redacts unsafe text before response data is returned.

## Known PR56 exceptions

This API does not fix PR56 freeze exceptions.

Relevant known exceptions remain:

- API chat freeform response wording assertion at `EndpointContractTests.cs:189`.
- Existing governance/agent runner approval assertions.
- Existing agent-memory boundary harness references to old `CollectiveMemoryRetrievalCandidate` naming.
- Existing L4 release gate failures dependent on the memory boundary harness.
- Existing static boundary scans for manual/model/boxed agent files.

None of those exceptions grants source apply, memory promotion, audit approval, vector/index authority, or model-output authority.

## Examples

List runs:

```text
GET /api/v1/agent-runs?projectId=1&take=50&skip=0
```

Get one run:

```text
GET /api/v1/agent-runs/manual-critic-review-1?projectId=1
```

Get audit summary:

```text
GET /api/v1/agent-runs/manual-critic-review-1/audit?projectId=1
```
