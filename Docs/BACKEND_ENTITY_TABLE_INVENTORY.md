# Backend Entity/Table Contract Inventory

PR 51.5 is entity/table contract cleanup, not domain redesign.

No behavior change intended.
No schema semantics change.
No stored procedure result-shape change.
No SQL/API/CLI/UI/runtime/persistence/capability changes.

SQL remains the source of truth. Vector, index, and retrieval surfaces remain lookup-only. Retrieval match is not memory candidate. Candidate is not memory. Proposal is not apply. Audit is not approval. Gate is not executor. Critic is not governance. Memory safe is not promotion. Human review remains required for source apply and memory promotion.

This inventory records the current backend persistence concepts before the Backend Contract Freeze Report. It does not drop active tables, reshape columns, rename stored procedure outputs, or change repository behavior.

## Status legend

| Status | Meaning |
| --- | --- |
| Active | Used by production services, migrations, or current integration tests. |
| Test-only | Used only for test fixtures/support. |
| Obsolete | Proven unused by production, tests, SQL scripts, stored procedures, setup, API/CLI/UI contracts. |
| Uncertain | Usage or deployment role is not proven enough for safe deletion or rename. Leave in place. |

## `dbo` application tables

| SQL schema/table | C# entity/model class | Repository/service owner | Stored procedure owner | Test coverage | Status | Changed in PR51.5 | Behavior unchanged |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `dbo.Tenants` | Auth/user DTOs and row models | Auth, tenant controller/services | None | API auth/tenant tests, integration seed/reset | Active | No | Yes |
| `dbo.Users` | Auth/user DTOs and row models | Auth/user service | None | API auth tests, integration seed/reset | Active | No | Yes |
| `dbo.TenantUsers` | Auth membership row models | Auth/user service | None | API auth/tenant tests | Active | No | Yes |
| `dbo.Projects` | Project DTOs/rows | Project service, ticket/project context services | None | Project, builder, API, reset tests | Active | No | Yes |
| `dbo.ProjectChatSessions` | Chat session DTOs/rows | Chat persistence services | None | Chat/API integration tests | Active | No | Yes |
| `dbo.ChatMessages` | Chat message DTOs/rows | Chat history/persistence services | None | Chat governance, API, reset tests | Active | No | Yes |
| `dbo.ChatMessageFeedback` | Feedback DTOs/rows | Chat feedback service | None | Chat grounding/feedback tests | Active | No | Yes |
| `dbo.ChatTurnGovernance` | Chat turn governance DTOs | Chat turn persistence service | None | Chat governance tests | Active | No | Yes |
| `dbo.ChatTurnClarifications` | Chat clarification DTOs | Chat turn persistence service | None | Chat governance tests | Active | No | Yes |
| `dbo.ChatTurnTraces` | Chat trace DTOs | Chat turn persistence service | None | Chat governance tests | Active | No | Yes |
| `dbo.ProjectTickets` | Ticket DTOs, builder ticket DTOs | `TicketService`, builder proposal services | None | Ticket, builder, API tests | Active | No | Yes |
| `dbo.ArtifactSourceReferences` | Artifact source reference DTOs | `ArtifactSourceReferenceService` | None | Source reference/ticket tests | Active | No | Yes |
| `dbo.ProjectRules` | Project rule DTOs/rows | Project context/governance services | None | Project context tests | Active | No | Yes |
| `dbo.ProjectSummaries` | Project summary DTOs/rows | Project memory/context service | None | Project memory/context tests | Active | No | Yes |
| `dbo.ProjectDecisions` | Project decision DTOs/rows | Decision/project memory services | None | Decision and context tests | Active | No | Yes |
| `dbo.DecisionCategories` | Decision category lookup rows | Decision lookup/migration support | None | Decision migration/tests | Active | No | Yes |
| `dbo.DecisionStatuses` | Decision status lookup rows | Decision lookup/migration support | None | Decision migration/tests | Active | No | Yes |
| `dbo.ProjectContextDocuments` | Project context document DTOs/rows | `ProjectMemoryService`, builder context service | None | Builder context/readiness tests | Active | No | Yes |
| `dbo.ProjectObservableStates` | Project observable state DTOs/rows | Builder readiness/context services | None | Builder readiness tests, integration reset guard | Active | No | Yes |
| `dbo.ProjectImplementationPlans` | Implementation plan DTOs/rows | Implementation plan services | None | Implementation plan/ticket tests | Active | No | Yes |
| `dbo.ProjectFiles` | Code/project file DTOs/rows | Code indexing services | None | Code index tests | Active | No | Yes |
| `dbo.CodeIndexEntries` | Code index entry DTOs/rows | Code indexing/search services | None | Code index tests | Active | No | Yes |
| `dbo.ProjectDocuments` | Project document DTOs/rows | Project document service | None | Project document tenant tests | Active | No | Yes |
| `dbo.ProjectDocumentVersions` | Project document version DTOs/rows | Project document service | None | Project document tenant tests | Active | No | Yes |
| `dbo.ProjectDocumentLinks` | Project document link DTOs/rows | Project document service | None | Project document tenant tests | Active | No | Yes |
| `dbo.ProjectProfiles` | Project profile DTOs/rows | Project profile/readiness services | None | Project profile/readiness tests | Active | No | Yes |
| `dbo.ProjectCommands` | Project command DTOs/rows | Project command/profile services | None | Project profile/readiness tests | Active | No | Yes |
| `dbo.ProjectProfileOptions` | Project profile option DTOs/rows | Project profile services | None | Project profile/readiness tests | Active | No | Yes |
| `dbo.SemanticEmbeddings` | Semantic embedding rows | `WeaviateSemanticMemoryService` legacy bootstrap | None | Semantic memory tests/legacy paths | Uncertain | No | Yes |
| `dbo.SemanticIndexRuns` | Semantic index run rows | `WeaviateSemanticMemoryService` legacy bootstrap | None | Semantic memory tests/legacy paths | Uncertain | No | Yes |
| `dbo.SemanticArtefacts` | Semantic artefact rows | `SemanticMemoryRepositories` | None | Semantic memory tests/legacy paths | Uncertain | No | Yes |
| `dbo.SemanticChunks` | Semantic chunk rows | `SemanticMemoryRepositories` | None | Semantic memory tests/legacy paths | Uncertain | No | Yes |
| `dbo.EmbeddingJobs` | Embedding job rows | `SemanticMemoryRepositories` | None | Semantic memory tests/legacy paths | Uncertain | No | Yes |
| `dbo.SemanticSearchTraces` | Semantic search trace rows | `SemanticMemoryRepositories` | None | Semantic memory tests/legacy paths | Uncertain | No | Yes |
| `dbo.SemanticSearchTraceResults` | Semantic search trace result rows | `SemanticMemoryRepositories` | None | Semantic memory tests/legacy paths | Uncertain | No | Yes |

## Agent memory, audit, retrieval, and promotion tables

| SQL schema/table | C# entity/model class | Repository/service owner | Stored procedure owner | Test coverage | Status | Changed in PR51.5 | Behavior unchanged |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `agent.AgentLocalMemoryItem` | `AgentLocalMemoryItem` / store rows | `SqlAgentLocalMemoryStore`, `IAgentMemorySilo` | `agent.usp_AgentLocalMemory_Create` | Agent memory silo/local memory tests | Active | No | Yes |
| `agent.AgentLocalMemoryEvidenceRef` | Memory evidence ref models | `SqlAgentLocalMemoryStore` | `agent.usp_AgentLocalMemory_Create` | Agent memory evidence tests | Active | No | Yes |
| `agent.AgentLocalMemoryEvent` | Memory lifecycle event models | `SqlAgentLocalMemoryStore` | `agent.usp_AgentLocalMemory_AddEvent` | Agent memory event/silo tests | Active | No | Yes |
| `agent.AgentMemoryInfluenceRecord` | `AgentMemoryInfluenceRecord` | `SqlAgentMemoryInfluenceStore` | `agent.usp_AgentMemoryInfluence_Create` | Influence and governance harness tests | Active | No | Yes |
| `agent.AgentMemoryHandoffSlice` | `AgentMemoryHandoffSlice` | `SqlAgentMemoryHandoffStore` | `agent.usp_AgentMemoryHandoff_Create` | Handoff and governance harness tests | Active | No | Yes |
| `agent.AgentMemoryImprovementProposal` | `MemoryImprovementProposal` | `SqlMemoryImprovementProposalService` | `agent.usp_MemoryImprovementProposal_Create` | Memory improvement proposal tests | Active | No | Yes |
| `agent.AgentMemoryImprovementProposalEvent` | Proposal event models | `SqlMemoryImprovementProposalService` | `agent.usp_MemoryImprovementProposal_AddEvent` | Memory improvement proposal lifecycle tests | Active | No | Yes |
| `agent.AgentMemoryIndexQueue` | Memory index queue record models | `SqlMemoryIndexQueueStore` | `agent.usp_MemoryIndexQueue_Create` | Memory indexing boundary tests | Active | No | Yes |
| `agent.AgentMemoryIndexEvent` | Memory index event record models | `SqlMemoryIndexQueueStore` | `agent.usp_MemoryIndexEvent_Add` | Memory indexing boundary tests, PR51 cleanup guard | Active | No | Yes |
| `agent.AgentMemoryExecutionAudit` | Memory execution audit models | SQL memory execution audit store/service | `agent.usp_MemoryExecutionAudit_Create` | Memory execution audit tests | Active | No | Yes |
| `agent.AgentRunAuditEnvelope` | `AgentRunAuditEnvelope` | SQL agent run audit store/read repository | None in current store contract; table constraints/triggers own persistence guard | Agent run audit store/API tests | Active | No | Yes |
| `agent.CollectiveMemoryItem` | Collective memory item/current state models | Collective memory promotion/retrieval services | `agent.usp_CollectiveMemory_CreateFromManualPromotion` | Collective memory promotion/retrieval tests | Active | No | Yes |
| `agent.CollectiveMemoryEvent` | Collective memory event models | Collective memory promotion service | `agent.usp_CollectiveMemory_AddEvent` | Collective memory promotion tests | Active | No | Yes |
| `agent.vwAgentLocalMemoryCurrentState` | Current-state projection models | Run report/read-model services | View, no proc | Run report/local memory tests | Active | No | Yes |
| `agent.vwCollectiveMemoryCurrentState` | Collective current-state projection models | Collective retrieval service | View, no proc | Collective retrieval tests | Active | No | Yes |
| `toolaudit.ToolExecutionAuditRecord` | `ToolExecutionAuditRecord` | `SqlToolExecutionAuditStore` | `toolaudit.AppendToolExecutionAuditRecord`, `GetToolExecutionAuditRecord`, `ListToolExecutionAuditRecordsByRun` | Tool execution audit store tests | Active | No | Yes |

## Workspace apply evidence files

These are file-backed evidence contracts, not SQL tables, but they are part of the backend persistence vocabulary and freeze risk.

| Persistence artifact | C# entity/model class | Repository/service owner | Stored procedure owner | Test coverage | Status | Changed in PR51.5 | Behavior unchanged |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `.irondev/workspace.json` | Disposable workspace metadata models | Workspace prepare/check services | None | Workspace CLI/service tests | Active | No | Yes |
| `.irondev/runs/<run-id>/validation.json` | Workspace validation models | Workspace validation service | None | Workspace validation tests | Active | No | Yes |
| `.irondev/runs/<run-id>/diff.json` | Workspace diff models | Workspace diff service | None | Workspace diff tests | Active | No | Yes |
| `.irondev/runs/<run-id>/promotion-package.json` | Promotion package models | Promotion package service | None | Promotion package tests | Active | No | Yes |
| `.irondev/runs/<run-id>/promotion-approval.json` | Promotion approval evidence models | Promotion approval service | None | Promotion approval tests | Active | No | Yes |
| `.irondev/runs/<run-id>/apply-preflight.json` | Apply preflight models | Apply preflight service | None | Apply preflight tests | Active | No | Yes |
| `.irondev/runs/<run-id>/apply-dry-run.json` | Apply dry-run models | Apply dry-run service | None | Apply dry-run tests | Active | No | Yes |
| `.irondev/runs/<run-id>/apply-copy.json` | Apply-copy models | Apply-copy service | None | Apply-copy tests | Active | No | Yes |
| `.irondev/runs/<run-id>/apply-verify.json` | Apply-verify models | Apply-verify service | None | Apply-verify tests | Active | No | Yes |
| `.irondev/runs/<run-id>/post-apply-validate.json` | Post-apply validation models | Post-apply validation service | None | Post-apply validation tests | Active | No | Yes |
| `.irondev/runs/<run-id>/source-report.json` | Source report models | Source report service | None | Source report tests | Active | No | Yes |
| `.irondev/runs/<run-id>/failure-package.json` | Failure package models | Failure package service | None | Failure package tests | Active | No | Yes |

## Tool, critic, gate, and proposal contracts

These are not table-per-entity contracts, but they are persisted through audit envelopes, tool-audit records, or file-backed evidence.

| Persistence concept | C# entity/model class | Repository/service owner | Stored procedure owner | Test coverage | Status | Changed in PR51.5 | Behavior unchanged |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Agent run audit | `AgentRunAuditEnvelope`, query DTOs | Agent run audit store/read repository | Agent audit table/constraints | Agent run audit tests | Active | No | Yes |
| Tool execution audit | `ToolExecutionAuditRecord` | `SqlToolExecutionAuditStore` | `toolaudit.*` stored procedures | Tool execution audit tests | Active | No | Yes |
| Tool request | `AgentToolRequest` | Tool request validator/gate inputs | None | Tool request contract tests | Active | No | Yes |
| Tool gate decision | `AgentToolExecutionGateDecision` | Tool execution gate service | None | Gate tests | Active | No | Yes |
| Critic review | `CriticReviewResult` | Manual/model-backed critic services | None | Critic/manual/model tests | Active | No | Yes |
| Builder patch proposal | Patch proposal models | Manual implementation proposal services | None | Patch proposal/loop tests | Active | No | Yes |
| Memory improvement detection | `MemoryImprovementDetectionResult`, proposal draft models | Manual/model-backed memory improvement services | None | Memory improvement tests | Active | No | Yes |

## Obsolete mappings removed

None.

No entity, model, mapping, table, stored procedure, DTO, API/CLI/UI contract, or test fixture was removed in PR51.5.

## Uncertain artifacts left in place

| Current name | Why it is confusing | Why not changed now | Handle after PR56? |
| --- | --- | --- | --- |
| `dbo.SemanticEmbeddings` / `dbo.SemanticIndexRuns` | The names can read like memory truth rather than retrieval/index support. | These are legacy semantic-memory bootstrap tables and changing/removing them would be behavior-affecting. | Yes, during runtime bootstrap DDL and semantic-memory ownership cleanup. |
| `dbo.SemanticArtefacts` / `dbo.SemanticChunks` | The names are broad and do not explicitly say retrieval/index projection. | Current repository behavior and tests depend on the existing names. | Yes, only with migration and contract review. |
| `CollectiveMemoryRetrievalAuthorityFilter.IncludeCandidates` | Uses `Candidate`, but here it refers to memory authority level, not retrieval row shape. | PR48 already documented why this term is accurate in this specific context. | No immediate change. |
| `AgentToolExecutionGateDecision.GrantsExecution` | Could sound like direct execution permission. | Boundary tests and naming inventory say gate is not executor; renaming would ripple through reviewed contracts. | Maybe in API/CLI contract phase, not here. |
| Runtime bootstrap DDL services | Service names hide schema ownership because they still ensure/create tables at runtime. | PR51 inventoried them as explicit runtime DDL debt. Removing them now would change runtime bootstrap behavior. | Yes, `PR 52 - Runtime Bootstrap DDL Removal / Migration Ownership Cleanup`. |

## Boundary confirmation

- SQL remains source of truth.
- Vector/index/retrieval remains lookup only.
- Retrieval match remains distinct from memory candidate.
- Candidate remains distinct from memory.
- Proposal remains distinct from apply.
- Audit remains distinct from approval.
- Gate remains distinct from executor.
- Critic remains distinct from governance.
- Memory safe remains distinct from promotion.
- Human review remains required for source apply and memory promotion.
- No active table was dropped.
- No uncertain artifact was deleted.
- No column, FK, index, stored procedure result shape, repository behavior, API/CLI/UI contract, or runtime capability was changed.

## PR79 entity/table addition

| Persistence concept | SQL schema/table | C# entity/model class | Repository/service owner | Stored procedure owner | Test coverage | Status | Changed in this PR | Behaviour unchanged |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| ThoughtLedger governance event reference | `governance.ThoughtLedgerGovernanceEventReference` | `ThoughtLedgerGovernanceEventReference`, `ThoughtLedgerGovernanceEventReferenceReadModel`, `ThoughtLedgerGovernanceEventReferenceSummary` | `SqlThoughtLedgerGovernanceEventReferenceStore` | `governance.usp_ThoughtLedgerGovernanceEventReference_*` | `ThoughtLedgerGovernanceReferenceStoreTests`, migration verifier, smoke script | Active | Yes | Yes |

This table preserves `ThoughtLedgerEntryId` as an exact text identity because there is no durable ThoughtLedger table in this slice. The table links ThoughtLedger entries to existing `governance.GovernanceEvent` rows as evidence only. It is not approval, execution permission, policy satisfaction, workflow continuation, source apply, release approval, dogfood receipt creation, A2A handoff creation, or memory promotion.
## PR93 A2A agent handoff persistence

Persistence concept: durable agent handoff record.

SQL schema/table:

- `a2a.AgentHandoff`
- `a2a.AgentHandoffEvidenceReference`
- `a2a.AgentHandoffEvidenceAllowedUse`
- `a2a.AgentHandoffConstraint`

C# entity/model class:

- `AgentHandoff`
- `AgentHandoffEvidenceReference`
- `AgentHandoffConstraint`
- `AgentHandoffSummary`

Repository/service owner:

- `SqlAgentHandoffStore`
- `IAgentHandoffStore`

Stored procedure owner:

- `a2a.usp_AgentHandoff_Create`
- `a2a.usp_AgentHandoff_Get`
- `a2a.usp_AgentHandoff_ListByProject`
- `a2a.usp_AgentHandoff_ListByCorrelation`
- `a2a.usp_AgentHandoff_ListBySubject`

Test coverage:

- `AgentHandoffStoreTests`
- `AgentHandoffContractTests`
- `NoAuthorityTransferValidatorTests`
- `DatabaseMigrationApplicationReceiptTests`

Status: active.

Changed in PR93: yes.

Behavior unchanged confirmation: the store records handoff context, evidence references, allowed uses, and constraints only. It does not deliver messages, start workflow, approve, satisfy policy, execute, mutate source, promote memory, or approve release.
## PR107 - Memory proposal staging entity/table contract

| SQL table | C# model/store | Status | Changed in PR107 | Behavior unchanged confirmation |
| --- | --- | --- | --- | --- |
| `memory.MemoryProposal` | `MemoryProposal`, `SqlMemoryProposalStagingStore` | active | yes | Staged proposal only. It is not accepted memory, promotion, policy, approval, workflow progress, retrieval authority, source apply, or vector/index content. |
| `memory.MemoryProposalEvidenceReference` | `MemoryProposalEvidenceReference` | active | yes | Review evidence only. Evidence does not grant authority. |
| `memory.MemoryProposalGroundingReference` | `MemoryProposalGroundingReference` | active | yes | Traceability only. Grounding does not create truth or acceptance. |
| `memory.MemoryProposalWorkflowReference` | `MemoryProposalWorkflowReference` | active | yes | Provenance only. Workflow reference does not resume or continue workflow. |

Ugly-name note: `agent.AgentMemoryImprovementProposal` remains in place as the older manual improvement proposal queue and is not renamed or repurposed in PR107.
