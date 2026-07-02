# G09b - Governed Tool Policy Evaluator Unit Tests

## Purpose

G09b adds the full fast unit-test corpus for the Core governed-tool policy evaluator extracted in G09a.

Target:

- `IronDev.Core.Tools.GovernedToolPolicyEvaluator`
- `IronDev.Core.Tools.GovernedToolPolicyDecision`

Review line:

> Tool policy evaluator tests are not tool execution.

Killjoy line:

> Saying a tool is blocked is not safely running it.

## Files Changed

- `IronDev.UnitTests/Tools/GovernedToolPolicyEvaluatorTestFixtures.cs`
- `IronDev.UnitTests/Tools/GovernedToolPolicyEvaluatorUnitTests.cs`
- `Docs/receipts/G09B_GOVERNED_TOOL_POLICY_EVALUATOR_UNIT_TESTS.md`

No production code changed.

No Infrastructure code changed.

No integration tests were moved, deleted, or weakened.

No project references, package references, API, CLI, SQL, provider, worker, registry/runtime, real tool, executor, dogfood, ReplayRunner, or CI files changed.

## G09a Seam Confirmation

G09a moved the real policy evaluator into Core:

- `IronDev.Core.Tools.GovernedToolPolicyEvaluator`
- `IronDev.Core.Tools.GovernedToolPolicyDecision`

G09b tests that Core seam directly from `IronDev.UnitTests`.

## Tests Added

The G09b fast unit suite covers:

- read-only matching tool request allowed
- stable allowed decision reason
- case-insensitive tool-name matching
- case-insensitive allowed-caller matching
- nested requests allowed only when the definition explicitly allows nested calls
- tool-name mismatch rejection
- tool-name mismatch reason naming both requested and registered tool
- tool-name mismatch checked before dangerous capabilities
- tool-name mismatch checked before caller policy
- tool-name mismatch checked before nested-call policy
- mutation-capable tool rejection
- file-write-capable tool rejection
- process-execution-capable tool rejection
- network-capable tool rejection
- workspace-mutation-capable tool rejection
- governed read-only path reason wording for capability blocks
- first observable dangerous capability order
- mutation before file-write
- file-write before process execution
- process execution before network access
- network access before workspace mutation
- workspace mutation before caller policy
- disallowed caller rejection
- empty allowed-caller list rejecting all callers
- multi-caller allow list
- caller reason naming requested caller and tool
- caller policy before nested-call policy
- nested depth rejection when nested calls are disabled
- parent request rejection when nested calls are disabled
- nested depth plus parent rejection when nested calls are disabled
- nested reason naming the tool
- zero depth without parent is not nested
- whitespace parent request id is not nested
- allowed/rejected decision object shape
- no execution, approval, mutation, retry, or repair flags on the decision
- policy allow/reject remains policy evidence only
- deterministic decisions for identical inputs
- equivalent case-insensitive allowed inputs
- equivalent rejected inputs
- evaluator independence from request timestamp
- `IronDev.UnitTests` remains Core-only and MSTest-only
- G09b source guard against Infrastructure, registry/runtime, real tool, execution, API/SQL/provider/process/file/network/workspace/environment/current-time surfaces

## Why These Tests Are Pure

The tests use:

- fixed in-memory `GovernedToolDefinition` fixtures
- fixed in-memory `GovernedToolRequest<TInput>` fixtures
- tiny test input/output records
- fixed timestamp data only
- direct calls to `GovernedToolPolicyEvaluator.Evaluate(...)`

The tests do not:

- instantiate `GovernedToolRegistry`
- instantiate real governed tools
- implement `IGovernedTool<TInput,TOutput>`
- call `ExecuteAsync`
- call providers
- run processes
- write files
- access network
- mutate workspace
- call API or CLI
- call SQL or repositories
- use integration helpers
- use environment variables
- use current time directly

## Dependencies Excluded

`IronDev.UnitTests` remains a fast Core-only test project:

- project reference: `IronDev.Core`
- packages: MSTest test SDK/adapter/framework only

G09b adds no reference to Infrastructure, API, CLI, UI, SQL, persistence, workers, providers, registry runtime, model/runtime services, tool bodies, ReplayRunner, dogfood, or executors.

## Reported Validation

Local validation:

- `dotnet restore IronDev.slnx`: passed with existing NU1510 warnings
- `dotnet build IronDev.slnx --no-restore -v:minimal -clp:ErrorsOnly`: passed, 0 errors / 1645 warnings
- `dotnet build IronDev.UnitTests/IronDev.UnitTests.csproj --no-restore -v:minimal -clp:ErrorsOnly`: passed, 0 errors / 5 warnings
- `dotnet test IronDev.UnitTests/IronDev.UnitTests.csproj --no-build`: 246/246 passed
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~GovernedToolArchitectureTests`: 18/18 passed
- C11 secret scan: 9/9 passed after clearing an orphaned timed-out testhost from the first attempt
- `git diff --check`: passed
- `git diff --cached --check`: passed after staging the exact G09b files

GitHub validation:

- fast-unit-ci on pushed G09b head: tracked in PR checks/body

## Known Limitations

G09b tests governed-tool policy decisions only.

G09b does not execute tools.

G09b does not test `GovernedToolRegistry` runtime wiring.

G09b does not test real tool bodies.

G09b does not test providers.

G09b does not run processes.

G09b does not write files.

G09b does not access network.

G09b does not mutate workspace.

G09b does not test API or CLI.

G09b does not test SQL persistence.

G09b does not test thought-ledger recording.

G09b does not change production governed-tool behavior.

G09b does not replace integration tests.

G09b does not grant authority.

## Boundary Statement

Tool policy evaluator tests are not tool execution.

Saying a tool is blocked is not safely running it.

Policy allow is not tool execution.

Policy allow is not approval.

Policy allow is not source mutation.

Policy allow is not workspace mutation.

Policy allow is not file-write permission.

Policy allow is not process-execution permission.

Policy allow is not network permission.

Policy allow is not workflow continuation.

Policy reject can block a tool but cannot authorize repair.

A governed tool definition is not execution authority.

A governed tool request is not execution authority.

A governed tool policy decision is not execution authority.

Fast policy tests are not registry/runtime proof, provider proof, API/CLI proof, SQL proof, or release readiness.

## Next Intended Migration Area

G10 - Hostile text corpus tests for memory/status/UI authority claims.
