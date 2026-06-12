# Database migrations

This folder contains the current SQL Server migration scripts used by the backend.

PR 74a adds a small manifest and two scripts so the Block G governance migrations are not just repository files. They can be applied, in order and repeatedly, to a target SQL Server database.

## Manifest

`Database/migrations.json` is the ordered migration manifest. Later SQL migrations must be appended to this file instead of floating loose.

Current Block G order:

1. `Database/migrate_governance_event.sql`
2. `Database/migrate_tool_request.sql`

The order matters because `governance.ToolRequest` depends on `governance.GovernanceEvent`.

## Apply migrations

```powershell
.\Database\apply-migrations.ps1 -Server ".\SQLEXPRESS" -Database "IronDeveloper" -TrustServerCertificate
.\Database\apply-migrations.ps1 -Server ".\SQLEXPRESS" -Database "IronDeveloper_Test" -TrustServerCertificate
```

The script also supports `-ConnectionString` for test automation.

The script:

- reads `Database/migrations.json`
- validates every manifest path exists
- applies migrations in manifest order
- splits `GO` batches safely
- exits non-zero on missing files or SQL failure

## Verify migrations

```powershell
.\Database\verify-migrations.ps1 -Server ".\SQLEXPRESS" -Database "IronDeveloper" -TrustServerCertificate
.\Database\verify-migrations.ps1 -Server ".\SQLEXPRESS" -Database "IronDeveloper_Test" -TrustServerCertificate
```

The verifier checks the governance schema, governance event table/procedures/trigger, tool request table/procedures, the ToolRequest-to-GovernanceEvent foreign key, and the key JSON/version constraints.

## Runtime DDL boundary

Runtime services may call stored procedures. They must not create the governance schema or governance/tool-request tables on startup.

PR 74a does not remove older non-Block-G runtime DDL debt. Those legacy exceptions remain documented cleanup debt; this receipt only prevents the new governance/tool-request path from relying on hidden runtime schema creation.

## Non-goals

This is not a migration framework rewrite. It does not add gate decisions, approval decisions, policy decisions, dogfood receipt durability, workflow, A2A, LangGraph, source apply, memory promotion, UI, API expansion, or CLI expansion.
