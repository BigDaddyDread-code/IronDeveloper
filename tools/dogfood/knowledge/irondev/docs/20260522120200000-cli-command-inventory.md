---
id: 20260522120200000-cli-command-inventory
project: IronDev
title: CLI_COMMAND_INVENTORY
document_type: Inventory
authority: Accepted
source: C:\Users\bob\source\repos\AIDeveloper\Docs\CLI_COMMAND_INVENTORY.md
dogfood_run_id: AlphaTestPhase-094-103
created_utc: 2026-05-22T12:00:00.0000000+00:00
---

# CLI Command Inventory

## Purpose

This document summarises the current `IronDev.ReplayRunner` command surface.

The machine-readable inventory is stored at:

`tools/dogfood/cli-command-inventory.json`

## Command Groups

- Agent commands: 12
- Chat commands: 1
- Docs commands: 6
- Ticket commands: 1
- Failure commands: 1
- Govern commands: 1
- Memory commands: 6
- Builder commands: 1
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
- `govern review`
- `foundation break-test`
- `failure latest`
- `builder proposal-safety-smoke`

These are closest to the control surface Codex will use.

`foundation break-test` is a dogfood control command for the 121-130 hardening phase. It is evidence/report oriented and must not mutate the real repository.

`agent conscience review` and `agent thought-ledger explain` are governed-autonomy control-plane commands. They review and explain proposed actions only; they do not execute, mutate memory, create tickets, or patch files.

`govern review` combines ConscienceAgent and ThoughtLedger into one review package. It still does not execute the proposed action.

`agent retriever search` now returns a weighted context bundle. It preserves the real memory search result while adding included sources, rejected or filtered-context notes, source risk notes, semantic trace id, and an agent-facing summary.

`agent supervisor run-goal` now performs governed autonomy: Tier 3 read/test/report loops and Tier 4 disposable-workspace apply loops. It still requires ConscienceAgent review, ThoughtLedger explanation, and TesterAgent execution, and real repository writes remain blocked.

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


