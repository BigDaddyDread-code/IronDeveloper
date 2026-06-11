# IronDev CLI foundation

## Purpose

The IronDev CLI foundation is an API client shell. It is not a backend runtime, not an agent runner, and not a source mutation path.

Backend services remain the kitchen. The API remains the serving hatch. The CLI is the docket printer and waiter.

## Supported foundation commands

```powershell
irondev --help
irondev --version
irondev config show
irondev api ping
irondev agent-runs list
irondev agent-runs get <agentRunId>
irondev agent-runs audit <agentRunId>
irondev critic review create
irondev critic review get <agentRunId>
irondev memory-improvements create
irondev memory-improvements get <agentRunId>
```

`api ping` performs a safe health check against the configured API base URL. It must not call mutating endpoints.

Agent-run commands are read-only API clients over the PR58 Agent Run API. They do not execute agents, append audit, approve requests, apply source, promote memory, or expose hidden reasoning.

Manual critic commands are API clients over the PR59 Manual Critic API. `critic review create` may request the API to create manual critic audit evidence, but that evidence is advisory only. `critic review get` is read-only inspection. Neither command is governance, approval, execution permission, source apply, memory promotion, tool execution, GitHub review submission, or a local critic implementation.

Manual memory-improvement commands are API clients over the PR60 Manual Memory Improvement API. They may request and inspect memory-improvement proposals. They do not promote memory, create accepted memory, write CollectiveMemory, write vector/index authority, approve requests, apply source, execute tools, or expose hidden reasoning.

## Configuration

Configuration precedence is:

1. command flags
2. environment variables
3. safe defaults

Supported environment variables:

| Variable | Purpose |
| --- | --- |
| `IRONDEV_API_BASE_URL` | API base URL used by API-facing commands. |
| `IRONDEV_API_TOKEN` | bearer token attached to API requests. |
| `IRONDEV_OUTPUT` | output mode, `text` or `json`. |

Supported common flags:

| Flag | Purpose |
| --- | --- |
| `--api-base-url <url>` | overrides `IRONDEV_API_BASE_URL`. |
| `--token <token>` | overrides `IRONDEV_API_TOKEN`. |
| `--output text\|json` | overrides `IRONDEV_OUTPUT`. |
| `--json` | shorthand for `--output json`. |
| `--verbose` | enables verbose mode for future diagnostics. |

Tokens must never be printed. `config show` reports only whether a token is configured.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | success |
| `2` | configuration error |
| `3` | usage error |
| `4` | API returned a non-success response |
| `6` | API connection failure |

## JSON output envelope

JSON output uses a stable envelope:

```json
{
  "ok": true,
  "command": "api ping",
  "status": "succeeded",
  "data": {},
  "warnings": [],
  "errors": []
}
```

Warnings are advisory. They are not approval, execution permission, policy clearance, or governance evidence.

## Boundaries

The CLI foundation must not:

- execute agents
- execute tools
- run shell commands
- mutate source files
- apply patches
- promote memory
- grant approval
- create tickets
- call external systems outside the configured IronDev API
- append audit records
- bypass API contracts

The foundation intentionally does not expose PR58-PR63 domain commands. Those commands get separate API/CLI slices after the foundation is stable.

PR65 adds only the read-only agent-run inspection commands from PR58.

PR66 adds only the Manual Critic API commands from PR59. Manual memory improvement, tool request, tool gate, and dogfood loop CLI commands remain separate slices.

PR67 adds only the Manual Memory Improvement API commands from PR60. Tool request, tool gate, and dogfood loop CLI commands remain separate slices.
