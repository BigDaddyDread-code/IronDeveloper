# PR74B Retrospective SQL Inventory Receipt

Date: 2026-06-12

Branch: governance-retro-sql-inventory

Commit: recorded by PR head commit

## Scope

This receipt records a retrospective SQL inventory and runtime dependency map. It does not apply migrations, add schema, change stored procedure shapes, change runtime behavior, add API/CLI/UI behavior, create workflow/A2A/LangGraph state, persist gates or approvals, promote memory, or apply source changes.

## Search method

Searched for SQL scripts and SQL usage across:

- `Database/*.sql`
- `Database/*.ps1`
- `tools/**/*.sql`
- `tools/**/*.ps1`
- `IronDev.Core/**/*.cs`
- `IronDev.Infrastructure/**/*.cs`
- `IronDev.Api/**/*.cs`
- `IronDev.IntegrationTests/**/*.cs`
- `IronDev.IntegrationTests.Api/**/*.cs`
- `Docs/**/*.md`
- `Docs/**/*.sql`

Search tokens included `CREATE TABLE`, `CREATE PROCEDURE`, `CREATE OR ALTER PROCEDURE`, `ALTER TABLE`, `DROP TABLE`, `INSERT INTO`, `UPDATE`, `DELETE FROM`, `MERGE`, `EXEC`, `CommandType.StoredProcedure`, `QueryAsync`, `ExecuteAsync`, `SqlConnection`, and `Dapper`.

## Files searched

The inventory is captured in `Database/sql-inventory.json`. It includes all current `Database/*.sql` files plus key runtime stored procedure callers, test fixtures, local/dev utilities, and historical docs SQL artifacts.

## Scripts found

All current `Database/*.sql` files are represented in `Database/sql-inventory.json`.

Current ordered migration manifest entries are represented:

- `Database/migrate_governance_event.sql`
- `Database/migrate_tool_request.sql`

## Runtime-required SQL found

Current Block G runtime SQL found:

- `governance.GovernanceEvent`
- `governance.ToolRequest`
- governance event stored procedures
- tool request stored procedures
- `FK_ToolRequest_GovernanceEvent`
- JSON/version constraints verified by `Database/verify-migrations.ps1`

Older active runtime SQL also exists for agent memory, agent audit, collective memory, tool execution audit, project documents, project profiles, chat, tickets, code indexing, project context, decisions, semantic memory, and run stores. Those are inventoried separately and are not claimed as current manifest-verified unless the JSON says so.

## Test-only SQL found

Test-only SQL includes integration reset helpers, API reset/bootstrap helpers, agent memory schema helpers, direct SQL regression tests, and audit/API fixture seed helpers.

## Legacy/unused SQL found

Legacy or uncertain SQL includes older documentation migrations and `Database/update_schema_v1_indexing.sql`. These remain in place and are not deleted by this PR.

## Gaps and uncertainties

- Current `Database/migrations.json` is intentionally narrow and covers only current Block G governance/tool-request migrations.
- Older runtime schema still needs ownership decisions before it can be folded into a broader ordered migration manifest.
- Runtime DDL/bootstrap candidates remain documented debt.
- PR74c remains necessary for real DB API smoke proof.

## Boundary statement

This PR does not change schema or runtime behavior. Inventory is not migration. Inventory is not approval. Inventory is not cleanup. Inventory is not execution authority.
