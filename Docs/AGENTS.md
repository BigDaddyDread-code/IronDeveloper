# IronDev Agents

This document is the current source of truth for the IronDev/IDA agent layer.

## Operating Principle

IronDev uses governed autonomy, not free autonomy.

Agents may reason, package evidence, run bounded plans, and write inside explicit disposable workspaces when the cage is proven. Agents must not write the real repository, mutate accepted memory, create live tickets, or approve themselves unless a future reviewed control path explicitly grants that capability.

## Current Agent Roles

| Agent | Current maturity | Default profile | Authority |
| --- | --- | --- | --- |
| SupervisorAgent | Opt-in live governed orchestration | strong-reasoner | Coordinates memory, ConscienceAgent, ThoughtLedger, and TesterAgent loops. May call a configured model only after deterministic orchestration state exists. Stops on missing evidence or blocked governance. |
| PlannerAgent | Opt-in live governed planning path with deterministic fallback | standard-reasoner | Drafts plans and product-spike intake packages. May call a configured model only when explicitly enabled. Does not execute or write. |
| ArchitectAgent | Opt-in live governed architecture review | strong-reasoner | Reviews proposals against weighted context and safety boundaries. May call a configured model only when explicitly enabled. Does not create accepted decisions or patch. |
| BuilderAgent | Caged disposable repair loop | code-builder | May write only inside explicit disposable workspaces. Real repo writes remain blocked. |
| TesterAgent | C# dogfood runner wrapper | cheap-runner | Executes plans and reports. Does not fix. |
| QualityAgent / KilljoyAgent | Opt-in live governed quality commentary | cheap-runner | Runs build/test/format/package/code-standards checks and reports debt. May call a configured model for advisory risk notes only. Does not refactor or override gates. |
| RetrieverAgent | Opt-in live governed weighted context packer | cheap-runner | Packages accepted project memory, rankings, rejected context, and trace evidence. May call a configured model only when explicitly enabled. Does not decide implementation or override ranking. |
| CriticAgent | Opt-in live governed failure/evidence reviewer | strong-reviewer | Reviews failure packages and risks. May call a configured model only when explicitly enabled. Does not patch. |
| SentinelAgent | Opt-in live governed internal observation | cheap-runner | Emits advisory insight artefacts. May call a configured model only when explicitly enabled. Does not create tickets or mutate memory. |
| ResearchAgent | Opt-in live governed external evidence packer | cheap-runner | Packages explicit external evidence only. May call a configured model only when explicitly enabled. Project memory remains authority. |
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

IRONDEV-160 extends the same opt-in live governed pattern to RetrieverAgent and SentinelAgent. Live model output cannot override memory ranking, project scoping, or insight classification; it remains advisory evidence only.

IRONDEV-161 completes the current useful opt-in live agent pass for ResearchAgent, QualityAgent, and SupervisorAgent. TesterAgent, ConscienceAgent, and ThoughtLedger intentionally remain deterministic because they execute, gate, and explain rather than freely decide.

IRONDEV-162 through IRONDEV-167 add the first governed Planner/Critic tool-using reasoning loop. Agents now request named capabilities such as `memory.search`, `code.search`, `quality.run-gate`, and `project.build`; the registry validates the request, the runner records evidence, CriticAgent reviews sufficiency, PlannerAgent revises the plan, and a human escalation gate decides whether more evidence or review is required. Tool requests remain read/test/report-only in this slice.
