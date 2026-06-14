IF SCHEMA_ID(N'memory') IS NULL
    EXEC(N'CREATE SCHEMA memory');
GO

IF OBJECT_ID(N'memory.MemoryProposal', N'U') IS NULL
BEGIN
    CREATE TABLE memory.MemoryProposal
    (
        MemoryProposalId UNIQUEIDENTIFIER NOT NULL,
        TenantId UNIQUEIDENTIFIER NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        ProposalKey NVARCHAR(180) NOT NULL,
        ProposalType NVARCHAR(80) NOT NULL,
        TargetMemoryScope NVARCHAR(80) NOT NULL,
        ProposalStatus NVARCHAR(80) NOT NULL,
        SourceType NVARCHAR(120) NOT NULL,
        SourceId NVARCHAR(300) NULL,
        SourceAgentRole NVARCHAR(120) NULL,
        SourceAgentId NVARCHAR(200) NULL,
        SubjectType NVARCHAR(120) NULL,
        SubjectId NVARCHAR(300) NULL,
        SafeProposedMemory NVARCHAR(2000) NOT NULL,
        SafeRationaleSummary NVARCHAR(1000) NULL,
        SafeRiskSummary NVARCHAR(1000) NULL,
        ConfidenceLabel NVARCHAR(80) NULL,
        ConfidentialityLabel NVARCHAR(80) NOT NULL,
        SanitizationStatus NVARCHAR(80) NOT NULL,
        WorkflowRunId UNIQUEIDENTIFIER NULL,
        WorkflowRunStepId UNIQUEIDENTIFIER NULL,
        WorkflowCheckpointId UNIQUEIDENTIFIER NULL,
        CorrelationId UNIQUEIDENTIFIER NULL,
        CausationId UNIQUEIDENTIFIER NULL,
        CreatedByActorType NVARCHAR(80) NOT NULL,
        CreatedByActorId NVARCHAR(200) NOT NULL,
        MetadataVersion INT NOT NULL,
        MetadataJson NVARCHAR(MAX) NOT NULL,
        IsAcceptedMemory BIT NOT NULL CONSTRAINT DF_MemoryProposal_IsAcceptedMemory DEFAULT 0,
        CreatesAcceptedMemory BIT NOT NULL CONSTRAINT DF_MemoryProposal_CreatesAcceptedMemory DEFAULT 0,
        PromotesMemory BIT NOT NULL CONSTRAINT DF_MemoryProposal_PromotesMemory DEFAULT 0,
        WritesCollectiveMemory BIT NOT NULL CONSTRAINT DF_MemoryProposal_WritesCollectiveMemory DEFAULT 0,
        WritesAgentMemory BIT NOT NULL CONSTRAINT DF_MemoryProposal_WritesAgentMemory DEFAULT 0,
        WritesVectorIndex BIT NOT NULL CONSTRAINT DF_MemoryProposal_WritesVectorIndex DEFAULT 0,
        IsRetrievalAuthority BIT NOT NULL CONSTRAINT DF_MemoryProposal_IsRetrievalAuthority DEFAULT 0,
        IsPolicy BIT NOT NULL CONSTRAINT DF_MemoryProposal_IsPolicy DEFAULT 0,
        IsApproval BIT NOT NULL CONSTRAINT DF_MemoryProposal_IsApproval DEFAULT 0,
        SatisfiesPolicy BIT NOT NULL CONSTRAINT DF_MemoryProposal_SatisfiesPolicy DEFAULT 0,
        GrantsApproval BIT NOT NULL CONSTRAINT DF_MemoryProposal_GrantsApproval DEFAULT 0,
        GrantsExecution BIT NOT NULL CONSTRAINT DF_MemoryProposal_GrantsExecution DEFAULT 0,
        StartsWorkflow BIT NOT NULL CONSTRAINT DF_MemoryProposal_StartsWorkflow DEFAULT 0,
        ContinuesWorkflow BIT NOT NULL CONSTRAINT DF_MemoryProposal_ContinuesWorkflow DEFAULT 0,
        MutatesSource BIT NOT NULL CONSTRAINT DF_MemoryProposal_MutatesSource DEFAULT 0,
        ApprovesRelease BIT NOT NULL CONSTRAINT DF_MemoryProposal_ApprovesRelease DEFAULT 0,
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_MemoryProposal_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_MemoryProposal PRIMARY KEY CLUSTERED (MemoryProposalId),
        CONSTRAINT UQ_MemoryProposal_Project_Key UNIQUE (ProjectId, ProposalKey),
        CONSTRAINT CK_MemoryProposal_Id_NotEmpty CHECK (MemoryProposalId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_MemoryProposal_ProjectId_NotEmpty CHECK (ProjectId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_MemoryProposal_Key_NotBlank CHECK (LEN(LTRIM(RTRIM(ProposalKey))) > 0),
        CONSTRAINT CK_MemoryProposal_Type_Allowed CHECK (ProposalType IN (N'ProjectFactCandidate', N'ProjectDecisionCandidate', N'ProjectCorrectionCandidate', N'ProjectRiskCandidate', N'ProjectConstraintCandidate', N'ProjectConventionCandidate', N'AgentLocalMemoryCandidate', N'EngineeringPatternCandidate', N'FailureModeCandidate', N'DebuggingLessonCandidate', N'PortableEngineeringMemoryCandidate', N'DeprecationCandidate', N'DuplicateCandidate', N'ClarificationNeededCandidate')),
        CONSTRAINT CK_MemoryProposal_TargetScope_Allowed CHECK (TargetMemoryScope IN (N'ProjectLocalCandidate', N'AgentLocalCandidate', N'PortableEngineeringMemoryCandidate', N'RequiresTriage')),
        CONSTRAINT CK_MemoryProposal_Status_StagingOnly CHECK (ProposalStatus IN (N'Staged', N'ReadyForReview', N'NeedsEvidence', N'NeedsClarification', N'Quarantined', N'DuplicateCandidate', N'Superseded', N'Withdrawn')),
        CONSTRAINT CK_MemoryProposal_SourceType_NotBlank CHECK (LEN(LTRIM(RTRIM(SourceType))) > 0),
        CONSTRAINT CK_MemoryProposal_SafeMemory_NotBlank CHECK (LEN(LTRIM(RTRIM(SafeProposedMemory))) > 0),
        CONSTRAINT CK_MemoryProposal_Confidentiality_Allowed CHECK (ConfidentialityLabel IN (N'ProjectConfidential', N'AgentLocal', N'ContainsSensitiveProjectDetail', N'ContainsExternalConfidentialDetail', N'PortableCandidateRequiresSanitization', N'SanitizedCandidateForReview', N'UnknownRequiresReview')),
        CONSTRAINT CK_MemoryProposal_Sanitization_Allowed CHECK (SanitizationStatus IN (N'NotApplicable', N'RequiresReview', N'RequiresSanitization', N'SanitizedCandidate', N'Quarantined')),
        CONSTRAINT CK_MemoryProposal_CreatedByActorType_NotBlank CHECK (LEN(LTRIM(RTRIM(CreatedByActorType))) > 0),
        CONSTRAINT CK_MemoryProposal_CreatedByActorId_NotBlank CHECK (LEN(LTRIM(RTRIM(CreatedByActorId))) > 0),
        CONSTRAINT CK_MemoryProposal_MetadataVersion_Positive CHECK (MetadataVersion > 0),
        CONSTRAINT CK_MemoryProposal_MetadataJson_IsJson CHECK (ISJSON(MetadataJson) = 1),
        CONSTRAINT CK_MemoryProposal_NoAcceptedMemory CHECK (IsAcceptedMemory = 0),
        CONSTRAINT CK_MemoryProposal_NoAcceptedMemoryCreation CHECK (CreatesAcceptedMemory = 0),
        CONSTRAINT CK_MemoryProposal_NoMemoryPromotion CHECK (PromotesMemory = 0),
        CONSTRAINT CK_MemoryProposal_NoCollectiveMemoryWrite CHECK (WritesCollectiveMemory = 0),
        CONSTRAINT CK_MemoryProposal_NoAgentMemoryWrite CHECK (WritesAgentMemory = 0),
        CONSTRAINT CK_MemoryProposal_NoVectorIndexWrite CHECK (WritesVectorIndex = 0),
        CONSTRAINT CK_MemoryProposal_NoRetrievalAuthority CHECK (IsRetrievalAuthority = 0),
        CONSTRAINT CK_MemoryProposal_NoPolicy CHECK (IsPolicy = 0),
        CONSTRAINT CK_MemoryProposal_NoApproval CHECK (IsApproval = 0),
        CONSTRAINT CK_MemoryProposal_NoPolicySatisfaction CHECK (SatisfiesPolicy = 0),
        CONSTRAINT CK_MemoryProposal_NoApprovalGrant CHECK (GrantsApproval = 0),
        CONSTRAINT CK_MemoryProposal_NoExecutionGrant CHECK (GrantsExecution = 0),
        CONSTRAINT CK_MemoryProposal_NoWorkflowStart CHECK (StartsWorkflow = 0),
        CONSTRAINT CK_MemoryProposal_NoWorkflowContinue CHECK (ContinuesWorkflow = 0),
        CONSTRAINT CK_MemoryProposal_NoSourceMutation CHECK (MutatesSource = 0),
        CONSTRAINT CK_MemoryProposal_NoReleaseApproval CHECK (ApprovesRelease = 0)
    );
END;
GO

IF OBJECT_ID(N'memory.MemoryProposalEvidenceReference', N'U') IS NULL
BEGIN
    CREATE TABLE memory.MemoryProposalEvidenceReference
    (
        MemoryProposalEvidenceReferenceId UNIQUEIDENTIFIER NOT NULL,
        MemoryProposalId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        EvidenceType NVARCHAR(120) NOT NULL,
        EvidenceId NVARCHAR(300) NOT NULL,
        EvidenceLabel NVARCHAR(240) NULL,
        SafeSummary NVARCHAR(1000) NULL,
        AllowedUse NVARCHAR(120) NULL,
        GovernanceEventId UNIQUEIDENTIFIER NULL,
        WorkflowRunEvidenceReferenceId UNIQUEIDENTIFIER NULL,
        WorkflowRunStepId UNIQUEIDENTIFIER NULL,
        WorkflowCheckpointId UNIQUEIDENTIFIER NULL,
        HandoffId UNIQUEIDENTIFIER NULL,
        ThoughtLedgerEntryId UNIQUEIDENTIFIER NULL,
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_MemoryProposalEvidenceReference_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_MemoryProposalEvidenceReference PRIMARY KEY CLUSTERED (MemoryProposalEvidenceReferenceId),
        CONSTRAINT FK_MemoryProposalEvidenceReference_Proposal FOREIGN KEY (MemoryProposalId) REFERENCES memory.MemoryProposal(MemoryProposalId),
        CONSTRAINT CK_MemoryProposalEvidenceReference_Id_NotEmpty CHECK (MemoryProposalEvidenceReferenceId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_MemoryProposalEvidenceReference_ProjectId_NotEmpty CHECK (ProjectId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_MemoryProposalEvidenceReference_EvidenceType_Allowed CHECK (EvidenceType IN (N'GovernanceEvent', N'ToolRequest', N'ToolGateDecision', N'ApprovalDecision', N'PolicyDecisionEvent', N'DogfoodReceipt', N'WorkflowRun', N'WorkflowRunStep', N'WorkflowCheckpoint', N'Handoff', N'ThoughtLedgerReference', N'GroundingReference', N'CriticReview', N'ValidationOutput', N'HumanNote', N'RunReport', N'TestFailure', N'BuildFailure', N'SourceReport', N'FailurePackage')),
        CONSTRAINT CK_MemoryProposalEvidenceReference_EvidenceId_NotBlank CHECK (LEN(LTRIM(RTRIM(EvidenceId))) > 0),
        CONSTRAINT CK_MemoryProposalEvidenceReference_AllowedUse_Allowed CHECK (AllowedUse IS NULL OR AllowedUse IN (N'Context', N'Review', N'Debugging', N'Validation', N'Traceability', N'HumanDecisionSupport', N'AuditReference', N'PolicyInput', N'HandoffExplanation', N'RequirementEvaluation', N'Grounding', N'MemoryProposalReview'))
    );
END;
GO

IF OBJECT_ID(N'memory.MemoryProposalGroundingReference', N'U') IS NULL
BEGIN
    CREATE TABLE memory.MemoryProposalGroundingReference
    (
        MemoryProposalGroundingReferenceId UNIQUEIDENTIFIER NOT NULL,
        MemoryProposalId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        GroundingReferenceId UNIQUEIDENTIFIER NOT NULL,
        ClaimType NVARCHAR(120) NOT NULL,
        ClaimId NVARCHAR(300) NOT NULL,
        SafeSummary NVARCHAR(1000) NULL,
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_MemoryProposalGroundingReference_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_MemoryProposalGroundingReference PRIMARY KEY CLUSTERED (MemoryProposalGroundingReferenceId),
        CONSTRAINT FK_MemoryProposalGroundingReference_Proposal FOREIGN KEY (MemoryProposalId) REFERENCES memory.MemoryProposal(MemoryProposalId),
        CONSTRAINT CK_MemoryProposalGroundingReference_Id_NotEmpty CHECK (MemoryProposalGroundingReferenceId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_MemoryProposalGroundingReference_ProjectId_NotEmpty CHECK (ProjectId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_MemoryProposalGroundingReference_GroundingId_NotEmpty CHECK (GroundingReferenceId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_MemoryProposalGroundingReference_ClaimType_Allowed CHECK (ClaimType IN (N'EvidenceSupport', N'RequirementTrace', N'DecisionTrace', N'HandoffTrace', N'PolicyTrace', N'ValidationTrace', N'WorkflowTrace', N'MemoryProposalTrace')),
        CONSTRAINT CK_MemoryProposalGroundingReference_ClaimId_NotBlank CHECK (LEN(LTRIM(RTRIM(ClaimId))) > 0)
    );
END;
GO

IF OBJECT_ID(N'memory.MemoryProposalWorkflowReference', N'U') IS NULL
BEGIN
    CREATE TABLE memory.MemoryProposalWorkflowReference
    (
        MemoryProposalWorkflowReferenceId UNIQUEIDENTIFIER NOT NULL,
        MemoryProposalId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        WorkflowRunId UNIQUEIDENTIFIER NULL,
        WorkflowRunStepId UNIQUEIDENTIFIER NULL,
        WorkflowCheckpointId UNIQUEIDENTIFIER NULL,
        ReferenceType NVARCHAR(120) NOT NULL,
        SafeSummary NVARCHAR(1000) NULL,
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_MemoryProposalWorkflowReference_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_MemoryProposalWorkflowReference PRIMARY KEY CLUSTERED (MemoryProposalWorkflowReferenceId),
        CONSTRAINT FK_MemoryProposalWorkflowReference_Proposal FOREIGN KEY (MemoryProposalId) REFERENCES memory.MemoryProposal(MemoryProposalId),
        CONSTRAINT CK_MemoryProposalWorkflowReference_Id_NotEmpty CHECK (MemoryProposalWorkflowReferenceId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_MemoryProposalWorkflowReference_ProjectId_NotEmpty CHECK (ProjectId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_MemoryProposalWorkflowReference_Target CHECK (WorkflowRunId IS NOT NULL OR WorkflowRunStepId IS NOT NULL OR WorkflowCheckpointId IS NOT NULL),
        CONSTRAINT CK_MemoryProposalWorkflowReference_Type_Allowed CHECK (ReferenceType IN (N'Origin', N'RelatedRun', N'RelatedStep', N'RelatedCheckpoint', N'GeneratedFrom', N'SupportsReview', N'Traceability'))
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MemoryProposal_Project_Status_CreatedUtc' AND object_id = OBJECT_ID(N'memory.MemoryProposal'))
    CREATE INDEX IX_MemoryProposal_Project_Status_CreatedUtc ON memory.MemoryProposal(ProjectId, ProposalStatus, CreatedUtc DESC, MemoryProposalId DESC);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MemoryProposal_Project_WorkflowRun_CreatedUtc' AND object_id = OBJECT_ID(N'memory.MemoryProposal'))
    CREATE INDEX IX_MemoryProposal_Project_WorkflowRun_CreatedUtc ON memory.MemoryProposal(ProjectId, WorkflowRunId, CreatedUtc DESC, MemoryProposalId DESC) WHERE WorkflowRunId IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MemoryProposal_Project_Source_CreatedUtc' AND object_id = OBJECT_ID(N'memory.MemoryProposal'))
    CREATE INDEX IX_MemoryProposal_Project_Source_CreatedUtc ON memory.MemoryProposal(ProjectId, SourceType, SourceId, CreatedUtc DESC, MemoryProposalId DESC) WHERE SourceId IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MemoryProposalEvidenceReference_Proposal' AND object_id = OBJECT_ID(N'memory.MemoryProposalEvidenceReference'))
    CREATE INDEX IX_MemoryProposalEvidenceReference_Proposal ON memory.MemoryProposalEvidenceReference(MemoryProposalId, CreatedUtc, MemoryProposalEvidenceReferenceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MemoryProposalGroundingReference_Proposal' AND object_id = OBJECT_ID(N'memory.MemoryProposalGroundingReference'))
    CREATE INDEX IX_MemoryProposalGroundingReference_Proposal ON memory.MemoryProposalGroundingReference(MemoryProposalId, CreatedUtc, MemoryProposalGroundingReferenceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MemoryProposalWorkflowReference_Proposal' AND object_id = OBJECT_ID(N'memory.MemoryProposalWorkflowReference'))
    CREATE INDEX IX_MemoryProposalWorkflowReference_Proposal ON memory.MemoryProposalWorkflowReference(MemoryProposalId, CreatedUtc, MemoryProposalWorkflowReferenceId);
GO

CREATE OR ALTER TRIGGER memory.TR_MemoryProposal_ValidateInsert ON memory.MemoryProposal AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted i
        CROSS APPLY (VALUES (LOWER(CONCAT_WS(N' ', i.ProposalKey, i.ProposalType, i.TargetMemoryScope, i.ProposalStatus, i.SourceType, i.SourceId, i.SourceAgentRole, i.SourceAgentId, i.SubjectType, i.SubjectId, i.SafeProposedMemory, i.SafeRationaleSummary, i.SafeRiskSummary, i.ConfidenceLabel, i.ConfidentialityLabel, i.SanitizationStatus, i.CreatedByActorType, i.CreatedByActorId, i.MetadataJson)))) v(TextValue)
        WHERE v.TextValue LIKE N'%private reasoning%' OR v.TextValue LIKE N'%hidden reasoning%' OR v.TextValue LIKE N'%chainofthought%' OR v.TextValue LIKE N'%chain of thought%' OR v.TextValue LIKE N'%chain-of-thought%' OR v.TextValue LIKE N'%scratchpad%' OR v.TextValue LIKE N'%rawprompt%' OR v.TextValue LIKE N'%raw prompt%' OR v.TextValue LIKE N'%rawcompletion%' OR v.TextValue LIKE N'%raw completion%' OR v.TextValue LIKE N'%rawtooloutput%' OR v.TextValue LIKE N'%raw tool output%' OR v.TextValue LIKE N'%entirepatch%' OR v.TextValue LIKE N'%entire patch%' OR v.TextValue LIKE N'%approval granted%' OR v.TextValue LIKE N'%approved for execution%' OR v.TextValue LIKE N'%execution permission%' OR v.TextValue LIKE N'%execution allowed%' OR v.TextValue LIKE N'%can execute%' OR v.TextValue LIKE N'%authorize execution%' OR v.TextValue LIKE N'%policy satisfied%' OR v.TextValue LIKE N'%satisfy policy%' OR v.TextValue LIKE N'%accepted memory%' OR v.TextValue LIKE N'%memory accepted%' OR v.TextValue LIKE N'%memory promoted%' OR v.TextValue LIKE N'%promote memory%' OR v.TextValue LIKE N'%collective memory created%' OR v.TextValue LIKE N'%write collective memory%' OR v.TextValue LIKE N'%write vector index%' OR v.TextValue LIKE N'%retrieval authority%' OR v.TextValue LIKE N'%source applied%' OR v.TextValue LIKE N'%apply source%' OR v.TextValue LIKE N'%apply patch%' OR v.TextValue LIKE N'%release approved%' OR v.TextValue LIKE N'%approve release%' OR v.TextValue LIKE N'%ready to ship%' OR v.TextValue LIKE N'%can ship%' OR v.TextValue LIKE N'%workflow continued%' OR v.TextValue LIKE N'%continue workflow%' OR v.TextValue LIKE N'%workflow started%' OR v.TextValue LIKE N'%start workflow%' OR v.TextValue LIKE N'%authority transferred%' OR v.TextValue LIKE N'%transfer authority%')
        THROW 54410, 'Memory proposal staging text cannot contain raw/private reasoning or authority language.', 1;
END;
GO
CREATE OR ALTER TRIGGER memory.TR_MemoryProposal_BlockUpdateDelete ON memory.MemoryProposal AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; THROW 54411, 'memory.MemoryProposal is append-only; update/delete is not allowed.', 1; END;
GO
CREATE OR ALTER TRIGGER memory.TR_MemoryProposalEvidenceReference_ValidateInsert ON memory.MemoryProposalEvidenceReference AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN memory.MemoryProposal p ON p.MemoryProposalId = i.MemoryProposalId WHERE p.ProjectId <> i.ProjectId) THROW 54412, 'Memory proposal evidence reference project must match parent proposal project.', 1;
    IF EXISTS (SELECT 1 FROM inserted i CROSS APPLY (VALUES (LOWER(CONCAT_WS(N' ', i.EvidenceType, i.EvidenceId, i.EvidenceLabel, i.SafeSummary, i.AllowedUse)))) v(TextValue) WHERE v.TextValue LIKE N'%private reasoning%' OR v.TextValue LIKE N'%hidden reasoning%' OR v.TextValue LIKE N'%chainofthought%' OR v.TextValue LIKE N'%rawprompt%' OR v.TextValue LIKE N'%rawcompletion%' OR v.TextValue LIKE N'%rawtooloutput%' OR v.TextValue LIKE N'%entirepatch%' OR v.TextValue LIKE N'%approval granted%' OR v.TextValue LIKE N'%execution permission%' OR v.TextValue LIKE N'%memory promoted%' OR v.TextValue LIKE N'%accepted memory%' OR v.TextValue LIKE N'%source applied%' OR v.TextValue LIKE N'%release approved%' OR v.TextValue LIKE N'%authority transferred%') THROW 54413, 'Memory proposal evidence reference cannot contain raw/private reasoning or authority language.', 1;
END;
GO
CREATE OR ALTER TRIGGER memory.TR_MemoryProposalEvidenceReference_BlockUpdateDelete ON memory.MemoryProposalEvidenceReference AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; THROW 54414, 'memory.MemoryProposalEvidenceReference is append-only; update/delete is not allowed.', 1; END;
GO
CREATE OR ALTER TRIGGER memory.TR_MemoryProposalGroundingReference_ValidateInsert ON memory.MemoryProposalGroundingReference AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN memory.MemoryProposal p ON p.MemoryProposalId = i.MemoryProposalId WHERE p.ProjectId <> i.ProjectId) THROW 54415, 'Memory proposal grounding reference project must match parent proposal project.', 1;
    IF EXISTS (SELECT 1 FROM inserted i CROSS APPLY (VALUES (LOWER(CONCAT_WS(N' ', i.ClaimType, i.ClaimId, i.SafeSummary)))) v(TextValue) WHERE v.TextValue LIKE N'%private reasoning%' OR v.TextValue LIKE N'%hidden reasoning%' OR v.TextValue LIKE N'%chainofthought%' OR v.TextValue LIKE N'%rawprompt%' OR v.TextValue LIKE N'%rawcompletion%' OR v.TextValue LIKE N'%rawtooloutput%' OR v.TextValue LIKE N'%entirepatch%' OR v.TextValue LIKE N'%approval granted%' OR v.TextValue LIKE N'%execution permission%' OR v.TextValue LIKE N'%memory promoted%' OR v.TextValue LIKE N'%accepted memory%' OR v.TextValue LIKE N'%source applied%' OR v.TextValue LIKE N'%release approved%' OR v.TextValue LIKE N'%authority transferred%') THROW 54416, 'Memory proposal grounding reference cannot contain raw/private reasoning or authority language.', 1;
END;
GO
CREATE OR ALTER TRIGGER memory.TR_MemoryProposalGroundingReference_BlockUpdateDelete ON memory.MemoryProposalGroundingReference AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; THROW 54417, 'memory.MemoryProposalGroundingReference is append-only; update/delete is not allowed.', 1; END;
GO
CREATE OR ALTER TRIGGER memory.TR_MemoryProposalWorkflowReference_ValidateInsert ON memory.MemoryProposalWorkflowReference AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN memory.MemoryProposal p ON p.MemoryProposalId = i.MemoryProposalId WHERE p.ProjectId <> i.ProjectId) THROW 54418, 'Memory proposal workflow reference project must match parent proposal project.', 1;
    IF EXISTS (SELECT 1 FROM inserted i CROSS APPLY (VALUES (LOWER(CONCAT_WS(N' ', i.ReferenceType, i.SafeSummary)))) v(TextValue) WHERE v.TextValue LIKE N'%private reasoning%' OR v.TextValue LIKE N'%hidden reasoning%' OR v.TextValue LIKE N'%chainofthought%' OR v.TextValue LIKE N'%rawprompt%' OR v.TextValue LIKE N'%rawcompletion%' OR v.TextValue LIKE N'%rawtooloutput%' OR v.TextValue LIKE N'%entirepatch%' OR v.TextValue LIKE N'%approval granted%' OR v.TextValue LIKE N'%execution permission%' OR v.TextValue LIKE N'%memory promoted%' OR v.TextValue LIKE N'%accepted memory%' OR v.TextValue LIKE N'%source applied%' OR v.TextValue LIKE N'%release approved%' OR v.TextValue LIKE N'%authority transferred%') THROW 54419, 'Memory proposal workflow reference cannot contain raw/private reasoning or authority language.', 1;
END;
GO
CREATE OR ALTER TRIGGER memory.TR_MemoryProposalWorkflowReference_BlockUpdateDelete ON memory.MemoryProposalWorkflowReference AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; THROW 54420, 'memory.MemoryProposalWorkflowReference is append-only; update/delete is not allowed.', 1; END;
GO

CREATE OR ALTER PROCEDURE memory.usp_MemoryProposal_Get @ProjectId UNIQUEIDENTIFIER, @MemoryProposalId UNIQUEIDENTIFIER WITH EXECUTE AS OWNER AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM memory.MemoryProposal WHERE ProjectId = @ProjectId AND MemoryProposalId = @MemoryProposalId;
    SELECT * FROM memory.MemoryProposalEvidenceReference WHERE ProjectId = @ProjectId AND MemoryProposalId = @MemoryProposalId ORDER BY CreatedUtc, MemoryProposalEvidenceReferenceId;
    SELECT * FROM memory.MemoryProposalGroundingReference WHERE ProjectId = @ProjectId AND MemoryProposalId = @MemoryProposalId ORDER BY CreatedUtc, MemoryProposalGroundingReferenceId;
    SELECT * FROM memory.MemoryProposalWorkflowReference WHERE ProjectId = @ProjectId AND MemoryProposalId = @MemoryProposalId ORDER BY CreatedUtc, MemoryProposalWorkflowReferenceId;
END;
GO

CREATE OR ALTER PROCEDURE memory.usp_MemoryProposal_Create
    @MemoryProposalId UNIQUEIDENTIFIER, @TenantId UNIQUEIDENTIFIER = NULL, @ProjectId UNIQUEIDENTIFIER, @ProposalKey NVARCHAR(180), @ProposalType NVARCHAR(80), @TargetMemoryScope NVARCHAR(80), @ProposalStatus NVARCHAR(80), @SourceType NVARCHAR(120), @SourceId NVARCHAR(300) = NULL, @SourceAgentRole NVARCHAR(120) = NULL, @SourceAgentId NVARCHAR(200) = NULL, @SubjectType NVARCHAR(120) = NULL, @SubjectId NVARCHAR(300) = NULL, @SafeProposedMemory NVARCHAR(2000), @SafeRationaleSummary NVARCHAR(1000) = NULL, @SafeRiskSummary NVARCHAR(1000) = NULL, @ConfidenceLabel NVARCHAR(80) = NULL, @ConfidentialityLabel NVARCHAR(80), @SanitizationStatus NVARCHAR(80), @WorkflowRunId UNIQUEIDENTIFIER = NULL, @WorkflowRunStepId UNIQUEIDENTIFIER = NULL, @WorkflowCheckpointId UNIQUEIDENTIFIER = NULL, @CorrelationId UNIQUEIDENTIFIER = NULL, @CausationId UNIQUEIDENTIFIER = NULL, @CreatedByActorType NVARCHAR(80), @CreatedByActorId NVARCHAR(200), @MetadataVersion INT, @MetadataJson NVARCHAR(MAX), @IsAcceptedMemory BIT = 0, @CreatesAcceptedMemory BIT = 0, @PromotesMemory BIT = 0, @WritesCollectiveMemory BIT = 0, @WritesAgentMemory BIT = 0, @WritesVectorIndex BIT = 0, @IsRetrievalAuthority BIT = 0, @IsPolicy BIT = 0, @IsApproval BIT = 0, @SatisfiesPolicy BIT = 0, @GrantsApproval BIT = 0, @GrantsExecution BIT = 0, @StartsWorkflow BIT = 0, @ContinuesWorkflow BIT = 0, @MutatesSource BIT = 0, @ApprovesRelease BIT = 0, @EvidenceReferencesJson NVARCHAR(MAX) = N'[]', @GroundingReferencesJson NVARCHAR(MAX) = N'[]', @WorkflowReferencesJson NVARCHAR(MAX) = N'[]'
WITH EXECUTE AS OWNER AS
BEGIN
    SET NOCOUNT ON;
    IF @MemoryProposalId = '00000000-0000-0000-0000-000000000000' OR @ProjectId = '00000000-0000-0000-0000-000000000000' THROW 54430, 'Memory proposal id and project id are required.', 1;
    IF LEN(LTRIM(RTRIM(ISNULL(@ProposalKey, N'')))) = 0 OR LEN(LTRIM(RTRIM(ISNULL(@SourceType, N'')))) = 0 OR LEN(LTRIM(RTRIM(ISNULL(@SafeProposedMemory, N'')))) = 0 THROW 54431, 'Memory proposal key, source type, and safe proposed memory are required.', 1;
    IF ISJSON(@MetadataJson) <> 1 OR ISJSON(@EvidenceReferencesJson) <> 1 OR ISJSON(@GroundingReferencesJson) <> 1 OR ISJSON(@WorkflowReferencesJson) <> 1 THROW 54432, 'Memory proposal JSON payloads must be valid JSON.', 1;
    IF @ProposalType NOT IN (N'ProjectFactCandidate', N'ProjectDecisionCandidate', N'ProjectCorrectionCandidate', N'ProjectRiskCandidate', N'ProjectConstraintCandidate', N'ProjectConventionCandidate', N'AgentLocalMemoryCandidate', N'EngineeringPatternCandidate', N'FailureModeCandidate', N'DebuggingLessonCandidate', N'PortableEngineeringMemoryCandidate', N'DeprecationCandidate', N'DuplicateCandidate', N'ClarificationNeededCandidate') THROW 54433, 'Memory proposal type is not allowed.', 1;
    IF @TargetMemoryScope NOT IN (N'ProjectLocalCandidate', N'AgentLocalCandidate', N'PortableEngineeringMemoryCandidate', N'RequiresTriage') THROW 54434, 'Memory proposal target scope is not allowed.', 1;
    IF @ProposalStatus NOT IN (N'Staged', N'ReadyForReview', N'NeedsEvidence', N'NeedsClarification', N'Quarantined', N'DuplicateCandidate', N'Superseded', N'Withdrawn') THROW 54435, 'Memory proposal status must remain a staging state.', 1;
    IF @IsAcceptedMemory = 1 OR @CreatesAcceptedMemory = 1 OR @PromotesMemory = 1 OR @WritesCollectiveMemory = 1 OR @WritesAgentMemory = 1 OR @WritesVectorIndex = 1 OR @IsRetrievalAuthority = 1 OR @IsPolicy = 1 OR @IsApproval = 1 OR @SatisfiesPolicy = 1 OR @GrantsApproval = 1 OR @GrantsExecution = 1 OR @StartsWorkflow = 1 OR @ContinuesWorkflow = 1 OR @MutatesSource = 1 OR @ApprovesRelease = 1 THROW 54436, 'Memory proposal staging cannot grant authority or perform memory/source/workflow side effects.', 1;
    DECLARE @UnsafeText NVARCHAR(MAX) = LOWER(CONCAT_WS(N' ', @ProposalKey, @ProposalType, @TargetMemoryScope, @ProposalStatus, @SourceType, @SourceId, @SourceAgentRole, @SourceAgentId, @SubjectType, @SubjectId, @SafeProposedMemory, @SafeRationaleSummary, @SafeRiskSummary, @ConfidenceLabel, @ConfidentialityLabel, @SanitizationStatus, @CreatedByActorType, @CreatedByActorId, @MetadataJson, @EvidenceReferencesJson, @GroundingReferencesJson, @WorkflowReferencesJson));
    IF @UnsafeText LIKE N'%private reasoning%' OR @UnsafeText LIKE N'%hidden reasoning%' OR @UnsafeText LIKE N'%chainofthought%' OR @UnsafeText LIKE N'%chain of thought%' OR @UnsafeText LIKE N'%chain-of-thought%' OR @UnsafeText LIKE N'%scratchpad%' OR @UnsafeText LIKE N'%rawprompt%' OR @UnsafeText LIKE N'%raw prompt%' OR @UnsafeText LIKE N'%rawcompletion%' OR @UnsafeText LIKE N'%raw completion%' OR @UnsafeText LIKE N'%rawtooloutput%' OR @UnsafeText LIKE N'%raw tool output%' OR @UnsafeText LIKE N'%entirepatch%' OR @UnsafeText LIKE N'%entire patch%' OR @UnsafeText LIKE N'%approval granted%' OR @UnsafeText LIKE N'%approved for execution%' OR @UnsafeText LIKE N'%execution permission%' OR @UnsafeText LIKE N'%execution allowed%' OR @UnsafeText LIKE N'%can execute%' OR @UnsafeText LIKE N'%authorize execution%' OR @UnsafeText LIKE N'%policy satisfied%' OR @UnsafeText LIKE N'%satisfy policy%' OR @UnsafeText LIKE N'%accepted memory%' OR @UnsafeText LIKE N'%memory accepted%' OR @UnsafeText LIKE N'%memory promoted%' OR @UnsafeText LIKE N'%promote memory%' OR @UnsafeText LIKE N'%collective memory created%' OR @UnsafeText LIKE N'%write collective memory%' OR @UnsafeText LIKE N'%write vector index%' OR @UnsafeText LIKE N'%retrieval authority%' OR @UnsafeText LIKE N'%source applied%' OR @UnsafeText LIKE N'%apply source%' OR @UnsafeText LIKE N'%apply patch%' OR @UnsafeText LIKE N'%release approved%' OR @UnsafeText LIKE N'%approve release%' OR @UnsafeText LIKE N'%ready to ship%' OR @UnsafeText LIKE N'%can ship%' OR @UnsafeText LIKE N'%workflow continued%' OR @UnsafeText LIKE N'%continue workflow%' OR @UnsafeText LIKE N'%workflow started%' OR @UnsafeText LIKE N'%start workflow%' OR @UnsafeText LIKE N'%authority transferred%' OR @UnsafeText LIKE N'%transfer authority%' THROW 54437, 'Memory proposal staging payload cannot contain raw/private reasoning or authority language.', 1;

    BEGIN TRANSACTION;
    INSERT INTO memory.MemoryProposal (MemoryProposalId, TenantId, ProjectId, ProposalKey, ProposalType, TargetMemoryScope, ProposalStatus, SourceType, SourceId, SourceAgentRole, SourceAgentId, SubjectType, SubjectId, SafeProposedMemory, SafeRationaleSummary, SafeRiskSummary, ConfidenceLabel, ConfidentialityLabel, SanitizationStatus, WorkflowRunId, WorkflowRunStepId, WorkflowCheckpointId, CorrelationId, CausationId, CreatedByActorType, CreatedByActorId, MetadataVersion, MetadataJson, IsAcceptedMemory, CreatesAcceptedMemory, PromotesMemory, WritesCollectiveMemory, WritesAgentMemory, WritesVectorIndex, IsRetrievalAuthority, IsPolicy, IsApproval, SatisfiesPolicy, GrantsApproval, GrantsExecution, StartsWorkflow, ContinuesWorkflow, MutatesSource, ApprovesRelease)
    VALUES (@MemoryProposalId, @TenantId, @ProjectId, LTRIM(RTRIM(@ProposalKey)), @ProposalType, @TargetMemoryScope, @ProposalStatus, LTRIM(RTRIM(@SourceType)), NULLIF(LTRIM(RTRIM(@SourceId)), N''), NULLIF(LTRIM(RTRIM(@SourceAgentRole)), N''), NULLIF(LTRIM(RTRIM(@SourceAgentId)), N''), NULLIF(LTRIM(RTRIM(@SubjectType)), N''), NULLIF(LTRIM(RTRIM(@SubjectId)), N''), LTRIM(RTRIM(@SafeProposedMemory)), NULLIF(LTRIM(RTRIM(@SafeRationaleSummary)), N''), NULLIF(LTRIM(RTRIM(@SafeRiskSummary)), N''), NULLIF(LTRIM(RTRIM(@ConfidenceLabel)), N''), @ConfidentialityLabel, @SanitizationStatus, @WorkflowRunId, @WorkflowRunStepId, @WorkflowCheckpointId, @CorrelationId, @CausationId, LTRIM(RTRIM(@CreatedByActorType)), LTRIM(RTRIM(@CreatedByActorId)), @MetadataVersion, @MetadataJson, @IsAcceptedMemory, @CreatesAcceptedMemory, @PromotesMemory, @WritesCollectiveMemory, @WritesAgentMemory, @WritesVectorIndex, @IsRetrievalAuthority, @IsPolicy, @IsApproval, @SatisfiesPolicy, @GrantsApproval, @GrantsExecution, @StartsWorkflow, @ContinuesWorkflow, @MutatesSource, @ApprovesRelease);

    INSERT INTO memory.MemoryProposalEvidenceReference (MemoryProposalEvidenceReferenceId, MemoryProposalId, ProjectId, EvidenceType, EvidenceId, EvidenceLabel, SafeSummary, AllowedUse, GovernanceEventId, WorkflowRunEvidenceReferenceId, WorkflowRunStepId, WorkflowCheckpointId, HandoffId, ThoughtLedgerEntryId)
    SELECT COALESCE(e.MemoryProposalEvidenceReferenceId, NEWID()), @MemoryProposalId, @ProjectId, e.EvidenceType, LTRIM(RTRIM(e.EvidenceId)), NULLIF(LTRIM(RTRIM(e.EvidenceLabel)), N''), NULLIF(LTRIM(RTRIM(e.SafeSummary)), N''), e.AllowedUse, e.GovernanceEventId, e.WorkflowRunEvidenceReferenceId, e.WorkflowRunStepId, e.WorkflowCheckpointId, e.HandoffId, e.ThoughtLedgerEntryId
    FROM OPENJSON(@EvidenceReferencesJson) WITH (MemoryProposalEvidenceReferenceId UNIQUEIDENTIFIER '$.memoryProposalEvidenceReferenceId', EvidenceType NVARCHAR(120) '$.evidenceType', EvidenceId NVARCHAR(300) '$.evidenceId', EvidenceLabel NVARCHAR(240) '$.evidenceLabel', SafeSummary NVARCHAR(1000) '$.safeSummary', AllowedUse NVARCHAR(120) '$.allowedUse', GovernanceEventId UNIQUEIDENTIFIER '$.governanceEventId', WorkflowRunEvidenceReferenceId UNIQUEIDENTIFIER '$.workflowRunEvidenceReferenceId', WorkflowRunStepId UNIQUEIDENTIFIER '$.workflowRunStepId', WorkflowCheckpointId UNIQUEIDENTIFIER '$.workflowCheckpointId', HandoffId UNIQUEIDENTIFIER '$.handoffId', ThoughtLedgerEntryId UNIQUEIDENTIFIER '$.thoughtLedgerEntryId') e;

    INSERT INTO memory.MemoryProposalGroundingReference (MemoryProposalGroundingReferenceId, MemoryProposalId, ProjectId, GroundingReferenceId, ClaimType, ClaimId, SafeSummary)
    SELECT COALESCE(g.MemoryProposalGroundingReferenceId, NEWID()), @MemoryProposalId, @ProjectId, g.GroundingReferenceId, g.ClaimType, LTRIM(RTRIM(g.ClaimId)), NULLIF(LTRIM(RTRIM(g.SafeSummary)), N'')
    FROM OPENJSON(@GroundingReferencesJson) WITH (MemoryProposalGroundingReferenceId UNIQUEIDENTIFIER '$.memoryProposalGroundingReferenceId', GroundingReferenceId UNIQUEIDENTIFIER '$.groundingReferenceId', ClaimType NVARCHAR(120) '$.claimType', ClaimId NVARCHAR(300) '$.claimId', SafeSummary NVARCHAR(1000) '$.safeSummary') g;

    INSERT INTO memory.MemoryProposalWorkflowReference (MemoryProposalWorkflowReferenceId, MemoryProposalId, ProjectId, WorkflowRunId, WorkflowRunStepId, WorkflowCheckpointId, ReferenceType, SafeSummary)
    SELECT COALESCE(w.MemoryProposalWorkflowReferenceId, NEWID()), @MemoryProposalId, @ProjectId, w.WorkflowRunId, w.WorkflowRunStepId, w.WorkflowCheckpointId, w.ReferenceType, NULLIF(LTRIM(RTRIM(w.SafeSummary)), N'')
    FROM OPENJSON(@WorkflowReferencesJson) WITH (MemoryProposalWorkflowReferenceId UNIQUEIDENTIFIER '$.memoryProposalWorkflowReferenceId', WorkflowRunId UNIQUEIDENTIFIER '$.workflowRunId', WorkflowRunStepId UNIQUEIDENTIFIER '$.workflowRunStepId', WorkflowCheckpointId UNIQUEIDENTIFIER '$.workflowCheckpointId', ReferenceType NVARCHAR(120) '$.referenceType', SafeSummary NVARCHAR(1000) '$.safeSummary') w;

    COMMIT TRANSACTION;
    EXEC memory.usp_MemoryProposal_Get @ProjectId = @ProjectId, @MemoryProposalId = @MemoryProposalId;
END;
GO

CREATE OR ALTER PROCEDURE memory.usp_MemoryProposal_ListByProject @ProjectId UNIQUEIDENTIFIER, @Take INT = 100 WITH EXECUTE AS OWNER AS
BEGIN
    SET NOCOUNT ON; SET @Take = CASE WHEN @Take < 1 THEN 1 WHEN @Take > 500 THEN 500 ELSE @Take END;
    SELECT TOP (@Take) p.MemoryProposalId, p.ProjectId, p.ProposalKey, p.ProposalType, p.TargetMemoryScope, p.ProposalStatus, p.SourceType, p.SourceId, p.SubjectType, p.SubjectId, p.SafeProposedMemory, p.WorkflowRunId, p.WorkflowRunStepId, p.WorkflowCheckpointId, p.CorrelationId, (SELECT COUNT(1) FROM memory.MemoryProposalEvidenceReference e WHERE e.MemoryProposalId = p.MemoryProposalId) AS EvidenceReferenceCount, (SELECT COUNT(1) FROM memory.MemoryProposalGroundingReference g WHERE g.MemoryProposalId = p.MemoryProposalId) AS GroundingReferenceCount, (SELECT COUNT(1) FROM memory.MemoryProposalWorkflowReference w WHERE w.MemoryProposalId = p.MemoryProposalId) AS WorkflowReferenceCount, p.CreatedUtc FROM memory.MemoryProposal p WHERE p.ProjectId = @ProjectId ORDER BY p.CreatedUtc DESC, p.MemoryProposalId DESC;
END;
GO
CREATE OR ALTER PROCEDURE memory.usp_MemoryProposal_ListByStatus @ProjectId UNIQUEIDENTIFIER, @ProposalStatus NVARCHAR(80), @Take INT = 100 WITH EXECUTE AS OWNER AS
BEGIN
    SET NOCOUNT ON; SET @Take = CASE WHEN @Take < 1 THEN 1 WHEN @Take > 500 THEN 500 ELSE @Take END;
    SELECT TOP (@Take) p.MemoryProposalId, p.ProjectId, p.ProposalKey, p.ProposalType, p.TargetMemoryScope, p.ProposalStatus, p.SourceType, p.SourceId, p.SubjectType, p.SubjectId, p.SafeProposedMemory, p.WorkflowRunId, p.WorkflowRunStepId, p.WorkflowCheckpointId, p.CorrelationId, (SELECT COUNT(1) FROM memory.MemoryProposalEvidenceReference e WHERE e.MemoryProposalId = p.MemoryProposalId) AS EvidenceReferenceCount, (SELECT COUNT(1) FROM memory.MemoryProposalGroundingReference g WHERE g.MemoryProposalId = p.MemoryProposalId) AS GroundingReferenceCount, (SELECT COUNT(1) FROM memory.MemoryProposalWorkflowReference w WHERE w.MemoryProposalId = p.MemoryProposalId) AS WorkflowReferenceCount, p.CreatedUtc FROM memory.MemoryProposal p WHERE p.ProjectId = @ProjectId AND p.ProposalStatus = @ProposalStatus ORDER BY p.CreatedUtc DESC, p.MemoryProposalId DESC;
END;
GO
CREATE OR ALTER PROCEDURE memory.usp_MemoryProposal_ListByWorkflowRun @ProjectId UNIQUEIDENTIFIER, @WorkflowRunId UNIQUEIDENTIFIER, @Take INT = 100 WITH EXECUTE AS OWNER AS
BEGIN
    SET NOCOUNT ON; SET @Take = CASE WHEN @Take < 1 THEN 1 WHEN @Take > 500 THEN 500 ELSE @Take END;
    SELECT TOP (@Take) p.MemoryProposalId, p.ProjectId, p.ProposalKey, p.ProposalType, p.TargetMemoryScope, p.ProposalStatus, p.SourceType, p.SourceId, p.SubjectType, p.SubjectId, p.SafeProposedMemory, p.WorkflowRunId, p.WorkflowRunStepId, p.WorkflowCheckpointId, p.CorrelationId, (SELECT COUNT(1) FROM memory.MemoryProposalEvidenceReference e WHERE e.MemoryProposalId = p.MemoryProposalId) AS EvidenceReferenceCount, (SELECT COUNT(1) FROM memory.MemoryProposalGroundingReference g WHERE g.MemoryProposalId = p.MemoryProposalId) AS GroundingReferenceCount, (SELECT COUNT(1) FROM memory.MemoryProposalWorkflowReference w WHERE w.MemoryProposalId = p.MemoryProposalId) AS WorkflowReferenceCount, p.CreatedUtc FROM memory.MemoryProposal p WHERE p.ProjectId = @ProjectId AND p.WorkflowRunId = @WorkflowRunId ORDER BY p.CreatedUtc DESC, p.MemoryProposalId DESC;
END;
GO
CREATE OR ALTER PROCEDURE memory.usp_MemoryProposal_ListBySource @ProjectId UNIQUEIDENTIFIER, @SourceType NVARCHAR(120), @SourceId NVARCHAR(300), @Take INT = 100 WITH EXECUTE AS OWNER AS
BEGIN
    SET NOCOUNT ON; SET @Take = CASE WHEN @Take < 1 THEN 1 WHEN @Take > 500 THEN 500 ELSE @Take END;
    SELECT TOP (@Take) p.MemoryProposalId, p.ProjectId, p.ProposalKey, p.ProposalType, p.TargetMemoryScope, p.ProposalStatus, p.SourceType, p.SourceId, p.SubjectType, p.SubjectId, p.SafeProposedMemory, p.WorkflowRunId, p.WorkflowRunStepId, p.WorkflowCheckpointId, p.CorrelationId, (SELECT COUNT(1) FROM memory.MemoryProposalEvidenceReference e WHERE e.MemoryProposalId = p.MemoryProposalId) AS EvidenceReferenceCount, (SELECT COUNT(1) FROM memory.MemoryProposalGroundingReference g WHERE g.MemoryProposalId = p.MemoryProposalId) AS GroundingReferenceCount, (SELECT COUNT(1) FROM memory.MemoryProposalWorkflowReference w WHERE w.MemoryProposalId = p.MemoryProposalId) AS WorkflowReferenceCount, p.CreatedUtc FROM memory.MemoryProposal p WHERE p.ProjectId = @ProjectId AND p.SourceType = @SourceType AND p.SourceId = @SourceId ORDER BY p.CreatedUtc DESC, p.MemoryProposalId DESC;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'IronDevMemoryRuntimeRole') IS NULL
    CREATE ROLE IronDevMemoryRuntimeRole;
GO
GRANT EXECUTE ON OBJECT::memory.usp_MemoryProposal_Create TO IronDevMemoryRuntimeRole;
GRANT EXECUTE ON OBJECT::memory.usp_MemoryProposal_Get TO IronDevMemoryRuntimeRole;
GRANT EXECUTE ON OBJECT::memory.usp_MemoryProposal_ListByProject TO IronDevMemoryRuntimeRole;
GRANT EXECUTE ON OBJECT::memory.usp_MemoryProposal_ListByStatus TO IronDevMemoryRuntimeRole;
GRANT EXECUTE ON OBJECT::memory.usp_MemoryProposal_ListByWorkflowRun TO IronDevMemoryRuntimeRole;
GRANT EXECUTE ON OBJECT::memory.usp_MemoryProposal_ListBySource TO IronDevMemoryRuntimeRole;
GRANT SELECT ON SCHEMA::memory TO IronDevMemoryRuntimeRole;
DENY INSERT, UPDATE, DELETE ON OBJECT::memory.MemoryProposal TO IronDevMemoryRuntimeRole;
DENY INSERT, UPDATE, DELETE ON OBJECT::memory.MemoryProposalEvidenceReference TO IronDevMemoryRuntimeRole;
DENY INSERT, UPDATE, DELETE ON OBJECT::memory.MemoryProposalGroundingReference TO IronDevMemoryRuntimeRole;
DENY INSERT, UPDATE, DELETE ON OBJECT::memory.MemoryProposalWorkflowReference TO IronDevMemoryRuntimeRole;
DENY ALTER ON SCHEMA::memory TO IronDevMemoryRuntimeRole;
GO
