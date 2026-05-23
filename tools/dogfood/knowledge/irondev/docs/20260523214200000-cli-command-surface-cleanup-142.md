---
id: CLI_COMMAND_SURFACE_CLEANUP_142
project: IronDev
title: CLI Command Surface Cleanup 142
document_type: ArchitectureProof
authority: Accepted
status: Accepted
dogfood_run_id: CliCommandSurfaceCleanup142
created_utc: 2026-05-23T21:42:00Z
primary_retrieval_questions:
  - What did CLI Command Surface Cleanup 142 change?
  - What are the clean IronDev CLI aliases?
  - How should dogfood commands be separated from product-shaped commands?
  - Does inventory validate mutate project state?
boundary: Command-surface cleanup only. Existing commands remain compatible; no retrieval, builder, governance, memory, or real repo write semantics are changed.
---

# CLI Command Surface Cleanup 142

This slice cleans the IronDev ReplayRunner command surface without changing command semantics.

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

Added `inventory validate` as a read-only check over CLI inventory, CLI docs, and dogfood test-plan inventory.

The preferred run id option for clean aliases is `--run-id`. Existing dogfood commands still accept `--dogfood-run-id`.

## Boundary

This slice does not grant new agent authority, change retrieval semantics, change builder behaviour, apply patches, mutate memory, create workspaces, or permit real repository writes.
