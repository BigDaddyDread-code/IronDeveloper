IF SCHEMA_ID(N'workflow') IS NULL
    EXEC(N'CREATE SCHEMA workflow');
GO

IF OBJECT_ID(N'workflow.WorkflowRun', N'U') IS NULL
BEGIN
    CREATE TABLE workflow.WorkflowRun
    (
        WorkflowRunId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        WorkflowType NVARCHAR(120) NOT NULL,
        WorkflowName NVARCHAR(200) NOT NULL,
        Status NVARCHAR(80) NOT NULL,
        SubjectType NVARCHAR(120) NOT NULL,
        SubjectId NVARCHAR(300) NOT NULL,
        SubjectSummary NVARCHAR(1000) NULL,
        CorrelationId UNIQUEIDENTIFIER NULL,
        CausationId UNIQUEIDENTIFIER NULL,
        CreatedByActorType NVARCHAR(80) NOT NULL,
        CreatedByActorId NVARCHAR(200) NOT NULL,
        MetadataVersion INT NOT NULL,
        MetadataJson NVARCHAR(MAX) NOT NULL,
        GrantsApproval BIT NOT NULL CONSTRAINT DF_WorkflowRun_GrantsApproval DEFAULT 0,
        GrantsExecution BIT NOT NULL CONSTRAINT DF_WorkflowRun_GrantsExecution DEFAULT 0,
        MutatesSource BIT NOT NULL CONSTRAINT DF_WorkflowRun_MutatesSource DEFAULT 0,
        PromotesMemory BIT NOT NULL CONSTRAINT DF_WorkflowRun_PromotesMemory DEFAULT 0,
        StartsWorkflow BIT NOT NULL CONSTRAINT DF_WorkflowRun_StartsWorkflow DEFAULT 0,
        ContinuesWorkflow BIT NOT NULL CONSTRAINT DF_WorkflowRun_ContinuesWorkflow DEFAULT 0,
        SatisfiesPolicy BIT NOT NULL CONSTRAINT DF_WorkflowRun_SatisfiesPolicy DEFAULT 0,
        TransfersAuthority BIT NOT NULL CONSTRAINT DF_WorkflowRun_TransfersAuthority DEFAULT 0,
        ApprovesRelease BIT NOT NULL CONSTRAINT DF_WorkflowRun_ApprovesRelease DEFAULT 0,
        CreatesAcceptedMemory BIT NOT NULL CONSTRAINT DF_WorkflowRun_CreatesAcceptedMemory DEFAULT 0,
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_WorkflowRun_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_WorkflowRun PRIMARY KEY CLUSTERED (WorkflowRunId),
        CONSTRAINT CK_WorkflowRun_WorkflowRunId_NotEmpty CHECK (WorkflowRunId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_WorkflowRun_ProjectId_NotEmpty CHECK (ProjectId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_WorkflowRun_WorkflowType_Allowed CHECK (WorkflowType IN (N'ManualDogfoodLoop', N'A2aHandoffReview', N'SourceApplyReview', N'MemoryPromotionReview', N'PolicyReview', N'EvidenceReview', N'TestFailureRepairReview')),
        CONSTRAINT CK_WorkflowRun_Status_Allowed CHECK (Status IN (N'Created', N'ReadyForReview', N'Blocked', N'Completed', N'Failed', N'Cancelled', N'Superseded')),
        CONSTRAINT CK_WorkflowRun_Name_NotBlank CHECK (LEN(LTRIM(RTRIM(WorkflowName))) > 0),
        CONSTRAINT CK_WorkflowRun_SubjectType_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectType))) > 0),
        CONSTRAINT CK_WorkflowRun_SubjectId_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectId))) > 0),
        CONSTRAINT CK_WorkflowRun_ActorType_NotBlank CHECK (LEN(LTRIM(RTRIM(CreatedByActorType))) > 0),
        CONSTRAINT CK_WorkflowRun_ActorId_NotBlank CHECK (LEN(LTRIM(RTRIM(CreatedByActorId))) > 0),
        CONSTRAINT CK_WorkflowRun_MetadataVersion_Positive CHECK (MetadataVersion > 0),
        CONSTRAINT CK_WorkflowRun_MetadataJson_IsJson CHECK (ISJSON(MetadataJson) = 1),
        CONSTRAINT CK_WorkflowRun_NoApprovalGrant CHECK (GrantsApproval = 0),
        CONSTRAINT CK_WorkflowRun_NoExecutionGrant CHECK (GrantsExecution = 0),
        CONSTRAINT CK_WorkflowRun_NoSourceMutation CHECK (MutatesSource = 0),
        CONSTRAINT CK_WorkflowRun_NoMemoryPromotion CHECK (PromotesMemory = 0),
        CONSTRAINT CK_WorkflowRun_NoWorkflowStart CHECK (StartsWorkflow = 0),
        CONSTRAINT CK_WorkflowRun_NoWorkflowContinuation CHECK (ContinuesWorkflow = 0),
        CONSTRAINT CK_WorkflowRun_NoPolicySatisfaction CHECK (SatisfiesPolicy = 0),
        CONSTRAINT CK_WorkflowRun_NoAuthorityTransfer CHECK (TransfersAuthority = 0),
        CONSTRAINT CK_WorkflowRun_NoReleaseApproval CHECK (ApprovesRelease = 0),
        CONSTRAINT CK_WorkflowRun_NoAcceptedMemory CHECK (CreatesAcceptedMemory = 0)
    );
END;
GO

IF OBJECT_ID(N'workflow.WorkflowRunStep', N'U') IS NULL
BEGIN
    CREATE TABLE workflow.WorkflowRunStep
    (
        WorkflowRunStepId UNIQUEIDENTIFIER NOT NULL,
        WorkflowRunId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        StepKey NVARCHAR(160) NOT NULL,
        StepName NVARCHAR(200) NOT NULL,
        StepType NVARCHAR(120) NOT NULL,
        Status NVARCHAR(80) NOT NULL,
        AgentRole NVARCHAR(80) NULL,
        AgentId NVARCHAR(200) NULL,
        SubjectType NVARCHAR(120) NULL,
        SubjectId NVARCHAR(300) NULL,
        SafeSummary NVARCHAR(1000) NULL,
        MetadataVersion INT NOT NULL,
        MetadataJson NVARCHAR(MAX) NOT NULL,
        GrantsApproval BIT NOT NULL CONSTRAINT DF_WorkflowRunStep_GrantsApproval DEFAULT 0,
        GrantsExecution BIT NOT NULL CONSTRAINT DF_WorkflowRunStep_GrantsExecution DEFAULT 0,
        MutatesSource BIT NOT NULL CONSTRAINT DF_WorkflowRunStep_MutatesSource DEFAULT 0,
        PromotesMemory BIT NOT NULL CONSTRAINT DF_WorkflowRunStep_PromotesMemory DEFAULT 0,
        StartsWorkflow BIT NOT NULL CONSTRAINT DF_WorkflowRunStep_StartsWorkflow DEFAULT 0,
        ContinuesWorkflow BIT NOT NULL CONSTRAINT DF_WorkflowRunStep_ContinuesWorkflow DEFAULT 0,
        SatisfiesPolicy BIT NOT NULL CONSTRAINT DF_WorkflowRunStep_SatisfiesPolicy DEFAULT 0,
        TransfersAuthority BIT NOT NULL CONSTRAINT DF_WorkflowRunStep_TransfersAuthority DEFAULT 0,
        ApprovesRelease BIT NOT NULL CONSTRAINT DF_WorkflowRunStep_ApprovesRelease DEFAULT 0,
        CreatesAcceptedMemory BIT NOT NULL CONSTRAINT DF_WorkflowRunStep_CreatesAcceptedMemory DEFAULT 0,
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_WorkflowRunStep_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_WorkflowRunStep PRIMARY KEY CLUSTERED (WorkflowRunStepId),
        CONSTRAINT FK_WorkflowRunStep_WorkflowRun FOREIGN KEY (WorkflowRunId) REFERENCES workflow.WorkflowRun(WorkflowRunId),
        CONSTRAINT UQ_WorkflowRunStep_Run_StepKey UNIQUE (WorkflowRunId, StepKey),
        CONSTRAINT CK_WorkflowRunStep_ProjectId_NotEmpty CHECK (ProjectId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_WorkflowRunStep_Key_NotBlank CHECK (LEN(LTRIM(RTRIM(StepKey))) > 0),
        CONSTRAINT CK_WorkflowRunStep_Name_NotBlank CHECK (LEN(LTRIM(RTRIM(StepName))) > 0),
        CONSTRAINT CK_WorkflowRunStep_StepType_Allowed CHECK (StepType IN (N'Planning', N'Review', N'Validation', N'HandoffSummary', N'GroundingSummary', N'HumanDecisionSupport', N'EvidenceCollection', N'Receipt')),
        CONSTRAINT CK_WorkflowRunStep_Status_Allowed CHECK (Status IN (N'Created', N'ReadyForReview', N'Blocked', N'Completed', N'Failed', N'Cancelled', N'Superseded')),
        CONSTRAINT CK_WorkflowRunStep_MetadataVersion_Positive CHECK (MetadataVersion > 0),
        CONSTRAINT CK_WorkflowRunStep_MetadataJson_IsJson CHECK (ISJSON(MetadataJson) = 1),
        CONSTRAINT CK_WorkflowRunStep_NoApprovalGrant CHECK (GrantsApproval = 0),
        CONSTRAINT CK_WorkflowRunStep_NoExecutionGrant CHECK (GrantsExecution = 0),
        CONSTRAINT CK_WorkflowRunStep_NoSourceMutation CHECK (MutatesSource = 0),
        CONSTRAINT CK_WorkflowRunStep_NoMemoryPromotion CHECK (PromotesMemory = 0),
        CONSTRAINT CK_WorkflowRunStep_NoWorkflowStart CHECK (StartsWorkflow = 0),
        CONSTRAINT CK_WorkflowRunStep_NoWorkflowContinuation CHECK (ContinuesWorkflow = 0),
        CONSTRAINT CK_WorkflowRunStep_NoPolicySatisfaction CHECK (SatisfiesPolicy = 0),
        CONSTRAINT CK_WorkflowRunStep_NoAuthorityTransfer CHECK (TransfersAuthority = 0),
        CONSTRAINT CK_WorkflowRunStep_NoReleaseApproval CHECK (ApprovesRelease = 0),
        CONSTRAINT CK_WorkflowRunStep_NoAcceptedMemory CHECK (CreatesAcceptedMemory = 0)
    );
END;
GO

IF OBJECT_ID(N'workflow.WorkflowRunEvidenceReference', N'U') IS NULL
BEGIN
    CREATE TABLE workflow.WorkflowRunEvidenceReference
    (
        WorkflowRunEvidenceReferenceId UNIQUEIDENTIFIER NOT NULL,
        WorkflowRunId UNIQUEIDENTIFIER NOT NULL,
        WorkflowRunStepId UNIQUEIDENTIFIER NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        EvidenceType NVARCHAR(120) NOT NULL,
        EvidenceId NVARCHAR(300) NOT NULL,
        EvidenceLabel NVARCHAR(300) NULL,
        SafeSummary NVARCHAR(1000) NULL,
        AllowedUse NVARCHAR(120) NULL,
        GovernanceEventId UNIQUEIDENTIFIER NULL,
        AgentHandoffId UNIQUEIDENTIFIER NULL,
        ThoughtLedgerEntryId UNIQUEIDENTIFIER NULL,
        GroundingEvidenceReferenceId UNIQUEIDENTIFIER NULL,
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_WorkflowRunEvidenceReference_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_WorkflowRunEvidenceReference PRIMARY KEY CLUSTERED (WorkflowRunEvidenceReferenceId),
        CONSTRAINT FK_WorkflowRunEvidenceReference_WorkflowRun FOREIGN KEY (WorkflowRunId) REFERENCES workflow.WorkflowRun(WorkflowRunId),
        CONSTRAINT FK_WorkflowRunEvidenceReference_WorkflowRunStep FOREIGN KEY (WorkflowRunStepId) REFERENCES workflow.WorkflowRunStep(WorkflowRunStepId),
        CONSTRAINT CK_WorkflowRunEvidenceReference_ProjectId_NotEmpty CHECK (ProjectId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_WorkflowRunEvidenceReference_EvidenceType_Allowed CHECK (EvidenceType IN (N'GovernanceEvent', N'ToolRequest', N'ToolGateDecision', N'ApprovalDecision', N'PolicyDecisionEvent', N'DogfoodReceipt', N'AgentHandoff', N'ThoughtLedgerReference', N'GroundingEvidenceReference', N'CriticReview', N'ValidationOutput', N'HumanNote', N'RunReport', N'ApprovalPackage')),
        CONSTRAINT CK_WorkflowRunEvidenceReference_EvidenceId_NotBlank CHECK (LEN(LTRIM(RTRIM(EvidenceId))) > 0),
        CONSTRAINT CK_WorkflowRunEvidenceReference_AllowedUse_Allowed CHECK (AllowedUse IS NULL OR AllowedUse IN (N'Context', N'Review', N'Debugging', N'Validation', N'Traceability', N'HumanDecisionSupport', N'AuditReference', N'PolicyInput', N'HandoffExplanation', N'RequirementEvaluation', N'Grounding'))
    );
END;
GO

IF OBJECT_ID(N'workflow.WorkflowRunGroundingReference', N'U') IS NULL
BEGIN
    CREATE TABLE workflow.WorkflowRunGroundingReference
    (
        WorkflowRunGroundingReferenceId UNIQUEIDENTIFIER NOT NULL,
        WorkflowRunId UNIQUEIDENTIFIER NOT NULL,
        WorkflowRunStepId UNIQUEIDENTIFIER NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        GroundingEvidenceReferenceId UNIQUEIDENTIFIER NOT NULL,
        ClaimType NVARCHAR(120) NOT NULL,
        ClaimId NVARCHAR(300) NOT NULL,
        SafeSummary NVARCHAR(1000) NULL,
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_WorkflowRunGroundingReference_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_WorkflowRunGroundingReference PRIMARY KEY CLUSTERED (WorkflowRunGroundingReferenceId),
        CONSTRAINT FK_WorkflowRunGroundingReference_WorkflowRun FOREIGN KEY (WorkflowRunId) REFERENCES workflow.WorkflowRun(WorkflowRunId),
        CONSTRAINT FK_WorkflowRunGroundingReference_WorkflowRunStep FOREIGN KEY (WorkflowRunStepId) REFERENCES workflow.WorkflowRunStep(WorkflowRunStepId),
        CONSTRAINT CK_WorkflowRunGroundingReference_ProjectId_NotEmpty CHECK (ProjectId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_WorkflowRunGroundingReference_GroundingId_NotEmpty CHECK (GroundingEvidenceReferenceId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_WorkflowRunGroundingReference_ClaimType_Allowed CHECK (ClaimType IN (N'EvidenceSupport', N'RequirementTrace', N'DecisionTrace', N'HandoffTrace', N'PolicyTrace', N'ValidationTrace')),
        CONSTRAINT CK_WorkflowRunGroundingReference_ClaimId_NotBlank CHECK (LEN(LTRIM(RTRIM(ClaimId))) > 0)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowRun_Project_CreatedUtc' AND object_id = OBJECT_ID(N'workflow.WorkflowRun'))
    CREATE INDEX IX_WorkflowRun_Project_CreatedUtc ON workflow.WorkflowRun(ProjectId, CreatedUtc DESC, WorkflowRunId DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowRun_Project_Correlation_CreatedUtc' AND object_id = OBJECT_ID(N'workflow.WorkflowRun'))
    CREATE INDEX IX_WorkflowRun_Project_Correlation_CreatedUtc ON workflow.WorkflowRun(ProjectId, CorrelationId, CreatedUtc DESC, WorkflowRunId DESC) WHERE CorrelationId IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowRun_Project_Subject_CreatedUtc' AND object_id = OBJECT_ID(N'workflow.WorkflowRun'))
    CREATE INDEX IX_WorkflowRun_Project_Subject_CreatedUtc ON workflow.WorkflowRun(ProjectId, SubjectType, SubjectId, CreatedUtc DESC, WorkflowRunId DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowRunStep_Run' AND object_id = OBJECT_ID(N'workflow.WorkflowRunStep'))
    CREATE INDEX IX_WorkflowRunStep_Run ON workflow.WorkflowRunStep(WorkflowRunId, CreatedUtc, WorkflowRunStepId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowRunEvidenceReference_Run' AND object_id = OBJECT_ID(N'workflow.WorkflowRunEvidenceReference'))
    CREATE INDEX IX_WorkflowRunEvidenceReference_Run ON workflow.WorkflowRunEvidenceReference(WorkflowRunId, CreatedUtc, WorkflowRunEvidenceReferenceId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowRunGroundingReference_Run' AND object_id = OBJECT_ID(N'workflow.WorkflowRunGroundingReference'))
    CREATE INDEX IX_WorkflowRunGroundingReference_Run ON workflow.WorkflowRunGroundingReference(WorkflowRunId, CreatedUtc, WorkflowRunGroundingReferenceId);
GO

CREATE OR ALTER TRIGGER workflow.TR_WorkflowRun_ValidateInsert
ON workflow.WorkflowRun
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        WHERE JSON_VALUE(i.MetadataJson, '$.grantsApproval') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.grantsExecution') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.mutatesSource') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.promotesMemory') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.startsWorkflow') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.continuesWorkflow') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.satisfiesPolicy') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.transfersAuthority') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.approvesRelease') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.createsAcceptedMemory') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.approvalGranted') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.executionAllowed') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.workflowContinued') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.releaseApproved') = N'true'
    )
        THROW 54010, 'Workflow run metadata must not claim approval, execution, source mutation, memory promotion, workflow action, policy satisfaction, release approval, accepted memory, or authority transfer.', 1;

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
           OR LOWER(i.MetadataJson) LIKE N'%raw tool output%'
           OR LOWER(i.MetadataJson) LIKE N'%entirepatch%'
           OR LOWER(i.MetadataJson) LIKE N'%entire patch%'
           OR LOWER(COALESCE(i.SubjectSummary, N'')) LIKE N'%approval granted%'
           OR LOWER(COALESCE(i.SubjectSummary, N'')) LIKE N'%execution permission%'
           OR LOWER(COALESCE(i.SubjectSummary, N'')) LIKE N'%policy satisfied%'
           OR LOWER(COALESCE(i.SubjectSummary, N'')) LIKE N'%release approved%'
           OR LOWER(COALESCE(i.SubjectSummary, N'')) LIKE N'%continue workflow%'
           OR LOWER(COALESCE(i.SubjectSummary, N'')) LIKE N'%promote memory%'
    )
        THROW 54011, 'Workflow run text must not contain authority or raw/private reasoning markers.', 1;
END;
GO

CREATE OR ALTER TRIGGER workflow.TR_WorkflowRunStep_ValidateInsert
ON workflow.WorkflowRunStep
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        JOIN workflow.WorkflowRun r ON r.WorkflowRunId = i.WorkflowRunId
        WHERE r.ProjectId <> i.ProjectId
    )
        THROW 54020, 'Workflow run step project must match parent workflow run project.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        WHERE JSON_VALUE(i.MetadataJson, '$.grantsApproval') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.grantsExecution') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.mutatesSource') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.promotesMemory') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.startsWorkflow') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.continuesWorkflow') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.satisfiesPolicy') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.transfersAuthority') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.approvesRelease') = N'true'
           OR JSON_VALUE(i.MetadataJson, '$.createsAcceptedMemory') = N'true'
    )
        THROW 54021, 'Workflow run step metadata must not claim authority or action.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        WHERE LOWER(i.MetadataJson) LIKE N'%rawprompt%'
           OR LOWER(i.MetadataJson) LIKE N'%rawcompletion%'
           OR LOWER(i.MetadataJson) LIKE N'%chain-of-thought%'
           OR LOWER(i.MetadataJson) LIKE N'%private reasoning%'
           OR LOWER(i.MetadataJson) LIKE N'%scratchpad%'
           OR LOWER(i.MetadataJson) LIKE N'%rawtooloutput%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%approval granted%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%execution permission%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%policy satisfied%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%release approved%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%continue workflow%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%promote memory%'
    )
        THROW 54022, 'Workflow run step text must not contain authority or raw/private reasoning markers.', 1;
END;
GO

CREATE OR ALTER TRIGGER workflow.TR_WorkflowRunEvidenceReference_ValidateInsert
ON workflow.WorkflowRunEvidenceReference
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        JOIN workflow.WorkflowRun r ON r.WorkflowRunId = i.WorkflowRunId
        WHERE r.ProjectId <> i.ProjectId
    )
        THROW 54030, 'Workflow evidence project must match parent workflow run project.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        JOIN workflow.WorkflowRunStep s ON s.WorkflowRunStepId = i.WorkflowRunStepId
        WHERE i.WorkflowRunStepId IS NOT NULL AND (s.ProjectId <> i.ProjectId OR s.WorkflowRunId <> i.WorkflowRunId)
    )
        THROW 54031, 'Workflow evidence step must belong to the same workflow run.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        WHERE LOWER(COALESCE(i.EvidenceLabel, N'')) LIKE N'%approval granted%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%approval granted%'
           OR LOWER(COALESCE(i.EvidenceLabel, N'')) LIKE N'%execution permission%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%execution permission%'
           OR LOWER(COALESCE(i.EvidenceLabel, N'')) LIKE N'%policy satisfied%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%policy satisfied%'
           OR LOWER(COALESCE(i.EvidenceLabel, N'')) LIKE N'%release approved%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%release approved%'
           OR LOWER(COALESCE(i.EvidenceLabel, N'')) LIKE N'%continue workflow%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%continue workflow%'
           OR LOWER(COALESCE(i.EvidenceLabel, N'')) LIKE N'%promote memory%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%promote memory%'
           OR LOWER(COALESCE(i.EvidenceLabel, N'')) LIKE N'%rawprompt%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%rawprompt%'
           OR LOWER(COALESCE(i.EvidenceLabel, N'')) LIKE N'%chain-of-thought%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%chain-of-thought%'
           OR LOWER(COALESCE(i.EvidenceLabel, N'')) LIKE N'%private reasoning%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%private reasoning%'
           OR LOWER(COALESCE(i.EvidenceLabel, N'')) LIKE N'%scratchpad%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%scratchpad%'
    )
        THROW 54032, 'Workflow evidence text must not contain authority or raw/private reasoning markers.', 1;
END;
GO

CREATE OR ALTER TRIGGER workflow.TR_WorkflowRunGroundingReference_ValidateInsert
ON workflow.WorkflowRunGroundingReference
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        JOIN workflow.WorkflowRun r ON r.WorkflowRunId = i.WorkflowRunId
        WHERE r.ProjectId <> i.ProjectId
    )
        THROW 54040, 'Workflow grounding project must match parent workflow run project.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        JOIN workflow.WorkflowRunStep s ON s.WorkflowRunStepId = i.WorkflowRunStepId
        WHERE i.WorkflowRunStepId IS NOT NULL AND (s.ProjectId <> i.ProjectId OR s.WorkflowRunId <> i.WorkflowRunId)
    )
        THROW 54041, 'Workflow grounding step must belong to the same workflow run.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        WHERE LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%approval granted%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%execution permission%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%policy satisfied%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%release approved%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%continue workflow%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%promote memory%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%chain-of-thought%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%private reasoning%'
           OR LOWER(COALESCE(i.SafeSummary, N'')) LIKE N'%scratchpad%'
    )
        THROW 54042, 'Workflow grounding text must not contain authority or raw/private reasoning markers.', 1;
END;
GO

CREATE OR ALTER TRIGGER workflow.TR_WorkflowRun_BlockUpdateDelete
ON workflow.WorkflowRun
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 54050, 'Workflow run records are append-only.', 1;
    IF TRY_CONVERT(BIT, SESSION_CONTEXT(N'IronDevGovernedWorkflowContinuation')) = 1
        RETURN;
    THROW 54050, 'Workflow run records are append-only.', 1;
END;
GO

CREATE OR ALTER TRIGGER workflow.TR_WorkflowRunStep_BlockUpdateDelete
ON workflow.WorkflowRunStep
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 54051, 'Workflow run steps are append-only.', 1;
    IF TRY_CONVERT(BIT, SESSION_CONTEXT(N'IronDevGovernedWorkflowContinuation')) = 1
        RETURN;
    THROW 54051, 'Workflow run steps are append-only.', 1;
END;
GO

CREATE OR ALTER TRIGGER workflow.TR_WorkflowRunEvidenceReference_BlockUpdateDelete
ON workflow.WorkflowRunEvidenceReference
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 54052, 'Workflow run evidence references are append-only.', 1;
END;
GO

CREATE OR ALTER TRIGGER workflow.TR_WorkflowRunGroundingReference_BlockUpdateDelete
ON workflow.WorkflowRunGroundingReference
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 54053, 'Workflow run grounding references are append-only.', 1;
END;
GO

CREATE OR ALTER PROCEDURE workflow.usp_WorkflowRun_Get
    @ProjectId UNIQUEIDENTIFIER,
    @WorkflowRunId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (1)
        WorkflowRunId,
        ProjectId,
        WorkflowType,
        WorkflowName,
        Status,
        SubjectType,
        SubjectId,
        SubjectSummary,
        CorrelationId,
        CausationId,
        CreatedByActorType,
        CreatedByActorId,
        MetadataVersion,
        MetadataJson,
        GrantsApproval,
        GrantsExecution,
        MutatesSource,
        PromotesMemory,
        StartsWorkflow,
        ContinuesWorkflow,
        SatisfiesPolicy,
        TransfersAuthority,
        ApprovesRelease,
        CreatesAcceptedMemory,
        CreatedUtc
    FROM workflow.WorkflowRun
    WHERE ProjectId = @ProjectId
      AND WorkflowRunId = @WorkflowRunId;

    SELECT
        WorkflowRunStepId,
        WorkflowRunId,
        ProjectId,
        StepKey,
        StepName,
        StepType,
        Status,
        AgentRole,
        AgentId,
        SubjectType,
        SubjectId,
        SafeSummary,
        MetadataVersion,
        MetadataJson,
        GrantsApproval,
        GrantsExecution,
        MutatesSource,
        PromotesMemory,
        StartsWorkflow,
        ContinuesWorkflow,
        SatisfiesPolicy,
        TransfersAuthority,
        ApprovesRelease,
        CreatesAcceptedMemory,
        CreatedUtc
    FROM workflow.WorkflowRunStep
    WHERE ProjectId = @ProjectId
      AND WorkflowRunId = @WorkflowRunId
    ORDER BY CreatedUtc, WorkflowRunStepId;

    SELECT
        e.WorkflowRunEvidenceReferenceId,
        e.WorkflowRunId,
        e.WorkflowRunStepId,
        s.StepKey,
        e.ProjectId,
        e.EvidenceType,
        e.EvidenceId,
        e.EvidenceLabel,
        e.SafeSummary,
        e.AllowedUse,
        e.GovernanceEventId,
        e.AgentHandoffId,
        e.ThoughtLedgerEntryId,
        e.GroundingEvidenceReferenceId,
        e.CreatedUtc
    FROM workflow.WorkflowRunEvidenceReference e
    LEFT JOIN workflow.WorkflowRunStep s ON s.WorkflowRunStepId = e.WorkflowRunStepId
    WHERE e.ProjectId = @ProjectId
      AND e.WorkflowRunId = @WorkflowRunId
    ORDER BY e.CreatedUtc, e.WorkflowRunEvidenceReferenceId;

    SELECT
        g.WorkflowRunGroundingReferenceId,
        g.WorkflowRunId,
        g.WorkflowRunStepId,
        s.StepKey,
        g.ProjectId,
        g.GroundingEvidenceReferenceId,
        g.ClaimType,
        g.ClaimId,
        g.SafeSummary,
        g.CreatedUtc
    FROM workflow.WorkflowRunGroundingReference g
    LEFT JOIN workflow.WorkflowRunStep s ON s.WorkflowRunStepId = g.WorkflowRunStepId
    WHERE g.ProjectId = @ProjectId
      AND g.WorkflowRunId = @WorkflowRunId
    ORDER BY g.CreatedUtc, g.WorkflowRunGroundingReferenceId;
END;
GO

CREATE OR ALTER PROCEDURE workflow.usp_WorkflowRun_Create
    @WorkflowRunId UNIQUEIDENTIFIER,
    @ProjectId UNIQUEIDENTIFIER,
    @WorkflowType NVARCHAR(120),
    @WorkflowName NVARCHAR(200),
    @Status NVARCHAR(80),
    @SubjectType NVARCHAR(120),
    @SubjectId NVARCHAR(300),
    @SubjectSummary NVARCHAR(1000) = NULL,
    @CorrelationId UNIQUEIDENTIFIER = NULL,
    @CausationId UNIQUEIDENTIFIER = NULL,
    @CreatedByActorType NVARCHAR(80),
    @CreatedByActorId NVARCHAR(200),
    @MetadataVersion INT,
    @MetadataJson NVARCHAR(MAX),
    @StepsJson NVARCHAR(MAX),
    @EvidenceReferencesJson NVARCHAR(MAX),
    @GroundingReferencesJson NVARCHAR(MAX),
    @CreatedUtc DATETIMEOFFSET(7) = NULL
AS
BEGIN
    SET XACT_ABORT ON;
    SET NOCOUNT ON;

    IF ISJSON(@StepsJson) <> 1
        THROW 54060, 'StepsJson must be valid JSON.', 1;

    IF ISJSON(@EvidenceReferencesJson) <> 1
        THROW 54061, 'EvidenceReferencesJson must be valid JSON.', 1;

    IF ISJSON(@GroundingReferencesJson) <> 1
        THROW 54062, 'GroundingReferencesJson must be valid JSON.', 1;

    DECLARE @EffectiveCreatedUtc DATETIMEOFFSET(7) = COALESCE(@CreatedUtc, SYSUTCDATETIME());
    DECLARE @EffectiveCorrelationId UNIQUEIDENTIFIER = COALESCE(@CorrelationId, @WorkflowRunId);

    DECLARE @StepRows TABLE
    (
        RowIndex INT NOT NULL,
        WorkflowRunStepId UNIQUEIDENTIFIER NOT NULL,
        StepKey NVARCHAR(160) NOT NULL,
        StepName NVARCHAR(200) NOT NULL,
        StepType NVARCHAR(120) NOT NULL,
        Status NVARCHAR(80) NOT NULL,
        AgentRole NVARCHAR(80) NULL,
        AgentId NVARCHAR(200) NULL,
        SubjectType NVARCHAR(120) NULL,
        SubjectId NVARCHAR(300) NULL,
        SafeSummary NVARCHAR(1000) NULL,
        MetadataVersion INT NOT NULL,
        MetadataJson NVARCHAR(MAX) NOT NULL
    );

    INSERT INTO @StepRows
    SELECT
        TRY_CONVERT(INT, j.[key]),
        NEWID(),
        LTRIM(RTRIM(JSON_VALUE(j.value, '$.StepKey'))),
        LTRIM(RTRIM(JSON_VALUE(j.value, '$.StepName'))),
        LTRIM(RTRIM(JSON_VALUE(j.value, '$.stepType'))),
        LTRIM(RTRIM(JSON_VALUE(j.value, '$.status'))),
        NULLIF(LTRIM(RTRIM(COALESCE(JSON_VALUE(j.value, '$.AgentRole'), N''))), N''),
        NULLIF(LTRIM(RTRIM(COALESCE(JSON_VALUE(j.value, '$.AgentId'), N''))), N''),
        NULLIF(LTRIM(RTRIM(COALESCE(JSON_VALUE(j.value, '$.SubjectType'), N''))), N''),
        NULLIF(LTRIM(RTRIM(COALESCE(JSON_VALUE(j.value, '$.SubjectId'), N''))), N''),
        NULLIF(LTRIM(RTRIM(COALESCE(JSON_VALUE(j.value, '$.SafeSummary'), N''))), N''),
        TRY_CONVERT(INT, JSON_VALUE(j.value, '$.MetadataVersion')),
        JSON_VALUE(j.value, '$.MetadataJson')
    FROM OPENJSON(@StepsJson) j;

    IF NOT EXISTS (SELECT 1 FROM @StepRows)
        THROW 54063, 'At least one workflow run step is required.', 1;

    IF EXISTS (SELECT 1 FROM @StepRows WHERE StepKey IS NULL OR LEN(StepKey) = 0 OR StepName IS NULL OR LEN(StepName) = 0 OR StepType IS NULL OR Status IS NULL OR MetadataVersion IS NULL OR MetadataVersion <= 0 OR MetadataJson IS NULL OR ISJSON(MetadataJson) <> 1)
        THROW 54064, 'Workflow run steps must include key, name, type, status, positive metadata version, and metadata JSON.', 1;

    IF EXISTS (SELECT StepKey FROM @StepRows GROUP BY StepKey HAVING COUNT(*) > 1)
        THROW 54065, 'Workflow run step keys must be unique.', 1;

    DECLARE @EvidenceRows TABLE
    (
        RowIndex INT NOT NULL,
        WorkflowRunEvidenceReferenceId UNIQUEIDENTIFIER NOT NULL,
        StepKey NVARCHAR(160) NULL,
        WorkflowRunStepId UNIQUEIDENTIFIER NULL,
        EvidenceType NVARCHAR(120) NOT NULL,
        EvidenceId NVARCHAR(300) NOT NULL,
        EvidenceLabel NVARCHAR(300) NULL,
        SafeSummary NVARCHAR(1000) NULL,
        AllowedUse NVARCHAR(120) NULL,
        GovernanceEventId UNIQUEIDENTIFIER NULL,
        AgentHandoffId UNIQUEIDENTIFIER NULL,
        ThoughtLedgerEntryId UNIQUEIDENTIFIER NULL,
        GroundingEvidenceReferenceId UNIQUEIDENTIFIER NULL
    );

    INSERT INTO @EvidenceRows
    (
        RowIndex,
        WorkflowRunEvidenceReferenceId,
        StepKey,
        WorkflowRunStepId,
        EvidenceType,
        EvidenceId,
        EvidenceLabel,
        SafeSummary,
        AllowedUse,
        GovernanceEventId,
        AgentHandoffId,
        ThoughtLedgerEntryId,
        GroundingEvidenceReferenceId
    )
    SELECT
        TRY_CONVERT(INT, j.[key]),
        NEWID(),
        NULLIF(LTRIM(RTRIM(COALESCE(JSON_VALUE(j.value, '$.StepKey'), N''))), N''),
        NULL,
        LTRIM(RTRIM(JSON_VALUE(j.value, '$.evidenceType'))),
        LTRIM(RTRIM(JSON_VALUE(j.value, '$.EvidenceId'))),
        NULLIF(LTRIM(RTRIM(COALESCE(JSON_VALUE(j.value, '$.EvidenceLabel'), N''))), N''),
        NULLIF(LTRIM(RTRIM(COALESCE(JSON_VALUE(j.value, '$.SafeSummary'), N''))), N''),
        NULLIF(LTRIM(RTRIM(COALESCE(JSON_VALUE(j.value, '$.allowedUse'), N''))), N''),
        TRY_CONVERT(UNIQUEIDENTIFIER, JSON_VALUE(j.value, '$.GovernanceEventId')),
        TRY_CONVERT(UNIQUEIDENTIFIER, JSON_VALUE(j.value, '$.AgentHandoffId')),
        TRY_CONVERT(UNIQUEIDENTIFIER, JSON_VALUE(j.value, '$.ThoughtLedgerEntryId')),
        TRY_CONVERT(UNIQUEIDENTIFIER, JSON_VALUE(j.value, '$.GroundingEvidenceReferenceId'))
    FROM OPENJSON(@EvidenceReferencesJson) j;

    IF NOT EXISTS (SELECT 1 FROM @EvidenceRows)
        THROW 54066, 'At least one workflow evidence reference is required.', 1;

    IF EXISTS (SELECT 1 FROM @EvidenceRows WHERE EvidenceType IS NULL OR EvidenceId IS NULL OR LEN(EvidenceId) = 0)
        THROW 54067, 'Workflow evidence references must include type and id.', 1;

    UPDATE e
        SET WorkflowRunStepId = s.WorkflowRunStepId
    FROM @EvidenceRows e
    JOIN @StepRows s ON s.StepKey = e.StepKey;

    IF EXISTS (SELECT 1 FROM @EvidenceRows WHERE StepKey IS NOT NULL AND WorkflowRunStepId IS NULL)
        THROW 54068, 'Workflow evidence StepKey must reference a workflow run step.', 1;

    DECLARE @GroundingRows TABLE
    (
        RowIndex INT NOT NULL,
        WorkflowRunGroundingReferenceId UNIQUEIDENTIFIER NOT NULL,
        StepKey NVARCHAR(160) NULL,
        WorkflowRunStepId UNIQUEIDENTIFIER NULL,
        GroundingEvidenceReferenceId UNIQUEIDENTIFIER NOT NULL,
        ClaimType NVARCHAR(120) NOT NULL,
        ClaimId NVARCHAR(300) NOT NULL,
        SafeSummary NVARCHAR(1000) NULL
    );

    INSERT INTO @GroundingRows
    (
        RowIndex,
        WorkflowRunGroundingReferenceId,
        StepKey,
        WorkflowRunStepId,
        GroundingEvidenceReferenceId,
        ClaimType,
        ClaimId,
        SafeSummary
    )
    SELECT
        TRY_CONVERT(INT, j.[key]),
        NEWID(),
        NULLIF(LTRIM(RTRIM(COALESCE(JSON_VALUE(j.value, '$.StepKey'), N''))), N''),
        NULL,
        TRY_CONVERT(UNIQUEIDENTIFIER, JSON_VALUE(j.value, '$.GroundingEvidenceReferenceId')),
        LTRIM(RTRIM(JSON_VALUE(j.value, '$.claimType'))),
        LTRIM(RTRIM(JSON_VALUE(j.value, '$.ClaimId'))),
        NULLIF(LTRIM(RTRIM(COALESCE(JSON_VALUE(j.value, '$.SafeSummary'), N''))), N'')
    FROM OPENJSON(@GroundingReferencesJson) j;

    IF EXISTS (SELECT 1 FROM @GroundingRows WHERE GroundingEvidenceReferenceId IS NULL OR ClaimType IS NULL OR ClaimId IS NULL OR LEN(ClaimId) = 0)
        THROW 54069, 'Workflow grounding references must include grounding id, claim type, and claim id.', 1;

    UPDATE g
        SET WorkflowRunStepId = s.WorkflowRunStepId
    FROM @GroundingRows g
    JOIN @StepRows s ON s.StepKey = g.StepKey;

    IF EXISTS (SELECT 1 FROM @GroundingRows WHERE StepKey IS NOT NULL AND WorkflowRunStepId IS NULL)
        THROW 54070, 'Workflow grounding StepKey must reference a workflow run step.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF EXISTS (SELECT 1 FROM workflow.WorkflowRun WHERE WorkflowRunId = @WorkflowRunId)
            THROW 54071, 'Workflow run already exists.', 1;

        INSERT INTO workflow.WorkflowRun
        (
            WorkflowRunId,
            ProjectId,
            WorkflowType,
            WorkflowName,
            Status,
            SubjectType,
            SubjectId,
            SubjectSummary,
            CorrelationId,
            CausationId,
            CreatedByActorType,
            CreatedByActorId,
            MetadataVersion,
            MetadataJson,
            GrantsApproval,
            GrantsExecution,
            MutatesSource,
            PromotesMemory,
            StartsWorkflow,
            ContinuesWorkflow,
            SatisfiesPolicy,
            TransfersAuthority,
            ApprovesRelease,
            CreatesAcceptedMemory,
            CreatedUtc
        )
        VALUES
        (
            @WorkflowRunId,
            @ProjectId,
            LTRIM(RTRIM(@WorkflowType)),
            LTRIM(RTRIM(@WorkflowName)),
            LTRIM(RTRIM(@Status)),
            LTRIM(RTRIM(@SubjectType)),
            LTRIM(RTRIM(@SubjectId)),
            NULLIF(LTRIM(RTRIM(COALESCE(@SubjectSummary, N''))), N''),
            @EffectiveCorrelationId,
            @CausationId,
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
            0,
            0,
            0,
            @EffectiveCreatedUtc
        );

        INSERT INTO workflow.WorkflowRunStep
        (
            WorkflowRunStepId,
            WorkflowRunId,
            ProjectId,
            StepKey,
            StepName,
            StepType,
            Status,
            AgentRole,
            AgentId,
            SubjectType,
            SubjectId,
            SafeSummary,
            MetadataVersion,
            MetadataJson,
            GrantsApproval,
            GrantsExecution,
            MutatesSource,
            PromotesMemory,
            StartsWorkflow,
            ContinuesWorkflow,
            SatisfiesPolicy,
            TransfersAuthority,
            ApprovesRelease,
            CreatesAcceptedMemory,
            CreatedUtc
        )
        SELECT
            WorkflowRunStepId,
            @WorkflowRunId,
            @ProjectId,
            StepKey,
            StepName,
            StepType,
            Status,
            AgentRole,
            AgentId,
            SubjectType,
            SubjectId,
            SafeSummary,
            MetadataVersion,
            MetadataJson,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            @EffectiveCreatedUtc
        FROM @StepRows;

        INSERT INTO workflow.WorkflowRunEvidenceReference
        (
            WorkflowRunEvidenceReferenceId,
            WorkflowRunId,
            WorkflowRunStepId,
            ProjectId,
            EvidenceType,
            EvidenceId,
            EvidenceLabel,
            SafeSummary,
            AllowedUse,
            GovernanceEventId,
            AgentHandoffId,
            ThoughtLedgerEntryId,
            GroundingEvidenceReferenceId,
            CreatedUtc
        )
        SELECT
            WorkflowRunEvidenceReferenceId,
            @WorkflowRunId,
            WorkflowRunStepId,
            @ProjectId,
            EvidenceType,
            EvidenceId,
            EvidenceLabel,
            SafeSummary,
            AllowedUse,
            GovernanceEventId,
            AgentHandoffId,
            ThoughtLedgerEntryId,
            GroundingEvidenceReferenceId,
            @EffectiveCreatedUtc
        FROM @EvidenceRows;

        INSERT INTO workflow.WorkflowRunGroundingReference
        (
            WorkflowRunGroundingReferenceId,
            WorkflowRunId,
            WorkflowRunStepId,
            ProjectId,
            GroundingEvidenceReferenceId,
            ClaimType,
            ClaimId,
            SafeSummary,
            CreatedUtc
        )
        SELECT
            WorkflowRunGroundingReferenceId,
            @WorkflowRunId,
            WorkflowRunStepId,
            @ProjectId,
            GroundingEvidenceReferenceId,
            ClaimType,
            ClaimId,
            SafeSummary,
            @EffectiveCreatedUtc
        FROM @GroundingRows;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;

    EXEC workflow.usp_WorkflowRun_Get @ProjectId = @ProjectId, @WorkflowRunId = @WorkflowRunId;
END;
GO

CREATE OR ALTER PROCEDURE workflow.usp_WorkflowRun_ListByProject
    @ProjectId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @BoundedTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@BoundedTake)
        r.WorkflowRunId,
        r.ProjectId,
        r.WorkflowType,
        r.WorkflowName,
        r.Status,
        r.SubjectType,
        r.SubjectId,
        r.CorrelationId,
        r.CausationId,
        (SELECT COUNT(1) FROM workflow.WorkflowRunStep s WHERE s.WorkflowRunId = r.WorkflowRunId) AS StepCount,
        (SELECT COUNT(1) FROM workflow.WorkflowRunEvidenceReference e WHERE e.WorkflowRunId = r.WorkflowRunId) AS EvidenceReferenceCount,
        (SELECT COUNT(1) FROM workflow.WorkflowRunGroundingReference g WHERE g.WorkflowRunId = r.WorkflowRunId) AS GroundingReferenceCount,
        r.CreatedUtc
    FROM workflow.WorkflowRun r
    WHERE r.ProjectId = @ProjectId
    ORDER BY r.CreatedUtc DESC, r.WorkflowRunId DESC;
END;
GO

CREATE OR ALTER PROCEDURE workflow.usp_WorkflowRun_ListByCorrelation
    @ProjectId UNIQUEIDENTIFIER,
    @CorrelationId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @BoundedTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@BoundedTake)
        r.WorkflowRunId,
        r.ProjectId,
        r.WorkflowType,
        r.WorkflowName,
        r.Status,
        r.SubjectType,
        r.SubjectId,
        r.CorrelationId,
        r.CausationId,
        (SELECT COUNT(1) FROM workflow.WorkflowRunStep s WHERE s.WorkflowRunId = r.WorkflowRunId) AS StepCount,
        (SELECT COUNT(1) FROM workflow.WorkflowRunEvidenceReference e WHERE e.WorkflowRunId = r.WorkflowRunId) AS EvidenceReferenceCount,
        (SELECT COUNT(1) FROM workflow.WorkflowRunGroundingReference g WHERE g.WorkflowRunId = r.WorkflowRunId) AS GroundingReferenceCount,
        r.CreatedUtc
    FROM workflow.WorkflowRun r
    WHERE r.ProjectId = @ProjectId
      AND r.CorrelationId = @CorrelationId
    ORDER BY r.CreatedUtc DESC, r.WorkflowRunId DESC;
END;
GO

CREATE OR ALTER PROCEDURE workflow.usp_WorkflowRun_ListBySubject
    @ProjectId UNIQUEIDENTIFIER,
    @SubjectType NVARCHAR(120),
    @SubjectId NVARCHAR(300),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @BoundedTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@BoundedTake)
        r.WorkflowRunId,
        r.ProjectId,
        r.WorkflowType,
        r.WorkflowName,
        r.Status,
        r.SubjectType,
        r.SubjectId,
        r.CorrelationId,
        r.CausationId,
        (SELECT COUNT(1) FROM workflow.WorkflowRunStep s WHERE s.WorkflowRunId = r.WorkflowRunId) AS StepCount,
        (SELECT COUNT(1) FROM workflow.WorkflowRunEvidenceReference e WHERE e.WorkflowRunId = r.WorkflowRunId) AS EvidenceReferenceCount,
        (SELECT COUNT(1) FROM workflow.WorkflowRunGroundingReference g WHERE g.WorkflowRunId = r.WorkflowRunId) AS GroundingReferenceCount,
        r.CreatedUtc
    FROM workflow.WorkflowRun r
    WHERE r.ProjectId = @ProjectId
      AND r.SubjectType = LTRIM(RTRIM(@SubjectType))
      AND r.SubjectId = LTRIM(RTRIM(@SubjectId))
    ORDER BY r.CreatedUtc DESC, r.WorkflowRunId DESC;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'IronDevGovernanceEventRuntimeRole') IS NULL
    CREATE ROLE IronDevGovernanceEventRuntimeRole;
GO

GRANT EXECUTE ON OBJECT::workflow.usp_WorkflowRun_Create TO IronDevGovernanceEventRuntimeRole;
GRANT EXECUTE ON OBJECT::workflow.usp_WorkflowRun_Get TO IronDevGovernanceEventRuntimeRole;
GRANT EXECUTE ON OBJECT::workflow.usp_WorkflowRun_ListByProject TO IronDevGovernanceEventRuntimeRole;
GRANT EXECUTE ON OBJECT::workflow.usp_WorkflowRun_ListByCorrelation TO IronDevGovernanceEventRuntimeRole;
GRANT EXECUTE ON OBJECT::workflow.usp_WorkflowRun_ListBySubject TO IronDevGovernanceEventRuntimeRole;
GRANT SELECT ON SCHEMA::workflow TO IronDevGovernanceEventRuntimeRole;
DENY INSERT, UPDATE, DELETE ON OBJECT::workflow.WorkflowRun TO IronDevGovernanceEventRuntimeRole;
DENY INSERT, UPDATE, DELETE ON OBJECT::workflow.WorkflowRunStep TO IronDevGovernanceEventRuntimeRole;
DENY INSERT, UPDATE, DELETE ON OBJECT::workflow.WorkflowRunEvidenceReference TO IronDevGovernanceEventRuntimeRole;
DENY INSERT, UPDATE, DELETE ON OBJECT::workflow.WorkflowRunGroundingReference TO IronDevGovernanceEventRuntimeRole;
DENY ALTER ON SCHEMA::workflow TO IronDevGovernanceEventRuntimeRole;
GO
