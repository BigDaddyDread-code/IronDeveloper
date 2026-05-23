# IronDev Agents

This document is the current source of truth for the IronDev/IDA agent layer.

## Operating Principle

IronDev uses governed autonomy, not free autonomy.

Agents may reason, package evidence, run bounded plans, and write inside explicit disposable workspaces when the cage is proven. Agents must not write the real repository, mutate accepted memory, create live tickets, or approve themselves unless a future reviewed control path explicitly grants that capability.

## Current Agent Roles

| Agent | Current maturity | Default profile | Authority |
| --- | --- | --- | --- |
| SupervisorAgent | Governed orchestration | strong-reasoner | Coordinates memory, ConscienceAgent, ThoughtLedger, and TesterAgent loops. Stops on missing evidence or blocked governance. |
| PlannerAgent | Opt-in live governed planning path with deterministic fallback | standard-reasoner | Drafts plans and product-spike intake packages. May call a configured model only when explicitly enabled. Does not execute or write. |
| ArchitectAgent | Opt-in live governed architecture review | strong-reasoner | Reviews proposals against weighted context and safety boundaries. May call a configured model only when explicitly enabled. Does not create accepted decisions or patch. |
| BuilderAgent | Caged disposable repair loop | code-builder | May write only inside explicit disposable workspaces. Real repo writes remain blocked. |
| TesterAgent | C# dogfood runner wrapper | cheap-runner | Executes plans and reports. Does not fix. |
| QualityAgent / KilljoyAgent | Deterministic quality gate | cheap-runner | Runs build/test/format/package/code-standards checks and reports debt. Does not refactor. |
| RetrieverAgent | Weighted context packer | cheap-runner | Packages accepted project memory, rankings, rejected context, and trace evidence. Does not decide implementation. |
| CriticAgent | Opt-in live governed failure/evidence reviewer | strong-reviewer | Reviews failure packages and risks. May call a configured model only when explicitly enabled. Does not patch. |
| SentinelAgent | Internal observation | cheap-runner | Emits advisory insight artefacts. Does not create tickets or mutate memory. |
| ResearchAgent | External evidence packer | cheap-runner | Packages explicit external evidence only. Project memory remains authority. |
| ConscienceAgent | Governance gate | cheap-runner | Returns Allow, Block, or NeedsMoreEvidence. Does not execute. |
| ThoughtLedger | Visible reasoning summary | cheap-runner | Explains evidence, uncertainty, blocked actions, and safer alternatives without exposing hidden chain-of-thought. |

## Model Profiles

Model profiles are runtime-configurable from `IronDeveloper/appsettings*.json`.

Supported providers:

- `OpenAI`
- `LocalOpenAI`
- `Ollama`

Local profiles are configuration support only. A configured local profile does not grant authority or bypass ConscienceAgent.

Live model calls are opt-in per governed command. If the provider is unavailable, the agent records the attempt and keeps deterministic fallback behaviour in force.

## Governance Rules

- Real repository writes remain blocked.
- Disposable workspace writes require explicit cage evidence.
- ConscienceAgent and ThoughtLedger are required before write-capable workflows.
- TesterAgent executes only.
- SentinelAgent observes only.
- ResearchAgent evidence cannot override accepted project memory.
- BuilderAgent may repair only inside the disposable workspace.
- Human approval is still required before any future real repository apply path.

## Current Campaign

IRONDEV-157 matures the control plane by:

- enabling OpenAI, LocalOpenAI, and Ollama model profiles,
- adding a governed ArchitectAgent path,
- keeping BuilderAgent caged and trace-backed,
- keeping SupervisorAgent bounded by ConscienceAgent and ThoughtLedger,
- preserving C# dogfood execution as the primary plan runner,
- preserving Run Reports as the trace/evidence viewer foundation.

IRONDEV-158 adds the first opt-in live governed agent execution path for ArchitectAgent while preserving deterministic fallback and all no-write boundaries.

IRONDEV-159 extends the same opt-in live governed pattern to CriticAgent and PlannerAgent. Live model output is advisory evidence only; deterministic routing/review remains in force, and no writes, memory mutation, ticket creation, patch apply, or self-approval authority is granted.
