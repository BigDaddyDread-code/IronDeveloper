# G09a - Governed Tool Policy Evaluator Extraction

## Purpose

G09a extracts the governed-tool policy evaluator from Infrastructure into Core.

This creates the pure Core seam required for G09b fast governed-tool policy unit tests.

Review line:

> Extracting tool policy is not granting tool authority.

Killjoy line:

> Moving the gate does not run the tool.

## Why G09 Was Blocked

G09 could not proceed honestly because the real governed-tool policy evaluator lived in:

- `IronDev.Infrastructure/Tools/GovernedToolPolicyEvaluator.cs`

`IronDev.UnitTests` is intentionally Core-only. Testing the real evaluator from the fast lane would have required one of the wrong moves:

- referencing `IronDev.Infrastructure` from `IronDev.UnitTests`
- copying policy rules into tests
- testing only Core contracts and pretending they were policy coverage
- instantiating registry/runtime paths

G09a fixes that by moving the evaluator itself into Core.

## Files Changed

- `IronDev.Core/Tools/GovernedToolPolicyEvaluator.cs`
- `IronDev.Infrastructure/Tools/GovernedToolPolicyEvaluator.cs` removed
- `IronDev.IntegrationTests/GovernedToolArchitectureTests.cs`
- `IronDev.IntegrationTests/PromotionWorkflowTests.cs`
- `IronDev.UnitTests/Tools/GovernedToolPolicyEvaluatorExtractionTests.cs`
- `Docs/receipts/G09A_GOVERNED_TOOL_POLICY_EVALUATOR_EXTRACTION.md`

No API, CLI, UI, worker, persistence, SQL, provider, tool body, ReplayRunner, dogfood, package, project-reference, or CI files changed.

## Core Evaluator

Core now owns:

- `IronDev.Core.Tools.GovernedToolPolicyEvaluator`
- `IronDev.Core.Tools.GovernedToolPolicyDecision`

No Infrastructure compatibility shim remains.

Policy logic lives only in Core.

## Behavior Preserved

The extracted Core evaluator preserves the existing rejection order:

1. tool-name mismatch
2. `MutatesState`
3. `AllowsFileWrites`
4. `AllowsProcessExecution`
5. `AllowsNetworkAccess`
6. `AllowsWorkspaceMutation`
7. disallowed caller
8. nested call when nesting is disabled
9. allowed read-only call

The existing reason wording is preserved:

- `Request tool '{request.ToolName}' does not match registered tool '{definition.Name}'.`
- `Tool '{definition.Name}' is mutation-capable and cannot run in the governed read-only tool path.`
- `Tool '{definition.Name}' allows file writes and cannot run in the governed read-only tool path.`
- `Tool '{definition.Name}' allows process execution and cannot run in the governed read-only tool path.`
- `Tool '{definition.Name}' allows network access and cannot run in the governed read-only tool path.`
- `Tool '{definition.Name}' allows workspace mutation and cannot run in the governed read-only tool path.`
- `Caller '{request.RequestedBy}' is not allowed to run governed tool '{definition.Name}'.`
- `Nested governed tool call '{definition.Name}' was rejected.`
- `Governed tool policy allowed this read-only call.`

## Infrastructure Usage

`GovernedToolRegistry` continues to:

- resolve a registered tool
- validate input/output type shape
- evaluate policy before typed tool execution
- return a rejected result when policy blocks
- execute the typed tool only after policy allows
- record thought-ledger entries as before

DI continues registering `GovernedToolPolicyEvaluator`, now resolved from `IronDev.Core.Tools`.

`PromotionWorkflowTests` now imports `IronDev.Core.Tools` for direct evaluator construction.

## Tests Added Or Updated

Added Core-only characterization tests:

- `CoreEvaluatorAllowsReadOnlyToolForAllowedCaller`
- `CoreEvaluatorRejectsMutationCapableTool`
- `CoreEvaluatorRejectsFileWriteCapableTool`
- `CoreEvaluatorRejectsProcessExecutionCapableTool`
- `CoreEvaluatorRejectsNetworkCapableTool`
- `CoreEvaluatorRejectsWorkspaceMutationCapableTool`
- `CoreEvaluatorRejectsDisallowedCaller`
- `CoreEvaluatorRejectsNestedCallWhenDisabled`
- `CoreEvaluatorPreservesObservableRejectionOrder`
- `CoreEvaluatorTestsRemainCoreOnly`
- `CoreEvaluatorExtractionTestsDoNotUseRuntimeOrMutationSurfaces`

Updated governed-tool integration coverage:

- `GovernedToolRegistry_UsesCorePolicyEvaluatorBeforeExecution`

No integration tests were deleted or weakened.

## Why The Characterization Tests Are Pure

The G09a unit tests:

- use only `IronDev.Core` types
- use fixed in-memory tool definitions and requests
- call the Core policy evaluator directly
- do not instantiate `GovernedToolRegistry`
- do not instantiate real tools
- do not call `ExecuteAsync`
- do not call providers
- do not run processes
- do not write files
- do not access network
- do not mutate workspace
- do not use API, CLI, SQL, persistence, or integration helpers
- do not use current time directly

## Reported Validation

Local validation:

- `dotnet restore IronDev.slnx`: passed with existing NU1510 warnings
- `dotnet build IronDev.slnx --no-restore -v:minimal -clp:ErrorsOnly`: passed, 0 errors / 2 warnings
- `dotnet build IronDev.UnitTests/IronDev.UnitTests.csproj --no-restore -v:minimal -clp:ErrorsOnly`: passed, 0 errors / 0 warnings
- `dotnet test IronDev.UnitTests/IronDev.UnitTests.csproj --no-build`: 193/193 passed
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~GovernedToolArchitectureTests`: 18/18 passed
- C11 secret scan: 9/9 passed
- `git diff --check`: passed
- `git diff --cached --check`: passed after staging the exact G09a files

GitHub validation:

- fast-unit-ci on pushed G09a head: tracked in PR checks/body

## Known Limitations

G09a extracts the pure policy seam only.

G09a does not add the full governed-tool policy unit-test corpus.

G09a does not execute tools.

G09a does not test full `GovernedToolRegistry` runtime behavior.

G09a does not test real tool bodies.

G09a does not test providers.

G09a does not run processes.

G09a does not write files.

G09a does not access network.

G09a does not mutate workspace.

G09a does not test API or CLI.

G09a does not test SQL persistence.

G09a does not replace integration tests.

G09a does not grant authority.

## Boundary Statement

Extracting tool policy is not granting tool authority.

Moving the gate does not run the tool.

Tool policy evaluation is not tool execution.

Policy allow is not approval.

Policy allow is not source or workspace mutation.

Policy allow is not file-write permission.

Policy allow is not process-execution permission.

Policy allow is not network permission.

Policy allow is not workflow continuation.

Policy reject can block a tool but cannot authorize repair.

The Core evaluator does not run tools, record thought-ledger entries, call providers, mutate state, access filesystem, access network, run processes, call API/CLI, call SQL, or use registry runtime.

## Next Intended Slice

G09b - Unit tests for governed tool policy evaluator.
