# CLI tool request commands

## Purpose

Tool request CLI commands are API clients over the PR61 Tool Request API. They may create and inspect request forms. They do not approve requests, execute tools, evaluate gates, apply source, promote memory, append tool execution audit, or expose hidden reasoning.

Backend is the kitchen. API is the serving hatch. CLI is the waiter and docket printer. The CLI can write the kitchen docket. It cannot turn on the oven.

## Commands

```powershell
irondev tool-requests create --project-id <id> --request-kind <kind> --tool-kind <kind> --run-id <id> --reason <text>
irondev tool-requests get <toolRequestId> --project-id <id>
```

## Configuration

These commands reuse the CLI foundation configuration:

| Setting | Purpose |
| --- | --- |
| `IRONDEV_API_BASE_URL` | API base URL. |
| `IRONDEV_API_TOKEN` | bearer token attached to API requests. |
| `IRONDEV_OUTPUT` | `text` or `json`. |
| `--api-base-url <url>` | command-line API base URL override. |
| `--token <token>` | command-line bearer token override. |
| `--output text|json` | output mode. |
| `--json` | shorthand for JSON output. |

Tokens are never printed or persisted by these commands.

## Create

```powershell
irondev tool-requests create `
  --project-id 42 `
  --request-kind readOnlyInspection `
  --tool-kind workspace.diff `
  --run-id agent-run-42 `
  --reason "Inspect workspace diff evidence." `
  --summary "Workspace diff request" `
  --evidence-ref source-report-42 `
  --input-ref workspace-42 `
  --policy-ref pr56-backend-freeze `
  --api-base-url http://localhost:5000
```

The command posts to:

```text
POST /api/v1/tool-requests
```

It creates a request form only. It does not run the requested tool.

Optional flags:

| Flag | Purpose |
| --- | --- |
| `--summary <text>` | request summary sent to the API. |
| `--evidence-ref <ref>` | repeatable evidence reference. |
| `--input-ref <ref>` | repeatable input reference carried in payload. |
| `--policy-ref <ref>` | repeatable policy reference carried in payload. |
| `--risk-level <level>` | caller-supplied risk hint carried in payload. |
| `--dry-run-required true|false` | caller-supplied dry-run hint carried in payload. |
| `--correlation-id <id>` | API correlation id. |

Unsupported authority-shaped flags are rejected before any API call, including `--approve`, `--execute`, `--apply`, `--source-apply`, `--promote-memory`, `--accept-memory`, `--gate-pass`, `--human-approved`, `--policy-cleared`, and `--submit-github`.

## Get

```powershell
irondev tool-requests get tool-request-42-workspacediff-001 --project-id 42
```

The command calls:

```text
GET /api/v1/tool-requests/{toolRequestId}?projectId={projectId}
```

It is read-only. It does not evaluate the gate, execute tools, approve the request, append execution audit, apply source, or promote memory.

## Output

Text output always includes the boundary summary:

```text
Tool request is request form, not execution permission.
Request approval is separate.
Tool execution is separate.
```

JSON output preserves the API envelope where practical:

```json
{
  "ok": true,
  "command": "tool-requests create",
  "status": "succeeded",
  "data": {},
  "warnings": [],
  "errors": []
}
```

Warnings and errors from the API are preserved.

## Durability boundary

Tool requests created through this CLI are durable SQL-backed request records when the API is backed by the durable Tool Request Store. They are not execution evidence, approval, gate decisions, source apply, or memory promotion.

When the API returns `durable: true`, the CLI keeps that request-record durability visible without treating it as execution permission.

## Authority boundaries

The CLI command is not approval. The CLI command is not execution permission. The CLI output is not governance. Tool request is request form, not execution permission. Request created is not request approved. Gate is not executor. Gate pass is not human approval. Audit is not approval. Model output is advisory only. API response status is not governance. Human review remains required for source apply. Human review remains required for memory promotion.

## Hidden reasoning boundary

The CLI rejects obvious raw/private reasoning markers before create requests are sent. It also redacts hidden-reasoning markers from API output before printing. It must not print raw prompts, raw completions, scratchpads, system prompts, developer prompts, chain-of-thought, or private reasoning markers.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | success |
| `2` | configuration error |
| `3` | validation or usage error |
| `4` | API returned a non-success response |
| `6` | API connection failure |

## Known limitations

These commands do not expose tool gate commands. They do not execute tools, apply patches, submit GitHub reviews, promote memory, write accepted memory, write CollectiveMemory, write vector/index authority, append execution audit, or run workflows.
