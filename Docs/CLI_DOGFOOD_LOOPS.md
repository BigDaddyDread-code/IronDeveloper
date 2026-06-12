# IronDev CLI dogfood loop commands

## Purpose

The dogfood loop CLI commands are API clients over the PR63 Dogfood Loop API v1.

They create and inspect dogfood loop receipts only. A dogfood receipt is not release approval, not workflow execution, not gate execution, not source apply, not memory promotion, and not durable backend evidence while PR63 remains API-local.

## Commands

```powershell
irondev dogfood-loops create --project-id <id> --summary <text> --goal <text> [--observation <text>] [--blocked-reason <text>] [--agent-run-id <id>] [--critic-review-run-id <id>] [--memory-improvement-run-id <id>] [--tool-request-id <id>] [--tool-gate-decision-id <id>] [--evidence-ref <ref>] [--correlation-id <id>] [--output text|json] [--api-base-url <url>] [--token <token>]
irondev dogfood-loops get <dogfoodLoopId> --project-id <id> [--output text|json] [--api-base-url <url>] [--token <token>]
```

## Create request mapping

`dogfood-loops create` sends:

- `projectId`
- `summary`
- `goal`
- `observations`
- `blockedReasons`
- `agentRunIds`
- `criticReviewRunIds`
- `memoryImprovementRunIds`
- `toolRequestIds`
- `toolGateDecisionIds`
- `evidenceRefs`
- `correlationId`

CLI `--evidence-ref <ref>` values are sent as caller-supplied CLI evidence references with `refType = cli_evidence`, `source = cli`, and `durable = false` once the API returns them.

## Boundary

The CLI must preserve the Dogfood Loop API boundary:

- dogfood receipt is not release approval
- dogfood loop is not autonomous workflow
- tool request is request form, not execution permission
- gate is not executor
- gate pass is not human approval
- audit evidence is not approval
- API response status is not governance
- endpoint access is not execution permission
- human review remains required for source apply
- human review remains required for memory promotion

The CLI does not add:

- workflow execution
- test/build execution
- tool execution
- gate evaluation
- release approval
- source apply
- memory promotion
- CollectiveMemory writes
- vector/index authority writes
- execution audit append
- Git or GitHub submission
- backend authority in the CLI

## Durability caveat

PR63 currently stores dogfood loop receipts in a non-durable API-local in-memory receipt store.

Dogfood loop receipts created through this CLI are therefore:

- not SQL-backed
- not durable across API process restart
- not visible across API instances
- not durable release evidence
- not release approval
- not workflow completion evidence

A future durable SQL Dogfood Loop Store may change the storage boundary. This CLI slice does not add that store.

## Output

Text output prints receipt identity, summary, goal, durability, boundary flags, and API warnings.

JSON output preserves the standard CLI envelope:

```json
{
  "ok": true,
  "command": "dogfood-loops create",
  "status": "succeeded",
  "data": {},
  "warnings": [],
  "errors": []
}
```

Warnings are advisory. They are not approval, execution permission, release readiness, or governance evidence.
