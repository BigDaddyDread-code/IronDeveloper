# CLI Command Inventory

## Purpose

This document summarises the current `IronDev.ReplayRunner` command surface.

The machine-readable inventory is stored at:

`tools/dogfood/cli-command-inventory.json`

## Command Groups

- Agent commands: 14
- Chat commands: 1
- Docs commands: 6
- Ticket commands: 1
- Failure commands: 1
- Govern commands: 1
- Memory commands: 8
- Builder commands: 3
- Foundation commands: 1
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
- `govern review`
- `foundation break-test`
- `failure latest`
- `builder disposable-workspace-apply-smoke`
- `builder proposal-safety-smoke`
- `builder solitaire-disposable-build-smoke`

These are closest to the control surface Codex will use.

`foundation break-test` is a dogfood control command for the 121-130 hardening phase. It is evidence/report oriented and must not mutate the real repository.

`agent conscience review` and `agent thought-ledger explain` are governed-autonomy control-plane commands. They review and explain proposed actions only; they do not execute, mutate memory, create tickets, or patch files.

`govern review` combines ConscienceAgent and ThoughtLedger into one review package. It still does not execute the proposed action.

`agent retriever search` now returns a weighted context bundle. It preserves the real memory search result while adding included sources, rejected or filtered-context notes, source risk notes, semantic trace id, and an agent-facing summary.

`agent supervisor run-goal` now performs governed autonomy: Tier 3 read/test/report loops and Tier 4 disposable-workspace apply loops. It still requires ConscienceAgent review, ThoughtLedger explanation, and TesterAgent execution, and real repository writes remain blocked.

`agent builder trace-smoke` creates a synthetic BuildAgent trace/report for a future heavy-duty disposable build. It records stage status, build/test attempts, repair attempts, evidence artefacts, mutation counts, and recommendation. It does not create a disposable workspace, generate app files, apply patches, mutate memory, or approve writes.

`agent builder repair-loop` runs the first real trace-backed disposable repair loop. It generates Solitaire inside an explicit disposable workspace, injects one build failure and one rule-test failure, repairs both inside the cage, reruns build/tests, and emits trace/report evidence with real repo mutation count zero.

## Dogfood/Smoke Commands

- `memory sql-version-smoke`
- `memory weaviate-sql-version-smoke`
- `memory cross-project-smoke`
- `memory reindex-freshness-smoke`
- `memory ticket-source-link-smoke`
- `memory builder-context-source-smoke`
- `docs discussion-smoke`
- `tickets document-to-tickets-smoke`

These prove slices of the spine but are not product commands.

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

## 140 BuildAgent Trace Smoke Command

- `agent builder trace-smoke --project Solitaire --dogfood-run-id <run> --json` creates a synthetic trace for the future heavy-duty disposable build path.
- It writes `build-run-trace.json`, `build-run-report.json`, `build-run-report.md`, and evidence placeholders under `tools/dogfood/runs/{runId}`.
- It is trace/report only. It does not create Solitaire app files or a real disposable workspace.

## 141 Trace-Backed Builder Repair Loop Command

- `agent builder repair-loop --project Solitaire --dogfood-run-id <run> --json` runs a real disposable repair loop.
- It intentionally breaks the generated WPF project reference, records the build failure, repairs the project file, intentionally breaks the empty-tableau King rule, records the test failure, repairs `KlondikeRules`, and verifies final build/test pass.
- It may write only inside the explicit temp disposable workspace. It must not mutate the real repo, memory, guardrails, or regression packs.

