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
