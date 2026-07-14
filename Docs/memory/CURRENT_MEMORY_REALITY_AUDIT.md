# Current Memory Reality Audit

**Status:** Canonical current-state audit

**Last reviewed:** 14 July 2026

**Programme slice:** CLN-23

## Decision

IronDev does not have one memory system. It has an active SQL product-context store, an active semantic derivative, a manifest-owned proposal-only store, session context, and a large dormant/legacy agent-memory subsystem. These must not be described as one coherent Project Canon implementation.

Every inventoried item below has one of the programme classifications. **Unknown count: 0.** Any future `Unknown` blocks CLN-24 through CLN-28 and all smarter-memory development.

## Tables

| Item | Classification | Runtime reality | Evidence |
| --- | --- | --- | --- |
| `dbo.ProjectContextDocuments` | ProjectCanon | Active SQL store can carry Binding/StrongGuidance authority-shaped content; direct-write containment is incomplete. | `ProjectMemoryService`, `MemoryController` |
| `dbo.ProjectDecisions` | ProjectCanon | Active durable decisions; current POST accepts a body model. | `ProjectMemoryService` |
| `dbo.ProjectRules` | ProjectCanon | Active authority-shaped rules; current POST accepts a body model. | `ProjectMemoryService` |
| `dbo.ProjectSummaries`, `dbo.ProjectImplementationPlans`, `dbo.ProjectObservableStates` | OperationalMemory | Active project/run context, not approval or canon by itself. | `ProjectMemoryService` |
| `dbo.ArtifactSourceReferences`, chat/document/run/audit tables consumed as context | RawEvidence | Provenance and source facts; not accepted memory. | `ArtifactSourceReferenceService`, context pipeline |
| `dbo.SemanticArtefacts`, `dbo.SemanticChunks`, `dbo.SemanticEmbeddings`, `dbo.EmbeddingJobs`, `dbo.SemanticSearchTraces`, `dbo.SemanticSearchTraceResults`, `dbo.SemanticIndexRuns` | DerivedIndex | Active SQL metadata for semantic retrieval; rebuildable and non-authoritative. | CLN-19 manifest, semantic repositories |
| `memory.MemoryProposal` and its evidence/grounding/workflow reference tables | Proposal | Manifest-owned append-only staging only; constraints prohibit accepted-memory and promotion claims. | `migrate_memory_proposal_staging.sql` |
| `agent.AgentLocalMemoryItem`, evidence, and event tables | AgentPrivateMemory | Legacy schema scripts; not manifest-owned or production-host registered. | `migrate_agent_local_memory.sql`, SQL inventory |
| `agent.AgentMemoryImprovementProposal` and event tables | Proposal | Legacy proposal implementation; not manifest-owned or production-host registered. | `migrate_agent_memory_improvement_proposals.sql` |
| `agent.CollectiveMemoryItem` and event tables | ProjectCanon | Legacy promoted collective-memory design; not manifest-owned or production-host registered. | `migrate_collective_memory.sql` |
| `agent.AgentMemoryInfluenceRecord`, handoff, run report, and execution-audit tables | OperationalMemory | Legacy operational/evidence subsystem; not manifest-owned or production-host registered. | agent-memory migrations |
| `agent.AgentMemoryIndexQueue` and event tables | DerivedIndex | Legacy rebuild/index queue; not manifest-owned or production-host registered. | `migrate_agent_memory_indexing.sql` |

## Models

| Item | Classification | Runtime reality | Evidence |
| --- | --- | --- | --- |
| `ProjectContextDocument`, `ProjectDecision`, `ProjectRule` | ProjectCanon | Active authority-shaped DTO/data models; authority/status are currently caller-supplied. | `IronDev.Data.Models`, `IronDev.Core.Models` |
| `ProjectSummary`, `ProjectImplementationPlan`, `ProjectObservableState`, chat context state | OperationalMemory | Active working/project context. | Core/Data models |
| `MemoryProposal*`, improvement candidate/package models | Proposal | Review candidates and evidence packages only. | `IronDev.Core/AgentMemory` |
| `AgentLocalMemory*`, `AgentMemorySilo*` | AgentPrivateMemory | Legacy agent-scoped contracts; no production DI registration. | `IronDev.Core/AgentMemory` |
| `CollectiveMemory*` | ProjectCanon | Legacy accepted/shared-memory contracts; production promotion path is not hosted. | `IronDev.Core/AgentMemory/Collective` |
| semantic artefact/chunk/search/index models | DerivedIndex | Retrieval projection and trace models. | `IronDev.Core/KnowledgeCompiler` |
| conversation/session state and prompt context models | SessionMemory | Request/run-local context assembly. | chat context and prompt boundary models |
| source, evidence, receipt, audit, and trace models | RawEvidence | Accountability inputs, never promotion by themselves. | governance/run/evidence models |

## Services

| Item | Classification | Runtime reality | Evidence |
| --- | --- | --- | --- |
| `ProjectMemoryService` | Legacy | Active mixed-responsibility service owns canon-shaped writes, operational reads, archive, and lexical retrieval. CLN-24 must split authority operations. | production DI registration |
| `ProjectChatContextPipeline`, `ProjectChatContextStateCompiler`, `ConversationContextResolver`, `PromptContextBuilder` | SessionMemory | Active consumers/assemblers; retrieved data can reach prompts. | production service graph |
| `ProjectMemoryMapService`, `ProjectContextExportService`, code/ticket/context consumers | OperationalMemory | Active read consumers and source-link projections. | infrastructure services |
| `SqlMemoryProposalStagingStore` | Proposal | Compiled and tested against the manifest-backed proposal-only schema, but not production-host registered; no deployed promotion authority. | SQL inventory, production DI absence, and contract tests |
| manual memory-improvement agent/store | Proposal | Active API creates proposal-only agent-run evidence, not accepted memory. | `ManualMemoryImprovementsV1Controller` |
| `SqlAgentLocalMemoryStore`, silo, collective-memory, conscience, influence, handoff, execution, and indexing services | Legacy | Compiled but not registered in the production host; depend on non-manifest schema. | `IronDev.Infrastructure/AgentMemory`, `Program.cs` |
| semantic repositories, ranker, bundle builder, evidence provider, workflow node | DerivedIndex | Active retrieval/index support. | semantic infrastructure |

## Controllers

| Item | Classification | Runtime reality | Evidence |
| --- | --- | --- | --- |
| `MemoryController` GET/search/status routes | OperationalMemory | Active authenticated read/retrieval surface; tenant filtering is mostly delegated to services. | route inventory |
| `MemoryController` POST summary/decision/document/plan/rule routes | Legacy | Active generic-authenticated direct writes; route project is not bound in the controller and authority-shaped body fields can be self-asserted. CLN-24 blocker. | controller source |
| `MemoryController` reindex route | Legacy | Active generic-authenticated maintenance mutation without an admin/maintenance capability. CLN-24 blocker. | controller source |
| `ManualMemoryImprovementsV1Controller` | Proposal | Active proposal-only evidence surface with explicit no-promotion claims. | controller contract |
| memory snippet/search health surfaces in code-index/project-services controllers | DerivedIndex | Active read/diagnostic derivatives. | controller routes |

## Clients

| Item | Classification | Runtime reality | Evidence |
| --- | --- | --- | --- |
| `IMemoryApiClient` / `MemoryApiClient` | Legacy | Active client mirrors direct canon-shaped writes and archive operations. | `IronDev.Client/Memory` |
| manual memory-improvement client methods | Proposal | Active proposal-only create/read client. | `IronDevApiClient` |
| generated OpenAPI memory operations | Legacy | Generated contract exposes active mixed read/write/reindex surface; not an authority grant. | generated API types |

## UI Surfaces

| Item | Classification | Runtime reality | Evidence |
| --- | --- | --- | --- |
| memory proposal review route | Proposal | Read-only proposal/evidence review surface; no promotion executor. | Tauri governance feature and tests |
| context inspector/panels and chat context panels | SessionMemory | Display request/project context; rendering is not authority. | Tauri context components |
| memory status/search consumers represented in generated client | DerivedIndex | Retrieval/diagnostic UI capability only. | generated types |

## Semantic Providers

| Item | Classification | Runtime reality | Evidence |
| --- | --- | --- | --- |
| `InMemorySemanticMemoryService` | DerivedIndex | Active selectable local/test provider built from authoritative SQL source records. | code-intelligence DI |
| `WeaviateSemanticMemoryService` | DerivedIndex | Active selectable vector provider; SQL metadata/source remains authoritative. | code-intelligence DI |
| fake/OpenAI embedding providers, chunker, ranker, context bundle | DerivedIndex | Embedding/ranking helpers only. | semantic infrastructure |

## Vector Providers

| Item | Classification | Runtime reality | Evidence |
| --- | --- | --- | --- |
| Weaviate collection and vectors | DerivedIndex | External rebuildable projection; similarity is not truth. | `WeaviateSemanticMemoryService` |
| in-process embedding/vector collection | DerivedIndex | Volatile provider for local/test use. | `InMemorySemanticMemoryService` |

## Write Paths

| Item | Classification | Runtime reality | Evidence |
| --- | --- | --- | --- |
| project summary/decision/document/plan/rule POSTs | Legacy | Direct authenticated writes exist and must be decomposed by CLN-24. | `MemoryController` |
| document archive DELETE | ProjectCanon | Lifecycle mutation exists, with artifact access guard, but lacks the full version/receipt contract required by CLN-25. | `MemoryController` |
| proposal staging create | Proposal | Append-only, staging-only, validator protected. | `SqlMemoryProposalStagingStore` |
| manual improvement execution/store | RawEvidence | Persists agent-run/proposal evidence, not memory authority. | manual API service |
| document processing and knowledge artefact apply | OperationalMemory | Internal source-derived context writes and semantic queueing. | processing/apply services |
| agent-local/collective promotion stores | Legacy | Compiled dormant paths over non-manifest schema. | agent-memory infrastructure |

## Read Paths

| Item | Classification | Runtime reality | Evidence |
| --- | --- | --- | --- |
| SQL project context/decisions/rules/summaries/plans reads | OperationalMemory | Active tenant-filtered service reads. | `ProjectMemoryService` |
| semantic and lexical memory search | DerivedIndex | Active project-scoped retrieval; CLN-26 must add actor/visibility/capability filtering before prompt use. | memory search routes/services |
| proposal list/detail/evidence reads | Proposal | Active review-only retrieval. | proposal store and Tauri review route |
| agent-local/collective retrieval | Legacy | Compiled dormant paths, not production-hosted. | agent-memory infrastructure |

## Promotion Paths

| Item | Classification | Runtime reality | Evidence |
| --- | --- | --- | --- |
| manifest-owned memory proposal staging | Proposal | Stops at proposal; cannot promote itself. | database checks and tests |
| manual improvement API | Proposal | Stops at proposal-only evidence. | API boundary response |
| `SqlCollectiveMemoryPromotionService` | Legacy | Contains a manual promotion implementation but is not hosted and relies on non-manifest schema. It is not current production capability. | production DI absence |
| direct Project Canon-shaped POSTs | Legacy | Bypass a governed promotion concept; CLN-24 must lock these down rather than relabel them promotion. | `MemoryController` |

## Reindex Paths

| Item | Classification | Runtime reality | Evidence |
| --- | --- | --- | --- |
| project memory reindex POST | Legacy | Active rebuild command available to any authenticated caller. CLN-24 must require maintenance/admin capability. | `MemoryController` |
| semantic rebuild/queue/delete operations | DerivedIndex | Provider-level rebuild lifecycle exists; CLN-27 must make source/archive/stale transitions explicit. | semantic services |
| agent memory index queue/service | Legacy | Dormant non-manifest indexing subsystem. | agent-memory indexing files |

## Retrieval Consumers

| Item | Classification | Runtime reality | Evidence |
| --- | --- | --- | --- |
| project chat pipeline, prompt builder, skill memory binder, retriever agent | SessionMemory | Active or compiled prompt/context consumers; CLN-26 owns filtering and instruction isolation. | infrastructure agents/services |
| builder, ticket generator, state review, snapshot, export, memory map | OperationalMemory | Bounded product/service consumers. | infrastructure services |
| semantic workflow/evidence providers | DerivedIndex | Retrieval and evidence projection consumers. | semantic services |

## Tests

| Item | Classification | Runtime reality | Evidence |
| --- | --- | --- | --- |
| memory governance, cannot-promote-itself, staging, authority, bleed, and prompt-context suites | RawEvidence | Executable boundary evidence; not runtime memory. | `IronDev.IntegrationTests` |
| semantic retrieval, ranking, reindex, cross-project, and release-smoke suites | RawEvidence | Executable retrieval/index evidence. | integration tests and dogfood plans |
| agent-memory schema/store/index suites | Legacy | Characterise dormant/non-manifest subsystem and often apply schema through test helpers. | agent-memory tests |

## Docs

| Item | Classification | Runtime reality | Evidence |
| --- | --- | --- | --- |
| ADR-001/002/003/007, Block K substrate, memory directory README, this audit | ProjectCanon | Current authority boundaries and current-state record. | documentation truth inventory |
| semantic-memory design, ranking/reindex/retriever notes, overall memory discussion | Legacy | Historical/future design material; not proof of current capability. | document status banners |
| dogfood knowledge copies and plans | RawEvidence | Test inputs and replay evidence, not product authority. | `tools/dogfood` |

## Receipts

| Item | Classification | Runtime reality | Evidence |
| --- | --- | --- | --- |
| PR107-116 memory proposal/governance receipts | RawEvidence | Historical evidence for proposal and non-promotion boundaries. | `Docs/receipts` |
| PR131/135/158 and safe-memory/planning receipts | RawEvidence | Historical implementation evidence; does not establish current hosting alone. | `Docs/receipts` |
| CLN-23 receipt | RawEvidence | Records this audit and its executable zero-Unknown gate. | `CLN_23_CURRENT_MEMORY_REALITY_AUDIT.md` |

## Blocking Findings for Later Phase H Slices

1. **CLN-24:** generic authenticated users can call direct Project Canon-shaped writes and reindex; route project/body scope and authority claims are not locked at the controller boundary.
2. **CLN-25:** active Project Canon-shaped rows lack the required stable identity/version/promotion-receipt lifecycle; archive is not sufficient.
3. **CLN-26:** prompt consumers need one enforced pre-prompt tenant/project/actor/visibility/status/freshness/authority/capability filter and quoted-data boundary.
4. **CLN-27:** semantic providers rebuild, but source/archive/stale/reindex transitions are not one explicit lifecycle contract.
5. **CLN-28:** existing retrieval tests and dogfood plans are not one fixed benchmark reporting all required metrics.

## Killjoy Line

Compiled memory code is not a deployed memory capability, and a retrieval hit is not Project Canon.
