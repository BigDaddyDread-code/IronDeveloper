# H09 UTC Timestamp DB Constraint Review

## Purpose

H09 reviews current database timestamp columns, defaults, constraints, and stored-procedure write patterns without changing schema.

UTC timestamps make time comparable. They do not make records authoritative.

A correctly timed lie is still a lie.

## 1. Review Basis

H09 compares current SQL metadata against the repository UTC standard:

- Persist UTC.
- Transmit UTC.
- Display UTC-aware dates.

Product timestamps should be UTC. Database defaults should use UTC sources such as `SYSUTCDATETIME()`. Local-only timestamp behavior is unsafe for product/audit evidence because cross-machine and cross-timezone ordering becomes ambiguous.

H09 is DB metadata review only. It does not expand into API formatting, UI display, client timezone behavior, or timestamp-writing behavior.

## 2. Timestamp Column Inventory

Discovery includes date/time SQL types and broad timestamp-like names: `Utc`, `Created`, `Updated`, `Started`, `Completed`, `Finished`, `Recorded`, `Observed`, `Applied`, `Stored`, `Expires`, `RolledBack`, `Imported`, and `Indexed`.

That broad rule intentionally catches non-timestamp fields such as `CreatedByActorId`, `ObservedBranch`, and `IndexedFileCount`. Those rows are recorded as `NotApplicable` rather than silently excluded.

Schemas reviewed: `dbo`, `governance`, `workflow`, `a2a`, `agent`, and `memory`.

Timestamp-like columns discovered: 135.

| Column | Type | Nullable | Default | Check | Classification |
| --- | --- | --- | --- | --- | --- |
| `a2a.AgentHandoff.CreatedByActorType` | `nvarchar` | NO | `none` | present | `NotApplicable` |
| `a2a.AgentHandoff.CreatedByActorId` | `nvarchar` | NO | `none` | present | `NotApplicable` |
| `a2a.AgentHandoff.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `a2a.AgentHandoffConstraint.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `a2a.AgentHandoffEvidenceAllowedUse.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `a2a.AgentHandoffEvidenceReference.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `agent.AgentLocalMemoryEvent.CreatedAtUtc` | `datetime2` | NO | `none` | absent | `UtcParameterOrProcedureDependent` |
| `agent.AgentLocalMemoryEvent.CreatedByAgentId` | `nvarchar` | YES | `none` | absent | `NotApplicable` |
| `agent.AgentLocalMemoryEvent.CreatedByUserId` | `nvarchar` | YES | `none` | absent | `NotApplicable` |
| `agent.AgentLocalMemoryEvidenceRef.CapturedAtUtc` | `datetime2` | YES | `none` | absent | `UtcParameterOrProcedureDependent` |
| `agent.AgentLocalMemoryItem.CreatedAtUtc` | `datetime2` | NO | `none` | absent | `UtcParameterOrProcedureDependent` |
| `agent.AgentLocalMemoryItem.ExpiresAtUtc` | `datetime2` | YES | `none` | absent | `UtcParameterOrProcedureDependent` |
| `dbo.ArtifactSourceReferences.CreatedUtc` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.ArtifactSourceReferences.CreatedBy` | `nvarchar` | YES | `none` | absent | `NotApplicable` |
| `dbo.ChatMessageFeedback.CreatedDate` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.ChatMessages.CreatedDate` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.ChatTurnClarifications.CreatedUtc` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.ChatTurnGovernance.CreatedUtc` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.ChatTurnTraces.CreatedUtc` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.CodeIndexEntries.CreatedDate` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.EmbeddingJobs.CreatedUtc` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.EmbeddingJobs.StartedUtc` | `datetime2` | YES | `none` | absent | `UtcParameterOrProcedureDependent` |
| `dbo.EmbeddingJobs.CompletedUtc` | `datetime2` | YES | `none` | absent | `UtcParameterOrProcedureDependent` |
| `dbo.ProjectChatSessions.CreatedDate` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.ProjectChatSessions.UpdatedDate` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.ProjectCommands.CreatedUtc` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.ProjectCommands.UpdatedUtc` | `datetime2` | YES | `none` | absent | `UtcParameterOrProcedureDependent` |
| `dbo.ProjectContextDocuments.CreatedDate` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.ProjectContextDocuments.UpdatedDate` | `datetime2` | YES | `none` | absent | `LegacyAssumedUtc` |
| `dbo.ProjectDecisions.CreatedDate` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.ProjectDocumentLinks.CreatedAtUtc` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.ProjectDocumentLinks.CreatedBy` | `nvarchar` | YES | `none` | absent | `NotApplicable` |
| `dbo.ProjectDocuments.CreatedAtUtc` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.ProjectDocuments.UpdatedAtUtc` | `datetime2` | YES | `none` | absent | `UtcParameterOrProcedureDependent` |
| `dbo.ProjectDocuments.CreatedBy` | `nvarchar` | YES | `none` | absent | `NotApplicable` |
| `dbo.ProjectDocuments.UpdatedBy` | `nvarchar` | YES | `none` | absent | `NotApplicable` |
| `dbo.ProjectDocumentVersions.CreatedAtUtc` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.ProjectDocumentVersions.CreatedBy` | `nvarchar` | YES | `none` | absent | `NotApplicable` |
| `dbo.ProjectFiles.LastIndexedDate` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.ProjectFiles.LastIndexedUtc` | `datetime2` | YES | `none` | absent | `UtcParameterOrProcedureDependent` |
| `dbo.ProjectImplementationPlans.CreatedDate` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.ProjectImplementationPlans.UpdatedDate` | `datetime2` | YES | `none` | absent | `LegacyAssumedUtc` |
| `dbo.ProjectObservableStates.UpdatedDate` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.ProjectProfiles.CreatedUtc` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.ProjectProfiles.UpdatedUtc` | `datetime2` | YES | `none` | absent | `UtcParameterOrProcedureDependent` |
| `dbo.ProjectRules.CreatedDate` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.ProjectRules.UpdatedDate` | `datetime2` | YES | `none` | absent | `LegacyAssumedUtc` |
| `dbo.Projects.CreatedDate` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.Projects.UpdatedDate` | `datetime2` | YES | `none` | absent | `LegacyAssumedUtc` |
| `dbo.Projects.LastIndexedUtc` | `datetime2` | YES | `none` | absent | `UtcParameterOrProcedureDependent` |
| `dbo.Projects.IndexedFileCount` | `int` | YES | `none` | absent | `NotApplicable` |
| `dbo.ProjectSummaries.CreatedDate` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.ProjectSummaries.UpdatedDate` | `datetime2` | YES | `none` | absent | `LegacyAssumedUtc` |
| `dbo.ProjectTickets.CreatedDate` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.RunEvents.TimestampUtc` | `datetime2` | NO | `none` | absent | `UtcParameterOrProcedureDependent` |
| `dbo.RunEvents.CreatedUtc` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.Runs.CreatedUtc` | `datetime2` | NO | `none` | absent | `UtcParameterOrProcedureDependent` |
| `dbo.Runs.UpdatedUtc` | `datetime2` | NO | `none` | absent | `UtcParameterOrProcedureDependent` |
| `dbo.Runs.StartedUtc` | `datetime2` | YES | `none` | absent | `UtcParameterOrProcedureDependent` |
| `dbo.Runs.CompletedUtc` | `datetime2` | YES | `none` | absent | `UtcParameterOrProcedureDependent` |
| `dbo.SemanticArtefacts.CreatedUtc` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.SemanticArtefacts.UpdatedUtc` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.SemanticChunks.EmbeddedAtUtc` | `datetime2` | YES | `none` | absent | `UtcParameterOrProcedureDependent` |
| `dbo.SemanticChunks.CreatedUtc` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.SemanticSearchTraces.CreatedUtc` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.Tenants.CreatedDate` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.TenantUsers.CreatedDate` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `dbo.Users.CreatedDate` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `governance.AcceptedApproval.AcceptedAtUtc` | `datetimeoffset` | NO | `none` | present | `UtcParameterOrProcedureDependent` |
| `governance.AcceptedApproval.ExpiresAtUtc` | `datetimeoffset` | YES | `none` | present | `UtcParameterOrProcedureDependent` |
| `governance.AcceptedApproval.CreatedAtUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `governance.ApprovalDecision.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `governance.ControlledDryRunReceipt.StartedAtUtc` | `datetimeoffset` | NO | `none` | present | `UtcParameterOrProcedureDependent` |
| `governance.ControlledDryRunReceipt.CompletedAtUtc` | `datetimeoffset` | NO | `none` | present | `UtcParameterOrProcedureDependent` |
| `governance.ControlledDryRunReceipt.DryRunCompleted` | `bit` | NO | `none` | absent | `NotApplicable` |
| `governance.ControlledDryRunReceipt.CreatedAtUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `governance.DogfoodReceipt.RecordedByActorType` | `nvarchar` | NO | `none` | present | `NotApplicable` |
| `governance.DogfoodReceipt.RecordedByActorId` | `nvarchar` | NO | `none` | present | `NotApplicable` |
| `governance.DogfoodReceipt.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `governance.GovernanceEvent.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `governance.PatchArtifact.CreatedAtUtc` | `datetimeoffset` | NO | `none` | present | `UtcParameterOrProcedureDependent` |
| `governance.PatchArtifact.ExpiresAtUtc` | `datetimeoffset` | YES | `none` | present | `UtcParameterOrProcedureDependent` |
| `governance.PatchArtifact.StoredAtUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `governance.PolicyDecisionEvent.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `governance.PolicySatisfaction.ApprovalEvaluatedAtUtc` | `datetimeoffset` | NO | `none` | absent | `UtcParameterOrProcedureDependent` |
| `governance.PolicySatisfaction.SatisfiedAtUtc` | `datetimeoffset` | NO | `none` | present | `UtcParameterOrProcedureDependent` |
| `governance.PolicySatisfaction.ExpiresAtUtc` | `datetimeoffset` | YES | `none` | present | `UtcParameterOrProcedureDependent` |
| `governance.PolicySatisfaction.CreatedAtUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `governance.ReleaseReadinessDecisionRecord.DecidedAtUtc` | `datetimeoffset` | NO | `none` | absent | `UtcParameterOrProcedureDependent` |
| `governance.ReleaseReadinessDecisionRecord.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `governance.RollbackExecutionReceipt.ObservedBranch` | `nvarchar` | NO | `none` | present | `NotApplicable` |
| `governance.RollbackExecutionReceipt.ObservedSourceBaselineHash` | `nvarchar` | NO | `none` | present | `NotApplicable` |
| `governance.RollbackExecutionReceipt.ObservedCleanWorktreeHashBeforeRollback` | `nvarchar` | NO | `none` | present | `NotApplicable` |
| `governance.RollbackExecutionReceipt.ObservedCleanWorktreeHashAfterRollback` | `nvarchar` | NO | `none` | present | `NotApplicable` |
| `governance.RollbackExecutionReceipt.RolledBackAtUtc` | `datetimeoffset` | NO | `none` | absent | `UtcParameterOrProcedureDependent` |
| `governance.RollbackExecutionReceipt.StoredAtUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `governance.RollbackSupportReceipt.CreatedAtUtc` | `datetimeoffset` | NO | `none` | present | `UtcParameterOrProcedureDependent` |
| `governance.RollbackSupportReceipt.ExpiresAtUtc` | `datetimeoffset` | YES | `none` | present | `UtcParameterOrProcedureDependent` |
| `governance.RollbackSupportReceipt.StoredAtUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `governance.SourceApplyDryRunReceipt.CreatedAtUtc` | `datetimeoffset` | NO | `none` | present | `UtcParameterOrProcedureDependent` |
| `governance.SourceApplyDryRunReceipt.ExpiresAtUtc` | `datetimeoffset` | YES | `none` | present | `UtcParameterOrProcedureDependent` |
| `governance.SourceApplyDryRunReceipt.StoredAtUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `governance.SourceApplyReceipt.ObservedBranch` | `nvarchar` | NO | `none` | present | `NotApplicable` |
| `governance.SourceApplyReceipt.ObservedCleanWorktreeHashBeforeApply` | `nvarchar` | NO | `none` | present | `NotApplicable` |
| `governance.SourceApplyReceipt.ObservedCleanWorktreeHashAfterApply` | `nvarchar` | NO | `none` | present | `NotApplicable` |
| `governance.SourceApplyReceipt.AppliedAtUtc` | `datetimeoffset` | NO | `none` | absent | `UtcParameterOrProcedureDependent` |
| `governance.SourceApplyReceipt.StoredAtUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `governance.ThoughtLedgerGovernanceEventReference.CreatedByActorType` | `nvarchar` | NO | `none` | present | `NotApplicable` |
| `governance.ThoughtLedgerGovernanceEventReference.CreatedByActorId` | `nvarchar` | NO | `none` | present | `NotApplicable` |
| `governance.ThoughtLedgerGovernanceEventReference.CreatedUtc` | `datetime2` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `governance.ToolGateDecision.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `governance.ToolRequest.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `governance.ToolRequest.CancelledUtc` | `datetimeoffset` | YES | `none` | present | `UtcParameterOrProcedureDependent` |
| `governance.WorkflowTransitionRecord.StepCompleted` | `bit` | NO | `none` | present | `NotApplicable` |
| `governance.WorkflowTransitionRecord.NextStepStarted` | `bit` | NO | `none` | present | `NotApplicable` |
| `governance.WorkflowTransitionRecord.TransitionedAtUtc` | `datetimeoffset` | NO | `none` | absent | `UtcParameterOrProcedureDependent` |
| `governance.WorkflowTransitionRecord.StoredAtUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `memory.MemoryProposal.CreatedByActorType` | `nvarchar` | NO | `none` | present | `NotApplicable` |
| `memory.MemoryProposal.CreatedByActorId` | `nvarchar` | NO | `none` | present | `NotApplicable` |
| `memory.MemoryProposal.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `memory.MemoryProposalEvidenceReference.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `memory.MemoryProposalGroundingReference.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `memory.MemoryProposalWorkflowReference.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `workflow.ApplyDryRunRecord.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `workflow.WorkflowCheckpoint.CreatedByActorType` | `nvarchar` | NO | `none` | absent | `NotApplicable` |
| `workflow.WorkflowCheckpoint.CreatedByActorId` | `nvarchar` | NO | `none` | absent | `NotApplicable` |
| `workflow.WorkflowCheckpoint.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `workflow.WorkflowCheckpointEvidenceReference.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `workflow.WorkflowCheckpointGroundingReference.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `workflow.WorkflowRun.CreatedByActorType` | `nvarchar` | NO | `none` | present | `NotApplicable` |
| `workflow.WorkflowRun.CreatedByActorId` | `nvarchar` | NO | `none` | present | `NotApplicable` |
| `workflow.WorkflowRun.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `workflow.WorkflowRunEvidenceReference.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `workflow.WorkflowRunGroundingReference.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |
| `workflow.WorkflowRunStep.CreatedUtc` | `datetimeoffset` | NO | `(sysutcdatetime())` | absent | `UtcDefaulted` |

Classification counts:

- `UtcEnforced`: 0
- `UtcDefaulted`: 65
- `UtcNamedOnly`: 0
- `UtcParameterOrProcedureDependent`: 35
- `LegacyAssumedUtc`: 5
- `Ambiguous`: 0
- `NonUtcOrLocalRisk`: 0
- `NotApplicable`: 30

## 3. UTC Classification Model

`UtcEnforced`: Database metadata enforces UTC behavior, not just naming. Example: a `datetimeoffset` column has a check constraint enforcing zero offset. H09 found no such columns.

`UtcDefaulted`: The column default is UTC-shaped, such as `SYSUTCDATETIME()`, but other write paths may still supply values.

`UtcNamedOnly`: The column name is UTC-explicit, but no UTC default, procedure dependency, or constraint was found. H09 did not classify any discovered column this way because UTC-named date/time columns without defaults are currently procedure/application dependent.

`UtcParameterOrProcedureDependent`: The column depends on stored procedure parameters or application-supplied values. The procedure/parameter names may be UTC-shaped, but the database does not prove UTC.

`LegacyAssumedUtc`: Legacy name lacks explicit UTC suffix, but the project standard treats it as UTC unless otherwise documented.

`Ambiguous`: Metadata does not prove UTC and naming is unclear.

`NonUtcOrLocalRisk`: Metadata suggests local/ambiguous/non-UTC behavior, such as `GETDATE()`, local-only naming, or unclear non-UTC defaults.

`NotApplicable`: Column was caught by broad discovery but is not timestamp evidence, such as `CreatedByActorId`, `ObservedBranch`, `StepCompleted`, `NextStepStarted`, and `IndexedFileCount`.

## 4. Default Constraint Review

Default expressions discovered: 65 `SYSUTCDATETIME()` defaults, 70 no-default/procedure-dependent candidates, and 0 local-risk defaults.

Default expression classification:

| Default expression | Count | Classification | Notes |
| --- | ---: | --- | --- |
| `SYSUTCDATETIME()` | 65 | UTC-shaped | Good default source, but not total write-path enforcement. |
| `GETUTCDATE()` | 0 | UTC-shaped but lower precision / legacy | Not currently found. |
| `SYSDATETIMEOFFSET()` | 0 | Offset-aware but not necessarily zero-offset UTC | Not currently found. |
| `SYSDATETIME()` | 0 | Ambiguous/local-server risk | Not currently found. |
| `GETDATE()` | 0 | Local-server risk | Not currently found. |
| literal date/time | 0 | Suspicious unless justified | Not currently found. |
| no default | 70 | procedure/application dependent | Includes parameter-supplied timestamps and non-date false positives. |

H09 does not change defaults.

## 5. Check Constraint Review

32 timestamp-like columns have check constraints, but those checks are expiry/order/shape/authority checks rather than UTC-offset enforcement.

No UTC-enforcing timestamp check constraints were found.

UTC enforcement is absent at the database constraint layer.

Observed timestamp-adjacent check patterns include:

- expiry ordering, such as accepted/satisfied timestamps before expiry timestamps
- hash/JSON/boundary-shape checks on rows that also contain timestamp fields
- authority-denial checks on receipt and workflow rows
- broad-rule false positives where the check belongs to a non-timestamp field, such as `ObservedBranch`

H09 does not add check constraints.

## 6. Stored Procedure Timestamp Write Review

Procedure candidates reviewed: 123.

Summary:

- 11 procedures use `SYSUTCDATETIME()`.
- 119 procedures have UTC-named parameters or UTC-shaped fields.
- 0 procedures use `GETUTCDATE()`.
- 0 procedures use `GETDATE()`.
- 0 procedures use `CURRENT_TIMESTAMP`.
- 0 procedures use `SYSDATETIME()`.
- 0 procedures use `SYSDATETIMEOFFSET()`.

Representative write procedures:

| Procedure | Pattern | Classification |
| --- | --- | --- |
| `governance.usp_AcceptedApproval_Save` | Uses `SYSUTCDATETIME()` fallback and UTC-named parameters. | `UtcParameterOrProcedureDependent` plus UTC default/fallback. |
| `governance.usp_ApprovalDecision_Record` | Uses `SYSUTCDATETIME()` fallback. | `UtcDefaulted`/procedure fallback. |
| `governance.usp_PolicySatisfaction_Save` | Uses `SYSUTCDATETIME()` fallback and UTC-named parameters. | `UtcParameterOrProcedureDependent` plus UTC default/fallback. |
| `governance.usp_SourceApplyReceipt_Save` | Uses UTC-named parameters for apply/storage evidence. | `UtcParameterOrProcedureDependent`. |
| `governance.usp_RollbackExecutionReceipt_Save` | Uses UTC-named rollback/storage parameters. | `UtcParameterOrProcedureDependent`. |
| `governance.usp_WorkflowTransitionRecord_Save` | Uses UTC-named transition/storage parameters. | `UtcParameterOrProcedureDependent`. |
| `governance.usp_ReleaseReadinessDecisionRecord_Save` | Uses UTC-named decision/create timestamps. | `UtcParameterOrProcedureDependent`. |
| `workflow.usp_WorkflowRun_Create` | Uses `SYSUTCDATETIME()` fallback and UTC-shaped fields. | `UtcDefaulted`/procedure fallback. |
| `workflow.usp_WorkflowStep_Create` | Uses `SYSUTCDATETIME()` fallback and UTC-shaped fields. | `UtcDefaulted`/procedure fallback. |

H09 does not alter stored procedures.

## 7. Findings

### H09-INFO-001

Severity: Info

Affected: UTC-defaulted product/audit timestamp columns.

Evidence: 65 discovered columns use `SYSUTCDATETIME()` defaults.

Risk: Low. UTC defaults improve write-path consistency for defaulted inserts, but supplied values can still bypass the default.

Recommended follow-up slice: None required for this info finding.

Why not fixed in H09: H09 is a review and metadata-test slice.

### H09-LOW-001

Severity: Low

Affected: UTC-named timestamp columns without defaults, including `governance.SourceApplyReceipt.AppliedAtUtc`, `governance.RollbackExecutionReceipt.RolledBackAtUtc`, `governance.ReleaseReadinessDecisionRecord.DecidedAtUtc`, `dbo.Runs.StartedUtc`, and `dbo.RunEvents.TimestampUtc`.

Evidence: 35 discovered columns are `UtcParameterOrProcedureDependent`.

Risk: The name is UTC-shaped, but the database does not independently prove the value is UTC.

Recommended follow-up slice: Consider a later timestamp-write hardening slice if these values must become DB-enforced instead of contract-enforced.

Why not fixed in H09: H09 must not add constraints or change timestamp-writing behavior.

### H09-LOW-002

Severity: Low

Affected: Legacy date columns without explicit `Utc` suffix, including `CreatedDate`, `UpdatedDate`, and `LastIndexedDate` forms.

Evidence: 5 discovered columns are `LegacyAssumedUtc`; several other legacy-named columns are `UtcDefaulted` because they default to `SYSUTCDATETIME()`.

Risk: Readers may assume local time from legacy naming even where defaults are UTC-shaped.

Recommended follow-up slice: Later naming/documentation review if the project wants explicit UTC naming everywhere.

Why not fixed in H09: H09 must not rename columns.

### H09-MEDIUM-001

Severity: Medium

Affected: All product/audit timestamp columns that rely on `datetime2` or `datetimeoffset` without UTC-offset enforcing check constraints.

Evidence: No `UtcEnforced` columns were found. No UTC-offset enforcing check constraints were found.

Risk: Timestamp comparability relies on defaults, naming, procedures, and application contracts rather than database-level UTC enforcement.

Recommended follow-up slice: Later UTC constraint implementation design, if required, with migration/data-backfill risk reviewed separately.

Why not fixed in H09: H09 must not add check constraints, alter columns, or migrate timestamp data.

## 8. Non-Authority Boundary

UTC timestamp shape improves comparability and audit ordering.

UTC timestamp shape is not approval.

UTC timestamp shape is not policy satisfaction.

UTC timestamp shape is not source-apply authority.

UTC timestamp shape is not workflow continuation authority.

UTC timestamp shape is not merge readiness.

UTC timestamp shape is not release readiness.

UTC timestamp shape is not deployment readiness.

UTC timestamp shape is not rollback authority.

UTC timestamp shape is not retry authority.

UTC timestamp shape is not mutation authority.

UTC timestamp shape does not prove the payload is true.

UTC timestamp shape does not prove the actor was authorized.

UTC timestamp shape does not prove the next action is safe.

UTC timestamps make time comparable only.

A correctly timed lie is still a lie.

## 9. What H09 Did Not Change

H09 does not add a SQL migration.

H09 does not alter tables.

H09 does not alter timestamp columns.

H09 does not rename timestamp columns.

H09 does not add default constraints.

H09 does not alter default constraints.

H09 does not add check constraints.

H09 does not alter check constraints.

H09 does not alter stored procedures.

H09 does not alter triggers.

H09 does not change permissions.

H09 does not change API/CLI/UI behavior.

H09 does not change workflow/source-apply/rollback/release/deployment authority.

H09 does not change Weaviate behavior.

H09 does not rebuild, replay, or backfill projections.

H09 does not adopt a migration runner or DbUp.

## 10. Next Intended Slice

H10 - Raw payload redaction/retention policy.

Review line: Redaction policy limits exposure. It does not make retained payloads safe.

Killjoy: A retained secret is still a secret.
