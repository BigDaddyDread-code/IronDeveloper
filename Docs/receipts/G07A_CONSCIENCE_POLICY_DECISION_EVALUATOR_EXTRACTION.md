# G07a - Conscience Policy Decision Evaluator Extraction

## Purpose

Extract the deterministic ConscienceAgent policy-decision rules from Infrastructure into a pure Core evaluator.

Review line:

> Extracting policy logic is not expanding policy authority.

Killjoy line:

> Moving the rule into Core does not make the rule stronger.

## Why G07 Was Blocked

G07 fast unit tests could not safely proceed because the Conscience policy rules were embedded in `IronDev.Infrastructure/Services/Agents/ConscienceAgent.cs`.

Testing those rules from `IronDev.UnitTests` would have required either referencing Infrastructure, instantiating `ConscienceAgent`, copying production logic into tests, or testing unrelated Core Conscience helpers. All four would weaken the fast lane.

## Files Changed

- `IronDev.Core/Agents/ConsciencePolicyDecisionModels.cs`
- `IronDev.Core/Agents/ConsciencePolicyDecisionEvaluator.cs`
- `IronDev.Infrastructure/Services/Agents/ConscienceAgent.cs`
- `IronDev.UnitTests/Conscience/ConsciencePolicyDecisionEvaluatorExtractionTests.cs`
- `IronDev.IntegrationTests/ConscienceAgentTests.cs`
- `Docs/receipts/G07A_CONSCIENCE_POLICY_DECISION_EVALUATOR_EXTRACTION.md`

## Core Evaluator And Models

- `ConsciencePolicyDecisionRequest`
- `ConsciencePolicyDecision`
- `ConsciencePolicyDecisionEvaluator`

The evaluator lives in `IronDev.Core.Agents` and uses only in-memory request values. It does not call agents, models, providers, memory, retrieval, tools, API, CLI, SQL, executors, Git, or workflow mutation.

## Exact Behavior Preserved

G07a preserves the existing policy-decision behavior:

- decision values remain `Allow`, `Block`, and `NeedsMoreEvidence`
- boundary text remains `ConscienceAgent reviews only. It does not patch, create tickets, mutate memory, or approve itself.`
- self-approval and auto-merge still block with `NoAgentSelfApproval`
- governance bypass still blocks with `GovernanceGatesCannotBeBypassed`
- real repository and developer working-tree mutation still block with `NoRealRepositoryWrites`
- TesterAgent repair or mutation still blocks with `TesterAgentExecutesOnly`
- SentinelAgent mutation still blocks with `SentinelAgentObservesOnly`
- ResearchAgent authority override still blocks with `ProjectMemoryRemainsAuthority`
- disposable workspace apply/patch still requires explicit workspace boundary evidence
- missing action type, project identity, and evidence still produce missing-evidence decisions
- stop and collect-evidence next steps remain unchanged
- allowing factors remain unchanged for evidence-backed safe reviews
- ConscienceAgent JSON field names remain unchanged

## Infrastructure Delegation

`ConscienceAgent` still resolves its model profile and reads `AgentRequest` inputs as before.

It now converts those input strings into `ConsciencePolicyDecisionRequest`, delegates to `ConsciencePolicyDecisionEvaluator.Evaluate(...)`, serializes the Core decision as the existing JSON output shape, and preserves the AgentResult status, summary, provider, model, command record, evidence paths, and completion behavior.

## Tests Added Or Updated

Fast unit characterization tests were added for the Core seam:

- self-approval blocks
- governance bypass blocks
- disposable workspace without boundary evidence needs more evidence
- evidence-backed disposable workspace review can return `Allow`
- `Allow` remains review output only, not authority
- `IronDev.UnitTests` remains Core-only and does not reference Infrastructure, ConscienceAgent, model resolver, API, SQL, providers, tools, memory, retrieval, filesystem writes, or current time

The existing ConscienceAgent integration tests remain in place. One preservation test was added to assert the delegated JSON shape remains the same.

No integration tests were deleted.

## Commands Run

- `dotnet restore IronDev.slnx`
- `dotnet build IronDev.slnx --no-restore`
- `dotnet build IronDev.UnitTests/IronDev.UnitTests.csproj --no-restore`
- `dotnet test IronDev.UnitTests/IronDev.UnitTests.csproj --no-build --logger "console;verbosity=minimal"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~ConscienceAgentTests" --logger "console;verbosity=minimal"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~BlockC11SecretScanningRegressionTests" --logger "console;verbosity=minimal"`
- `git diff --check`
- `git diff --cached --check`

## Reported Validation

- Restore: passed with existing integration package-prune warnings.
- Build: passed with 0 errors and existing warnings.
- Unit test project build: passed with 0 errors.
- Fast unit tests: 96/96 passed.
- ConscienceAgent integration filter: 10/10 passed.
- C11 secret scan: 9/9 passed.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## GitHub CI

`fast-unit-ci`: passed on the first pushed G07a head `f3394249a5671811d4a05ead5b4196b9ce575cc8`.

- Run: `28337733909`
- Job: `83946745266`

Current-head GitHub CI evidence is tracked on the PR checks and PR body after the final push.

## Known Limitations

G07a extracts the pure policy seam only.
G07a does not add the full Conscience policy unit-test corpus.
G07a does not expand Conscience authority.
G07a does not test model/provider behavior.
G07a does not test API or CLI.
G07a does not test SQL persistence.
G07a does not test executors.
G07a does not call memory or retrieval.
G07a does not replace ConscienceAgent integration tests.
G07a does not grant authority.

## Next Intended Slice

G07b should add the full fast unit-test corpus for `ConsciencePolicyDecisionEvaluator` without touching Infrastructure, model resolution, providers, memory, retrieval, tools, API, CLI, SQL, or executors.
