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

IRONDEV-168 wires that evidence loop into the product-shaped disposable build command. `build disposable run --project Solitaire --goal "I want build solitaire"` creates run-scoped docs, runs the governed Planner/Critic loop, runs the caged BuilderAgent repair build, runs QualityAgent/Killjoy, and returns one report. Generated app files stay in the disposable workspace; accepted memory mutation, ticket acceptance, real repository writes, and self-approval remain blocked.

IRONDEV-169 introduces `ProposedChange`, `PromotionPackage`, and the language runtime spine. A successful disposable run can now be packaged for human/Codex review with promotable files, blocked generated outputs, build/test/quality evidence, risks, checklist, runtime profile, and `NeedsHumanReview` approval state. `csharp-dotnet` is executable; Java, TypeScript, and Python profiles are contract-only until reviewed executors exist.

IRONDEV-170 consumes a `PromotionPackage` and applies only its promotable files into an isolated candidate workspace outside the active repo. It runs C#/.NET build/test in that workspace, rejects blocked generated outputs, preserves `ProposedChangeId`, and keeps `NeedsHumanReview`. It still does not write main, mutate accepted memory, accept tickets, auto-merge, or approve itself.

IRONDEV-171 hardens the Run Reports viewer into a promotion review cockpit. The shared run report services now expose promotion package id, proposed change id, approval state, runtime profile, promotable files, blocked files, configurable policy settings, and hard safety invariants. This is review visibility only; it does not grant approval or write authority.
