# G07b - Conscience Policy Decision Evaluator Unit Tests

## Purpose

G07b adds the full fast unit-test corpus for the pure Core `ConsciencePolicyDecisionEvaluator`.

G07a created the Core seam. G07b proves the policy decision rules in the fast unit lane so future ConscienceAgent changes cannot silently weaken the Core authority boundary.

Review line:

> Conscience policy tests are not conscience authority.

Killjoy line:

> A policy decision can block. It cannot self-authorize.

## Files Changed

- `IronDev.UnitTests/Conscience/ConsciencePolicyDecisionTestFixtures.cs`
- `IronDev.UnitTests/Conscience/ConsciencePolicyDecisionEvaluatorUnitTests.cs`
- `Docs/receipts/G07B_CONSCIENCE_POLICY_DECISION_UNIT_TESTS.md`

No production code changed.

No integration tests were moved, deleted, or weakened.

## Core Evaluator Tested

The tests target:

- `IronDev.Core.Agents.ConsciencePolicyDecisionEvaluator.Evaluate(...)`

The tests do not target:

- `ConscienceAgent`
- `AgentModelResolver`
- `StaticIronDevAgent`
- `AgentResult` JSON serialization
- API or CLI surfaces
- SQL persistence
- executors
- model, provider, tool, memory, or retrieval runtime paths

## Tests Added

The G07b unit suite covers:

- self-approval and automerge blocks
- governance bypass blocks
- real repository and developer working-tree mutation blocks
- TesterAgent repair/mutation blocks
- SentinelAgent mutation blocks
- ResearchAgent authority override blocks
- missing action/project/evidence handling
- disposable workspace boundary evidence
- safe evidence-backed review output
- decision priority when blocked and missing evidence both exist
- stable confidence values
- JSON decision field shape
- review-only boundary text
- no action-authority flags on the decision model
- requested tools echoed as context only
- memory authority refs preserved as context references only
- fast unit dependency guard for Core-only/MSTest-only references
- G07b source guard against Infrastructure, API host, SQL, process, filesystem mutation, provider, memory retrieval, and tool execution dependencies

## Purity

The fixtures are fixed and in-memory:

- fixed project IDs
- fixed evidence refs
- fixed memory context refs
- fixed safety-boundary refs
- no clock access
- no config access
- no file mutation
- no provider calls
- no memory or retrieval calls
- no tool execution
- no agent runtime construction

## Dependencies Excluded

`IronDev.UnitTests` remains a fast Core-only test project:

- project reference: `IronDev.Core`
- packages: MSTest test SDK/adapter/framework only

G07b adds no references to Infrastructure, API, CLI, UI, SQL, persistence, workers, providers, model resolution, memory, retrieval, or executor code.

## Reported Validation

Local validation:

- `dotnet restore IronDev.slnx`: passed with existing restore warnings
- `dotnet build IronDev.slnx --no-restore`: passed with existing warnings
- `dotnet build IronDev.UnitTests/IronDev.UnitTests.csproj --no-restore`: passed with existing warnings
- `dotnet test IronDev.UnitTests/IronDev.UnitTests.csproj --no-build`: passed, 151/151
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~ConscienceAgentTests`: passed, 10/10
- C11 secret scan: passed, 9/9
- `git diff --check`: passed
- `git diff --cached --check`: passed

GitHub validation:

- fast-unit-ci on initial G07b head `79206545bd563dc20e90cb2668edfb51d61d3aa8`: passed
- Run: `28338365668`
- Job: `83948427774`
- Current-head fast-unit-ci is tracked in the PR checks and PR body.

## Known Limitations

G07b tests the Core policy evaluator only.

G07b does not test ConscienceAgent infrastructure wiring.

G07b does not test AgentResult JSON serialization.

G07b does not test model resolution.

G07b does not call agents, models, tools, providers, memory, or retrieval.

G07b does not test API or CLI.

G07b does not test SQL persistence.

G07b does not test executors.

G07b does not change production Conscience behavior.

G07b does not replace integration tests.

G07b does not grant authority.

## Boundary Statement

Conscience `Allow` is review output only.

Conscience `Allow` is not execution permission, approval, policy satisfaction, source mutation authority, memory promotion authority, workflow continuation, merge authority, release authority, or deployment authority.

Conscience `Block` can stop work, but cannot authorize repair.

`NeedsMoreEvidence` means collect evidence, not infer permission.

Requested tools are echoed context, not executed tools.

Memory authority refs are context references, not authority grants.

Fast policy tests are not agent runtime proof, model/provider proof, API proof, SQL proof, release proof, or ship readiness.

## Next Intended Migration Area

G08 - Unit tests for chat mode classifier prompt construction.
