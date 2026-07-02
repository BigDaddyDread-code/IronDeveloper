# H05 Receipt Table / Index Review

## Purpose

H05 reviews the current SQL receipt storage tables, indexes, and lookup/query surfaces without changing schema.

Receipt indexes improve lookup. They do not make receipts authoritative.

A fast receipt lookup is still just evidence.

## 1. Receipt Storage Inventory

Discovery rule: table name contains `Receipt`, or current store/procedure code clearly treats the table as receipt storage.

| Table name | Purpose inferred from existing code/docs | Primary key | Tenant/project/correlation/run identifiers | Created/recorded timestamp columns | Payload/reference columns | Current indexes | Current write surface | Current read/query surface | Notes / risks |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `governance.ControlledDryRunReceipt` | Durable controlled dry-run execution audit receipt. | `PK_ControlledDryRunReceipt` on `DryRunExecutionAuditId` | `ProjectId`, `ControlledDryRunRequestId`, `PolicySatisfactionId`, subject/workspace identifiers. No `TenantId` or `RunId`. | `StartedAtUtc`, `CompletedAtUtc`, `CreatedAtUtc`, `RowVersion` | `CommandAuditsJson`, `EvidenceReferencesJson`, `BoundaryMaximsJson`, hashes and subject/workspace refs | `PK_ControlledDryRunReceipt`; `UX_ControlledDryRunReceipt_Project_AuditHash`; `IX_ControlledDryRunReceipt_Project_CompletedAt`; `IX_ControlledDryRunReceipt_Project_Request`; `IX_ControlledDryRunReceipt_Project_PolicySatisfaction`; `IX_ControlledDryRunReceipt_Project_Subject`; `IX_ControlledDryRunReceipt_Project_SubjectHash`; `IX_ControlledDryRunReceipt_Project_Workspace`; `IX_ControlledDryRunReceipt_Project_WorkspaceBoundaryHash`; `IX_ControlledDryRunReceipt_Project_ValidationPlanHash`; `IX_ControlledDryRunReceipt_Project_ExecutionReportHash`; `IX_ControlledDryRunReceipt_Project_Succeeded` | `governance.usp_ControlledDryRunReceipt_Save`; `SqlControlledDryRunReceiptStore.SaveAsync` | `governance.usp_ControlledDryRunReceipt_Get`; `ListByRequest`; `ListByPolicySatisfaction`; `ListBySubject`; `ListByAuditHash`; `SqlControlledDryRunReceiptStore` read methods | Strong index coverage for current dry-run receipt store paths. Project-only chronological index exists, but no current project-list store method was found. |
| `governance.DogfoodReceipt` | Dogfood evidence receipt tied to governance event and optional related governance records. | `PK_DogfoodReceipt` on `DogfoodReceiptId` | `ProjectId`, `GovernanceEventId`, optional `CorrelationId`, optional `CausationId`, related tool/gate/approval/policy IDs. No `TenantId` or `RunId`. | `CreatedUtc` | `EvidenceJson`, `Summary`, related governance IDs | `PK_DogfoodReceipt`; `IX_DogfoodReceipt_Project_CreatedUtc`; `IX_DogfoodReceipt_Subject_CreatedUtc`; `IX_DogfoodReceipt_Correlation_CreatedUtc`; `IX_DogfoodReceipt_GovernanceEventId`; `IX_DogfoodReceipt_RelatedToolRequest`; `IX_DogfoodReceipt_RelatedToolGateDecision`; `IX_DogfoodReceipt_RelatedApprovalDecision`; `IX_DogfoodReceipt_RelatedPolicyDecisionEvent`; `IX_DogfoodReceipt_Project_Outcome_CreatedUtc` | `governance.usp_DogfoodReceipt_Record`; `SqlDogfoodReceiptStore.RecordAsync` | `governance.usp_DogfoodReceipt_GetById`; `ListForSubject`; `ListForProject`; `ListForCorrelation`; `SqlDogfoodReceiptStore` read methods | Good support for subject/project/correlation and related-record diagnostics. `CausationId` exists without a current list procedure/index. |
| `governance.RollbackExecutionReceipt` | Durable rollback execution receipt after controlled rollback attempt. | `PK_RollbackExecutionReceipt` on `RollbackExecutionReceiptId` | `ProjectId`, rollback plan/support/source-apply/patch references. No `TenantId` or `RunId`. | `RolledBackAtUtc`, `StoredAtUtc`, `RowVersion` | `FileResultsJson`, `IssueCodesJson`, `EvidenceReferencesJson`, `BoundaryMaximsJson`, hashes | `PK_RollbackExecutionReceipt`; `UX_RollbackExecutionReceipt_Project_Hash`; `IX_RollbackExecutionReceipt_Project_SourceApplyReceipt`; `IX_RollbackExecutionReceipt_Project_RollbackPlan`; `IX_RollbackExecutionReceipt_Project_RollbackSupport`; `IX_RollbackExecutionReceipt_Project_PatchArtifact` | `governance.usp_RollbackExecutionReceipt_Save`; `SqlRollbackExecutionReceiptStore.SaveAsync` | `governance.usp_RollbackExecutionReceipt_Get`; `GetByReceiptHash`; `ListBySourceApplyReceipt`; `ListByRollbackPlan`; `ListByRollbackSupportReceipt`; `ListByPatchArtifact`; `SqlRollbackExecutionReceiptStore` read methods | Indexes align with current lookup methods and chronological rollback inspection by key. |
| `governance.RollbackSupportReceipt` | Rollback-support readiness receipt before rollback execution. | `PK_RollbackSupportReceipt` on `RollbackSupportReceiptId` | `ProjectId`, rollback plan, patch artifact/hash, policy satisfaction, controlled dry-run, subject, baseline/branch refs. No `TenantId` or `RunId`. | `CreatedAtUtc`, `ExpiresAtUtc`, `StoredAtUtc`, `RowVersion` | `EvidenceReferencesJson`, `BoundaryMaximsJson`, hashes, source snapshot refs | `PK_RollbackSupportReceipt`; `UX_RollbackSupportReceipt_Project_Hash`; `UX_RollbackSupportReceipt_Project_RollbackPlan`; `IX_RollbackSupportReceipt_Project_PatchArtifact`; `IX_RollbackSupportReceipt_Project_PatchHash`; `IX_RollbackSupportReceipt_Project_RollbackPlanHash`; `IX_RollbackSupportReceipt_Project_SourceBaselineHash`; `IX_RollbackSupportReceipt_Project_ExpectedBranch` | `governance.usp_RollbackSupportReceipt_Save`; `SqlRollbackSupportReceiptStore.SaveAsync` | `governance.usp_RollbackSupportReceipt_Get`; `GetByReceiptHash`; `ListByPatchArtifact`; `ListByPatchHash`; `ListByRollbackPlan`; `ListBySourceBaselineHash`; `SqlRollbackSupportReceiptStore` read methods | Current lookup paths are mostly supported; `ExpectedBranch` has an index but no current read method found. |
| `governance.SourceApplyDryRunReceipt` | Source-apply dry-run receipt used before actual source apply. | `PK_SourceApplyDryRunReceipt` on `SourceApplyDryRunReceiptId` | `ProjectId`, source-apply request/gate, patch artifact, rollback support refs. No `TenantId` or `RunId`. | `CreatedAtUtc`, `ExpiresAtUtc`, `StoredAtUtc`, `RowVersion` | `FileResultsJson`, `EvidenceReferencesJson`, `BoundaryMaximsJson`, hashes | `PK_SourceApplyDryRunReceipt`; `UX_SourceApplyDryRunReceipt_Project_Hash`; `IX_SourceApplyDryRunReceipt_Project_SourceApplyRequest`; `IX_SourceApplyDryRunReceipt_Project_Gate`; `IX_SourceApplyDryRunReceipt_Project_PatchArtifact`; `IX_SourceApplyDryRunReceipt_Project_RollbackSupport` | `governance.usp_SourceApplyDryRunReceipt_Save`; `SqlSourceApplyDryRunReceiptStore.SaveAsync` | `governance.usp_SourceApplyDryRunReceipt_Get`; `GetByReceiptHash`; `ListBySourceApplyRequest`; `ListBySourceApplyGateEvaluation`; `ListByPatchArtifact`; `ListByRollbackSupportReceipt`; `SqlSourceApplyDryRunReceiptStore` read methods | Indexes align with the current store/query service lookup methods. |
| `governance.SourceApplyReceipt` | Durable controlled source-apply receipt after working-tree mutation attempt. | `PK_SourceApplyReceipt` on `SourceApplyReceiptId` | `ProjectId`, controlled/source-apply request, dry-run receipt, gate, patch artifact, rollback support refs. No `TenantId` or `RunId`. | `AppliedAtUtc`, `StoredAtUtc`, `RowVersion` | `FileResultsJson`, `IssueCodesJson`, `EvidenceReferencesJson`, `BoundaryMaximsJson`, hashes | `PK_SourceApplyReceipt`; `UX_SourceApplyReceipt_Project_Hash`; `IX_SourceApplyReceipt_Project_SourceApplyRequest`; `IX_SourceApplyReceipt_Project_DryRunReceipt`; `IX_SourceApplyReceipt_Project_PatchArtifact`; `IX_SourceApplyReceipt_Project_RollbackSupport` | `governance.usp_SourceApplyReceipt_Save`; `SqlSourceApplyReceiptStore.SaveAsync` | `governance.usp_SourceApplyReceipt_Get`; `GetByReceiptHash`; `ListBySourceApplyRequest`; `ListBySourceApplyDryRunReceipt`; `ListByPatchArtifact`; `ListByRollbackSupportReceipt`; `SqlSourceApplyReceiptStore` read methods | Indexes align with current source-apply receipt lookup methods. |

## 2. Receipt Lookup Paths

Only lookup paths supported by current stored procedures, store methods, or tests are listed as current.

| Lookup category | Current evidence | Current support |
| --- | --- | --- |
| By receipt ID | `usp_*Receipt_Get`, `usp_DogfoodReceipt_GetById`; `GetAsync` methods on receipt stores | Supported by clustered primary keys and project predicates for all non-dogfood receipt stores. Dogfood get-by-id uses the receipt ID primary key directly. |
| By project | `usp_DogfoodReceipt_ListForProject`; `IX_DogfoodReceipt_Project_CreatedUtc` | Supported for `DogfoodReceipt`. Other receipt tables usually require a more specific project-scoped lookup key. |
| By operation ID | No explicit canonical `OperationId` receipt-table column or current receipt procedure was found. | Unsupported for H05. Operation-to-receipt linkage appears to happen through domain refs such as request IDs, patch artifact IDs, rollback plan IDs, and source-apply receipt IDs. |
| By run ID | No `RunId` receipt-table column or current receipt procedure was found. | Unsupported for H05. |
| By tool request / action request | `DogfoodReceipt` has `RelatedToolRequestId` and `IX_DogfoodReceipt_RelatedToolRequest`; source-apply/dry-run/controlled-dry-run receipts have request IDs and request-list procedures such as `usp_SourceApplyReceipt_ListBySourceApplyRequest` | Supported for controlled/source-apply request-shaped lookups. Dogfood related-tool indexes support diagnostics, but no direct dogfood list-by-tool-request procedure was found. |
| By correlation / causation | `usp_DogfoodReceipt_ListForCorrelation`; `IX_DogfoodReceipt_Correlation_CreatedUtc`; `DogfoodReceipt.CausationId` | Correlation lookup is supported for dogfood receipts. Causation is stored but has no current receipt list procedure/index in H05. |
| By created timestamp | `ListForProject`, `ListForSubject`, and receipt-specific list procedures order by `CreatedUtc`, `CreatedAtUtc`, `AppliedAtUtc`, `CompletedAtUtc`, or `RolledBackAtUtc` | Supported where list procedures exist. H05 does not measure performance. |
| Latest receipts for a project/run | `usp_DogfoodReceipt_ListForProject` supports project-level latest dogfood receipts | PartiallySupported. No run-level receipt lookup exists. |
| Diagnostic investigation lookup | `GetByReceiptHash`, `ListByPatchArtifact`, `ListByPatchHash`, `ListByRollbackPlan`, `ListBySourceApplyRequest`, `ListBySourceApplyDryRunReceipt`, `ListByRollbackSupportReceipt`, `ListBySourceBaselineHash`, `ListBySubject`, `ListByAuditHash`, and `usp_RollbackExecutionReceipt_ListBySourceApplyReceipt` | Supported across specific receipt stores. Some indexed diagnostic relations do not yet have direct stored procedures. |

## 3. Index Support Review

Finding labels used here are `Supported`, `PartiallySupported`, `Unsupported`, `Unclear`, and `NotApplicable`.

| Table | Index support review |
| --- | --- |
| `governance.ControlledDryRunReceipt` | `Supported` for current get/list methods by ID, audit hash, request, policy satisfaction, subject, and related diagnostic hashes. `PartiallySupported` for project-wide chronological inspection because an index exists but no current project-list procedure was found. |
| `governance.DogfoodReceipt` | `Supported` for get-by-id, project, subject, correlation, outcome, governance event, and related governance record diagnostics. `PartiallySupported` for causation because the column exists but no current causation list procedure/index was found. |
| `governance.RollbackExecutionReceipt` | `Supported` for current get/hash/source-apply/rollback-plan/rollback-support/patch-artifact list paths. |
| `governance.RollbackSupportReceipt` | `Supported` for current get/hash/rollback-plan/patch-artifact/patch-hash/source-baseline list paths. `PartiallySupported` for branch diagnostics because an index exists but no current branch-list procedure was found. |
| `governance.SourceApplyDryRunReceipt` | `Supported` for current get/hash/source-apply-request/gate/patch-artifact/rollback-support list paths. |
| `governance.SourceApplyReceipt` | `Supported` for current get/hash/source-apply-request/dry-run-receipt/patch-artifact/rollback-support list paths. |

H05 does not claim runtime performance improvement. It only records that current indexes appear shaped for current lookup predicates and orderings.

## 4. Tenant / Project Isolation Review

Every discovered receipt table includes `ProjectId`.

No discovered receipt table includes `TenantId`.

Current receipt procedures and store methods generally scope reads by `ProjectId`, except `DogfoodReceipt` get-by-id, which uses the receipt primary key directly. `DogfoodReceipt` also validates related governance records against the same project on insert.

H05 records this as review evidence only. It does not add `TenantId`, alter scoping, or claim table-level tenant isolation is complete.

## 5. UTC Timestamp Review

Receipt timestamp columns are UTC-oriented by name and type:

- `CreatedUtc` on `DogfoodReceipt`.
- `StartedAtUtc`, `CompletedAtUtc`, and `CreatedAtUtc` on `ControlledDryRunReceipt`.
- `CreatedAtUtc`, `ExpiresAtUtc`, and `StoredAtUtc` on `SourceApplyDryRunReceipt` and `RollbackSupportReceipt`.
- `AppliedAtUtc` and `StoredAtUtc` on `SourceApplyReceipt`.
- `RolledBackAtUtc` and `StoredAtUtc` on `RollbackExecutionReceipt`.

The columns use `DATETIMEOFFSET(7)` and defaults such as `SYSUTCDATETIME()` where the table owns storage time.

H05 does not add UTC constraints. H09 owns UTC timestamp DB constraint review.

## 6. Payload / Raw Evidence Risk Review

Receipt tables contain JSON or large text payload/reference columns:

- `EvidenceJson` on `DogfoodReceipt`.
- `CommandAuditsJson`, `EvidenceReferencesJson`, `BoundaryMaximsJson`, and `BoundaryText` on `ControlledDryRunReceipt`.
- `FileResultsJson`, `IssueCodesJson`, `EvidenceReferencesJson`, `BoundaryMaximsJson`, and `BoundaryText` on source-apply and rollback execution receipts.
- `EvidenceReferencesJson`, `BoundaryMaximsJson`, and `BoundaryText` on rollback support receipts.

Current insert triggers include unsafe-marker checks for raw/private material and authority claims, but H05 does not implement retention or redaction. H10/H11 own retention/redaction review.

## 7. Findings

| Finding ID | Severity | Table/procedure affected | Evidence | Risk | Recommended follow-up slice | Why not fixed in H05 |
| --- | --- | --- | --- | --- | --- | --- |
| H05-INFO-001 | Info | All discovered receipt tables | Current indexes align with current receipt get/list methods for primary IDs, hashes, request IDs, patch IDs, rollback refs, subject/project, and correlation paths. | Lookup shape is documented but not performance-measured. | None unless runtime evidence shows pressure. | H05 is review/test/receipt only. |
| H05-LOW-001 | Low | All discovered receipt tables | Tables use `ProjectId`; no discovered receipt table has `TenantId`. | Tenant isolation relies on project scoping/upstream context rather than table-level tenant columns. | Future tenant-scope DB hardening slice if product tenancy requires table-level `TenantId`. | H05 does not alter tables. |
| H05-LOW-002 | Low | `governance.DogfoodReceipt` | `CausationId` is stored, but no current causation list procedure/index was found. | Causation-based diagnostics may need broader scans or custom query work later. | Future receipt diagnostic lookup slice if causation investigation becomes a current workflow. | H05 records lookup gaps only. |
| H05-LOW-003 | Low | `governance.ControlledDryRunReceipt`; `governance.RollbackSupportReceipt` | Project/branch-style indexes exist without matching current public list procedures for every indexed path. | Some indexes may be ahead of current read surface or reserved for diagnostics. | Future index/use review after more read-model traffic exists. | H05 does not add or remove indexes. |
| H05-MEDIUM-001 | Medium | Receipt JSON/text payload columns | Receipt tables store JSON/text evidence or file/command result material. Insert triggers reject known unsafe markers, but retention/redaction policy is outside H05. | Long-lived raw-ish evidence material may require explicit retention/redaction review. | H10/H11 retention/redaction review. | H05 does not implement retention or redaction. |

## 8. Non-Authority Boundary

Receipt storage and receipt indexes are evidence infrastructure.

A receipt row is not approval.

A receipt row is not policy satisfaction.

A receipt row is not source-apply authority.

A receipt row is not workflow continuation authority.

A receipt row is not merge readiness.

A receipt row is not release readiness.

A receipt row is not deployment readiness.

A receipt index is not authority.

A fast receipt lookup is not authority.

A receipt table review is not schema hardening.

A receipt table review is not retention policy.

A receipt table review is not redaction policy.

A receipt table review is evidence about storage shape only.

SQL remains source of truth.

Weaviate is rebuildable.

Read models may be rebuildable.

Authority records cannot be vibes.

Receipt indexes improve lookup only.

A fast receipt lookup is still just evidence.
