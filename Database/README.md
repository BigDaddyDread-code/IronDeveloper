# Database migrations

This folder contains the current SQL Server migration scripts used by the backend.

PR 74a adds a small manifest and two scripts so the Block G governance migrations are not just repository files. They can be applied, in order and repeatedly, to a target SQL Server database.

## Manifest

`Database/migrations.json` is the ordered migration manifest. Later SQL migrations must be appended to this file instead of floating loose.

Current Block G order:

1. `Database/migrate_governance_event.sql`
2. `Database/migrate_tool_request.sql`
3. `Database/migrate_tool_gate_decision.sql`
4. `Database/migrate_approval_decision.sql`
5. `Database/migrate_policy_decision_event.sql`
6. `Database/migrate_dogfood_receipt.sql`
7. `Database/migrate_thoughtledger_governance_event_reference.sql`

The order matters because each durable ledger depends on the earlier governance evidence chain: tool requests depend on governance events, gate decisions depend on tool requests, approval decisions depend on governance events, and policy decision events may reference tool request, gate, and approval evidence, dogfood receipts may reference the full governance evidence chain without creating approval or execution authority, and ThoughtLedger governance references may cite existing governance events as evidence without creating approval, execution, policy, workflow, source apply, memory promotion, release, dogfood, or A2A authority.

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

The verifier checks the governance schema, governance event table/procedures/trigger, tool request, tool gate decision, approval decision, policy decision event, dogfood receipt, and ThoughtLedger governance reference tables/procedures/triggers/foreign keys plus key JSON/version constraints.

## Runtime DDL boundary

Runtime services may call stored procedures. They must not create the governance schema or governance/tool-request/tool-gate-decision/approval-decision/policy-decision-event/dogfood-receipt/thoughtledger-governance-reference tables on startup.

PR 74a does not remove older non-Block-G runtime DDL debt. Those legacy exceptions remain documented cleanup debt; this receipt only prevents the new governance/tool-request/tool-gate-decision path from relying on hidden runtime schema creation.

## Non-goals

This is not a migration framework rewrite. It adds ThoughtLedger governance references only as evidence. It does not add workflow, A2A, LangGraph, source apply, memory promotion, UI, API expansion, CLI expansion, approval, or execution permission.
