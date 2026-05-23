# CLI Command Inventory

## Purpose

This document summarises the current `IronDev.ReplayRunner` command surface.

The machine-readable inventory is stored at:

`tools/dogfood/cli-command-inventory.json`

## Command Groups

- Agent commands: 16
- Build commands: 2
- Chat commands: 1
- Docs commands: 6
- Dogfood commands: 10
- Ticket commands: 1
- Failure commands: 1
- Govern commands: 1
- Inventory commands: 1
- Memory commands: 8
- Builder commands: 3
- Foundation commands: 1
- Run report commands: 1
- Test commands: 1
- Trace commands: 1
- Replay scenario entrypoint: 1

## Product-Ish Commands

- `memory search`
- `memory triage`
- `agent tester run-plan`
- `agent retriever search`
- `agent sentinel observe`
- `agent research package`
- `agent conscience review`
- `agent thought-ledger explain`
- `agent builder trace-smoke`
- `agent builder repair-loop`
- `agent loop plan-review`
- `build disposable repair`
- `build disposable run`
- `govern review`
- `foundation break-test`
- `failure latest`
- `inventory validate`
- `test run-plan`
- `trace build-smoke`
- `builder disposable-workspace-apply-smoke`
- `builder proposal-safety-smoke`
- `builder solitaire-disposable-build-smoke`
- `run-report viewer-smoke`
- `campaign self-improvement-157`
- `campaign live-governed-agent-158`
- `campaign live-critic-planner-159`
- `campaign live-retriever-sentinel-160`
- `campaign governed-tool-loop-162-167`
- `agent architect review`

These are closest to the control surface Codex will use.

`foundation break-test` is a dogfood control command for the 121-130 hardening phase. It is evidence/report oriented and must not mutate the real repository.

`agent conscience review` and `agent thought-ledger explain` are governed-autonomy control-plane commands. They review and explain proposed actions only; they do not execute, mutate memory, create tickets, or patch files.

`govern review` combines ConscienceAgent and ThoughtLedger into one review package. It still does not execute the proposed action.

`agent retriever search` now returns a weighted context bundle. It preserves the real memory search result while adding included sources, rejected or filtered-context notes, source risk notes, semantic trace id, and an agent-facing summary.

`agent supervisor run-goal` now performs governed autonomy: Tier 3 read/test/report loops and Tier 4 disposable-workspace apply loops. It still requires ConscienceAgent review, ThoughtLedger explanation, and TesterAgent execution, and real repository writes remain blocked.

`agent builder trace-smoke` creates a synthetic BuildAgent trace/report for a future heavy-duty disposable build. It records stage status, build/test attempts, repair attempts, evidence artefacts, mutation counts, and recommendation. It does not create a disposable workspace, generate app files, apply patches, mutate memory, or approve writes.

`agent builder repair-loop` runs the first real trace-backed disposable repair loop. It generates Solitaire inside an explicit disposable workspace, injects one build failure and one rule-test failure, repairs both inside the cage, reruns build/tests, and emits trace/report evidence with real repo mutation count zero.

`build disposable repair` and `build disposable run` are clean product-shaped aliases for the same trace-backed disposable repair loop. They accept `--run-id`; `--dogfood-run-id` remains a compatibility alias.

`test run-plan` is the product-shaped alias for `agent tester run-plan`. TesterAgent remains an execution/reporting wrapper only.

As of 143, `test run-plan`, `dogfood run-plan`, and `agent tester run-plan` execute through the C# `TestPlanRunner`. `Invoke-TestAgentPlan.ps1` remains as a compatibility wrapper that delegates to `test run-plan`.

`trace build-smoke` is the product-shaped alias for `agent builder trace-smoke`.

`inventory validate` checks the CLI inventory, CLI documentation, and dogfood test-plan inventory. It is read-only and does not execute the listed commands.

`run-report viewer-smoke` proves the Run Reports viewer service can read file-backed run evidence through shared C# services. It does not execute BuilderAgent, apply patches, mutate memory, or prove WPF-to-CLI execution.

`campaign live-critic-planner-159` proves CriticAgent and PlannerAgent can attempt opt-in live model calls through configured profiles while deterministic fallback and no-write governance remain in force.

`campaign live-retriever-sentinel-160` proves RetrieverAgent and SentinelAgent can attempt opt-in live model calls through configured profiles while deterministic memory ranking, project scoping, insight classification, and no-write governance remain in force.

`agent loop plan-review` runs the 162-167 governed Planner/Critic tool loop. PlannerAgent requests named capabilities, the tool registry executes safe read/test/report tools, CriticAgent reviews evidence, PlannerAgent revises the plan, and a human escalation gate records review requirements. It does not grant writes, memory mutation, ticket creation, patch application, or raw command execution by agents.

`campaign governed-tool-loop-162-167` is the dogfood regression command for the same loop. It also proves the tool registry, trace report, evidence validation layer, human escalation gate, and `.NET`/Node/Python runtime profile contracts.

## Dogfood/Smoke Commands

- `memory sql-version-smoke`
- `memory weaviate-sql-version-smoke`
- `memory cross-project-smoke`
- `memory reindex-freshness-smoke`
- `memory ticket-source-link-smoke`
- `memory builder-context-source-smoke`
- `docs discussion-smoke`
- `tickets document-to-tickets-smoke`
- `dogfood memory sql-version-smoke`
- `dogfood memory weaviate-sql-version-smoke`
- `dogfood memory cross-project-smoke`
- `dogfood memory reindex-freshness-smoke`
- `dogfood memory ticket-source-link-smoke`
- `dogfood memory builder-context-source-smoke`
- `dogfood build disposable-apply-smoke`
- `dogfood build solitaire-disposable-build-smoke`
- `dogfood foundation break-test`
- `dogfood run-plan`

These prove slices of the spine but are not product commands.

The `dogfood ...` aliases make smoke/proof commands explicit while preserving the older command names for existing regression plans.

## Remaining Program.cs Surface

Some commands still live in `Program.cs`, including ticket source-link and builder context source smoke handlers. They are not urgent blockers, but Code Standards should keep them visible as debt.

## Boundary

This is an inventory and help audit. It does not change command semantics.


## Inventory Order

The JSON inventory is a flat command array sorted by `category`, then `command`. Keep new commands in that order so Codex and dogfood tooling can diff the surface cleanly.

## 137 Product Spike Intake Command

- `agent planner intake-product-spike` classifies vague new product prompts such as `i want build solitare` into a structured product-spike intake package.
- It returns detected project, assumptions, clarifying questions, recommended next steps, blocked unsafe actions, and a boundary statement.
- It does not create project memory, tickets, disposable workspaces, patches, or real repository writes.
- As of 159 it accepts `--live-llm --model-profile <profile>` for advisory model evidence only. Deterministic intake remains authoritative.

## 159 Live Critic And Planner Commands

- `agent critic review-failure --package <failure-package.json> --live-llm --model-profile <profile> --json` records advisory live model evidence while keeping deterministic failure-package review in force.
- `agent planner draft-test-plan --goal <goal> --project <project> --live-llm --model-profile <profile> --json` records advisory live model evidence while drafting plan JSON only.
- `campaign live-critic-planner-159 --run-id <run> --json` validates CriticAgent and PlannerAgent fallback/live-attempt behaviour.
- None of these commands can patch files, create tickets, mutate memory, apply patches, approve writes, or bypass ConscienceAgent/ThoughtLedger.

## 160 Live Retriever And Sentinel Commands

- `agent retriever search --project <project> --query <query> --live-llm --model-profile <profile> --json` records advisory live model evidence while keeping real memory search, project filtering, and authority ranking in force.
- `agent sentinel observe --observed-project <project> --affected-project <project> --evidence <text> --live-llm --model-profile <profile> --json` records advisory live model evidence while keeping deterministic insight classification in force.
- `campaign live-retriever-sentinel-160 --run-id <run> --json` validates RetrieverAgent and SentinelAgent fallback/live-attempt behaviour.
- None of these commands can override ranking, cross project boundaries silently, create tickets, mutate memory, patch files, approve writes, or bypass ConscienceAgent/ThoughtLedger.

## 161 Live Remaining Governed Agent Commands

- `agent research package --project <project> --topic <topic> --live-llm --model-profile <profile> --json` records advisory live model evidence while keeping accepted project memory authoritative.
- `agent quality run-gate --plan <plan> --live-llm --model-profile <profile> --json` records advisory live model evidence while keeping deterministic quality gates authoritative.
- `agent supervisor run-goal --project <project> --query <query> --plan <plan> --live-llm --model-profile <profile> --json` records advisory live model evidence while keeping Conscience, ThoughtLedger, and deterministic stop conditions authoritative.
- `campaign live-remaining-agents-161 --run-id <run> --json` validates ResearchAgent, QualityAgent, and SupervisorAgent fallback/live-attempt behaviour.
- None of these commands can override accepted memory, override deterministic gates, bypass governance, create tickets, mutate memory, patch files, approve writes, or self-approve.

## 140 BuildAgent Trace Smoke Command

- `agent builder trace-smoke --project Solitaire --dogfood-run-id <run> --json` creates a synthetic trace for the future heavy-duty disposable build path.
- It writes `build-run-trace.json`, `build-run-report.json`, `build-run-report.md`, and evidence placeholders under `tools/dogfood/runs/{runId}`.
- It is trace/report only. It does not create Solitaire app files or a real disposable workspace.

## 141 Trace-Backed Builder Repair Loop Command

- `agent builder repair-loop --project Solitaire --dogfood-run-id <run> --json` runs a real disposable repair loop.
- It intentionally breaks the generated WPF project reference, records the build failure, repairs the project file, intentionally breaks the empty-tableau King rule, records the test failure, repairs `KlondikeRules`, and verifies final build/test pass.
- It may write only inside the explicit temp disposable workspace. It must not mutate the real repo, memory, guardrails, or regression packs.

## 142 CLI Command Surface Cleanup

- `inventory validate --run-id <run> --json` validates command/docs/test-plan inventory consistency.
- `test run-plan --plan <path> --run-id <run> --json` aliases the TesterAgent execution surface.
- `trace build-smoke --project Solitaire --run-id <run> --json` aliases the BuildAgent trace smoke.
- `build disposable repair --project Solitaire --run-id <run> --json` aliases the trace-backed disposable repair loop.
- `build disposable run --project Solitaire --run-id <run> --json` is an alpha product-shaped entrypoint for the same caged repair loop.
- `dogfood build solitaire-disposable-build-smoke --run-id <run> --json` and `dogfood build disposable-apply-smoke --run-id <run> --json` keep dogfood build proofs visibly separate from product commands.
- `dogfood foundation break-test --scenario <scenario> --run-id <run> --json` keeps foundation break-test scenarios under dogfood naming.
- `dogfood memory ...` aliases keep memory-spine proof commands visibly dogfood-only.

Existing command names remain compatible. This cleanup changes the command surface shape; it does not change retrieval, builder, or governance semantics.

## 143 C# Dogfood Runner

- `test run-plan --plan <path> --run-id <run> --json` is now the primary C# Test Agent plan runner.
- `dogfood run-plan --plan <path> --run-id <run> --json` is the dogfood-shaped alias for the same C# runner.
- `agent tester run-plan --plan <path> --run-id <run> --json` remains compatible and uses the same C# runner.
- `Invoke-TestAgentPlan.ps1` is now a thin compatibility wrapper around `test run-plan`.
- `Invoke-TestAgentPlan.Legacy.ps1` preserves the previous PowerShell implementation for explicit fallback while older actions are ported.

The C# runner writes standard reports under `tools/dogfood/runs/{runId}`:

- `report.json`
- `test-agent-report.json`
- `trace.json`
- `report.md`
- `evidence/`
- `logs/`

If an unported action falls back to the legacy script, the report marks `compatibility_mode: true`.

## 144 Run Report Viewer Service

- `run-report viewer-smoke --run-id <run> --json` creates small file-backed run report fixtures and proves the shared `IRunReportService` / `IRunEvidenceService` path can read them.
- WPF uses the same shared services through `RunReportsViewModel`; it does not shell out to ReplayRunner or parse CLI stdout.
- The first UI surface is the `RunReports` workspace inside the existing `ProjectWorkspaceFrame`.

This is read/report infrastructure only. It does not change CLI command semantics, builder behavior, agent authority, retrieval, memory, or real repository write boundaries.

