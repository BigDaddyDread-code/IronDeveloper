# API/CLI Release Gate Report

## Purpose

PR 71 records the final Block F API/CLI release gate report.

This report is a receipt, not a trophy.

It summarizes what the Block F API and CLI surfaces expose, what they explicitly do not expose, which focused validation bands support them, and which known broader lanes remain outside the Block F claim.

## Verdict

Block F API/CLI exposure is ready as a controlled internal operating surface.

It is not a product release gate for IronDev as a whole.

Release gate decision:

- Block F API/CLI exposure: ready for internal completion.
- IronDev full release: not ready.

This is release-readiness evidence, not release approval.

## Completed API Surfaces

PR 58 exposed the read-only agent run API v1.

- `GET /api/v1/agent-runs`
- `GET /api/v1/agent-runs/{agentRunId}`
- `GET /api/v1/agent-runs/{agentRunId}/audit`
- Boundary: audit is evidence, not approval.
- Boundary: API status is not governance.

PR 59 exposed the manual critic API v1.

- `POST /api/v1/manual-critic/reviews`
- `GET /api/v1/manual-critic/reviews/{agentRunId}`
- Boundary: critic is not governance.
- Boundary: critic review is not approval.

PR 60 exposed the manual memory improvement API v1.

- `POST /api/v1/manual-memory-improvements`
- `GET /api/v1/manual-memory-improvements/{agentRunId}`
- Boundary: memory proposal is not promotion.
- Boundary: memory safe is not approval.

PR 61 exposed the tool request API v1.

- `POST /api/v1/tool-requests`
- `GET /api/v1/tool-requests/{toolRequestId}`
- Boundary: tool request is request form, not execution permission.
- Boundary: request approval is separate.
- Durability: non-durable API-local inspection cache.

PR 62 exposed the tool gate API v1.

- `POST /api/v1/tool-gates/evaluations`
- Boundary: gate evaluation is not execution.
- Boundary: gate pass is not human approval.
- Boundary: gate is not executor.
- Durability: non-durable API-local gate preview.
- CLI status: API only / no CLI yet.

PR 63 exposed the dogfood loop API v1.

- `POST /api/v1/dogfood-loops`
- `GET /api/v1/dogfood-loops/{dogfoodLoopId}`
- Boundary: dogfood receipt is evidence, not release approval.
- Boundary: dogfood loop is not autonomous workflow.
- Durability: non-durable API-local receipt storage.

## Completed CLI Surfaces

PR 64 added the CLI foundation.

- `api ping`
- Boundary: API health inspection is not execution permission.

PR 65 added CLI agent run inspection commands.

- `agent-runs list`
- `agent-runs get`
- `agent-runs audit`
- Boundary: audit is not approval.
- Boundary: evidence is not permission.
- Boundary: CLI output is not governance.

PR 66 added CLI manual critic commands.

- `critic review create`
- `critic review get`
- Boundary: critic is not governance.
- Boundary: critic review is not approval.

PR 67 added CLI memory improvement commands.

- `memory-improvements create`
- `memory-improvements get`
- Boundary: memory proposal is not promotion.
- Boundary: candidate is not memory.

PR 68 added CLI tool request commands.

- `tool-requests create`
- `tool-requests get`
- Boundary: tool request is request form, not execution permission.
- Boundary: tool execution is separate.

PR 69 added CLI dogfood loop commands.

- `dogfood-loops create`
- `dogfood-loops get`
- Boundary: dogfood receipt is evidence, not release approval.
- Boundary: dogfood loop is not autonomous workflow.

No CLI tool gate command exists in Block F. Tool gate evaluation remains API-only in the PR 70 matrix.

## Contract Suite Evidence

PR 70 added the API/CLI contract suite and matrix.

- `Docs/API_CLI_CONTRACT_TEST_SUITE.md`
- `Docs/API_CLI_CONTRACT_MATRIX.md`

The PR 70 matrix is the canonical Block F command-to-endpoint receipt for the exposed API/CLI surfaces. PR 71 adds the release gate report on top of that receipt; it does not add a new endpoint, command, or permission path.

Focused validation evidence for this report:

- Build: passed, 0 errors.
- PR 71 release gate band: `ApiCliReleaseGate` passed 10/10.
- PR 70 plus PR 71 contract band: `ApiCliContract|ApiCliReleaseGate` passed 39/39.
- CLI/API combined contract band: `CliFoundation|CliAgentRuns|CliManualCritic|CliMemoryImprovements|CliToolRequests|CliDogfoodLoops|ApiCliContract|ApiCliReleaseGate` passed 116/116.
- Block F API band: `AgentRunsApi|ManualCritic|ManualMemoryImprovement|ToolRequestApi|ToolGateApi|DogfoodLoopApi` passed 80/80.
- Backend docs/freeze band: `BackendContractFreezeReport|BackendConfigurationDependency|BackendOperationalDebugging|BackendAdrDocumentation|BackendArchitectureDocumentation|BackendEntityTableInventory|BackendSqlCleanup|InlineSql|BackendNamingNormalisation|BackendDeadCodeSweep|BackendFixtureConsolidation` passed 59/59.
- Full API lane: failed 1/126 on `EndpointContractTests.cs:189` chat wording assertion.
- Full solution lane: failed 26/1980, with 1953 passed and 1 skipped, in documented broad governance/static-boundary/memory/context lanes.
- `git diff --check`: passed.

## Frozen Boundary Invariants

- Audit is not approval.
- Evidence is not permission.
- Critic is not governance.
- Critic review is not approval.
- Memory proposal is not promotion.
- Memory safe is not approval.
- Candidate is not memory.
- Retrieval match is not memory candidate.
- Tool request is request form, not execution permission.
- Request approval is separate.
- Tool execution is separate.
- Gate is not executor.
- Gate evaluation is not execution.
- Gate pass is not human approval.
- Dogfood receipt is evidence, not release approval.
- Dogfood loop is not autonomous workflow.
- API access is not execution permission.
- API response status is not governance.
- CLI command is not approval.
- CLI output is not governance.
- Model output is advisory only.
- Human review remains required for source apply.
- Human review remains required for memory promotion.

## Non-Durable Boundaries

PR 61 Tool Request API remains non-durable API-local unless durable SQL-backed Tool Request Store has landed.

PR 62 Tool Gate API remains non-durable API-local gate preview unless durable SQL-backed Gate Decision Store has landed.

PR 63 Dogfood Loop API remains non-durable API-local receipt storage unless durable SQL-backed Dogfood Loop Store has landed.

Non-durable records are not:

- SQL source of truth.
- Durable audit evidence.
- Execution evidence.
- Approval.
- Release evidence.

## Known Red Lanes

Full API remains red on existing chat wording assertion unless fixed.

Full solution remains red in documented broad governance/static-boundary/memory/context lanes unless fixed.

Block F can be considered internally operable only within its focused validation bands. It cannot be used to claim full solution release readiness while the broad API/full-solution lanes remain red.

## Explicit Non-Goals

PR 71 does not add:

- API endpoints.
- CLI commands.
- Runtime scheduling.
- Orchestration.
- Tool execution.
- Approval granting.
- Source apply.
- Memory promotion.
- SQL schema changes.
- Stored procedure result-shape changes.
- UI behavior.

## Next Required Work

1. Durable SQL Tool Request Store.
2. Durable SQL Tool Gate Decision Store, if previews must become durable evidence.
3. Durable SQL Dogfood Loop Receipt Store, if receipts must become project history.
4. Clean known broad API/full-solution red lanes.
5. Decide whether CLI Tool Gate commands are needed separately.
6. Decide next backend dogfood execution path, if any, without hiding execution authority.
7. Only after backend/API/CLI stability, resume UI consumer work.

## Final Warning

This report closes Block F as an internal API/CLI exposure receipt.

It does not approve source mutation, memory promotion, autonomous execution, release publication, or product-wide readiness.