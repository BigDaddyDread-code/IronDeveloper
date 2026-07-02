# H06 Evidence Table / Index Review

## Purpose

H06 reviews the current SQL evidence-reference tables, indexes, and lookup/query surfaces without changing schema.

Evidence indexes improve retrieval. They do not make evidence authoritative.

Fast evidence is still just evidence.

## 1. Evidence Storage Inventory

Discovery rule: table name contains `Evidence`, and current store/procedure code treats the table as dedicated evidence-reference storage rather than a receipt payload column.

Adjacent grounding/reference tables are noted where they share the same read path, but H06 does not broaden the reviewed table set beyond dedicated evidence-reference tables.

| Table name | Purpose inferred from existing code/docs | Primary key | Tenant/project/correlation/run identifiers | Created/recorded timestamp columns | Payload/reference columns | Current indexes | Current write surface | Current read/query surface | Notes / risks |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `agent.AgentLocalMemoryEvidenceRef` | Evidence references attached to agent local memory items. | `PK_AgentLocalMemoryEvidenceRef` on `EvidenceRefRowId` | No direct `ProjectId` or `TenantId`; scope is inherited through `MemoryItemId` to `agent.AgentLocalMemoryItem`. | `CapturedAtUtc` | `EvidenceId`, `EvidenceType`, `SourceId`, `SourceUri`, `Summary` | `PK_AgentLocalMemoryEvidenceRef`; `UQ_AgentLocalMemoryEvidenceRef_MemoryItem_Evidence`; `IX_AgentLocalMemoryEvidenceRef_MemoryItem` | `agent.usp_AgentLocalMemory_Create`; `SqlAgentLocalMemoryStore` | `SqlAgentLocalMemoryStore` reads evidence refs by memory item after memory-item queries | Current lookup is parent-memory-item scoped. Direct evidence-ID search is not a current store path. |
| `a2a.AgentHandoffEvidenceReference` | Evidence references attached to agent handoff records. | `PK_AgentHandoffEvidenceReference` on `AgentHandoffEvidenceReferenceId` | `ProjectId`, `AgentHandoffId`, optional `GovernanceEventId`. No `TenantId`. | `CreatedUtc` | `EvidenceType`, `EvidenceId`, `EvidenceLabel`, `EvidenceSummary` | `PK_AgentHandoffEvidenceReference`; `IX_AgentHandoffEvidenceReference_Handoff` | `a2a.usp_AgentHandoff_Create`; `SqlAgentHandoffStore` | `a2a.usp_AgentHandoff_Get`; list procedures that return handoff rows plus evidence counts; `SqlAgentHandoffStore` materializes refs by handoff | Supported for parent handoff retrieval. No direct evidence-ID lookup path was found. |
| `a2a.AgentHandoffEvidenceAllowedUse` | Allowed-use rows for handoff evidence references. | `PK_AgentHandoffEvidenceAllowedUse` on `AgentHandoffEvidenceAllowedUseId` | `ProjectId`, `AgentHandoffId`, `AgentHandoffEvidenceReferenceId`. No `TenantId`. | `CreatedUtc` | `AllowedUse` | `PK_AgentHandoffEvidenceAllowedUse`; `UQ_AgentHandoffEvidenceAllowedUse_Reference_Use`; `IX_AgentHandoffEvidenceAllowedUse_Reference` | `a2a.usp_AgentHandoff_Create`; `SqlAgentHandoffStore` | `a2a.usp_AgentHandoff_Get`; `SqlAgentHandoffStore` groups allowed uses by evidence reference | Supported for parent evidence-reference materialization. |
| `workflow.WorkflowRunEvidenceReference` | Evidence references attached to workflow runs and optional run steps. | `PK_WorkflowRunEvidenceReference` on `WorkflowRunEvidenceReferenceId` | `ProjectId`, `WorkflowRunId`, optional `WorkflowRunStepId`, optional `GovernanceEventId`, optional `AgentHandoffId`, optional `ThoughtLedgerEntryId`, optional `GroundingEvidenceReferenceId`. No `TenantId`. | `CreatedUtc` | `EvidenceType`, `EvidenceId`, `EvidenceLabel`, `SafeSummary`, `AllowedUse` | `PK_WorkflowRunEvidenceReference`; `IX_WorkflowRunEvidenceReference_Run` | `workflow.usp_WorkflowRun_Create`; `workflow.usp_WorkflowStep_Create`; `SqlWorkflowRunStore`; `SqlWorkflowStepStore` | `workflow.usp_WorkflowRun_Get`; workflow run/step store materialization by run | Supported for run-scoped evidence replay. No direct evidence-ID lookup path was found. |
| `workflow.WorkflowCheckpointEvidenceReference` | Evidence references captured at workflow checkpoints. | `PK_WorkflowCheckpointEvidenceReference` on `WorkflowCheckpointEvidenceReferenceId` | `ProjectId`, `WorkflowCheckpointId`, `WorkflowRunId`, optional `WorkflowRunStepId`, optional `GovernanceEventId`, optional handoff/thought/grounding/workflow-run evidence refs. No `TenantId`. | `CreatedUtc` | `EvidenceType`, `EvidenceId`, `EvidenceLabel`, `SafeSummary`, `AllowedUse` | `PK_WorkflowCheckpointEvidenceReference` | `workflow.usp_WorkflowCheckpoint_Create`; `SqlWorkflowCheckpointStore` | `workflow.usp_WorkflowCheckpoint_Get`; checkpoint list methods return checkpoints, while get materializes evidence refs | Parent checkpoint primary-key lookup is supported. No nonclustered parent lookup index exists directly on this table; checkpoint get reads by `WorkflowCheckpointId`. |
| `memory.MemoryProposalEvidenceReference` | Evidence references attached to staged memory proposals. | `PK_MemoryProposalEvidenceReference` on `MemoryProposalEvidenceReferenceId` | `ProjectId`, `MemoryProposalId`, optional `GovernanceEventId`, optional workflow/checkpoint/handoff/thought refs. No direct `TenantId`; parent proposal has optional `TenantId`. | `CreatedUtc` | `EvidenceType`, `EvidenceId`, `EvidenceLabel`, `SafeSummary`, `AllowedUse` | `PK_MemoryProposalEvidenceReference`; `IX_MemoryProposalEvidenceReference_Proposal` | `memory.usp_MemoryProposal_Create`; `SqlMemoryProposalStagingStore` | `memory.usp_MemoryProposal_Get`; list methods return proposal counts; `SqlMemoryProposalStagingStore` materializes refs by proposal | Supported for proposal-scoped evidence review. No direct evidence-ID lookup path was found. |

## 2. Evidence Lookup Paths

Only lookup paths supported by current stored procedures, store methods, or tests are listed as current.

| Lookup category | Current evidence | Current support |
| --- | --- | --- |
| By parent memory item | `SqlAgentLocalMemoryStore` reads `agent.AgentLocalMemoryEvidenceRef` by `MemoryItemId`; `IX_AgentLocalMemoryEvidenceRef_MemoryItem` | Supported. |
| By handoff | `a2a.usp_AgentHandoff_Get` returns evidence references and allowed uses; `IX_AgentHandoffEvidenceReference_Handoff`; `IX_AgentHandoffEvidenceAllowedUse_Reference` | Supported. |
| By workflow run | `workflow.usp_WorkflowRun_Get`; `workflow.usp_WorkflowStep_Get`; `IX_WorkflowRunEvidenceReference_Run`; workflow run/step stores | Supported for run-scoped evidence replay. |
| By workflow checkpoint | `workflow.usp_WorkflowCheckpoint_Get`; `SqlWorkflowCheckpointStore`; `PK_WorkflowCheckpointEvidenceReference` | PartiallySupported. Checkpoint get materializes refs, but there is no direct nonclustered index on `WorkflowCheckpointId` in the evidence table. |
| By memory proposal | `memory.usp_MemoryProposal_Get`; `IX_MemoryProposalEvidenceReference_Proposal`; `SqlMemoryProposalStagingStore` | Supported. |
| By allowed use | `a2a.AgentHandoffEvidenceAllowedUse`; `AllowedUse` columns on workflow/memory evidence refs | PartiallySupported. Allowed-use values are stored and constrained. Dedicated allowed-use lookup is current for handoff materialization only. |
| By evidence ID | Evidence tables store `EvidenceId`; `agent.AgentLocalMemoryEvidenceRef` has unique `(MemoryItemId, EvidenceId)` | PartiallySupported. Evidence IDs are stored for diagnostics, but current dedicated lookup paths are parent-scoped rather than global evidence-ID search. |
| Adjacent grounding reference | `workflow.WorkflowRunGroundingReference`, `workflow.WorkflowCheckpointGroundingReference`, and `memory.MemoryProposalGroundingReference` | NotApplicable to the dedicated evidence-table inventory. H06 records them as adjacent traceability surfaces only. |

## 3. Index Support Review

Finding labels used here are `Supported`, `PartiallySupported`, `Unsupported`, `Unclear`, and `NotApplicable`.

| Table | Index support review |
| --- | --- |
| `agent.AgentLocalMemoryEvidenceRef` | `Supported` for current parent memory-item materialization. `PartiallySupported` for direct evidence-ID diagnostics because the unique index is scoped by memory item, not global evidence ID. |
| `a2a.AgentHandoffEvidenceReference` | `Supported` for current handoff get/materialization paths. |
| `a2a.AgentHandoffEvidenceAllowedUse` | `Supported` for current allowed-use grouping by evidence reference. |
| `workflow.WorkflowRunEvidenceReference` | `Supported` for current workflow-run evidence replay by run. |
| `workflow.WorkflowCheckpointEvidenceReference` | `PartiallySupported` because primary-key access exists, but no direct nonclustered parent-checkpoint evidence-reference index was found. |
| `memory.MemoryProposalEvidenceReference` | `Supported` for current memory-proposal evidence materialization. |

H06 does not claim runtime performance improvement. It only records current metadata shape.

## 4. Tenant / Project Isolation Review

Most discovered evidence-reference tables include `ProjectId`.

No discovered evidence-reference table includes a direct non-null `TenantId`.

`agent.AgentLocalMemoryEvidenceRef` has no direct `ProjectId` or `TenantId`; it inherits scope through `agent.AgentLocalMemoryItem`.

`memory.MemoryProposalEvidenceReference` includes `ProjectId`, while optional tenant context is carried by the parent `memory.MemoryProposal`.

H06 records this as review evidence only. It does not add `TenantId`, alter scoping, or claim table-level tenant isolation is complete.

## 5. UTC Timestamp Review

Evidence-reference timestamp columns are UTC-oriented by name and type:

- `CapturedAtUtc` on `agent.AgentLocalMemoryEvidenceRef`.
- `CreatedUtc` on `a2a.AgentHandoffEvidenceReference`.
- `CreatedUtc` on `a2a.AgentHandoffEvidenceAllowedUse`.
- `CreatedUtc` on `workflow.WorkflowRunEvidenceReference`.
- `CreatedUtc` on `workflow.WorkflowCheckpointEvidenceReference`.
- `CreatedUtc` on `memory.MemoryProposalEvidenceReference`.

The columns use `DATETIMEOFFSET(7)` and defaults such as `SYSUTCDATETIME()` where the table owns storage time.

H06 does not add UTC constraints. H09 owns UTC timestamp DB constraint review.

## 6. Payload / Retention / Artifact Risk Review

The reviewed tables store evidence references, labels, summaries, source IDs, source URIs, and allowed-use values. They do not store raw evidence artifact payloads as a dedicated blob store.

The following columns can still carry sensitive or high-retention metadata if upstream sanitization fails:

- `SourceUri` and `Summary` on `agent.AgentLocalMemoryEvidenceRef`.
- `EvidenceLabel` and `EvidenceSummary` on `a2a.AgentHandoffEvidenceReference`.
- `EvidenceLabel`, `SafeSummary`, and `AllowedUse` on workflow and memory-proposal evidence-reference tables.

Current triggers/check constraints reject known raw/private/authority markers on several parent/evidence-reference surfaces, but H06 does not implement retention, redaction, artifact retention, or evidence payload lifecycle.

Evidence artifact retention is later work.

## 7. Findings

| Finding ID | Severity | Table/procedure affected | Evidence | Risk | Recommended follow-up slice | Why not fixed in H06 |
| --- | --- | --- | --- | --- | --- | --- |
| H06-INFO-001 | Info | All discovered evidence-reference tables | Current indexes align with current parent-scoped evidence materialization paths for memory items, handoffs, workflow runs, and memory proposals. | Lookup shape is documented but not performance-measured. | None unless runtime evidence shows pressure. | H06 is review/test/receipt only. |
| H06-LOW-001 | Low | All discovered evidence-reference tables | Most evidence tables use `ProjectId`; none has direct non-null `TenantId`; local memory evidence inherits scope through the parent memory item. | Tenant isolation relies on parent/project scoping rather than table-level tenant columns. | Future tenant-scope DB hardening slice if product tenancy requires table-level `TenantId`. | H06 does not alter tables. |
| H06-LOW-002 | Low | Evidence-ID diagnostic lookup | Evidence IDs are stored, but current direct lookup paths are parent-scoped. | Future evidence resolver/debugging flows may need global evidence-ID lookup support or explicit read-model projection. | Future evidence diagnostic lookup slice if evidence-ID investigation becomes a current workflow. | H06 records lookup gaps only. |
| H06-LOW-003 | Low | `workflow.WorkflowCheckpointEvidenceReference` | The table has a primary key but no direct nonclustered checkpoint-parent index in the current metadata. | Checkpoint evidence materialization may rely on parent checkpoint retrieval and table scan/lookup shape for refs. | Future workflow checkpoint evidence lookup review if runtime pressure appears. | H06 does not add indexes. |
| H06-MEDIUM-001 | Medium | Evidence label/summary/source metadata columns | Evidence tables store source IDs, URIs, labels, summaries, and allowed-use metadata. Some surfaces have unsafe-marker checks, but retention/redaction policy is outside H06. | Long-lived evidence metadata and artifact references may require explicit retention/redaction review. | H10/H11 retention/redaction review and a future evidence artifact retention slice. | H06 does not implement retention, redaction, or artifact lifecycle. |

## 8. Non-Authority Boundary

Evidence storage and evidence indexes are retrieval infrastructure.

An evidence row is not approval.

An evidence row is not policy satisfaction.

An evidence row is not source-apply authority.

An evidence row is not workflow continuation authority.

An evidence row is not merge readiness.

An evidence row is not release readiness.

An evidence row is not deployment readiness.

An evidence index is not authority.

Fast evidence retrieval is not authority.

Evidence existence is not evidence truth.

Evidence retrieval is not evidence validation.

An evidence table review is not schema hardening.

An evidence table review is not retention policy.

An evidence table review is not redaction policy.

An evidence table review is evidence about storage shape only.

SQL remains source of truth.

Weaviate is rebuildable.

Read models may be rebuildable.

Authority records cannot be vibes.

Evidence indexes improve retrieval only.

Fast evidence is still just evidence.

## 9. Next Intended Slice

H07 - Operation status projection indexes.

Review line: Status indexes improve display and investigation. They do not make status authoritative.

Killjoy: Fast status is still not permission.
