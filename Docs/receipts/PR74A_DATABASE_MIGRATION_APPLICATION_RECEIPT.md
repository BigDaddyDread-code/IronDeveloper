# PR74A Database Migration Application Receipt

Date: 2026-06-12

## Purpose

PR 74a proves the current Block G SQL migration scripts can be applied in order and repeatedly to the real SQL Server databases, and that required governance/tool-request schema objects exist afterward.

This is a migration application receipt, not a new feature.

## Databases checked

- `IronDeveloper_Test`
- `IronDeveloper`

Server name format: local SQL Server instance or named instance supplied by `-Server`, for example `DESKTOP-KFA0H13` or `.\SQLEXPRESS`. No credentials or secrets are recorded in this receipt.

## Migration scripts applied

The ordered manifest is `Database/migrations.json`.

1. `Database/migrate_governance_event.sql`
2. `Database/migrate_tool_request.sql`

The order matters because `governance.ToolRequest` has a foreign key to `governance.GovernanceEvent`.

## Verification scope

`Database/verify-migrations.ps1` verifies:

- `governance` schema
- `governance.GovernanceEvent` table
- governance event read/append stored procedures
- `governance.TR_GovernanceEvent_BlockUpdateDelete`
- `governance.ToolRequest` table
- tool request stored procedures
- `ToolRequest` foreign key to `GovernanceEvent`
- governance event payload JSON and payload-version check constraints
- tool request payload JSON and payload-version check constraints

## Idempotency proof commands

For the test database:

```powershell
.\Database\apply-migrations.ps1 -Server "DESKTOP-KFA0H13" -Database "IronDeveloper_Test" -TrustServerCertificate
.\Database\apply-migrations.ps1 -Server "DESKTOP-KFA0H13" -Database "IronDeveloper_Test" -TrustServerCertificate
.\Database\verify-migrations.ps1 -Server "DESKTOP-KFA0H13" -Database "IronDeveloper_Test" -TrustServerCertificate
```

For the local development database:

```powershell
.\Database\apply-migrations.ps1 -Server "DESKTOP-KFA0H13" -Database "IronDeveloper" -TrustServerCertificate
.\Database\apply-migrations.ps1 -Server "DESKTOP-KFA0H13" -Database "IronDeveloper" -TrustServerCertificate
.\Database\verify-migrations.ps1 -Server "DESKTOP-KFA0H13" -Database "IronDeveloper" -TrustServerCertificate
```

## Local receipt status

- `IronDeveloper_Test`: verified by automated integration test using the configured test connection string.
- `IronDeveloper`: verified manually with the commands above when the local development database is available.

## Known limitations

- This PR does not prove API smoke behaviour against the migrated database. That is PR 74b.
- This PR does not add a schema migration history table.
- This PR does not remove older non-Block-G runtime DDL/bootstrap debt.
- This PR does not create durable gate decisions or dogfood receipts.

## Authority boundary

This PR adds no new authority behaviour.

It does not add approval decisions, policy decisions, gate decisions, workflow execution, A2A, LangGraph, memory promotion, source apply, UI, API feature expansion, CLI feature expansion, or runtime auto-migration.

SQL remains the source of truth. Runtime code may call stored procedures; it must not secretly create the governance/tool-request schema.

## Next slice

PR 74b should prove real database stored-procedure/API smoke behaviour against the migrated database.

## Current local application receipt

Recorded on 2026-06-12.

- `IronDeveloper_Test`: covered by `DatabaseMigrationApplicationReceiptTests`; migrations apply repeatedly and verification passes.
- `IronDeveloper`: `Database/apply-migrations.ps1` was run twice with `-Server "DESKTOP-KFA0H13" -Database "IronDeveloper" -TrustServerCertificate`, then `Database/verify-migrations.ps1` passed.
- Verified objects include governance schema, governance event table/procedures/append-only trigger, tool request table/procedures, `FK_ToolRequest_GovernanceEvent`, and JSON/version constraints.
- This receipt does not prove API smoke behavior against the migrated database. That remains PR 74b.
