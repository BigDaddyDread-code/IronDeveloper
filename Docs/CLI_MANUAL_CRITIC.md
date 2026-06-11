# Manual Critic CLI

## Purpose

The Manual Critic CLI exposes the PR59 Manual Critic API through `irondev` commands.

It is a request and inspection surface for critic review evidence. It is not governance, approval, execution permission, source apply, memory promotion, tool execution, GitHub review submission, or audit mutation outside the API.

## Commands

```powershell
irondev critic review create --project-id <id> --target-agent-run-id <id>
irondev critic review get <agentRunId> --project-id <id>
```

Common API options are inherited from the CLI foundation:

```powershell
--api-base-url <url>
--token <token>
--output text|json
--json
```

Tokens must never be printed.

## Create a critic review request

```powershell
irondev critic review create `
  --project-id 42 `
  --target-agent-run-id manual-run-1 `
  --review-kind code `
  --focus "Review the run boundary evidence" `
  --reason "Public reviewer note" `
  --evidence-ref agent-run:manual-run-1
```

The CLI sends a POST request to:

```text
/api/v1/manual-critic/reviews
```

The CLI maps the target run into the API contract as:

| CLI field | API field |
| --- | --- |
| `--project-id` | `projectId` |
| `--target-agent-run-id` | `subjectId` |
| fixed value | `subjectType = AgentRun` |
| `--focus` | `summary` |
| `--reason` | `content` |
| repeated `--evidence-ref` | `evidenceRefs` |
| `--review-kind` | public context text |
| `--correlation-id` | `correlationId` |

The response may create durable manual critic audit evidence through the API. That evidence is still advisory only.

## Inspect a critic review

```powershell
irondev critic review get manual-independent-critic-42-agentrun-run-1 --project-id 42
```

The CLI sends a GET request to:

```text
/api/v1/manual-critic/reviews/{agentRunId}?projectId={projectId}
```

Inspection is read-only. It does not append audit, execute agents, approve anything, apply source changes, promote memory, execute tools, or submit GitHub reviews.

## JSON envelope

JSON output uses the standard CLI envelope:

```json
{
  "ok": true,
  "command": "critic review get",
  "status": "succeeded",
  "data": {},
  "warnings": [],
  "errors": []
}
```

Warnings are advisory. They are not approval, execution permission, policy clearance, or governance evidence.

## Boundary rules

The Manual Critic CLI must preserve these rules:

- Critic review is advisory only.
- Critic review is not governance.
- Critic review is not approval.
- Audit evidence is not approval.
- API access is not execution permission.
- Source apply remains separate.
- Memory promotion remains separate.
- Tool execution remains separate.
- GitHub review submission remains separate.
- Human review remains required for source apply.
- Human review remains required for memory promotion.
- Hidden reasoning, raw prompts, raw completions, scratchpads, and private reasoning markers must not be printed.

Unsupported authority-shaped CLI flags such as `--approve`, `--apply`, `--source-apply`, `--promote-memory`, `--execute-tool`, `--submit-github-review`, and `--block-execution` are rejected as usage errors.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | success |
| `2` | configuration error |
| `3` | usage error |
| `4` | API returned a non-success response |
| `6` | API connection failure |
