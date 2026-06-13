IF SCHEMA_ID(N'a2a') IS NULL
    EXEC(N'CREATE SCHEMA a2a');
GO

IF OBJECT_ID(N'governance.GovernanceEvent', N'U') IS NULL
    THROW 53000, 'governance.GovernanceEvent must exist before a2a.AgentHandoff migration runs.', 1;
GO

IF OBJECT_ID(N'a2a.AgentHandoff', N'U') IS NULL
BEGIN
    CREATE TABLE a2a.AgentHandoff
    (
        AgentHandoffId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        GovernanceEventId UNIQUEIDENTIFIER NOT NULL,
        HandoffType NVARCHAR(80) NOT NULL,
        Status NVARCHAR(80) NOT NULL,
        SourceAgentId NVARCHAR(200) NOT NULL,
        SourceAgentRole NVARCHAR(80) NOT NULL,
        SourceAgentDisplayName NVARCHAR(200) NULL,
        TargetAgentId NVARCHAR(200) NOT NULL,
        TargetAgentRole NVARCHAR(80) NOT NULL,
        TargetAgentDisplayName NVARCHAR(200) NULL,
        SubjectType NVARCHAR(120) NOT NULL,
        SubjectId NVARCHAR(300) NOT NULL,
        SubjectActionName NVARCHAR(200) NULL,
        SubjectSummary NVARCHAR(1000) NULL,
        CorrelationId UNIQUEIDENTIFIER NULL,
        CausationId UNIQUEIDENTIFIER NULL,
        SupersedesHandoffId UNIQUEIDENTIFIER NULL,
        CreatedByActorType NVARCHAR(80) NOT NULL,
        CreatedByActorId NVARCHAR(200) NOT NULL,
        MetadataVersion INT NOT NULL,
        MetadataJson NVARCHAR(MAX) NOT NULL,
        GrantsApproval BIT NOT NULL CONSTRAINT DF_AgentHandoff_GrantsApproval DEFAULT 0,
        GrantsExecution BIT NOT NULL CONSTRAINT DF_AgentHandoff_GrantsExecution DEFAULT 0,
        MutatesSource BIT NOT NULL CONSTRAINT DF_AgentHandoff_MutatesSource DEFAULT 0,
        PromotesMemory BIT NOT NULL CONSTRAINT DF_AgentHandoff_PromotesMemory DEFAULT 0,
        StartsWorkflow BIT NOT NULL CONSTRAINT DF_AgentHandoff_StartsWorkflow DEFAULT 0,
        SatisfiesPolicy BIT NOT NULL CONSTRAINT DF_AgentHandoff_SatisfiesPolicy DEFAULT 0,
        TransfersAuthority BIT NOT NULL CONSTRAINT DF_AgentHandoff_TransfersAuthority DEFAULT 0,
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_AgentHandoff_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AgentHandoff PRIMARY KEY CLUSTERED (AgentHandoffId),
        CONSTRAINT CK_AgentHandoff_ProjectId_NotEmpty CHECK (ProjectId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_AgentHandoff_GovernanceEventId_NotEmpty CHECK (GovernanceEventId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_AgentHandoff_HandoffType_Allowed CHECK (HandoffType IN (N'TaskContext', N'ReviewRequest', N'EvidenceTransfer', N'RequirementTransfer', N'DebugContext', N'ImplementationContext', N'ValidationContext', N'MemoryCandidateContext', N'SourceApplyContext', N'ReleaseEvidenceContext')),
        CONSTRAINT CK_AgentHandoff_Status_Allowed CHECK (Status IN (N'Draft', N'ReadyForReview', N'Offered', N'Received', N'Rejected', N'Cancelled', N'Expired', N'Superseded')),
        CONSTRAINT CK_AgentHandoff_SourceAgentId_NotBlank CHECK (LEN(LTRIM(RTRIM(SourceAgentId))) > 0),
        CONSTRAINT CK_AgentHandoff_TargetAgentId_NotBlank CHECK (LEN(LTRIM(RTRIM(TargetAgentId))) > 0),
        CONSTRAINT CK_AgentHandoff_SourceTarget_Different CHECK (LOWER(LTRIM(RTRIM(SourceAgentId))) <> LOWER(LTRIM(RTRIM(TargetAgentId)))),
        CONSTRAINT CK_AgentHandoff_SourceAgentRole_Allowed CHECK (SourceAgentRole IN (N'Planner', N'Builder', N'Critic', N'Tester', N'Memory', N'Conscience', N'Reviewer', N'Operator', N'ToolGateway', N'Unknown')),
        CONSTRAINT CK_AgentHandoff_TargetAgentRole_Allowed CHECK (TargetAgentRole IN (N'Planner', N'Builder', N'Critic', N'Tester', N'Memory', N'Conscience', N'Reviewer', N'Operator', N'ToolGateway', N'Unknown')),
        CONSTRAINT CK_AgentHandoff_SubjectType_Allowed CHECK (SubjectType IN (N'ToolRequest', N'ApprovalPackage', N'PolicyRequirement', N'DogfoodReceipt', N'ValidationRun', N'RunReport', N'CodePatchCandidate', N'MemoryCandidate', N'DebugSession', N'WorkflowStepCandidate')),
        CONSTRAINT CK_AgentHandoff_SubjectId_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectId))) > 0),
        CONSTRAINT CK_AgentHandoff_ActorType_NotBlank CHECK (LEN(LTRIM(RTRIM(CreatedByActorType))) > 0),
        CONSTRAINT CK_AgentHandoff_ActorId_NotBlank CHECK (LEN(LTRIM(RTRIM(CreatedByActorId))) > 0),
        CONSTRAINT CK_AgentHandoff_MetadataVersion_Positive CHECK (MetadataVersion > 0),
        CONSTRAINT CK_AgentHandoff_MetadataJson_IsJson CHECK (ISJSON(MetadataJson) = 1),
        CONSTRAINT CK_AgentHandoff_NoApprovalGrant CHECK (GrantsApproval = 0),
        CONSTRAINT CK_AgentHandoff_NoExecutionGrant CHECK (GrantsExecution = 0),
        CONSTRAINT CK_AgentHandoff_NoSourceMutation CHECK (MutatesSource = 0),
        CONSTRAINT CK_AgentHandoff_NoMemoryPromotion CHECK (PromotesMemory = 0),
        CONSTRAINT CK_AgentHandoff_NoWorkflowStart CHECK (StartsWorkflow = 0),
        CONSTRAINT CK_AgentHandoff_NoPolicySatisfaction CHECK (SatisfiesPolicy = 0),
        CONSTRAINT CK_AgentHandoff_NoAuthorityTransfer CHECK (TransfersAuthority = 0)
    );
END;
GO

IF OBJECT_ID(N'a2a.AgentHandoffEvidenceReference', N'U') IS NULL
BEGIN
    CREATE TABLE a2a.AgentHandoffEvidenceReference
    (
        AgentHandoffEvidenceReferenceId UNIQUEIDENTIFIER NOT NULL,
        AgentHandoffId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        EvidenceType NVARCHAR(120) NOT NULL,
        EvidenceId NVARCHAR(300) NOT NULL,
        EvidenceLabel NVARCHAR(300) NULL,
        EvidenceSummary NVARCHAR(1000) NULL,
        GovernanceEventId UNIQUEIDENTIFIER NULL,
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_AgentHandoffEvidenceReference_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AgentHandoffEvidenceReference PRIMARY KEY CLUSTERED (AgentHandoffEvidenceReferenceId),
        CONSTRAINT FK_AgentHandoffEvidenceReference_Handoff FOREIGN KEY (AgentHandoffId) REFERENCES a2a.AgentHandoff(AgentHandoffId),
        CONSTRAINT CK_AgentHandoffEvidenceReference_ProjectId_NotEmpty CHECK (ProjectId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_AgentHandoffEvidenceReference_EvidenceType_Allowed CHECK (EvidenceType IN (N'GovernanceEvent', N'ToolRequest', N'ToolGateDecision', N'ApprovalRequirementEvaluation', N'ApprovalPackage', N'ApprovalDecision', N'PolicyDecisionEvent', N'DogfoodReceipt', N'ThoughtLedgerReference', N'ValidationOutput', N'RunReport', N'HumanNote', N'CriticReview', N'CodeStandardsReview')),
        CONSTRAINT CK_AgentHandoffEvidenceReference_EvidenceId_NotBlank CHECK (LEN(LTRIM(RTRIM(EvidenceId))) > 0)
    );
END;
GO

IF OBJECT_ID(N'a2a.AgentHandoffEvidenceAllowedUse', N'U') IS NULL
BEGIN
    CREATE TABLE a2a.AgentHandoffEvidenceAllowedUse
    (
        AgentHandoffEvidenceAllowedUseId UNIQUEIDENTIFIER NOT NULL,
        AgentHandoffEvidenceReferenceId UNIQUEIDENTIFIER NOT NULL,
        AgentHandoffId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        AllowedUse NVARCHAR(120) NOT NULL,
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_AgentHandoffEvidenceAllowedUse_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AgentHandoffEvidenceAllowedUse PRIMARY KEY CLUSTERED (AgentHandoffEvidenceAllowedUseId),
        CONSTRAINT FK_AgentHandoffEvidenceAllowedUse_Reference FOREIGN KEY (AgentHandoffEvidenceReferenceId) REFERENCES a2a.AgentHandoffEvidenceReference(AgentHandoffEvidenceReferenceId),
        CONSTRAINT FK_AgentHandoffEvidenceAllowedUse_Handoff FOREIGN KEY (AgentHandoffId) REFERENCES a2a.AgentHandoff(AgentHandoffId),
        CONSTRAINT UQ_AgentHandoffEvidenceAllowedUse_Reference_Use UNIQUE (AgentHandoffEvidenceReferenceId, AllowedUse),
        CONSTRAINT CK_AgentHandoffEvidenceAllowedUse_ProjectId_NotEmpty CHECK (ProjectId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_AgentHandoffEvidenceAllowedUse_Allowed CHECK (AllowedUse IN (N'Context', N'Review', N'Debugging', N'Validation', N'Traceability', N'RequirementEvaluation', N'HumanDecisionSupport', N'AuditReference', N'PolicyInput', N'HandoffExplanation'))
    );
END;
GO

IF OBJECT_ID(N'a2a.AgentHandoffConstraint', N'U') IS NULL
BEGIN
    CREATE TABLE a2a.AgentHandoffConstraint
    (
        AgentHandoffConstraintId UNIQUEIDENTIFIER NOT NULL,
        AgentHandoffId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        ConstraintType NVARCHAR(120) NOT NULL,
        ConstraintCode NVARCHAR(160) NOT NULL,
        Description NVARCHAR(1000) NOT NULL,
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_AgentHandoffConstraint_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AgentHandoffConstraint PRIMARY KEY CLUSTERED (AgentHandoffConstraintId),
        CONSTRAINT FK_AgentHandoffConstraint_Handoff FOREIGN KEY (AgentHandoffId) REFERENCES a2a.AgentHandoff(AgentHandoffId),
        CONSTRAINT CK_AgentHandoffConstraint_ProjectId_NotEmpty CHECK (ProjectId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_AgentHandoffConstraint_ConstraintType_Allowed CHECK (ConstraintType IN (N'RequiresHumanReview', N'RequiresApprovalDecision', N'RequiresPolicyEvaluation', N'RequiresValidation', N'RequiresDogfoodReceipt', N'RequiresSourceApplyApproval', N'RequiresMemoryPromotionApproval', N'EvidenceOnly', N'DoNotExecute', N'DoNotMutateSource', N'DoNotPromoteMemory', N'DoNotContinueWorkflow')),
        CONSTRAINT CK_AgentHandoffConstraint_Code_NotBlank CHECK (LEN(LTRIM(RTRIM(ConstraintCode))) > 0),
        CONSTRAINT CK_AgentHandoffConstraint_Description_NotBlank CHECK (LEN(LTRIM(RTRIM(Description))) > 0)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AgentHandoff_Project_CreatedUtc' AND object_id = OBJECT_ID(N'a2a.AgentHandoff'))
    CREATE INDEX IX_AgentHandoff_Project_CreatedUtc ON a2a.AgentHandoff(ProjectId, CreatedUtc DESC, AgentHandoffId DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AgentHandoff_Project_Correlation_CreatedUtc' AND object_id = OBJECT_ID(N'a2a.AgentHandoff'))
    CREATE INDEX IX_AgentHandoff_Project_Correlation_CreatedUtc ON a2a.AgentHandoff(ProjectId, CorrelationId, CreatedUtc DESC, AgentHandoffId DESC) WHERE CorrelationId IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AgentHandoff_Project_Subject_CreatedUtc' AND object_id = OBJECT_ID(N'a2a.AgentHandoff'))
    CREATE INDEX IX_AgentHandoff_Project_Subject_CreatedUtc ON a2a.AgentHandoff(ProjectId, SubjectType, SubjectId, CreatedUtc DESC, AgentHandoffId DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AgentHandoffEvidenceReference_Handoff' AND object_id = OBJECT_ID(N'a2a.AgentHandoffEvidenceReference'))
    CREATE INDEX IX_AgentHandoffEvidenceReference_Handoff ON a2a.AgentHandoffEvidenceReference(AgentHandoffId, CreatedUtc, AgentHandoffEvidenceReferenceId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AgentHandoffEvidenceAllowedUse_Reference' AND object_id = OBJECT_ID(N'a2a.AgentHandoffEvidenceAllowedUse'))
    CREATE INDEX IX_AgentHandoffEvidenceAllowedUse_Reference ON a2a.AgentHandoffEvidenceAllowedUse(AgentHandoffEvidenceReferenceId, AllowedUse);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AgentHandoffConstraint_Handoff' AND object_id = OBJECT_ID(N'a2a.AgentHandoffConstraint'))
    CREATE INDEX IX_AgentHandoffConstraint_Handoff ON a2a.AgentHandoffConstraint(AgentHandoffId, CreatedUtc, AgentHandoffConstraintId);
GO

CREATE OR ALTER TRIGGER a2a.TR_AgentHandoff_ValidateInsert
ON a2a.AgentHandoff
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        LEFT JOIN governance.GovernanceEvent ge ON ge.EventId = i.GovernanceEventId
        WHERE ge.EventId IS NULL OR ge.ProjectId <> i.ProjectId
    )
        THROW 53010, 'Agent handoff governance event must exist and belong to the same project.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        WHERE JSON_VALUE(i.MetadataJson, '$.grantsApproval') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.grantsExecution') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.mutatesSource') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.promotesMemory') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.startsWorkflow') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.satisfiesPolicy') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.transfersAuthority') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.approvalTransferred') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.canExecute') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.sourceApplyAllowed') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.memoryPromotionAllowed') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.workflowContinues') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.releaseApproved') = N'true'
    )
        THROW 53011, 'Agent handoff metadata must not claim approval, execution, source mutation, memory promotion, workflow, policy satisfaction, release approval, or authority transfer.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        WHERE LOWER(i.MetadataJson) LIKE N'%rawprompt%'
           OR LOWER(i.MetadataJson) LIKE N'%raw prompt%'
           OR LOWER(i.MetadataJson) LIKE N'%rawcompletion%'
           OR LOWER(i.MetadataJson) LIKE N'%raw completion%'
           OR LOWER(i.MetadataJson) LIKE N'%chainofthought%'
           OR LOWER(i.MetadataJson) LIKE N'%chain-of-thought%'
           OR LOWER(i.MetadataJson) LIKE N'%chain of thought%'
           OR LOWER(i.MetadataJson) LIKE N'%scratchpad%'
           OR LOWER(i.MetadataJson) LIKE N'%private reasoning%'
           OR LOWER(i.MetadataJson) LIKE N'%hidden reasoning%'
           OR LOWER(i.MetadataJson) LIKE N'%rawtooloutput%'
           OR LOWER(i.MetadataJson) LIKE N'%entirepatch%'
    )
        THROW 53012, 'Agent handoff metadata must not contain raw/private reasoning or oversized raw evidence markers.', 1;
END;
GO

CREATE OR ALTER TRIGGER a2a.TR_AgentHandoffEvidenceReference_ValidateInsert
ON a2a.AgentHandoffEvidenceReference
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        JOIN a2a.AgentHandoff h ON h.AgentHandoffId = i.AgentHandoffId
        WHERE h.ProjectId <> i.ProjectId
    )
        THROW 53020, 'Agent handoff evidence project must match parent handoff project.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        LEFT JOIN governance.GovernanceEvent ge ON ge.EventId = i.GovernanceEventId
        WHERE i.GovernanceEventId IS NOT NULL AND (ge.EventId IS NULL OR ge.ProjectId <> i.ProjectId)
    )
        THROW 53021, 'Agent handoff evidence governance event must belong to the same project.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        WHERE LOWER(COALESCE(i.EvidenceLabel, N'')) LIKE N'%approval granted%'
           OR LOWER(COALESCE(i.EvidenceSummary, N'')) LIKE N'%approval granted%'
           OR LOWER(COALESCE(i.EvidenceLabel, N'')) LIKE N'%approved to execute%'
           OR LOWER(COALESCE(i.EvidenceSummary, N'')) LIKE N'%approved to execute%'
           OR LOWER(COALESCE(i.EvidenceLabel, N'')) LIKE N'%execution permission%'
           OR LOWER(COALESCE(i.EvidenceSummary, N'')) LIKE N'%execution permission%'
           OR LOWER(COALESCE(i.EvidenceLabel, N'')) LIKE N'%policy satisfied%'
           OR LOWER(COALESCE(i.EvidenceSummary, N'')) LIKE N'%policy satisfied%'
           OR LOWER(COALESCE(i.EvidenceLabel, N'')) LIKE N'%release approved%'
           OR LOWER(COALESCE(i.EvidenceSummary, N'')) LIKE N'%release approved%'
           OR LOWER(COALESCE(i.EvidenceLabel, N'')) LIKE N'%can ship%'
           OR LOWER(COALESCE(i.EvidenceSummary, N'')) LIKE N'%can ship%'
           OR LOWER(COALESCE(i.EvidenceLabel, N'')) LIKE N'%authority transfer%'
           OR LOWER(COALESCE(i.EvidenceSummary, N'')) LIKE N'%authority transfer%'
           OR LOWER(COALESCE(i.EvidenceLabel, N'')) LIKE N'%rawprompt%'
           OR LOWER(COALESCE(i.EvidenceSummary, N'')) LIKE N'%rawprompt%'
           OR LOWER(COALESCE(i.EvidenceLabel, N'')) LIKE N'%chain-of-thought%'
           OR LOWER(COALESCE(i.EvidenceSummary, N'')) LIKE N'%chain-of-thought%'
           OR LOWER(COALESCE(i.EvidenceLabel, N'')) LIKE N'%private reasoning%'
           OR LOWER(COALESCE(i.EvidenceSummary, N'')) LIKE N'%private reasoning%'
           OR LOWER(COALESCE(i.EvidenceLabel, N'')) LIKE N'%scratchpad%'
           OR LOWER(COALESCE(i.EvidenceSummary, N'')) LIKE N'%scratchpad%'
    )
        THROW 53022, 'Agent handoff evidence text must not contain authority or raw/private reasoning markers.', 1;
END;
GO

CREATE OR ALTER TRIGGER a2a.TR_AgentHandoffEvidenceAllowedUse_ValidateInsert
ON a2a.AgentHandoffEvidenceAllowedUse
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        JOIN a2a.AgentHandoffEvidenceReference r ON r.AgentHandoffEvidenceReferenceId = i.AgentHandoffEvidenceReferenceId
        WHERE r.ProjectId <> i.ProjectId OR r.AgentHandoffId <> i.AgentHandoffId
    )
        THROW 53030, 'Agent handoff allowed use must match parent evidence reference.', 1;
END;
GO

CREATE OR ALTER TRIGGER a2a.TR_AgentHandoffConstraint_ValidateInsert
ON a2a.AgentHandoffConstraint
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        JOIN a2a.AgentHandoff h ON h.AgentHandoffId = i.AgentHandoffId
        WHERE h.ProjectId <> i.ProjectId
    )
        THROW 53040, 'Agent handoff constraint project must match parent handoff project.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        WHERE LOWER(i.ConstraintCode) LIKE N'%approval_granted%'
           OR LOWER(i.Description) LIKE N'%approval granted%'
           OR LOWER(i.ConstraintCode) LIKE N'%execution_granted%'
           OR LOWER(i.Description) LIKE N'%execution granted%'
           OR LOWER(i.ConstraintCode) LIKE N'%source_apply_granted%'
           OR LOWER(i.Description) LIKE N'%source apply permission%'
           OR LOWER(i.ConstraintCode) LIKE N'%memory_promotion_granted%'
           OR LOWER(i.Description) LIKE N'%memory promotion permission%'
           OR LOWER(i.ConstraintCode) LIKE N'%workflow_continuation_granted%'
           OR LOWER(i.Description) LIKE N'%workflow continuation granted%'
           OR LOWER(i.ConstraintCode) LIKE N'%release_approved%'
           OR LOWER(i.Description) LIKE N'%release approved%'
           OR LOWER(i.Description) LIKE N'%chain-of-thought%'
           OR LOWER(i.Description) LIKE N'%private reasoning%'
           OR LOWER(i.Description) LIKE N'%scratchpad%'
    )
        THROW 53041, 'Agent handoff constraint text must not grant authority or contain raw/private reasoning markers.', 1;
END;
GO

CREATE OR ALTER TRIGGER a2a.TR_AgentHandoff_BlockUpdateDelete
ON a2a.AgentHandoff
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 53050, 'Agent handoff records are append-only.', 1;
END;
GO

CREATE OR ALTER TRIGGER a2a.TR_AgentHandoffEvidenceReference_BlockUpdateDelete
ON a2a.AgentHandoffEvidenceReference
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 53051, 'Agent handoff evidence references are append-only.', 1;
END;
GO

CREATE OR ALTER TRIGGER a2a.TR_AgentHandoffEvidenceAllowedUse_BlockUpdateDelete
ON a2a.AgentHandoffEvidenceAllowedUse
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 53052, 'Agent handoff evidence allowed uses are append-only.', 1;
END;
GO

CREATE OR ALTER TRIGGER a2a.TR_AgentHandoffConstraint_BlockUpdateDelete
ON a2a.AgentHandoffConstraint
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 53053, 'Agent handoff constraints are append-only.', 1;
END;
GO

CREATE OR ALTER PROCEDURE a2a.usp_AgentHandoff_Get
    @ProjectId UNIQUEIDENTIFIER,
    @AgentHandoffId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (1)
        AgentHandoffId,
        ProjectId,
        GovernanceEventId,
        HandoffType,
        Status,
        SourceAgentId,
        SourceAgentRole,
        SourceAgentDisplayName,
        TargetAgentId,
        TargetAgentRole,
        TargetAgentDisplayName,
        SubjectType,
        SubjectId,
        SubjectActionName,
        SubjectSummary,
        CorrelationId,
        CausationId,
        SupersedesHandoffId,
        CreatedByActorType,
        CreatedByActorId,
        MetadataVersion,
        MetadataJson,
        GrantsApproval,
        GrantsExecution,
        MutatesSource,
        PromotesMemory,
        StartsWorkflow,
        SatisfiesPolicy,
        TransfersAuthority,
        CreatedUtc
    FROM a2a.AgentHandoff
    WHERE ProjectId = @ProjectId
      AND AgentHandoffId = @AgentHandoffId;

    SELECT
        AgentHandoffEvidenceReferenceId,
        EvidenceType,
        EvidenceId,
        EvidenceLabel,
        EvidenceSummary,
        GovernanceEventId
    FROM a2a.AgentHandoffEvidenceReference
    WHERE ProjectId = @ProjectId
      AND AgentHandoffId = @AgentHandoffId
    ORDER BY CreatedUtc, AgentHandoffEvidenceReferenceId;

    SELECT
        AgentHandoffEvidenceReferenceId,
        AllowedUse
    FROM a2a.AgentHandoffEvidenceAllowedUse
    WHERE ProjectId = @ProjectId
      AND AgentHandoffId = @AgentHandoffId
    ORDER BY CreatedUtc, AgentHandoffEvidenceAllowedUseId;

    SELECT
        ConstraintType,
        ConstraintCode,
        Description
    FROM a2a.AgentHandoffConstraint
    WHERE ProjectId = @ProjectId
      AND AgentHandoffId = @AgentHandoffId
    ORDER BY CreatedUtc, AgentHandoffConstraintId;
END;
GO

CREATE OR ALTER PROCEDURE a2a.usp_AgentHandoff_Create
    @AgentHandoffId UNIQUEIDENTIFIER,
    @ProjectId UNIQUEIDENTIFIER,
    @GovernanceEventId UNIQUEIDENTIFIER,
    @HandoffType NVARCHAR(80),
    @Status NVARCHAR(80),
    @SourceAgentId NVARCHAR(200),
    @SourceAgentRole NVARCHAR(80),
    @SourceAgentDisplayName NVARCHAR(200) = NULL,
    @TargetAgentId NVARCHAR(200),
    @TargetAgentRole NVARCHAR(80),
    @TargetAgentDisplayName NVARCHAR(200) = NULL,
    @SubjectType NVARCHAR(120),
    @SubjectId NVARCHAR(300),
    @SubjectActionName NVARCHAR(200) = NULL,
    @SubjectSummary NVARCHAR(1000) = NULL,
    @CorrelationId UNIQUEIDENTIFIER = NULL,
    @CausationId UNIQUEIDENTIFIER = NULL,
    @SupersedesHandoffId UNIQUEIDENTIFIER = NULL,
    @CreatedByActorType NVARCHAR(80),
    @CreatedByActorId NVARCHAR(200),
    @MetadataVersion INT,
    @MetadataJson NVARCHAR(MAX),
    @EvidenceReferencesJson NVARCHAR(MAX),
    @ConstraintsJson NVARCHAR(MAX),
    @GovernanceEventPayloadJson NVARCHAR(MAX),
    @CreatedUtc DATETIMEOFFSET(7) = NULL
AS
BEGIN
    SET XACT_ABORT ON;
    SET NOCOUNT ON;

    IF ISJSON(@EvidenceReferencesJson) <> 1
        THROW 53060, 'EvidenceReferencesJson must be valid JSON.', 1;

    IF ISJSON(@ConstraintsJson) <> 1
        THROW 53061, 'ConstraintsJson must be valid JSON.', 1;

    DECLARE @EffectiveCreatedUtc DATETIMEOFFSET(7) = COALESCE(@CreatedUtc, SYSUTCDATETIME());
    DECLARE @EffectiveCorrelationId UNIQUEIDENTIFIER = COALESCE(@CorrelationId, @AgentHandoffId);

    DECLARE @EvidenceRows TABLE
    (
        RowIndex INT NOT NULL,
        AgentHandoffEvidenceReferenceId UNIQUEIDENTIFIER NOT NULL,
        EvidenceType NVARCHAR(120) NOT NULL,
        EvidenceId NVARCHAR(300) NOT NULL,
        EvidenceLabel NVARCHAR(300) NULL,
        EvidenceSummary NVARCHAR(1000) NULL,
        GovernanceEventId UNIQUEIDENTIFIER NULL,
        AllowedUsesJson NVARCHAR(MAX) NOT NULL
    );

    INSERT INTO @EvidenceRows
    (
        RowIndex,
        AgentHandoffEvidenceReferenceId,
        EvidenceType,
        EvidenceId,
        EvidenceLabel,
        EvidenceSummary,
        GovernanceEventId,
        AllowedUsesJson
    )
    SELECT
        TRY_CONVERT(INT, j.[key]),
        NEWID(),
        LTRIM(RTRIM(JSON_VALUE(j.value, '$.evidenceType'))),
        LTRIM(RTRIM(JSON_VALUE(j.value, '$.EvidenceId'))),
        NULLIF(LTRIM(RTRIM(COALESCE(JSON_VALUE(j.value, '$.EvidenceLabel'), N''))), N''),
        NULLIF(LTRIM(RTRIM(COALESCE(JSON_VALUE(j.value, '$.EvidenceSummary'), N''))), N''),
        TRY_CONVERT(UNIQUEIDENTIFIER, JSON_VALUE(j.value, '$.GovernanceEventId')),
        JSON_QUERY(j.value, '$.allowedUses')
    FROM OPENJSON(@EvidenceReferencesJson) j;

    IF NOT EXISTS (SELECT 1 FROM @EvidenceRows)
        THROW 53062, 'At least one evidence reference is required.', 1;

    IF EXISTS (SELECT 1 FROM @EvidenceRows WHERE EvidenceType IS NULL OR EvidenceId IS NULL OR LEN(EvidenceId) = 0 OR AllowedUsesJson IS NULL OR ISJSON(AllowedUsesJson) <> 1)
        THROW 53063, 'Evidence references must include type, id, and allowed uses.', 1;

    IF EXISTS (SELECT 1 FROM @EvidenceRows e WHERE NOT EXISTS (SELECT 1 FROM OPENJSON(e.AllowedUsesJson)))
        THROW 53064, 'Evidence references must include at least one allowed use.', 1;

    DECLARE @ConstraintRows TABLE
    (
        RowIndex INT NOT NULL,
        AgentHandoffConstraintId UNIQUEIDENTIFIER NOT NULL,
        ConstraintType NVARCHAR(120) NOT NULL,
        ConstraintCode NVARCHAR(160) NOT NULL,
        Description NVARCHAR(1000) NOT NULL
    );

    INSERT INTO @ConstraintRows
    (
        RowIndex,
        AgentHandoffConstraintId,
        ConstraintType,
        ConstraintCode,
        Description
    )
    SELECT
        TRY_CONVERT(INT, j.[key]),
        NEWID(),
        LTRIM(RTRIM(JSON_VALUE(j.value, '$.constraintType'))),
        LTRIM(RTRIM(JSON_VALUE(j.value, '$.ConstraintCode'))),
        LTRIM(RTRIM(JSON_VALUE(j.value, '$.Description')))
    FROM OPENJSON(@ConstraintsJson) j;

    IF NOT EXISTS (SELECT 1 FROM @ConstraintRows)
        THROW 53065, 'At least one constraint is required.', 1;

    IF EXISTS (SELECT 1 FROM @ConstraintRows WHERE ConstraintType IS NULL OR ConstraintCode IS NULL OR Description IS NULL OR LEN(ConstraintCode) = 0 OR LEN(Description) = 0)
        THROW 53066, 'Constraints must include type, code, and description.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF EXISTS (SELECT 1 FROM a2a.AgentHandoff WHERE AgentHandoffId = @AgentHandoffId)
            THROW 53067, 'Agent handoff already exists.', 1;

        INSERT INTO governance.GovernanceEvent
        (
            EventId,
            ProjectId,
            EventType,
            ActorType,
            ActorId,
            CorrelationId,
            CausationId,
            SubjectType,
            SubjectId,
            PayloadVersion,
            PayloadJson,
            CreatedUtc
        )
        VALUES
        (
            @GovernanceEventId,
            @ProjectId,
            N'a2a.handoff.recorded',
            @CreatedByActorType,
            @CreatedByActorId,
            @EffectiveCorrelationId,
            @CausationId,
            N'agent_handoff',
            CONVERT(NVARCHAR(36), @AgentHandoffId),
            1,
            @GovernanceEventPayloadJson,
            @EffectiveCreatedUtc
        );

        INSERT INTO a2a.AgentHandoff
        (
            AgentHandoffId,
            ProjectId,
            GovernanceEventId,
            HandoffType,
            Status,
            SourceAgentId,
            SourceAgentRole,
            SourceAgentDisplayName,
            TargetAgentId,
            TargetAgentRole,
            TargetAgentDisplayName,
            SubjectType,
            SubjectId,
            SubjectActionName,
            SubjectSummary,
            CorrelationId,
            CausationId,
            SupersedesHandoffId,
            CreatedByActorType,
            CreatedByActorId,
            MetadataVersion,
            MetadataJson,
            GrantsApproval,
            GrantsExecution,
            MutatesSource,
            PromotesMemory,
            StartsWorkflow,
            SatisfiesPolicy,
            TransfersAuthority,
            CreatedUtc
        )
        VALUES
        (
            @AgentHandoffId,
            @ProjectId,
            @GovernanceEventId,
            LTRIM(RTRIM(@HandoffType)),
            LTRIM(RTRIM(@Status)),
            LTRIM(RTRIM(@SourceAgentId)),
            LTRIM(RTRIM(@SourceAgentRole)),
            NULLIF(LTRIM(RTRIM(COALESCE(@SourceAgentDisplayName, N''))), N''),
            LTRIM(RTRIM(@TargetAgentId)),
            LTRIM(RTRIM(@TargetAgentRole)),
            NULLIF(LTRIM(RTRIM(COALESCE(@TargetAgentDisplayName, N''))), N''),
            LTRIM(RTRIM(@SubjectType)),
            LTRIM(RTRIM(@SubjectId)),
            NULLIF(LTRIM(RTRIM(COALESCE(@SubjectActionName, N''))), N''),
            NULLIF(LTRIM(RTRIM(COALESCE(@SubjectSummary, N''))), N''),
            @EffectiveCorrelationId,
            @CausationId,
            @SupersedesHandoffId,
            LTRIM(RTRIM(@CreatedByActorType)),
            LTRIM(RTRIM(@CreatedByActorId)),
            @MetadataVersion,
            LTRIM(RTRIM(@MetadataJson)),
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            @EffectiveCreatedUtc
        );

        INSERT INTO a2a.AgentHandoffEvidenceReference
        (
            AgentHandoffEvidenceReferenceId,
            AgentHandoffId,
            ProjectId,
            EvidenceType,
            EvidenceId,
            EvidenceLabel,
            EvidenceSummary,
            GovernanceEventId,
            CreatedUtc
        )
        SELECT
            AgentHandoffEvidenceReferenceId,
            @AgentHandoffId,
            @ProjectId,
            EvidenceType,
            EvidenceId,
            EvidenceLabel,
            EvidenceSummary,
            GovernanceEventId,
            @EffectiveCreatedUtc
        FROM @EvidenceRows;

        INSERT INTO a2a.AgentHandoffEvidenceAllowedUse
        (
            AgentHandoffEvidenceAllowedUseId,
            AgentHandoffEvidenceReferenceId,
            AgentHandoffId,
            ProjectId,
            AllowedUse,
            CreatedUtc
        )
        SELECT
            NEWID(),
            e.AgentHandoffEvidenceReferenceId,
            @AgentHandoffId,
            @ProjectId,
            LTRIM(RTRIM(CONVERT(NVARCHAR(120), allowedUse.value))),
            @EffectiveCreatedUtc
        FROM @EvidenceRows e
        CROSS APPLY OPENJSON(e.AllowedUsesJson) allowedUse;

        INSERT INTO a2a.AgentHandoffConstraint
        (
            AgentHandoffConstraintId,
            AgentHandoffId,
            ProjectId,
            ConstraintType,
            ConstraintCode,
            Description,
            CreatedUtc
        )
        SELECT
            AgentHandoffConstraintId,
            @AgentHandoffId,
            @ProjectId,
            ConstraintType,
            ConstraintCode,
            Description,
            @EffectiveCreatedUtc
        FROM @ConstraintRows;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;

    EXEC a2a.usp_AgentHandoff_Get @ProjectId = @ProjectId, @AgentHandoffId = @AgentHandoffId;
END;
GO

CREATE OR ALTER PROCEDURE a2a.usp_AgentHandoff_ListByProject
    @ProjectId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @BoundedTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@BoundedTake)
        h.AgentHandoffId,
        h.ProjectId,
        h.HandoffType,
        h.Status,
        h.SourceAgentId,
        h.TargetAgentId,
        h.SubjectType,
        h.SubjectId,
        (SELECT COUNT(1) FROM a2a.AgentHandoffEvidenceReference er WHERE er.AgentHandoffId = h.AgentHandoffId) AS EvidenceReferenceCount,
        (SELECT COUNT(1) FROM a2a.AgentHandoffConstraint c WHERE c.AgentHandoffId = h.AgentHandoffId) AS ConstraintCount,
        h.CreatedUtc
    FROM a2a.AgentHandoff h
    WHERE h.ProjectId = @ProjectId
    ORDER BY h.CreatedUtc DESC, h.AgentHandoffId DESC;
END;
GO

CREATE OR ALTER PROCEDURE a2a.usp_AgentHandoff_ListByCorrelation
    @ProjectId UNIQUEIDENTIFIER,
    @CorrelationId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @BoundedTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@BoundedTake)
        h.AgentHandoffId,
        h.ProjectId,
        h.HandoffType,
        h.Status,
        h.SourceAgentId,
        h.TargetAgentId,
        h.SubjectType,
        h.SubjectId,
        (SELECT COUNT(1) FROM a2a.AgentHandoffEvidenceReference er WHERE er.AgentHandoffId = h.AgentHandoffId) AS EvidenceReferenceCount,
        (SELECT COUNT(1) FROM a2a.AgentHandoffConstraint c WHERE c.AgentHandoffId = h.AgentHandoffId) AS ConstraintCount,
        h.CreatedUtc
    FROM a2a.AgentHandoff h
    WHERE h.ProjectId = @ProjectId
      AND h.CorrelationId = @CorrelationId
    ORDER BY h.CreatedUtc DESC, h.AgentHandoffId DESC;
END;
GO

CREATE OR ALTER PROCEDURE a2a.usp_AgentHandoff_ListBySubject
    @ProjectId UNIQUEIDENTIFIER,
    @SubjectType NVARCHAR(120),
    @SubjectId NVARCHAR(300),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @BoundedTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@BoundedTake)
        h.AgentHandoffId,
        h.ProjectId,
        h.HandoffType,
        h.Status,
        h.SourceAgentId,
        h.TargetAgentId,
        h.SubjectType,
        h.SubjectId,
        (SELECT COUNT(1) FROM a2a.AgentHandoffEvidenceReference er WHERE er.AgentHandoffId = h.AgentHandoffId) AS EvidenceReferenceCount,
        (SELECT COUNT(1) FROM a2a.AgentHandoffConstraint c WHERE c.AgentHandoffId = h.AgentHandoffId) AS ConstraintCount,
        h.CreatedUtc
    FROM a2a.AgentHandoff h
    WHERE h.ProjectId = @ProjectId
      AND h.SubjectType = LTRIM(RTRIM(@SubjectType))
      AND h.SubjectId = LTRIM(RTRIM(@SubjectId))
    ORDER BY h.CreatedUtc DESC, h.AgentHandoffId DESC;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'IronDevGovernanceEventRuntimeRole') IS NOT NULL
BEGIN
    GRANT EXECUTE ON OBJECT::a2a.usp_AgentHandoff_Create TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::a2a.usp_AgentHandoff_Get TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::a2a.usp_AgentHandoff_ListByProject TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::a2a.usp_AgentHandoff_ListByCorrelation TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::a2a.usp_AgentHandoff_ListBySubject TO IronDevGovernanceEventRuntimeRole;
    GRANT SELECT ON SCHEMA::a2a TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::a2a.AgentHandoff TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::a2a.AgentHandoffEvidenceReference TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::a2a.AgentHandoffEvidenceAllowedUse TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::a2a.AgentHandoffConstraint TO IronDevGovernanceEventRuntimeRole;
    DENY ALTER ON SCHEMA::a2a TO IronDevGovernanceEventRuntimeRole;
END;
GO
