# Backend SQL inventory

PR 50 is SQL contract cleanup, not schema evolution.

No behavior change intended.

No stored procedure result-shape change.

No SQL/API/CLI/UI/runtime/persistence/capability changes.

This inventory records backend SQL artifacts before the Backend Contract Freeze Report. SQL remains the source of truth. Vector/index/retrieval remains lookup only and never truth, authority, approval, or promotion.

## Schemas

| Artifact | File path | Owning area | Referenced by | Source-of-truth role | Status | PR 50 changed | Behaviour/result shape |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `dbo` | `Database/local_dev_setup.sql`, `Database/rebuild_db.sql`, migration scripts | Core product data | Infrastructure services, integration tests | Primary application schema | Active | No | Unchanged |
| `agent` | `Database/migrate_agent_*.sql`, `Database/migrate_collective_memory.sql` | Agent memory, audit, collective memory | SQL-backed memory/audit stores and tests | Governed append-only memory/audit schema | Active | No | Unchanged |
| `toolaudit` | `Database/migrate_tool_execution_audit.sql` | Tool execution audit | `SqlToolExecutionAuditStore`, `ToolExecutionAuditStoreTests` | Append-only tool execution audit schema | Active | No | Unchanged |

## Active `dbo` tables

| Artifact | File path | Owning area | Referenced by | Source-of-truth role | Status | PR 50 changed | Behaviour/result shape |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `dbo.Tenants` | `local_dev_setup.sql`, `rebuild_db.sql` | Tenant/auth | User, project, test seed services | Tenant identity root | Active | No | Unchanged |
| `dbo.Users` | `local_dev_setup.sql`, `rebuild_db.sql` | Auth | User service, tests | User identity | Active | No | Unchanged |
| `dbo.TenantUsers` | `local_dev_setup.sql`, `rebuild_db.sql` | Auth | User service, tests | User-to-tenant membership | Active | No | Unchanged |
| `dbo.Projects` | `local_dev_setup.sql`, `rebuild_db.sql` | Project | Project service, builder/readiness tests | Project root row | Active | No | Unchanged |
| `dbo.ProjectChatSessions` | `local_dev_setup.sql`, `migrate_persistent_chat.sql`, `rebuild_db.sql` | Chat | Chat history/persistence services | Chat session persistence | Active | No | Unchanged |
| `dbo.ChatMessages` | `local_dev_setup.sql`, `migrate_persistent_chat.sql`, `rebuild_db.sql` | Chat | Chat history/persistence services | Chat message persistence | Active | No | Unchanged |
| `dbo.ChatMessageFeedback` | `local_dev_setup.sql`, `rebuild_db.sql` | Chat feedback | Chat feedback service/tests | Feedback rows | Active | No | Unchanged |
| `dbo.ChatTurnGovernance` | `local_dev_setup.sql`, `migrate_chat_turn_audit.sql`, `rebuild_db.sql` | Chat governance | Chat turn persistence tests/services | Governance classification evidence | Active | No | Unchanged |
| `dbo.ChatTurnClarifications` | `local_dev_setup.sql`, `migrate_chat_turn_audit.sql`, `rebuild_db.sql` | Chat governance | Chat turn persistence tests/services | Clarification evidence | Active | No | Unchanged |
| `dbo.ChatTurnTraces` | `local_dev_setup.sql`, `migrate_chat_turn_audit.sql`, `rebuild_db.sql` | Chat governance | Chat turn persistence tests/services | Trace evidence | Active | No | Unchanged |
| `dbo.ProjectTickets` | `local_dev_setup.sql`, `migrate_structured_tickets.sql`, `rebuild_db.sql` | Tickets | Ticket service, builder services/tests | Ticket source-of-truth | Active | No | Unchanged |
| `dbo.ArtifactSourceReferences` | `local_dev_setup.sql`, `migrate_project_context_documents.sql`, `rebuild_db.sql` | Evidence/provenance | Artifact source reference service/tests | Provenance evidence, not authority | Active | No | Unchanged |
| `dbo.ProjectRules` | `local_dev_setup.sql`, `rebuild_db.sql` | Project governance | Project services/tests | Project rule records | Active | No | Unchanged |
| `dbo.ProjectSummaries` | `local_dev_setup.sql`, `rebuild_db.sql` | Project context | Project memory/context services | Summary persistence | Active | No | Unchanged |
| `dbo.ProjectDecisions` | `local_dev_setup.sql`, `rebuild_db.sql` | Project decisions | Decision services/tests | Decision records | Active | No | Unchanged |
| `dbo.DecisionCategories` | `local_dev_setup.sql`, `migrate_decisions_category_status.sql`, `rebuild_db.sql` | Project decisions | Decision migration/tests | Decision lookup data | Active | No | Unchanged |
| `dbo.DecisionStatuses` | `local_dev_setup.sql`, `migrate_decisions_category_status.sql`, `rebuild_db.sql` | Project decisions | Decision migration/tests | Decision lookup data | Active | No | Unchanged |
| `dbo.ProjectContextDocuments` | `local_dev_setup.sql`, `migrate_project_context_documents.sql`, `rebuild_db.sql` | Builder/context | Builder context/readiness tests | Context documents, not runtime authority | Active | No | Unchanged |
| `dbo.ProjectObservableStates` | `local_dev_setup.sql`, `migrate_project_context_documents.sql`, `rebuild_db.sql` | Builder/readiness | Builder readiness tests, integration reset | Observable project state | Active | Test reset only | Unchanged |
| `dbo.ProjectImplementationPlans` | `local_dev_setup.sql`, `migrate_implementation_plans.sql`, `rebuild_db.sql` | Builder | Implementation plan services/tests | Implementation plan persistence | Active | No | Unchanged |
| `dbo.ProjectFiles` | `local_dev_setup.sql`, `migrate_code_indexing.sql`, `rebuild_db.sql` | Code indexing | Code index service/tests | Indexed file metadata | Active | No | Unchanged |
| `dbo.CodeIndexEntries` | `local_dev_setup.sql`, `migrate_code_indexing.sql`, `update_schema_v1_indexing.sql`, `rebuild_db.sql` | Code indexing | Code index service/tests | Code symbol lookup rows | Active | No | Unchanged |
| `dbo.ProjectDocuments` | `migrate_project_documents.sql` | Project docs | Project document tests/services | Document metadata | Active | No | Unchanged |
| `dbo.ProjectDocumentVersions` | `migrate_project_documents.sql` | Project docs | Project document tests/services | Document version history | Active | No | Unchanged |
| `dbo.ProjectDocumentLinks` | `migrate_project_documents.sql` | Project docs | Project document tests/services | Document link evidence | Active | No | Unchanged |
| `dbo.ProjectProfiles` | `migrate_project_profiles.sql` | Project profile | Profile/readiness services/tests | Project profile settings | Active | No | Unchanged |
| `dbo.ProjectCommands` | `migrate_project_profiles.sql` | Project profile | Command/profile services/tests | Build/test command definitions | Active | No | Unchanged |
| `dbo.ProjectProfileOptions` | `migrate_project_profiles.sql` | Project profile | Profile services/tests | Profile options | Active | No | Unchanged |

## Active agent memory and audit tables

| Artifact | File path | Owning area | Referenced by | Source-of-truth role | Status | PR 50 changed | Behaviour/result shape |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `agent.AgentLocalMemoryItem` | `migrate_agent_local_memory.sql` | Local memory | SQL memory store/tests | Append-only scoped local memory item | Active | No | Unchanged |
| `agent.AgentLocalMemoryEvidenceRef` | `migrate_agent_local_memory.sql` | Local memory | SQL memory store/tests | Evidence refs for local memory | Active | No | Unchanged |
| `agent.AgentLocalMemoryEvent` | `migrate_agent_local_memory.sql` | Local memory | SQL memory store/tests | Append-only lifecycle event stream | Active | No | Unchanged |
| `agent.AgentMemoryInfluenceRecord` | `migrate_agent_memory_influence.sql` | Memory influence | SQL influence store/tests | Explicit influence evidence, not approval | Active | No | Unchanged |
| `agent.AgentMemoryHandoffSlice` | `migrate_agent_memory_handoff.sql` | Memory handoff | SQL handoff store/tests | Handoff evidence, not ownership transfer | Active | No | Unchanged |
| `agent.AgentMemoryImprovementProposal` | `migrate_agent_memory_improvement_proposals.sql` | Memory proposals | Proposal service/tests | Manual proposal queue, not promotion | Active | No | Unchanged |
| `agent.AgentMemoryImprovementProposalEvent` | `migrate_agent_memory_improvement_proposals.sql` | Memory proposals | Proposal service/tests | Append-only proposal event history | Active | No | Unchanged |
| `agent.AgentMemoryIndexQueue` | `migrate_agent_memory_indexing.sql` | Retrieval/indexing | Indexing service/tests | Projection queue for retrieval acceleration | Active | No | Unchanged |
| `agent.AgentMemoryIndexEvent` | `migrate_agent_memory_indexing.sql` | Retrieval/indexing | Indexing service/tests | Indexing event history | Active | No | Unchanged |
| `agent.AgentMemoryExecutionAudit` | `migrate_agent_memory_execution_audit.sql` | Memory execution audit | Memory execution audit tests | Execution decision evidence, not approval | Active | No | Unchanged |
| `agent.AgentRunAuditEnvelope` | `migrate_agent_run_audit_envelope.sql` | Agent audit | Agent run audit store/API tests | Append-only agent run audit vault | Active | No | Unchanged |
| `agent.CollectiveMemoryItem` | `migrate_collective_memory.sql` | Collective memory | Collective memory services/tests | Accepted collective memory state | Active | No | Unchanged |
| `agent.CollectiveMemoryEvent` | `migrate_collective_memory.sql` | Collective memory | Collective memory services/tests | Append-only collective memory event stream | Active | No | Unchanged |
| `toolaudit.ToolExecutionAuditRecord` | `migrate_tool_execution_audit.sql` | Tool execution audit | Tool audit store/tests | Append-only tool execution audit; audit is not approval | Active | No | Unchanged |

## Views

| Artifact | File path | Owning area | Referenced by | Source-of-truth role | Status | PR 50 changed | Behaviour/result shape |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `agent.vwAgentLocalMemoryCurrentState` | `migrate_agent_local_memory.sql` | Local memory | Run report/read models/tests | Current-state projection over append-only local memory | Active | No | Unchanged |
| `agent.vwCollectiveMemoryCurrentState` | `migrate_collective_memory.sql` | Collective memory | Retrieval/promotion tests/services | Current-state projection over append-only collective memory | Active | No | Unchanged |

## Stored procedures

| Artifact | File path | Owning area | Referenced by | Source-of-truth role | Status | PR 50 changed | Behaviour/result shape |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `agent.usp_AgentLocalMemory_Create` | `migrate_agent_memory_stored_procedures.sql` | Local memory | SQL memory store/tests | Approved append boundary for local memory item creation | Active | No | Unchanged |
| `agent.usp_AgentLocalMemory_AddEvent` | `migrate_agent_memory_stored_procedures.sql` | Local memory | SQL memory store/tests | Approved append boundary for local memory events | Active | No | Unchanged |
| `agent.usp_AgentMemoryInfluence_Create` | `migrate_agent_memory_stored_procedures.sql` | Memory influence | SQL memory store/tests | Approved append boundary for influence evidence | Active | No | Unchanged |
| `agent.usp_AgentMemoryHandoff_Create` | `migrate_agent_memory_stored_procedures.sql` | Memory handoff | SQL memory store/tests | Approved append boundary for handoff evidence | Active | No | Unchanged |
| `agent.usp_MemoryImprovementProposal_Create` | `migrate_agent_memory_stored_procedures.sql` | Memory proposals | SQL proposal store/tests | Approved append boundary for proposal creation | Active | No | Unchanged |
| `agent.usp_MemoryImprovementProposal_AddEvent` | `migrate_agent_memory_stored_procedures.sql` | Memory proposals | SQL proposal store/tests | Approved append boundary for proposal lifecycle events | Active | No | Unchanged |
| `agent.usp_MemoryIndexQueue_Create` | `migrate_agent_memory_stored_procedures.sql` | Retrieval/indexing | SQL index queue/tests | Approved append boundary for index projection queue | Active | No | Unchanged |
| `agent.usp_MemoryIndexEvent_Add` | `migrate_agent_memory_stored_procedures.sql` | Retrieval/indexing | SQL index queue/tests | Approved append boundary for index event evidence | Active | No | Unchanged |
| `agent.usp_MemoryExecutionAudit_Create` | `migrate_agent_memory_stored_procedures.sql` | Memory execution audit | SQL execution audit store/tests | Approved append boundary for memory execution audit | Active | No | Unchanged |
| `agent.usp_CollectiveMemory_CreateFromManualPromotion` | `migrate_collective_memory.sql` | Collective memory | Collective memory promotion tests/services | Explicit manual promotion boundary | Active | No | Unchanged |
| `agent.usp_CollectiveMemory_AddEvent` | `migrate_collective_memory.sql` | Collective memory | Collective memory promotion tests/services | Append boundary for collective memory events | Active | No | Unchanged |
| `toolaudit.AppendToolExecutionAuditRecord` | `migrate_tool_execution_audit.sql` | Tool execution audit | `SqlToolExecutionAuditStore`, tests | Append-only audit record write boundary | Active | No | Unchanged |
| `toolaudit.GetToolExecutionAuditRecord` | `migrate_tool_execution_audit.sql` | Tool execution audit | `SqlToolExecutionAuditStore`, tests | Scoped read by audit ID | Active | No | Unchanged |
| `toolaudit.ListToolExecutionAuditRecordsByRun` | `migrate_tool_execution_audit.sql` | Tool execution audit | `SqlToolExecutionAuditStore`, tests | Scoped read by run | Active | No | Unchanged |

## Functions and TVPs/types

No user-defined SQL functions or table-valued parameter/type artifacts were found in the backend SQL files during this pass.

## Seed, setup, and migration scripts

| Artifact | File path | Owning area | Referenced by | Source-of-truth role | Status | PR 50 changed | Behaviour/result shape |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Local dev setup | `Database/local_dev_setup.sql` | Local database setup | Developer setup | Creates local base schema and dev-friendly objects | Active | No | Unchanged |
| Full rebuild script | `Database/rebuild_db.sql` | Local database setup | Developer setup | Drops/recreates local schema | Active | No | Unchanged |
| Dev seed data | `Database/seed_dev_data.sql` | Local database setup | Developer setup | Seeds local dev rows | Active | No | Unchanged |
| IronDev project seed | `Database/setup_irondev_record.sql` | Local database setup | Developer setup | Seeds local IronDev project record | Active | No | Unchanged |
| Core migrations | `Database/migrate_*.sql`, `Database/migration_tickets_linked_files.sql`, `Database/update_schema_v1_indexing.sql` | Runtime schema evolution | Tests/dev setup/scripts | Incremental schema setup | Active/uncertain per deployment path | No | Unchanged |

`update_schema_v1_indexing.sql` drops and recreates `dbo.CodeIndexEntries`. It is left in place because it appears to be an older local schema-update script and removing it would require compatibility proof. Status: uncertain.

## Test reset/support SQL

| Artifact | File path | Owning area | Referenced by | Source-of-truth role | Status | PR 50 changed | Behaviour/result shape |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Integration test global reset | `IronDev.IntegrationTests/IntegrationTestBase.cs` | Integration tests | Most SQL-backed tests | FK-safe cleanup before each test | Test-only | Yes | Test cleanup order only; result shape unchanged |
| Tool execution audit schema reset | `ToolExecutionAuditStoreTests.cs` | Tool audit tests | Tool audit store tests | Test-only schema drop/apply support | Test-only | No | Unchanged |
| Agent run audit schema reset | `AgentRunAuditStoreTests.cs`, `GovernedManualAgentExecutionStoreTests.cs` | Agent audit tests | Audit store tests | Test-only schema drop/apply support | Test-only | No | Unchanged |
| Agent memory schema resets | `AgentMemory/*Tests.cs`, `MemoryGovernanceEvaluationHarness.cs` | Agent memory tests | Memory SQL tests/harness | Test-only schema drop/apply support | Test-only | No | Unchanged |
| Collective memory schema resets | `CollectiveMemory*Tests.cs` | Collective memory tests | Collective memory tests | Test-only schema drop/apply support | Test-only | No | Unchanged |

## PR 50 changes

Changed:

- `IronDev.IntegrationTests/IntegrationTestBase.cs`: deletes `dbo.ProjectObservableStates` before deleting `dbo.Projects` and `dbo.Tenants`.
- `IronDev.IntegrationTests/BackendSqlCleanupTests.cs`: pins SQL inventory coverage and FK-safe reset ordering.
- `Docs/BACKEND_SQL_INVENTORY.md`: adds this inventory.

Removed:

- No SQL artifacts removed.

Stored procedure changes:

- None.

Result-shape changes:

- None.

## Boundary confirmation

- SQL remains source of truth.
- Vector/index/retrieval remains retrieval only.
- Retrieval match remains distinct from memory candidate.
- Candidate remains distinct from memory.
- Proposal remains distinct from apply.
- Audit remains distinct from approval.
- Gate remains distinct from executor.
- Critic remains distinct from governance.
- Memory safe remains distinct from promotion.
- No FK or constraint was weakened.

# PR74b retrospective SQL inventory and runtime dependency map

PR 74b is truth-finding, not schema cleanup. It updates this existing SQL inventory with the current Block G migration manifest view and separates required runtime schema from runtime queries, test fixtures, local/dev utilities, legacy artifacts, and future migration candidates.

No schema, stored procedure shape, API, CLI, UI, runtime behavior, persistence behavior, authority, workflow, A2A, LangGraph path, source apply, or memory promotion changes are made by PR74b.

## 1. Summary

- `Database/sql-inventory.json` is the machine-readable retrospective inventory.
- `Database/migrations.json` currently applies the Block G governance event, durable tool request, durable tool gate decision, and durable approval decision migrations.
- `Database/verify-migrations.ps1` currently verifies the Block G governance, tool-request, tool-gate-decision, and approval-decision objects and constraints.
- Older runtime SQL remains inventoried as required runtime schema, but mostly `appliedByManifest: false` and `verifiedByScript: false`.
- Existing runtime DDL candidates remain explicit migration-discipline debt; they are not fixed in this PR.

## 2. Required runtime schema

Current manifest-covered runtime schema:

| Object/script | Owner | Manifest applied | Verify script checked | Notes |
| --- | --- | --- | --- | --- |
| `Database/migrate_governance_event.sql` | governance | Yes | Yes | Creates `governance.GovernanceEvent` and governance event procedures. |
| `Database/migrate_tool_request.sql` | tool-request | Yes | Yes | Creates `governance.ToolRequest`, tool request procedures, and `FK_ToolRequest_GovernanceEvent`. |
| `Database/migrate_tool_gate_decision.sql` | tool-gate-decision | Yes | Yes | Creates `governance.ToolGateDecision`, gate decision procedures, `FK_ToolGateDecision_ToolRequest`, and `FK_ToolGateDecision_GovernanceEvent`. |
| `Database/migrate_approval_decision.sql` | approval-decision | Yes | Yes | Creates `governance.ApprovalDecision`, approval decision procedures, `FK_ApprovalDecision_GovernanceEvent`, `FK_ApprovalDecision_Supersedes`, and SQL insert validation for sensitive human-only scopes plus private-reasoning evidence rejection. |

Runtime schema not yet covered by the current PR74a manifest includes agent memory, agent run audit, collective memory, tool execution audit, project documents, project profiles, chat, ticket, code indexing, project context, and decision lookup migrations. Those are intentionally listed in `Database/sql-inventory.json` as required runtime schema with manifest/verify coverage set to false unless currently proven.

## 3. Required runtime stored procedures

Current Block G stored procedure dependencies:

| Procedure | Called by | Covered by manifest | Verified |
| --- | --- | --- | --- |
| `governance.AppendGovernanceEvent` | `SqlGovernanceEventStore` | Yes | Yes |
| `governance.GetGovernanceEvent` | `SqlGovernanceEventStore` | Yes | Yes |
| `governance.ListGovernanceEventsForProject` | `SqlGovernanceEventStore` | Yes | Yes |
| `governance.ListGovernanceEventsForCorrelation` | `SqlGovernanceEventStore` | Yes | Yes |
| `governance.ListGovernanceEventsForSubject` | `SqlGovernanceEventStore` | Yes | Yes |
| `governance.ListGovernanceEventsCausedBy` | `SqlGovernanceEventStore` | Yes | Yes |
| `governance.usp_ToolRequest_Create` | `SqlToolRequestStore` | Yes | Yes |
| `governance.usp_ToolRequest_GetById` | `SqlToolRequestStore` | Yes | Yes |
| `governance.usp_ToolRequest_ListForProject` | `SqlToolRequestStore` | Yes | Yes |
| `governance.usp_ToolRequest_ListForCorrelation` | `SqlToolRequestStore` | Yes | Yes |

Older stored procedure surfaces remain inventoried, including `agent.usp_*`, collective memory procedures, and `toolaudit.*` audit procedures. They are active but outside the current Block G manifest proof.

## 4. Required runtime inline SQL

Runtime inline SQL remains present in application services such as project, ticket, project memory/context, project document, semantic memory, and legacy run stores. These are required runtime queries or legacy bootstrap DDL candidates and are separately documented in `Docs/BACKEND_INLINE_SQL_INVENTORY.md`.

The new Block G governance/tool-request/tool-gate-decision/approval-decision stores use stored procedures for governed writes/reads and do not create schema at runtime.

## 5. Required test SQL

Required test SQL includes `IntegrationTestBase`, `ApiTestBase`, agent memory schema helpers, API audit seed helpers, and specific SQL-heavy integration tests. These are test fixtures and must not be mistaken for production migration ownership.

## 6. Local/dev utility SQL

Local/dev utility SQL includes `Database/local_dev_setup.sql`, `Database/rebuild_db.sql`, `Database/seed_dev_data.sql`, `Database/setup_irondev_record.sql`, `tools/localtest/localtest-seed.sql`, and localtest reset scripts.

These are useful developer workflows, not runtime migration proof.

## 7. Legacy or unused SQL

Legacy or uncertain SQL remains in place. Examples include `Database/update_schema_v1_indexing.sql` and `Docs/migrations/*.sql`. They are inventoried, not deleted.

## 8. Future migration candidates

Future migration candidates include legacy runtime DDL/bootstrap services and older active runtime schema scripts not yet represented in the ordered migration manifest and verification script.

Candidate areas:

- Run and run-event stores.
- Ticket/project memory/project document bootstrap DDL.
- Semantic memory cache/index bootstrap DDL.
- Agent memory, audit, collective memory, and tool audit migrations outside the current Block G manifest.

## 9. Known gaps

- The current ordered manifest is intentionally narrow and covers PR72/73/74/75/76 Block G governance/tool-request/tool-gate-decision/approval-decision migrations.
- Older active SQL still needs migration-ownership decisions.
- Runtime DDL candidates remain explicit debt.
- This PR does not prove real DB API smoke behavior; PR74c remains necessary.

## 10. Next cleanup candidates

- Expand ordered migration discipline beyond Block G after ownership is confirmed.
- Move remaining runtime bootstrap DDL behind migrations without changing runtime result shapes.
- Decide whether historical docs migrations should remain, move, or be retired after freeze.
- Add PR74c API smoke receipt against a migrated database.

## PR77 durable policy decision event update

PR77 adds the fifth Block G manifest-covered governance ledger:

| Object/script | Owner | Manifest applied | Verify script checked | Notes |
| --- | --- | --- | --- | --- |
| `Database/migrate_policy_decision_event.sql` | governance | Yes | Yes | Creates `governance.PolicyDecisionEvent`, stored procedures, validation triggers, and optional links to tool request, tool gate decision, and approval decision evidence. |
| `IronDev.Infrastructure/Governance/SqlPolicyDecisionEventStore.cs` | governance | Yes | Yes | Runtime store calls `governance.usp_PolicyDecisionEvent_*` stored procedures only; no runtime schema creation and no API/CLI endpoint. |

Policy decision events are evidence only. They do not approve, execute, satisfy policy, continue workflow, apply source, create A2A handoff, create dogfood receipt, or promote memory.

## PR78 durable dogfood receipt update

PR78 adds the sixth Block G manifest-covered governance ledger:

| Object/script | Owner | Manifest applied | Verify script checked | Notes |
| --- | --- | --- | --- | --- |
| `Database/migrate_dogfood_receipt.sql` | governance | Yes | Yes | Creates `governance.DogfoodReceipt`, stored procedures, validation triggers, and optional links to tool request, tool gate decision, approval decision, and policy decision evidence. |
| `IronDev.Infrastructure/Governance/SqlDogfoodReceiptStore.cs` | governance | Yes | Yes | Runtime store calls `governance.usp_DogfoodReceipt_*` stored procedures only; no runtime schema creation. |
| `IronDev.Api/Controllers/SqlDogfoodLoopApiStore.cs` | API/governance bridge | Yes | Yes | Dogfood Loop API stores durable receipt evidence through `IDogfoodReceiptStore`; it does not create approval, execution permission, policy satisfaction, source apply, workflow continuation, A2A handoff, or memory promotion. |

Dogfood receipts are evidence only. They do not approve release readiness, satisfy policy, continue workflow, execute tools, apply source, create A2A handoff, or promote memory.

## PR79 durable ThoughtLedger governance event reference update

PR79 adds the seventh Block G manifest-covered governance ledger:

| Object/script | Owner | Manifest applied | Verify script checked | Notes |
| --- | --- | --- | --- | --- |
| `Database/migrate_thoughtledger_governance_event_reference.sql` | governance | Yes | Yes | Creates `governance.ThoughtLedgerGovernanceEventReference`, stored procedures, validation triggers, and a FK to existing `governance.GovernanceEvent`. |
| `IronDev.Infrastructure/Governance/SqlThoughtLedgerGovernanceEventReferenceStore.cs` | governance | Yes | Yes | Runtime store calls `governance.usp_ThoughtLedgerGovernanceEventReference_*` stored procedures only; no runtime schema creation. |

ThoughtLedger governance event references are evidence links only. They preserve the ThoughtLedger entry ID exactly as text because no durable ThoughtLedger table exists yet. They do not approve, authorize, execute, satisfy policy, continue workflow, apply source, approve release, create dogfood receipts, create A2A handoffs, or promote memory.
## PR93 durable A2A handoff store

Status: active.

SQL artifacts:

- `a2a.AgentHandoff`
- `a2a.AgentHandoffEvidenceReference`
- `a2a.AgentHandoffEvidenceAllowedUse`
- `a2a.AgentHandoffConstraint`
- `a2a.usp_AgentHandoff_Create`
- `a2a.usp_AgentHandoff_Get`
- `a2a.usp_AgentHandoff_ListByProject`
- `a2a.usp_AgentHandoff_ListByCorrelation`
- `a2a.usp_AgentHandoff_ListBySubject`

Owner:

- `IronDev.Infrastructure/Governance/SqlAgentHandoffStore.cs`

Boundary:

- records handoff context and evidence only
- validates `AgentHandoffValidator` before persistence
- validates `AgentHandoffAuthorityTransferValidator` before persistence
- writes a governance event for traceability
- append-only after insert
- no A2A transport
- no inbox/outbox
- no workflow continuation
- no approval satisfaction
- no policy satisfaction
- no execution permission
- no source apply permission
- no memory promotion permission
- no release approval

Behavior changed in this PR: no product/runtime behavior; only the durable handoff ledger and repository are added.