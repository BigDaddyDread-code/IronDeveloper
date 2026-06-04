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
| QualityAgent / KilljoyAgent / Code Review Agent | Opt-in live governed quality commentary | cheap-runner | Runs build/test/format/package/code-standards checks and reports debt. May call a configured model for advisory risk notes only. Does not refactor or override gates. |
| RetrieverAgent | Opt-in live governed weighted context packer | cheap-runner | Packages accepted project memory, rankings, rejected context, and trace evidence. May call a configured model only when explicitly enabled. Does not decide implementation or override ranking. |
| CriticAgent | Opt-in live governed failure/evidence reviewer | strong-reviewer | Reviews failure packages and risks. May call a configured model only when explicitly enabled. Does not patch. |
| DoubtAgent | Adversarial review path with deterministic fallback | strong-reviewer | Stress-tests plans, promotion packages, and proposed changes for hidden risk. High/Critical findings require explicit Killjoy rebuttal. Does not patch, block forever, mutate memory, or approve writes. |
| MemoryImprovementAgent | Level 1 proposal-only memory improvement path | standard-reasoner | Reviews completed-run evidence and proposes memory improvements with evidence bundles and token/proposal budgets. Does not write staging memory, mutate accepted memory, create tickets, or receive accepted-memory keys in Alpha. |
| SentinelAgent | Opt-in live governed internal observation | cheap-runner | Emits advisory insight artefacts. May call a configured model only when explicitly enabled. Does not create tickets or mutate memory. |
| ResearchAgent | Opt-in live governed external evidence packer | cheap-runner | Packages explicit external evidence only. May call a configured model only when explicitly enabled. Project memory remains authority. |
| ConscienceAgent | Governance gate | cheap-runner | Returns Allow, Block, or NeedsMoreEvidence. Does not execute. |
| ThoughtLedger | Visible reasoning summary | cheap-runner | Explains evidence, uncertainty, blocked actions, and safer alternatives without exposing hidden chain-of-thought. |

## Model Profiles

Model profiles are runtime-configurable from `IronDev.Api/appsettings*.json`.

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
- DoubtAgent can require rebuttal/revision but cannot mutate state or create infinite review loops.
- MemoryImprovementAgent starts at Level 1 ProposalOnly. It can recommend memory improvements with governed evidence bundles, but it cannot write staging memory or accepted memory until MemoryKeyGate evidence proves it deserves a higher key.
- BuilderAgent may repair only inside the disposable workspace.
- Human approval is still required before any future real repository apply path.

## Workflow Boundary Governance (Discussion, Chat, Proposal, Build, Run)

Every workflow refactor touching the self-improvement spine must be governed by a concrete ownership matrix in `Docs/ARCHITECTURE.md` and validated by tests.

Mandatory checks before merge:

- The stage matrix in `Docs/ARCHITECTURE.md` includes all five stages: Discussion, Chat, Proposal, Build, and Run.
- Changes that alter stage ownership or contracts also update `Docs/ALPHA_COCKPIT_BACKEND_CONTRACT.md`.
- Chat-mode behavior changes that alter governance surfacing must update:
  - `Docs/ARCHITECTURE.md` with explicit Exploration/Formalization/Confirmation rules.
  - `Docs/ALPHA_COCKPIT_BACKEND_CONTRACT.md` with UI/back-end mode contract.
  - Decision log entries in `Docs/decisions/` when mode taxonomy or governance gates change.
- Chat replay and persistence changes must also update the persisted `ChatMessage.Tags` schema:
  - Persisted assistant responses must include a versioned envelope (`v:1`) with mode + reasoning + governance metadata.
  - UI mapping code must read this envelope when restoring messages and must not infer modes from empty defaults.
- API boundary tests include updated seam ownership checks in `IronDev.IntegrationTests/ApiBoundaryTests.cs`.
- Proposal outputs that enter Run must be hard-failed by validation gates before any build, test, or command execution.
  - `DisposableCodeRunService` must only transition a run from `Running` to `PausedForApproval` when `CodeProposalValidationResult.IsValid` is true.
  - Invalid proposals must always persist `code-proposal.json`, `code-proposal-validation.json`, emit `CodeProposalRejected`, and transition to `Failed`.
- Ticket/build/review packages continue to use project-scoped IDs only; no product workflow route accepts free-form workspace roots, command lists, or cleanup policy from clients.
- Product CLI and Tauri client integration only use `IronDev.Client`; any scenario helpers remain labeled internal dogfood unless they map to the same product spine.

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

IRONDEV-162 through IRONDEV-167 added the first governed Planner/Critic tool-using reasoning loop. IRONDEV-018 contains that path: agents request named capabilities such as `memory.search`, `code.search`, `quality.run-gate`, and `project.build`; the registry validates the request; the runner records evidence; deterministic evidence validators review sufficiency; PlannerAgent revises the plan; and a human escalation gate decides whether more evidence or review is required. Tool requests remain read/test/report-only in this slice. CriticAgent remains a legacy opt-in failure-package reviewer, not the default evidence-sufficiency step for governed tools.

IRONDEV-168 wires that evidence loop into the product-shaped disposable build command. `build disposable run --project Solitaire --goal "I want build solitaire"` creates run-scoped docs, runs the governed Planner/Critic loop, runs the caged BuilderAgent repair build, runs QualityAgent/Killjoy, and returns one report. IRONDEV-184 proves the same command can run a second product, Minesweeper, with `MINESWEEPER_*` run-scoped docs and a Minesweeper-specific disposable repair loop instead of silently reusing Solitaire scope. IRONDEV-185 proves the loop can leave WPF/game assumptions behind by building a Tiny ASP.NET Core REST API with endpoints, DTOs, validation tests, and `TINYRESTAPI_*` run-scoped docs. Generated app files stay in the disposable workspace; accepted memory mutation, ticket acceptance, real repository writes, and self-approval remain blocked.

IRONDEV-169 introduces `ProposedChange`, `PromotionPackage`, and the language runtime spine. A successful disposable run can now be packaged for human/Codex review with promotable files, blocked generated outputs, build/test/quality evidence, risks, checklist, runtime profile, and `NeedsHumanReview` approval state. `csharp-dotnet` is executable; Java, TypeScript, and Python profiles are contract-only until reviewed executors exist.

IRONDEV-170 consumes a `PromotionPackage` and applies only its promotable files into an isolated candidate workspace outside the active repo. It runs C#/.NET build/test in that workspace, rejects blocked generated outputs, preserves `ProposedChangeId`, and keeps `NeedsHumanReview`. It still does not write main, mutate accepted memory, accept tickets, auto-merge, or approve itself.

IRONDEV-171 hardens the Run Reports viewer into a promotion review cockpit. The shared run report services now expose promotion package id, proposed change id, approval state, runtime profile, promotable files, blocked files, configurable policy settings, and hard safety invariants. This is review visibility only; it does not grant approval or write authority.

IRONDEV-172 defines the controlled real-repository write path before implementing it. The future path is settings-first for runtime adapters, commands, branch naming, worktree roots, reviewer roles, evidence retention, and policy thresholds, but hard invariants still win: no direct `main` writes, no active developer working tree writes, no self-approval, no ConscienceAgent/ThoughtLedger bypass, no accepted memory mutation, and no promotion of blocked files. This is design only; no branch apply command or PR creation authority exists yet.

IRONDEV-173 through IRONDEV-175 add the first controlled-write gates without performing the write. IRONDEV-173 resolves layered policy settings into an effective policy while proving hard invariants cannot be configured away. IRONDEV-174 creates a scoped human approval record valid only for one promotion package and `ControlledWorktreeDryRun`. IRONDEV-175 validates a future worktree apply as a dry-run: target path explicit, target outside active repo, branch not main/master, approval/package/policy match, promotable files identified, blocked files rejected, active repo mutation count zero, and no worktree created. These slices still do not grant real repo write, PR creation, memory mutation, ticket acceptance, or self-approval authority.

IRONDEV-183 adds the first advanced adversarial and self-improving memory agents. `DoubtAgent` is the formal Adversarial Review Agent and may be referred to internally as the AssholeAgent nickname, but product/docs use DoubtAgent. `MemoryImprovementAgent` analyses completed-run evidence, Doubt findings, Killjoy rebuttals, and promotion outcomes to propose memory improvements. It starts at Level 1 ProposalOnly and includes evidence bundles plus a MemoryKeyGate review. The gate currently returns `NeedsMoreEvidence` for Level 2 staging-area write; accepted-memory key readiness remains false during Alpha. No staging write, accepted memory mutation, ticket creation, patching, or self-approval authority is granted.
