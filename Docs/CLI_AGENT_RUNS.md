# CLI agent run inspection

## Purpose

Agent run CLI commands are read-only API clients over the PR58 Agent Run API. They do not execute agents, append audit, approve requests, apply source, promote memory, or expose hidden reasoning.

The backend remains the kitchen. The API remains the serving hatch. The CLI asks for existing receipts.

## Commands

```powershell
irondev agent-runs list --project-id 42
irondev agent-runs get agent-run-001 --project-id 42
irondev agent-runs audit agent-run-001 --project-id 42
```

No command in this slice starts, runs, replays, approves, applies, promotes, or mutates an agent run.

## Required configuration

The commands reuse the CLI foundation configuration:

| Source | Name |
| --- | --- |
| Flag | `--api-base-url <url>` |
| Flag | `--token <token>` |
| Flag | `--output text\|json` |
| Flag | `--json` |
| Environment | `IRONDEV_API_BASE_URL` |
| Environment | `IRONDEV_API_TOKEN` |
| Environment | `IRONDEV_OUTPUT` |

Flags win over environment variables. Tokens are attached as bearer tokens and are never printed.

## `agent-runs list`

Calls:

```text
GET /api/v1/agent-runs
```

Required:

```text
--project-id <id>
```

Optional filters:

```text
--agent-id <id>
--agent-kind <kind>
--status <status>
--trigger-type <type>
--created-after-utc <timestamp>
--created-before-utc <timestamp>
--run-id <id>
--correlation-id <id>
--take <n>
--skip <n>
```

The CLI does not add authority interpretation to the API response. Empty results are still a successful read when the API says the request succeeded.

## `agent-runs get`

Calls:

```text
GET /api/v1/agent-runs/{agentRunId}
```

Required:

```text
<agentRunId>
--project-id <id>
```

Text output includes run identity, status, agent identity, timestamps, input/output counts, and boundary warning summary.

## `agent-runs audit`

Calls:

```text
GET /api/v1/agent-runs/{agentRunId}/audit
```

Required:

```text
<agentRunId>
--project-id <id>
```

Text output includes input, output, thought-ledger, capability-use, boundary-decision, and evidence-reference counts.

It always states:

```text
Audit is not approval.
Evidence is not permission.
```

## Output modes

Text output is intentionally boring and human-readable.

JSON output uses the CLI foundation envelope:

```json
{
  "ok": true,
  "command": "agent-runs list",
  "status": "succeeded",
  "data": {},
  "warnings": [],
  "errors": []
}
```

The `data` field preserves the API response envelope where practical so boundary flags are not collapsed away.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | success |
| `2` | configuration error |
| `3` | validation or usage error |
| `4` | API returned a non-success response |
| `6` | API connection failure |

## Read-only guarantee

These commands must not:

- execute agents
- start agent runs
- append audit
- create evidence
- create tool requests
- evaluate gates
- apply source
- promote memory
- query SQL directly
- call backend services directly
- change API behavior

## Authority boundaries

CLI command output is not approval.

CLI command output is not execution permission.

Audit is evidence, not approval.

Evidence is accountability, not permission.

API response status is not governance.

Model output remains advisory only.

Human review remains required for source apply and memory promotion.

## Hidden reasoning boundary

The CLI relies on the PR58 API sanitization boundary and applies an additional marker redaction before printing. Hidden chain-of-thought, raw prompts, raw completions, scratchpads, private reasoning, system prompts, and developer prompts must not be printed.

## Known limitations

The CLI does not provide manual critic, manual memory-improvement, tool request, tool gate, or dogfood loop commands in this PR.

The CLI does not implement a local audit projection. It renders the PR58 API response only.
