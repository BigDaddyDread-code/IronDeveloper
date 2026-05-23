# CLI Command Inventory

## Purpose

This document summarises the current `IronDev.ReplayRunner` command surface.

The machine-readable inventory is stored at:

`tools/dogfood/cli-command-inventory.json`

## Command Groups

- Agent commands: 9
- Chat commands: 1
- Docs commands: 6
- Ticket commands: 1
- Failure commands: 1
- Memory commands: 6
- Builder commands: 1
- Replay scenario entrypoint: 1

## Product-Ish Commands

- `memory search`
- `memory triage`
- `agent tester run-plan`
- `agent retriever search`
- `agent sentinel observe`
- `failure latest`
- `builder proposal-safety-smoke`

These are closest to the control surface Codex will use.

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

