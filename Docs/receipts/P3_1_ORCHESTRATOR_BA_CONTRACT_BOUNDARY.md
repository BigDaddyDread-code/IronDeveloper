# P3-1 Orchestrator/BA Contract Boundary

## Purpose

P3-1 adds the missing Core Orchestrator/BA role boundary.

The Orchestrator is the requirements shaper and flow coordinator. It turns messy human intent into a structured work contract that Builder, Tester, Critic, and Human Gate can each measure against.

This is a contract/boundary slice only. It does not build the durable Orchestrator loop.

## Role Definition

The Orchestrator is defined as:

- contract author
- scope clarifier
- acceptance-criteria shaper
- next-safe-step recommender
- role-boundary coordinator

The Orchestrator is explicitly not:

- source mutator
- test author
- critic
- approval authority
- policy authority
- workflow continuation authority
- apply authority
- release/deployment authority
- judge of its own contract

## Agent Boundary

P3-1 adds `AgentKind.OrchestratorAgent = 9` without renumbering existing agent kinds.

The catalog definition is:

- `agentId`: `builtin.orchestrator-ba`
- `name`: `OrchestratorAgent`
- `kind`: `OrchestratorAgent`
- `mode`: `ProposalOnly`
- allowed capabilities: `CreateReport`, `CreateHandoff`

The Orchestrator does not receive `RunTool`, `MutateSource`, `CallExternalSystem`, `PromoteCollectiveMemory`, human-decision representation, `BlockExecution`, `CreateCriticFinding`, or `CreateTestReport`.

It may recommend a blocked next safe step. Backend gates own actual refusal.

## Contract Boundary

P3-1 adds Core-only contract models and validation for:

- contract identity
- source intent reference
- scope items
- measurable acceptance criteria
- Builder / Tester / Critic / Human Gate role boundaries
- risks
- open questions
- retrieved context references
- recommendation-only next safe step
- explicit false authority flags

Boundary text:

> The Orchestrator writes the contract. It does not judge the result.

Retrieved context may inform a draft contract, but it is not authority.

## Authority Boundary

An Orchestrator work contract is not:

- approval
- test proof
- critic review
- policy satisfaction
- workflow continuation
- source apply permission
- release readiness
- deployment readiness

The contract can describe what another role should measure. It cannot declare that the measurement has passed.

## Files Changed

- `IronDev.Core/Agents/AgentModels.cs`
- `IronDev.Core/Agents/AgentDefinitionCatalog.cs`
- `IronDev.Core/Agents/AgentDefinitionValidator.cs`
- `IronDev.Core/Orchestration/OrchestratorContractModels.cs`
- `IronDev.Core/Orchestration/OrchestratorContractValidator.cs`
- `IronDev.IntegrationTests/Agents/OrchestratorAgentBoundaryTests.cs`
- `IronDev.IntegrationTests/Orchestration/OrchestratorContractBoundaryTests.cs`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`
- `Docs/receipts/P3_1_ORCHESTRATOR_BA_CONTRACT_BOUNDARY.md`

## Out Of Scope

P3-1 does not add:

- durable Orchestrator loop behavior
- run start behavior
- run continuation behavior
- source apply behavior
- test execution or test authoring behavior
- critic execution or critic-review recording
- approval recording
- policy satisfaction
- commits
- pushes
- PR creation or ready-for-review behavior
- merge, release, or deployment behavior
- memory promotion
- channel or chat behavior
- API, UI, SQL schema, or provider integration

## Validation

- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "FullyQualifiedName~OrchestratorAgentBoundaryTests|FullyQualifiedName~OrchestratorContractBoundaryTests" --logger "console;verbosity=minimal"`: passed, 14/14.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~IntegrationTestCategoryContractTests --logger "console;verbosity=minimal"`: passed, 7/7.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~AgentDefinitionBoundaryTests --logger "console;verbosity=minimal"`: passed, 15/15.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests --logger "console;verbosity=minimal"`: passed, 9/9.
- `dotnet restore IronDev.slnx`: passed with existing warnings.
- `dotnet build IronDev.slnx --no-restore`: passed, 0 errors / 7 warnings.
- `git diff --check`: passed.

Additional validation is tracked in the PR body and GitHub checks.

## Next PR

P3-2 - Skeleton evidence package to critic.

Review line: The Orchestrator writes the contract. It does not judge the result.

Killjoy line: A BA who approves their own acceptance criteria is just self-approval with a nicer title.
