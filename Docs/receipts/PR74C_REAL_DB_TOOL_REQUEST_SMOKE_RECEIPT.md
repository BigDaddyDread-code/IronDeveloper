# PR74C Real DB Tool Request Smoke Receipt

## Purpose

PR74C proves that the installed governance schema can create, read, and list durable SQL-backed tool requests with linked governance events against real SQL Server databases.

This is smoke proof only. It does not add backend capability.

## Scope

In scope:

- Apply existing database migrations.
- Verify existing database migrations.
- Smoke the approved tool request stored procedure path.
- Prove `governance.GovernanceEvent` exists.
- Prove `governance.ToolRequest` exists.
- Prove `governance.usp_ToolRequest_Create` works.
- Prove `governance.usp_ToolRequest_GetById` works.
- Prove `governance.usp_ToolRequest_ListForProject` works.
- Prove `governance.usp_ToolRequest_ListForCorrelation` works.
- Prove creating a tool request creates a linked `tool.request.created` governance event.
- Prove the API-backed tool request path remains request-only and durable through the existing API tests.

Out of scope:

- Gate decision persistence.
- Approval persistence.
- Policy persistence.
- Dogfood receipt persistence.
- Workflow state.
- A2A handoff state.
- Source apply.
- Memory promotion.
- New API features.
- New CLI features.
- UI.

## Smoke command

Run database-backed smoke commands sequentially. Do not run the DB-backed bands in parallel.

```powershell
.\Database\apply-migrations.ps1 -Server "DESKTOP-KFA0H13" -Database "IronDeveloper" -TrustServerCertificate
.\Database\verify-migrations.ps1 -Server "DESKTOP-KFA0H13" -Database "IronDeveloper" -TrustServerCertificate
.\Database\smoke-tool-request.ps1 -Server "DESKTOP-KFA0H13" -Database "IronDeveloper" -TrustServerCertificate

.\Database\apply-migrations.ps1 -Server "DESKTOP-KFA0H13" -Database "IronDeveloper_Test" -TrustServerCertificate
.\Database\verify-migrations.ps1 -Server "DESKTOP-KFA0H13" -Database "IronDeveloper_Test" -TrustServerCertificate
.\Database\smoke-tool-request.ps1 -Server "DESKTOP-KFA0H13" -Database "IronDeveloper_Test" -TrustServerCertificate
```

The smoke script creates append-only smoke rows with:

- purpose: `PR74C real DB smoke test`
- tool name: `smoke.tool_request`
- operation: `create_read_list`
- actor type: `system_test_fixture`
- actor id: `pr74c-real-db-smoke`

Smoke rows are intentionally easy to identify later. They are not deleted by the script because the governance event and tool request tables are append-only evidence stores.

## Stored procedure proof

The smoke script proves the following objects and paths:

| Proof | Expected result |
| --- | --- |
| `governance.GovernanceEvent` | table exists |
| `governance.ToolRequest` | table exists |
| `governance.usp_ToolRequest_Create` | procedure exists and creates a `Recorded` request |
| `governance.usp_ToolRequest_GetById` | procedure returns the created request |
| `governance.usp_ToolRequest_ListForProject` | procedure returns the created request |
| `governance.usp_ToolRequest_ListForCorrelation` | procedure returns the created request |
| linked governance event | exactly one `tool.request.created` event exists for the request subject |

## Boundary proof

The smoke script asserts the following side-effect tables do not exist in this slice:

- `governance.ToolGateDecision`
- `governance.ApprovalDecision`
- `governance.PolicyDecision`
- `governance.DogfoodReceipt`
- `governance.WorkflowState`
- `governance.WorkflowStep`
- `governance.A2aHandoff`
- `governance.AgentHandoff`
- `governance.SourceApply`
- `governance.MemoryPromotion`

This is object-level smoke proof for the current Block G boundary. Tool request creation is not a gate decision, approval decision, workflow transition, A2A handoff, source apply, or memory promotion.

## API path proof

The API path is covered by the existing `ToolRequestApi` contract tests against the configured backend connection string.

Those tests prove:

- the API creates durable SQL-backed tool request records,
- API responses keep `durable: true`,
- API create/get remains request-only,
- API access is not execution permission,
- API status is not governance,
- audit evidence is not approval,
- gate is not executor,
- source apply is not performed,
- memory promotion is not performed.

There is intentionally no API list endpoint in this slice. Durable list behavior is proven through `governance.usp_ToolRequest_ListForProject` and `governance.usp_ToolRequest_ListForCorrelation`.

## Validation receipt

| Check | Result |
| --- | --- |
| `apply-migrations.ps1` against `IronDeveloper` | passed |
| `verify-migrations.ps1` against `IronDeveloper` | passed |
| `smoke-tool-request.ps1` against `IronDeveloper` | passed; tool request `162d12cd-0956-464f-9fd7-91c7f2eb2a98`, governance event `f6b32b11-d75c-4e39-ab71-0c75bcb75684` |
| `apply-migrations.ps1` against `IronDeveloper_Test` | passed |
| `verify-migrations.ps1` against `IronDeveloper_Test` | passed |
| `smoke-tool-request.ps1` against `IronDeveloper_Test` | passed; tool request `cc60378f-f7cc-4e53-b277-630f7b379f55`, governance event `972154c6-7d18-4a2b-8185-245cda81fc7c` |
| `dotnet build IronDev.slnx --no-restore -v:minimal` | passed; 0 errors, 2 warnings |
| `TestCategory=RealDatabaseToolRequestSmoke` | passed; 5/5 |
| `TestCategory=DatabaseMigrationReceipt|TestCategory=SqlInventory` | passed; 15/15 |
| `GovernanceEventStore|ToolRequestStore` | passed; 22/22 |
| `ToolRequestApi|ToolGateApi|DogfoodLoopApi` | passed; 44/44 |
| `ApiCliContract|ApiCliReleaseGate` | passed; 39/39 |
| `ThoughtLedger` | passed; 11/11 |
| `git diff --check` | pending |

Both database smoke runs verified:

- table/procedure existence,
- create/read/list through approved stored procedures,
- exactly linked `tool.request.created` governance event,
- no gate decision table,
- no approval decision table,
- no policy decision table,
- no dogfood receipt table,
- no workflow state/step table,
- no A2A/agent handoff table,
- no source apply table,
- no memory promotion table.

## Merge statement

PR74C can merge only when both `IronDeveloper` and `IronDeveloper_Test` are migrated, verified, and can create/read/list durable SQL-backed tool requests with linked governance events through the real database path, without creating gate decisions, approvals, dogfood receipts, workflow state, A2A handoffs, source mutations, or memory promotions.
