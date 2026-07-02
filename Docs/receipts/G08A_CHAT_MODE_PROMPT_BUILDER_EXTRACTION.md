# G08a - Chat Mode Prompt Builder Extraction

## Purpose

G08a extracts deterministic chat-mode classifier prompt construction from Infrastructure into a pure Core prompt builder.

This creates the seam required for the full G08b fast unit-test corpus.

Review line:

> Extracting prompt construction is not changing classifier authority.

Killjoy line:

> Moving the prompt does not make the model safer.

## Why G08 Was Blocked

G08 was blocked because chat-mode prompt construction lived inside:

- `IronDev.Infrastructure/Services/LlmChatModeClassifier.cs`
- private method: `BuildPrompt(...)`

Fast unit tests could not target production prompt construction without referencing Infrastructure, using reflection against a private method, copying prompt text into tests, or testing through an LLM stub.

G08a fixes that by creating a Core prompt-builder seam.

## Files Changed

- `IronDev.Core/Chat/ChatModeClassificationPromptBuilder.cs`
- `IronDev.Infrastructure/Services/LlmChatModeClassifier.cs`
- `IronDev.UnitTests/Chat/ChatModeClassificationPromptBuilderExtractionTests.cs`
- `IronDev.IntegrationTests/LlmChatModeClassifierTests.cs`
- `Docs/receipts/G08A_CHAT_MODE_PROMPT_BUILDER_EXTRACTION.md`

No API, CLI, SQL, UI, workflow, executor, provider, memory retrieval, route judge, project-reference, package-reference, or CI files changed.

## Core Prompt Builder

New Core seam:

- `IronDev.Core.Chat.ChatModeClassificationPromptBuilder`
- API: `BuildPrompt(ChatModeClassificationRequest request)`

The builder is static, deterministic, and in-memory.

It does not call models, providers, tools, memory retrieval, route judge services, API, CLI, SQL, or filesystem mutation.

## Behavior Preserved

The extracted prompt builder preserves the existing prompt behavior:

- governance mode classification instruction
- Exploration, Formalization, and Confirmation definitions
- default-to-Exploration rule
- explicit save/capture/record Formalization signal
- short-affirmation governance rule
- route hints as context retrieval hints only
- context clarification as passive evidence only
- RequestKind insufficiency language
- ExplicitModeConstraint non-bypass language
- do-not-answer-user instruction
- prompt-constrained JSON-only shape
- user message
- recent conversation or `none`
- project summary or `none`
- working memory context
- context-state origin
- trusted compiler versus untrusted-input-blocked markings
- episodic memory forced false
- semantic memory evidence as ContextOnly citations, not directives
- procedural skill hints as availability only, no policy
- route hint fields
- context clarification and explicit mode fields

Memory/context normalization remains equivalent:

- trusted ProjectChatResponseCompiler context can include normalized semantic evidence
- untrusted context removes semantic evidence
- untrusted context removes skill hints
- `EpisodicMemoryEnabled` is forced false
- memory evidence `UsedFor` is normalized to `ContextOnly`
- memory directive tokens are redacted
- memory excerpts are truncated

## Infrastructure Delegation

`LlmChatModeClassifier` now delegates model-prompt construction to:

- `ChatModeClassificationPromptBuilder.BuildPrompt(request)`

The classifier keeps:

- deterministic short-circuit behavior
- model call timing
- JSON parsing behavior
- fail-closed behavior
- `IChatModeClassifier` contract
- model output interpretation

## Tests Added Or Updated

Added fast Core-only characterization tests:

- mode definitions and JSON prompt shape
- route hints/context clarification/request kind/explicit mode as non-authority
- trusted memory normalized to ContextOnly
- untrusted context memory and skill hints blocked
- memory directive tokens redacted
- memory excerpt truncation
- unit project remains Core-only/MSTest-only
- chat prompt unit tests avoid Infrastructure, LLM service, API host, SQL, provider, memory retrieval, tool execution, filesystem mutation, and clock usage

Updated focused Infrastructure integration coverage:

- `ClassifyAsync_DelegatesToCorePromptBuilderShape`

Existing `LlmChatModeClassifierTests` remain in place.

## Reported Validation

Local validation:

- `dotnet restore IronDev.slnx`: passed with existing restore warnings
- `dotnet build IronDev.slnx --no-restore`: passed with existing warnings
- `dotnet build IronDev.UnitTests/IronDev.UnitTests.csproj --no-restore`: passed with existing warnings
- `dotnet test IronDev.UnitTests/IronDev.UnitTests.csproj --no-build`: passed, 158/158
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~LlmChatModeClassifierTests`: passed, 17/17
- C11 secret scan: passed, 9/9
- `git diff --check`: passed
- `git diff --cached --check`: passed

GitHub validation:

- fast-unit-ci on initial G08a head `67df42714b009b9d5fbc8b9515a87bf4b38336cb`: passed
- Run: `28339602825`
- Job: `83951635463`
- Current-head fast-unit-ci is tracked in the PR checks and PR body.

## Known Limitations

G08a extracts the pure prompt construction seam only.

G08a does not add the full prompt construction unit-test corpus.

G08a does not test model behavior.

G08a does not call ILLMService.

G08a does not test prompt JSON parsing.

G08a does not test fail-closed classifier behavior.

G08a does not change deterministic classifier rules.

G08a does not test API or CLI.

G08a does not test SQL persistence.

G08a does not test memory retrieval.

G08a does not test route judge behavior.

G08a does not replace LlmChatModeClassifier integration tests.

G08a does not grant authority.

## Boundary Statement

Prompt construction is not classification.

Prompt text is not authority.

Prompt text is not model behavior proof.

Route hints are not authority.

Memory evidence is not authority.

Skill hints are not authority.

Explicit mode constraint is not authority.

Context clarification is not authority.

RequestKind is not authority.

Prompt JSON shape is not provider-enforced schema mode.

Core prompt builder does not call a model, create tickets, formalize work, invoke tools, mutate memory, or continue workflow.

## Next Intended Slice

G08b - Unit tests for chat mode classifier prompt construction.
