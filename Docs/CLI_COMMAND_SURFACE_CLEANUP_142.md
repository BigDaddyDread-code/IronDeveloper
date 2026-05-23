# CLI Command Surface Cleanup 142

## Purpose

This slice cleans the IronDev ReplayRunner command surface without changing command semantics.

The CLI is becoming the shared control language for Codex, agents, dogfood plans, CI-style validation, and the future WPF cockpit. It needs product-shaped commands, dogfood-only commands, and inventory validation before BuilderAgent and SupervisorAgent grow further.

## What Changed

Added clean aliases:

- `test run-plan`
- `trace build-smoke`
- `build disposable repair`
- `build disposable run`
- `dogfood build solitaire-disposable-build-smoke`
- `dogfood build disposable-apply-smoke`
- `dogfood foundation break-test`
- `dogfood memory sql-version-smoke`
- `dogfood memory weaviate-sql-version-smoke`
- `dogfood memory cross-project-smoke`
- `dogfood memory reindex-freshness-smoke`
- `dogfood memory ticket-source-link-smoke`
- `dogfood memory builder-context-source-smoke`

Added:

- `inventory validate`

## Compatibility

Existing commands remain valid.

The new aliases are routing and naming cleanup only. They do not change memory search, ranking, governance, disposable workspace, builder repair, or Test Agent semantics.

## Run Id Rule

Clean aliases prefer:

```text
--run-id
```

Existing dogfood commands still accept:

```text
--dogfood-run-id
```

Where practical, both options are accepted.

## Inventory Validation

`inventory validate --json` checks:

- CLI inventory parses.
- Test plan inventory parses.
- required clean aliases are listed.
- CLI documentation mentions the clean aliases.
- duplicate commands are absent.
- command inventory ordering remains category then command.

It is read-only. It does not execute commands and does not mutate project state.

## Boundary

This is command-surface cleanup only.

It does not:

- grant new agent authority
- change retrieval semantics
- change builder behaviour
- apply patches
- mutate memory
- create workspaces
- permit real repository writes

## What This Proves

IronDev now has a cleaner CLI shape:

```text
test run-plan
trace build-smoke
build disposable repair
dogfood build ...
dogfood memory ...
inventory validate
```

Smoke/proof commands are still available, but the product-shaped control surface is no longer forced to speak only in one-off dogfood names.
