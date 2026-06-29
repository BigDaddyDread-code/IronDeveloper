# G08b - Chat Mode Prompt Construction Unit Tests

## Purpose

G08b adds the full fast unit-test corpus for the pure Core chat-mode classifier prompt builder.

G08a provided the Core seam:

- `IronDev.Core.Chat.ChatModeClassificationPromptBuilder.BuildPrompt(...)`

G08b proves the prompt construction rules directly in the fast unit lane.

Review line:

> Prompt construction tests are not model behavior proof.

Killjoy line:

> A safer prompt is not a safer model.

## Files Changed

- `IronDev.UnitTests/Chat/ChatModeClassificationPromptBuilderTestFixtures.cs`
- `IronDev.UnitTests/Chat/ChatModeClassificationPromptBuilderUnitTests.cs`
- `Docs/receipts/G08B_CHAT_MODE_PROMPT_CONSTRUCTION_UNIT_TESTS.md`

No production code changed.

No Infrastructure code changed.

No integration tests were moved, deleted, or weakened.

No project references, package references, API, CLI, SQL, provider, worker, route judge, memory retrieval, tool execution, or CI files changed.

## Core Prompt Builder Tested

The tests target:

- `ChatModeClassificationPromptBuilder.BuildPrompt(ChatModeClassificationRequest request)`

The tests do not target:

- `LlmChatModeClassifier`
- `IChatModeClassifier` integration behavior
- `ILLMService`
- stub LLM behavior
- model-output JSON parsing
- fail-closed behavior
- deterministic classifier short-circuit rules
- route judge services
- ProjectChatResponseService
- API chat endpoints

## G08a Seam Confirmation

G08a extracted prompt construction into Core so G08b can test production prompt construction without referencing Infrastructure, reflecting private methods, copying prompt text into tests, or calling an LLM stub.

## Tests Added

The G08b unit suite covers:

- governance mode headings and definitions
- static prompt rules for Exploration, Formalization, and Confirmation
- default-to-Exploration language
- product vagueness and missing-scope handling
- save/capture/record Formalization signal wording
- short-affirmation boundary wording
- prompt-constrained JSON-only shape
- do-not-answer-user instruction
- route hints as non-authority context retrieval hints
- context clarification as passive evidence only
- RequestKind insufficiency
- ExplicitModeConstraint non-bypass language
- route hint fields
- user message, recent conversation, and project summary rendering
- current user message and missing-context fallbacks
- trusted compiler context rendering
- active artifact formatting
- semantic evidence as ContextOnly citations
- untrusted context memory and skill hint blocking
- memory metadata rendering
- memory list cap
- memory excerpt truncation and newline normalization
- directive-token redaction
- episodic memory forced false
- recent turns as context only
- skill hints as availability only, capped at six
- non-authority prompt phrase guard
- deterministic prompt construction
- Core-only/MSTest-only project guard
- G08b source guard against Infrastructure, LLM service, API host, SQL, provider, memory retrieval, route judge, tool execution, filesystem mutation, and clock/environment access

## Why These Tests Are Pure

The fixtures are fixed and in-memory:

- fixed user messages
- fixed route hints
- fixed project summaries
- fixed context states
- fixed memory evidence
- fixed skill hints
- no current time
- no config
- no environment reads
- no file mutation
- no model/provider calls
- no memory retrieval calls
- no route judge calls
- no tool execution
- no API/CLI/SQL calls

## Dependencies Excluded

`IronDev.UnitTests` remains a fast Core-only test project:

- project reference: `IronDev.Core`
- packages: MSTest test SDK/adapter/framework only

G08b adds no references to Infrastructure, API, CLI, UI, SQL, persistence, workers, providers, model resolution, memory retrieval, route judge, tool execution, or executor code.

## Reported Validation

Local validation:

- `dotnet restore IronDev.slnx`: passed with existing NU1510 warnings
- `dotnet build IronDev.slnx --no-restore`: passed with existing warnings
- `dotnet build IronDev.UnitTests/IronDev.UnitTests.csproj --no-restore`: passed
- `dotnet test IronDev.UnitTests/IronDev.UnitTests.csproj --no-build`: 182/182 passed
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~LlmChatModeClassifierTests`: 18/18 passed
- C11 secret scan: 9/9 passed
- `git diff --check`: passed
- `git diff --cached --check`: passed after staging the exact G08b files

GitHub validation:

- fast-unit-ci on pushed G08b head: tracked in PR checks/body

## Known Limitations

G08b tests Core prompt construction only.

G08b does not test model behavior.

G08b does not call ILLMService.

G08b does not test LlmChatModeClassifier integration behavior.

G08b does not test deterministic classifier short-circuit behavior.

G08b does not test prompt JSON parsing.

G08b does not test fail-closed classifier behavior.

G08b does not test API or CLI.

G08b does not test SQL persistence.

G08b does not test memory retrieval.

G08b does not test route judge behavior.

G08b does not change production classifier behavior.

G08b does not replace integration tests.

G08b does not grant authority.

## Boundary Statement

Prompt construction tests are not model behavior proof.

A safer prompt is not a safer model.

Prompt text is not authority.

Prompt text is not classification.

Route hints are not authority.

Memory evidence is not authority.

Skill hints are not authority.

Explicit mode constraint is not authority.

Context clarification is not authority.

RequestKind is not authority.

Prompt JSON shape is not provider-enforced schema mode.

Prompt construction does not create tickets, formalize work, call tools, mutate memory, continue workflow, or grant downstream authority.

Fast prompt tests are not integration classifier proof, model/provider proof, API proof, SQL proof, or release readiness.

## Next Intended Migration Area

G09 - Unit tests for governed tool policy evaluator.
