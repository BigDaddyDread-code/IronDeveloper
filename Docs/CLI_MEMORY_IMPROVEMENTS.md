# Manual Memory Improvement CLI

## Purpose

The Manual Memory Improvement CLI exposes the PR60 Manual Memory Improvement API through `irondev` commands.

It may request and inspect memory-improvement proposals. It does not promote memory, create accepted memory, write CollectiveMemory, write vector or index authority, approve requests, apply source, execute tools, or expose hidden reasoning.

Memory proposal is not memory promotion.

## Commands

```powershell
irondev memory-improvements create --project-id <id> --target-agent-run-id <id>
irondev memory-improvements get <agentRunId> --project-id <id>
```

Common API options are inherited from the CLI foundation:

```powershell
--api-base-url <url>
--token <token>
--output text|json
--json
```

Tokens must never be printed or persisted.

## Create a memory-improvement proposal request

```powershell
irondev memory-improvements create `
  --project-id 42 `
  --target-agent-run-id agent-run-001 `
  --focus "Repeated manual correction around approval evidence" `
  --reason "Public run summaries show the same correction pattern" `
  --evidence-ref agent-run-audit:agent-run-001
```

The CLI sends a POST request to:

```text
/api/v1/manual-memory-improvements
```

The CLI maps the target run into the API contract as:

| CLI field | API field |
| --- | --- |
| `--project-id` | `projectId` |
| fixed value | `sourceType = AgentRunAuditEnvelope` |
| `--target-agent-run-id` | `sourceId` |
| `--focus` | `summary` |
| `--reason` | `content` |
| repeated `--evidence-ref` | `evidenceRefs` |
| fixed value | `candidateType = RepeatedManualCorrection` |
| `--correlation-id` | `correlationId` |

The response may create durable manual memory-improvement audit evidence through the API. That evidence is proposal-only.

## Inspect a memory-improvement proposal

```powershell
irondev memory-improvements get manual-memory-improvement-42 --project-id 42
```

The CLI sends a GET request to:

```text
/api/v1/manual-memory-improvements/{agentRunId}?projectId={projectId}
```

Inspection is read-only. It does not append audit, rerun memory-improvement logic, promote memory, write CollectiveMemory, write vector/index authority, approve anything, execute tools, or apply source.

## JSON envelope

JSON output uses the standard CLI envelope:

```json
{
  "ok": true,
  "command": "memory-improvements get",
  "status": "succeeded",
  "data": {},
  "warnings": [],
  "errors": []
}
```

Warnings are advisory. They are not approval, execution permission, policy clearance, memory promotion, or governance evidence.

## Boundary rules

The Manual Memory Improvement CLI must preserve these rules:

- Manual memory-improvement CLI commands are API clients over the PR60 Manual Memory Improvement API.
- Memory-improvement output is proposal-only.
- Memory proposal is not promotion.
- Memory safe is not approval.
- Candidate is not memory.
- Retrieval match is not memory candidate.
- Audit evidence is not approval.
- API response status is not governance.
- Source apply remains separate.
- Tool execution remains separate.
- Human review remains required for memory promotion.
- Human review remains required for source apply.
- Hidden reasoning, raw prompts, raw completions, scratchpads, and private reasoning markers must not be printed.

Unsupported authority-shaped CLI flags such as `--approve`, `--apply`, `--source-apply`, `--promote-memory`, `--accept-memory`, `--write-memory`, `--collective-write`, `--vector-write`, `--index-write`, and `--execute-tool` are rejected as usage errors.

## Known limitations

This CLI slice does not add tool request commands, tool gate commands, dogfood loop commands, local memory-improvement implementation, accepted-memory persistence, memory promotion, CollectiveMemory writes, vector/index writes, or hidden workflow execution.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | success |
| `2` | configuration error |
| `3` | usage error |
| `4` | API returned a non-success response |
| `6` | API connection failure |
