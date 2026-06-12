# API/CLI Contract Test Suite

## Purpose

PR 70 adds a focused API/CLI contract test suite for the stable Block F surfaces.

This is contract testing only. It does not add endpoints, CLI commands, backend authority, execution, approval, source apply, memory promotion, persistence behavior, or runtime orchestration.

## Covered Surfaces

The suite covers these API and CLI families:

- `api ping`
- `agent-runs list`
- `agent-runs get`
- `agent-runs audit`
- `critic review create`
- `critic review get`
- `memory-improvements create`
- `memory-improvements get`
- `tool-requests create`
- `tool-requests get`
- `dogfood-loops create`
- `dogfood-loops get`

## Required Contract Checks

The focused `ApiCliContract` tests verify:

- CLI command-to-endpoint mappings remain stable.
- JSON envelopes preserve `ok`, `command`, `status`, `data`, `warnings`, and `errors`.
- Text output preserves human-facing boundary warnings.
- Non-durable API-local inspection caches remain labelled non-durable.
- Authority-shaped CLI flags are rejected before an HTTP request is sent.
- Tokens and secret-like values are not echoed in output or error text.
- Hidden/private reasoning markers are rejected or redacted before reaching CLI output.
- Static boundary checks prevent API/CLI test surfaces from becoming execution or authority paths.

## Boundary Rules

The suite pins these rules:

- API status is not approval.
- Audit evidence is not approval.
- Gate evaluation is not execution.
- Tool request is not execution permission.
- Dogfood receipt is not release approval.
- Critic review is not governance.
- Memory proposal is not promotion.
- Retrieval match is not memory candidate.
- Candidate is not memory.
- Human review remains required for source apply and memory promotion.

## Non-Durable Boundaries

Some Block F API surfaces intentionally remain API-local inspection caches until a later durable SQL store exists.

The contract suite requires those surfaces to state that they are not durable source-of-truth records:

- `tool-requests`
- `dogfood-loops`
- `tool-gate` evaluation responses

The tests do not treat those records as approval, execution, audit authority, source apply evidence, or memory promotion evidence.

## Non-Goals

PR 70 does not add:

- new API endpoints
- new CLI commands
- tool execution
- approval granting
- source apply
- memory promotion
- audit append
- SQL schema changes
- stored procedure shape changes
- runtime scheduling
- orchestrator behavior
- UI behavior

## Validation

Focused validation:

```powershell
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "ApiCliContract" --no-restore
```

Combined CLI/API validation:

```powershell
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "CliFoundation|CliAgentRuns|CliManualCritic|CliMemoryImprovements|CliToolRequests|CliDogfoodLoops|ApiCliContract" --no-restore
dotnet test IronDev.IntegrationTests.Api\IronDev.IntegrationTests.Api.csproj --filter "AgentRunsApi|ManualCritic|ManualMemoryImprovement|ToolRequestApi|ToolGateApi|DogfoodLoopApi" --no-restore
```

