# Backend Inline SQL / Runtime DDL Inventory

PR 51 is SQL hygiene and fixture cleanup, not schema evolution.

No behavior change intended.
No schema semantics change.
No stored procedure result-shape change.
No SQL/API/CLI/UI/runtime/persistence/capability changes.

This inventory records inline SQL and runtime DDL candidates discovered before the Backend Contract Freeze Report. Normal parameterized runtime `SELECT`, `INSERT`, `UPDATE`, and `DELETE` statements are not targets unless they create, alter, drop, seed, reset, or tear down schema.

## Summary

| Area | Finding | Action |
| --- | --- | --- |
| Agent memory proposal tests | `MemoryImprovementProposalTests` used a local `DropAgentMemorySchemaAsync` that did not drop `agent.AgentMemoryIndexEvent` / `agent.AgentMemoryIndexQueue` before `DROP SCHEMA agent`. | Moved that test to `AgentMemorySchemaTestSupport.DropAgentMemorySchemaInDependencyOrderAsync`. |
| Agent memory schema test support | Agent memory schema teardown needs FK-safe dependency order. | Added named test helper with explicit index-event cleanup before proposal/local memory teardown and schema drop. |
| Production runtime DDL candidates | Legacy infrastructure services still contain table/column bootstrap DDL. | Left intentionally and inventoried as legacy runtime DDL exceptions. Removing them would be behavior-affecting and belongs in a separate migration/bootstrap cleanup PR. |
| SQL scripts and migrations | `Database/*.sql`, `Docs/migrations/*.sql`, and localtest SQL scripts contain expected DDL. | Left intentionally as SQL source-of-truth/setup artifacts. |
| Test setup/reset SQL | Integration and API tests contain setup/reset DDL/DML. | Left intentionally unless part of the agent memory schema cleanup defect. |

## Runtime DDL candidates

These are production/runtime paths containing inline schema mutation SQL. They are not changed in PR 51 because removing them would change runtime bootstrap behavior. They are explicit exceptions for later cleanup.

| File | SQL kind | Feature area | Runtime/test | Contains DDL | Action taken | Reason |
| --- | --- | --- | --- | --- | --- | --- |
| `IronDev.Infrastructure/Services/Runs/SqlRunStore.cs` | setup DDL | Runs | Runtime | `CREATE TABLE dbo.Runs` | Left intentionally | Legacy runtime bootstrap. Behavior/result shape unchanged. |
| `IronDev.Infrastructure/Services/RunReports/SqlRunEventStore.cs` | setup DDL | Run reports | Runtime | `CREATE TABLE dbo.RunEvents` | Left intentionally | Legacy runtime bootstrap. Behavior/result shape unchanged. |
| `IronDev.Infrastructure/Services/TicketService.cs` | setup DDL | Tickets | Runtime | `ALTER TABLE dbo.ProjectTickets` | Left intentionally | Legacy compatibility column bootstrap. Behavior/result shape unchanged. |
| `IronDev.Infrastructure/Services/ProjectMemoryService.cs` | setup DDL | Project memory/context | Runtime | `CREATE TABLE dbo.ProjectContextDocuments` | Left intentionally | Legacy runtime bootstrap. Behavior/result shape unchanged. |
| `IronDev.Infrastructure/Services/ArtifactSourceReferenceService.cs` | setup DDL | Artifact source references | Runtime | `CREATE TABLE` / `ALTER TABLE dbo.ArtifactSourceReferences` | Left intentionally | Legacy runtime bootstrap. Behavior/result shape unchanged. |
| `IronDev.Infrastructure/Services/SemanticMemory/WeaviateSemanticMemoryService.cs` | setup DDL | Semantic memory cache/index | Runtime | `CREATE TABLE dbo.SemanticEmbeddings`, `CREATE TABLE dbo.SemanticIndexRuns` | Left intentionally | Legacy semantic-memory bootstrap. Behavior/result shape unchanged. |
| `IronDev.Infrastructure/Services/SemanticMemory/SemanticMemoryRepositories.cs` | setup/cleanup DDL | Semantic memory repositories | Runtime | `CREATE TABLE`, `DROP TABLE` for semantic trace tables | Left intentionally | Existing repository bootstrap/trace cleanup behavior. Behavior/result shape unchanged. |

Runtime DDL boundary note: agent memory runtime stores under `IronDev.Infrastructure/AgentMemory` remain DDL-free. Agent memory schema is owned by `Database/migrate_agent_*.sql` scripts and governed stored procedures.

## Test-only inline SQL candidates

These test paths contain inline setup/reset/teardown SQL. They are test-only and are not product runtime behavior.

| File | SQL kind | Feature area | Runtime/test | Contains DDL | Action taken | Reason |
| --- | --- | --- | --- | --- | --- | --- |
| `IronDev.IntegrationTests/IntegrationTestBase.cs` | setup/reset | General integration DB | Test-only | `CREATE TABLE`, `ALTER TABLE`, reset `DELETE` | Left intentionally | Named base fixture; PR 50 fixed FK-safe reset ordering for `ProjectObservableStates`. |
| `IronDev.IntegrationTests.Api/ApiTestBase.cs` | setup | API integration DB | Test-only | `CREATE TABLE`, `ALTER TABLE` | Left intentionally | API fixture bootstrap. |
| `IronDev.IntegrationTests/AgentMemory/AgentMemorySchemaTestSupport.cs` | teardown/setup helper | Agent memory | Test-only | `DROP PROCEDURE`, `DROP TRIGGER`, `DROP TABLE`, `DROP SCHEMA` | Moved/centralized | Named dependency-order helper for agent memory SQL-heavy tests. |
| `IronDev.IntegrationTests/AgentMemory/MemoryImprovementProposalTests.cs` | setup/teardown | Memory improvement proposals | Test-only | Previously local schema teardown | Moved | Now uses named support helper and keeps proposal tests focused. |
| `IronDev.IntegrationTests/AgentMemory/*Tests.cs` | setup/teardown | Agent memory | Test-only | duplicated schema teardown in older tests | Left intentionally | Existing duplication remains as known cleanup debt; PR 51 centralizes the failing proposal lane only. |
| `IronDev.IntegrationTests/Agents/ToolExecutionAuditStoreTests.cs` | setup/teardown | Tool execution audit | Test-only | `DROP PROCEDURE`, `DROP TABLE`, `DROP SCHEMA` | Left intentionally | Test-owned audit schema fixture. |
| `IronDev.IntegrationTests/Agents/AgentRunAuditStoreTests.cs` | setup/teardown | Agent run audit | Test-only | `DROP TABLE`, `DROP SCHEMA` | Left intentionally | Test-owned audit schema fixture. |
| `IronDev.IntegrationTests/Agents/GovernedManualAgentExecutionStoreTests.cs` | setup/teardown | Stored manual execution audit | Test-only | `DROP TABLE`, `DROP SCHEMA` | Left intentionally | Test-owned audit schema fixture. |
| `IronDev.IntegrationTests/ChatGroundingTests.cs` | setup/teardown | Chat grounding | Test-only | `DROP TABLE`, `CREATE TABLE` | Left intentionally | Targeted chat feedback fixture. |
| `IronDev.IntegrationTests/ProjectDocumentServiceTenantTests.cs` | setup | Project documents | Test-only | `CREATE TABLE` | Left intentionally | Targeted document fixture. |
| `IronDev.IntegrationTests/TicketServiceIntegrationTests.cs` | cleanup | Tickets/source refs | Test-only | `DROP TABLE`, `ALTER TABLE DROP COLUMN` | Left intentionally | Targeted legacy ticket fixture cleanup. |

## SQL scripts and setup artifacts

These files are SQL source-of-truth/setup artifacts and are expected to contain DDL.

| Path | SQL kind | Runtime/test | Action taken | Reason |
| --- | --- | --- | --- | --- |
| `Database/*.sql` | migration/setup/rebuild | Setup artifact | Left intentionally | SQL source-of-truth scripts. |
| `Docs/migrations/*.sql` | migration docs | Setup artifact | Left intentionally | Documented migration artifacts. |
| `tools/localtest/*.sql` and `tools/localtest/*.ps1` | local test setup/reset | Local test support | Left intentionally | Explicit local reset/setup tooling. |

## AgentMemoryIndexEvent cleanup fix

Known failure from PR 50:

`MemoryImprovementProposalTests` schema cleanup failed because its local agent-schema teardown tried to drop `agent` while `agent.AgentMemoryIndexEvent` still referenced the schema.

PR 51 fix:

1. Added `AgentMemorySchemaTestSupport.DropAgentMemorySchemaInDependencyOrderAsync`.
2. The helper drops memory stored procedures first.
3. The helper drops execution-audit objects.
4. The helper drops `agent.TR_AgentMemoryIndexEvent_*`, `agent.AgentMemoryIndexEvent`, and `agent.AgentMemoryIndexQueue` before proposal, handoff, influence, and local-memory objects.
5. `MemoryImprovementProposalTests` now uses the helper for setup and cleanup.

This does not weaken constraints, disable triggers, ignore cleanup errors, or remove any SQL object from the active schema.

## Boundary confirmations

- Audit remains evidence only, not approval.
- Proposal remains distinct from apply.
- Retrieval match remains lookup output, not memory candidate.
- Candidate remains proposed memory content, not accepted memory.
- Memory safe remains not approval.
- Gate remains not executor.
- Critic remains not governance.
- Model output remains advisory only.
- Human review remains required for source apply and memory promotion.

## Remaining intentional exceptions

The production runtime DDL candidates listed above are explicit cleanup debt. They remain because this PR is not allowed to change product bootstrap behavior. A later cleanup can remove them only by moving schema ownership into setup/migration artifacts while preserving behavior and result shapes.

# PR74b retrospective inline SQL inventory

PR74b does not remove inline SQL. It classifies the current inline SQL surface so later cleanup can separate runtime requirements from tests, local utilities, and legacy bootstrap behavior.

## 1. Runtime inline SQL

Runtime inline SQL remains in core application services for projects, tickets, project memory/context, project documents, semantic memory, and legacy run stores. Normal parameterized CRUD SQL is allowed when it is the service-owned persistence boundary. Runtime DDL remains an exception and must move to migration/bootstrap ownership in a later cleanup.

## 2. Test inline SQL

Integration and API tests use inline SQL for fixture setup, reset, direct-invariant checks, and unsafe-path regression checks. This is allowed when clearly test-only and scoped to the test database.

## 3. Migration/helper inline SQL

Migration scripts, `Database/apply-migrations.ps1`, `Database/verify-migrations.ps1`, local setup scripts, and LocalTest reset scripts are expected to contain SQL. They are setup/verification tools, not runtime feature code.

## 4. Inline SQL allowed/forbidden guidance

Allowed:

- Parameterized runtime CRUD owned by a repository/service.
- Stored procedure calls through explicit store classes.
- Test setup/reset SQL in test projects.
- Migration and local/dev utility SQL in `Database` or `tools/localtest`.

Forbidden without a later migration-ownership PR:

- New runtime DDL in application services.
- New direct writes bypassing governed stored procedure boundaries.
- New API/CLI/controller SQL writes that claim authority, approval, promotion, source apply, workflow state, or gate execution.

## 5. Candidates to move behind stored procedures

Candidates remain the previously documented runtime DDL/bootstrap areas plus any future durable governance stores added after Block G. Current Block G governance/tool-request/tool-gate-decision writes already use stored procedures and are not candidates for inline SQL expansion.
