IF SCHEMA_ID(N'workflow') IS NULL
    THROW 54200, 'workflow schema must exist before applying workflow checkpoint store migration.', 1;
GO

IF OBJECT_ID(N'workflow.WorkflowRun', N'U') IS NULL
    THROW 54201, 'workflow.WorkflowRun must exist before applying workflow checkpoint store migration.', 1;
GO

IF OBJECT_ID(N'workflow.WorkflowRunStep', N'U') IS NULL
    THROW 54202, 'workflow.WorkflowRunStep must exist before applying workflow checkpoint store migration.', 1;
GO

IF OBJECT_ID(N'workflow.WorkflowCheckpoint', N'U') IS NULL
BEGIN
    CREATE TABLE workflow.WorkflowCheckpoint
    (
        WorkflowCheckpointId UNIQUEIDENTIFIER NOT NULL,
        WorkflowRunId UNIQUEIDENTIFIER NOT NULL,
        WorkflowRunStepId UNIQUEIDENTIFIER NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        CheckpointKey NVARCHAR(160) NOT NULL,
        CheckpointName NVARCHAR(200) NOT NULL,
        CheckpointType NVARCHAR(80) NOT NULL,
        Status NVARCHAR(80) NOT NULL,
        SubjectType NVARCHAR(120) NULL,
        SubjectId NVARCHAR(300) NULL,
        SafeSummary NVARCHAR(1000) NULL,
        StateVersion INT NOT NULL,
        StateJson NVARCHAR(MAX) NOT NULL,
        StateHashSha256 NVARCHAR(128) NULL,
        CorrelationId UNIQUEIDENTIFIER NULL,
        CausationId UNIQUEIDENTIFIER NULL,
        CreatedByActorType NVARCHAR(80) NOT NULL,
        CreatedByActorId NVARCHAR(200) NOT NULL,
        MetadataVersion INT NOT NULL,
        MetadataJson NVARCHAR(MAX) NOT NULL,
        GrantsApproval BIT NOT NULL CONSTRAINT DF_WorkflowCheckpoint_GrantsApproval DEFAULT 0,
        GrantsExecution BIT NOT NULL CONSTRAINT DF_WorkflowCheckpoint_GrantsExecution DEFAULT 0,
        MutatesSource BIT NOT NULL CONSTRAINT DF_WorkflowCheckpoint_MutatesSource DEFAULT 0,
        PromotesMemory BIT NOT NULL CONSTRAINT DF_WorkflowCheckpoint_PromotesMemory DEFAULT 0,
        StartsWorkflow BIT NOT NULL CONSTRAINT DF_WorkflowCheckpoint_StartsWorkflow DEFAULT 0,
        ContinuesWorkflow BIT NOT NULL CONSTRAINT DF_WorkflowCheckpoint_ContinuesWorkflow DEFAULT 0,
        ResumesWorkflow BIT NOT NULL CONSTRAINT DF_WorkflowCheckpoint_ResumesWorkflow DEFAULT 0,
        SatisfiesPolicy BIT NOT NULL CONSTRAINT DF_WorkflowCheckpoint_SatisfiesPolicy DEFAULT 0,
        TransfersAuthority BIT NOT NULL CONSTRAINT DF_WorkflowCheckpoint_TransfersAuthority DEFAULT 0,
        ApprovesRelease BIT NOT NULL CONSTRAINT DF_WorkflowCheckpoint_ApprovesRelease DEFAULT 0,
        CreatesAcceptedMemory BIT NOT NULL CONSTRAINT DF_WorkflowCheckpoint_CreatesAcceptedMemory DEFAULT 0,
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_WorkflowCheckpoint_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_WorkflowCheckpoint PRIMARY KEY CLUSTERED (WorkflowCheckpointId),
        CONSTRAINT FK_WorkflowCheckpoint_WorkflowRun FOREIGN KEY (WorkflowRunId) REFERENCES workflow.WorkflowRun(WorkflowRunId),
        CONSTRAINT FK_WorkflowCheckpoint_WorkflowRunStep FOREIGN KEY (WorkflowRunStepId) REFERENCES workflow.WorkflowRunStep(WorkflowRunStepId),
        CONSTRAINT UQ_WorkflowCheckpoint_Run_Key UNIQUE (WorkflowRunId, CheckpointKey),
        CONSTRAINT CK_WorkflowCheckpoint_Id_NotEmpty CHECK (WorkflowCheckpointId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_WorkflowCheckpoint_ProjectId_NotEmpty CHECK (ProjectId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_WorkflowCheckpoint_Key_NotBlank CHECK (LEN(LTRIM(RTRIM(CheckpointKey))) > 0),
        CONSTRAINT CK_WorkflowCheckpoint_Name_NotBlank CHECK (LEN(LTRIM(RTRIM(CheckpointName))) > 0),
        CONSTRAINT CK_WorkflowCheckpoint_Type_Allowed CHECK (CheckpointType IN (N'RunCreated', N'StepRecorded', N'EvidenceCollected', N'GroundingRecorded', N'ReviewSnapshot', N'ValidationSnapshot', N'HumanDecisionSupport', N'Receipt', N'FailureSnapshot', N'BlockedSnapshot', N'CancelledSnapshot')),
        CONSTRAINT CK_WorkflowCheckpoint_Status_Allowed CHECK (Status IN (N'Created', N'Captured', N'ReadyForReview', N'Blocked', N'Completed', N'Failed', N'Cancelled', N'Superseded')),
        CONSTRAINT CK_WorkflowCheckpoint_StateVersion_Positive CHECK (StateVersion > 0),
        CONSTRAINT CK_WorkflowCheckpoint_StateJson_IsJson CHECK (ISJSON(StateJson) = 1),
        CONSTRAINT CK_WorkflowCheckpoint_MetadataVersion_Positive CHECK (MetadataVersion > 0),
        CONSTRAINT CK_WorkflowCheckpoint_MetadataJson_IsJson CHECK (ISJSON(MetadataJson) = 1),
        CONSTRAINT CK_WorkflowCheckpoint_NoApproval CHECK (GrantsApproval = 0),
        CONSTRAINT CK_WorkflowCheckpoint_NoExecution CHECK (GrantsExecution = 0),
        CONSTRAINT CK_WorkflowCheckpoint_NoSourceMutation CHECK (MutatesSource = 0),
        CONSTRAINT CK_WorkflowCheckpoint_NoMemoryPromotion CHECK (PromotesMemory = 0),
        CONSTRAINT CK_WorkflowCheckpoint_NoWorkflowStart CHECK (StartsWorkflow = 0),
        CONSTRAINT CK_WorkflowCheckpoint_NoWorkflowContinue CHECK (ContinuesWorkflow = 0),
        CONSTRAINT CK_WorkflowCheckpoint_NoWorkflowResume CHECK (ResumesWorkflow = 0),
        CONSTRAINT CK_WorkflowCheckpoint_NoPolicySatisfaction CHECK (SatisfiesPolicy = 0),
        CONSTRAINT CK_WorkflowCheckpoint_NoAuthorityTransfer CHECK (TransfersAuthority = 0),
        CONSTRAINT CK_WorkflowCheckpoint_NoReleaseApproval CHECK (ApprovesRelease = 0),
        CONSTRAINT CK_WorkflowCheckpoint_NoAcceptedMemory CHECK (CreatesAcceptedMemory = 0)
    );
END;
GO

IF OBJECT_ID(N'workflow.WorkflowCheckpointEvidenceReference', N'U') IS NULL
BEGIN
    CREATE TABLE workflow.WorkflowCheckpointEvidenceReference
    (
        WorkflowCheckpointEvidenceReferenceId UNIQUEIDENTIFIER NOT NULL,
        WorkflowCheckpointId UNIQUEIDENTIFIER NOT NULL,
        WorkflowRunId UNIQUEIDENTIFIER NOT NULL,
        WorkflowRunStepId UNIQUEIDENTIFIER NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        EvidenceType NVARCHAR(120) NOT NULL,
        EvidenceId NVARCHAR(300) NOT NULL,
        EvidenceLabel NVARCHAR(240) NULL,
        SafeSummary NVARCHAR(1000) NULL,
        AllowedUse NVARCHAR(120) NULL,
        GovernanceEventId UNIQUEIDENTIFIER NULL,
        HandoffRecordId UNIQUEIDENTIFIER NULL,
        ThoughtLedgerEntryId UNIQUEIDENTIFIER NULL,
        GroundingReferenceId UNIQUEIDENTIFIER NULL,
        WorkflowRunEvidenceReferenceId UNIQUEIDENTIFIER NULL,
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_WorkflowCheckpointEvidenceReference_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_WorkflowCheckpointEvidenceReference PRIMARY KEY CLUSTERED (WorkflowCheckpointEvidenceReferenceId),
        CONSTRAINT FK_WorkflowCheckpointEvidenceReference_Checkpoint FOREIGN KEY (WorkflowCheckpointId) REFERENCES workflow.WorkflowCheckpoint(WorkflowCheckpointId),
        CONSTRAINT FK_WorkflowCheckpointEvidenceReference_Run FOREIGN KEY (WorkflowRunId) REFERENCES workflow.WorkflowRun(WorkflowRunId),
        CONSTRAINT FK_WorkflowCheckpointEvidenceReference_Step FOREIGN KEY (WorkflowRunStepId) REFERENCES workflow.WorkflowRunStep(WorkflowRunStepId),
        CONSTRAINT CK_WorkflowCheckpointEvidenceReference_ProjectId_NotEmpty CHECK (ProjectId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_WorkflowCheckpointEvidenceReference_EvidenceType_Allowed CHECK (EvidenceType IN (N'GovernanceEvent', N'ToolRequest', N'ToolGateDecision', N'ApprovalDecision', N'PolicyDecisionEvent', N'DogfoodReceipt', N'HandoffRecord', N'ThoughtLedgerReference', N'GroundingReference', N'CriticReview', N'ValidationOutput', N'HumanNote', N'RunReport', N'ReviewPackage')),
        CONSTRAINT CK_WorkflowCheckpointEvidenceReference_EvidenceId_NotBlank CHECK (LEN(LTRIM(RTRIM(EvidenceId))) > 0),
        CONSTRAINT CK_WorkflowCheckpointEvidenceReference_AllowedUse_Allowed CHECK (AllowedUse IS NULL OR AllowedUse IN (N'Context', N'Review', N'Debugging', N'Validation', N'Traceability', N'HumanDecisionSupport', N'AuditReference', N'PolicyInput', N'HandoffExplanation', N'RequirementEvaluation', N'Grounding'))
    );
END;
GO

IF OBJECT_ID(N'workflow.WorkflowCheckpointGroundingReference', N'U') IS NULL
BEGIN
    CREATE TABLE workflow.WorkflowCheckpointGroundingReference
    (
        WorkflowCheckpointGroundingReferenceId UNIQUEIDENTIFIER NOT NULL,
        WorkflowCheckpointId UNIQUEIDENTIFIER NOT NULL,
        WorkflowRunId UNIQUEIDENTIFIER NOT NULL,
        WorkflowRunStepId UNIQUEIDENTIFIER NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        GroundingReferenceId UNIQUEIDENTIFIER NOT NULL,
        ClaimType NVARCHAR(120) NOT NULL,
        ClaimId NVARCHAR(300) NOT NULL,
        SafeSummary NVARCHAR(1000) NULL,
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_WorkflowCheckpointGroundingReference_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_WorkflowCheckpointGroundingReference PRIMARY KEY CLUSTERED (WorkflowCheckpointGroundingReferenceId),
        CONSTRAINT FK_WorkflowCheckpointGroundingReference_Checkpoint FOREIGN KEY (WorkflowCheckpointId) REFERENCES workflow.WorkflowCheckpoint(WorkflowCheckpointId),
        CONSTRAINT FK_WorkflowCheckpointGroundingReference_Run FOREIGN KEY (WorkflowRunId) REFERENCES workflow.WorkflowRun(WorkflowRunId),
        CONSTRAINT FK_WorkflowCheckpointGroundingReference_Step FOREIGN KEY (WorkflowRunStepId) REFERENCES workflow.WorkflowRunStep(WorkflowRunStepId),
        CONSTRAINT CK_WorkflowCheckpointGroundingReference_ProjectId_NotEmpty CHECK (ProjectId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_WorkflowCheckpointGroundingReference_GroundingId_NotEmpty CHECK (GroundingReferenceId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_WorkflowCheckpointGroundingReference_ClaimType_Allowed CHECK (ClaimType IN (N'EvidenceSupport', N'RequirementTrace', N'DecisionTrace', N'HandoffTrace', N'PolicyTrace', N'ValidationTrace')),
        CONSTRAINT CK_WorkflowCheckpointGroundingReference_ClaimId_NotBlank CHECK (LEN(LTRIM(RTRIM(ClaimId))) > 0)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowCheckpoint_Run_CreatedUtc' AND object_id = OBJECT_ID(N'workflow.WorkflowCheckpoint'))
    CREATE INDEX IX_WorkflowCheckpoint_Run_CreatedUtc ON workflow.WorkflowCheckpoint(WorkflowRunId, CreatedUtc DESC, WorkflowCheckpointId DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowCheckpoint_Step_CreatedUtc' AND object_id = OBJECT_ID(N'workflow.WorkflowCheckpoint'))
    CREATE INDEX IX_WorkflowCheckpoint_Step_CreatedUtc ON workflow.WorkflowCheckpoint(WorkflowRunStepId, CreatedUtc DESC, WorkflowCheckpointId DESC) WHERE WorkflowRunStepId IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowCheckpoint_Project_Correlation_CreatedUtc' AND object_id = OBJECT_ID(N'workflow.WorkflowCheckpoint'))
    CREATE INDEX IX_WorkflowCheckpoint_Project_Correlation_CreatedUtc ON workflow.WorkflowCheckpoint(ProjectId, CorrelationId, CreatedUtc DESC, WorkflowCheckpointId DESC) WHERE CorrelationId IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowCheckpoint_Project_Subject_CreatedUtc' AND object_id = OBJECT_ID(N'workflow.WorkflowCheckpoint'))
    CREATE INDEX IX_WorkflowCheckpoint_Project_Subject_CreatedUtc ON workflow.WorkflowCheckpoint(ProjectId, SubjectType, SubjectId, CreatedUtc DESC, WorkflowCheckpointId DESC) WHERE SubjectType IS NOT NULL AND SubjectId IS NOT NULL;
GO

CREATE OR ALTER TRIGGER workflow.TR_WorkflowCheckpoint_ValidateInsert
ON workflow.WorkflowCheckpoint
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
        FROM inserted i
        JOIN workflow.WorkflowRun r ON r.WorkflowRunId = i.WorkflowRunId
        WHERE r.ProjectId <> i.ProjectId
    )
        THROW 54210, 'Workflow checkpoint project must match parent workflow run project.', 1;

    IF EXISTS (
        SELECT 1
        FROM inserted i
        JOIN workflow.WorkflowRunStep s ON s.WorkflowRunStepId = i.WorkflowRunStepId
        WHERE i.WorkflowRunStepId IS NOT NULL AND (s.ProjectId <> i.ProjectId OR s.WorkflowRunId <> i.WorkflowRunId)
    )
        THROW 54211, 'Workflow checkpoint step must belong to the same workflow run.', 1;

    IF EXISTS (
        SELECT 1
        FROM inserted i
        CROSS APPLY (VALUES (LOWER(CONCAT_WS(N' ', i.CheckpointKey, i.CheckpointName, i.SubjectType, i.SubjectId, i.SafeSummary, i.StateJson, i.StateHashSha256, i.CreatedByActorType, i.CreatedByActorId, i.MetadataJson)))) v(TextValue)
        WHERE v.TextValue LIKE N'%hiddenreasoning%'
           OR v.TextValue LIKE N'%hidden reasoning%'
           OR v.TextValue LIKE N'%chainofthought%'
           OR v.TextValue LIKE N'%chain of thought%'
           OR v.TextValue LIKE N'%chain-of-thought%'
           OR v.TextValue LIKE N'%private reasoning%'
           OR v.TextValue LIKE N'%scratchpad%'
           OR v.TextValue LIKE N'%rawprompt%'
           OR v.TextValue LIKE N'%raw prompt%'
           OR v.TextValue LIKE N'%rawcompletion%'
           OR v.TextValue LIKE N'%raw completion%'
           OR v.TextValue LIKE N'%rawtooloutput%'
           OR v.TextValue LIKE N'%raw tool output%'
           OR v.TextValue LIKE N'%entirepatch%'
           OR v.TextValue LIKE N'%entire patch%'
           OR v.TextValue LIKE N'%approval granted%'
           OR v.TextValue LIKE N'%approved for execution%'
           OR v.TextValue LIKE N'%execution permission%'
           OR v.TextValue LIKE N'%execution allowed%'
           OR v.TextValue LIKE N'%can execute%'
           OR v.TextValue LIKE N'%authorize execution%'
           OR v.TextValue LIKE N'%policy satisfied%'
           OR v.TextValue LIKE N'%satisfy policy%'
           OR v.TextValue LIKE N'%source applied%'
           OR v.TextValue LIKE N'%apply source%'
           OR v.TextValue LIKE N'%apply patch%'
           OR v.TextValue LIKE N'%memory promoted%'
           OR v.TextValue LIKE N'%promote memory%'
           OR v.TextValue LIKE N'%accepted memory%'
           OR v.TextValue LIKE N'%release approved%'
           OR v.TextValue LIKE N'%approve release%'
           OR v.TextValue LIKE N'%ready to ship%'
           OR v.TextValue LIKE N'%can ship%'
           OR v.TextValue LIKE N'%authority transferred%'
           OR v.TextValue LIKE N'%transfer authority%'
           OR v.TextValue LIKE N'%workflow continued%'
           OR v.TextValue LIKE N'%continue workflow%'
           OR v.TextValue LIKE N'%workflow started%'
           OR v.TextValue LIKE N'%start workflow%'
           OR v.TextValue LIKE N'%resume workflow%'
           OR v.TextValue LIKE N'%workflow resumed%'
           OR v.TextValue LIKE N'%resume allowed%'
           OR v.TextValue LIKE N'%restorable%'
           OR v.TextValue LIKE N'%restore workflow%'
           OR v.TextValue LIKE N'%dispatch agent%'
           OR v.TextValue LIKE N'%tool executed%'
           OR v.TextValue LIKE N'%tool ran%'
    )
        THROW 54212, 'Workflow checkpoint contains forbidden private-reasoning or authority/resume text.', 1;

    IF EXISTS (
        SELECT 1 FROM inserted
        WHERE GrantsApproval = 1 OR GrantsExecution = 1 OR MutatesSource = 1 OR PromotesMemory = 1 OR StartsWorkflow = 1 OR ContinuesWorkflow = 1 OR ResumesWorkflow = 1 OR SatisfiesPolicy = 1 OR TransfersAuthority = 1 OR ApprovesRelease = 1 OR CreatesAcceptedMemory = 1
    )
        THROW 54213, 'Workflow checkpoint cannot grant authority, resume workflow, mutate source, or promote memory.', 1;
END;
GO

CREATE OR ALTER TRIGGER workflow.TR_WorkflowCheckpoint_BlockUpdateDelete
ON workflow.WorkflowCheckpoint
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 54214, 'workflow.WorkflowCheckpoint is append-only.', 1;
END;
GO

CREATE OR ALTER TRIGGER workflow.TR_WorkflowCheckpointEvidenceReference_ValidateInsert
ON workflow.WorkflowCheckpointEvidenceReference
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
        FROM inserted i
        JOIN workflow.WorkflowCheckpoint c ON c.WorkflowCheckpointId = i.WorkflowCheckpointId
        WHERE c.ProjectId <> i.ProjectId OR c.WorkflowRunId <> i.WorkflowRunId OR ISNULL(c.WorkflowRunStepId, '00000000-0000-0000-0000-000000000000') <> ISNULL(i.WorkflowRunStepId, '00000000-0000-0000-0000-000000000000')
    )
        THROW 54220, 'Workflow checkpoint evidence must match parent checkpoint scope.', 1;

    IF EXISTS (
        SELECT 1
        FROM inserted i
        JOIN workflow.WorkflowRunStep s ON s.WorkflowRunStepId = i.WorkflowRunStepId
        WHERE i.WorkflowRunStepId IS NOT NULL AND (s.ProjectId <> i.ProjectId OR s.WorkflowRunId <> i.WorkflowRunId)
    )
        THROW 54221, 'Workflow checkpoint evidence step must belong to the same workflow run.', 1;

    IF EXISTS (
        SELECT 1
        FROM inserted i
        CROSS APPLY (VALUES (LOWER(CONCAT_WS(N' ', i.EvidenceType, i.EvidenceId, i.EvidenceLabel, i.SafeSummary, i.AllowedUse)))) v(TextValue)
        WHERE v.TextValue LIKE N'%hiddenreasoning%'
           OR v.TextValue LIKE N'%hidden reasoning%'
           OR v.TextValue LIKE N'%chainofthought%'
           OR v.TextValue LIKE N'%chain of thought%'
           OR v.TextValue LIKE N'%chain-of-thought%'
           OR v.TextValue LIKE N'%private reasoning%'
           OR v.TextValue LIKE N'%scratchpad%'
           OR v.TextValue LIKE N'%rawprompt%'
           OR v.TextValue LIKE N'%raw prompt%'
           OR v.TextValue LIKE N'%rawcompletion%'
           OR v.TextValue LIKE N'%raw completion%'
           OR v.TextValue LIKE N'%rawtooloutput%'
           OR v.TextValue LIKE N'%raw tool output%'
           OR v.TextValue LIKE N'%entirepatch%'
           OR v.TextValue LIKE N'%entire patch%'
           OR v.TextValue LIKE N'%approval granted%'
           OR v.TextValue LIKE N'%execution allowed%'
           OR v.TextValue LIKE N'%source applied%'
           OR v.TextValue LIKE N'%memory promoted%'
           OR v.TextValue LIKE N'%resume workflow%'
           OR v.TextValue LIKE N'%tool executed%'
    )
        THROW 54222, 'Workflow checkpoint evidence contains forbidden private-reasoning or authority text.', 1;
END;
GO

CREATE OR ALTER TRIGGER workflow.TR_WorkflowCheckpointEvidenceReference_BlockUpdateDelete
ON workflow.WorkflowCheckpointEvidenceReference
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 54223, 'workflow.WorkflowCheckpointEvidenceReference is append-only.', 1;
END;
GO

CREATE OR ALTER TRIGGER workflow.TR_WorkflowCheckpointGroundingReference_ValidateInsert
ON workflow.WorkflowCheckpointGroundingReference
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
        FROM inserted i
        JOIN workflow.WorkflowCheckpoint c ON c.WorkflowCheckpointId = i.WorkflowCheckpointId
        WHERE c.ProjectId <> i.ProjectId OR c.WorkflowRunId <> i.WorkflowRunId OR ISNULL(c.WorkflowRunStepId, '00000000-0000-0000-0000-000000000000') <> ISNULL(i.WorkflowRunStepId, '00000000-0000-0000-0000-000000000000')
    )
        THROW 54230, 'Workflow checkpoint grounding must match parent checkpoint scope.', 1;

    IF EXISTS (
        SELECT 1
        FROM inserted i
        JOIN workflow.WorkflowRunStep s ON s.WorkflowRunStepId = i.WorkflowRunStepId
        WHERE i.WorkflowRunStepId IS NOT NULL AND (s.ProjectId <> i.ProjectId OR s.WorkflowRunId <> i.WorkflowRunId)
    )
        THROW 54231, 'Workflow checkpoint grounding step must belong to the same workflow run.', 1;

    IF EXISTS (
        SELECT 1
        FROM inserted i
        CROSS APPLY (VALUES (LOWER(CONCAT_WS(N' ', i.ClaimType, i.ClaimId, i.SafeSummary)))) v(TextValue)
        WHERE v.TextValue LIKE N'%hiddenreasoning%'
           OR v.TextValue LIKE N'%hidden reasoning%'
           OR v.TextValue LIKE N'%chainofthought%'
           OR v.TextValue LIKE N'%chain of thought%'
           OR v.TextValue LIKE N'%chain-of-thought%'
           OR v.TextValue LIKE N'%private reasoning%'
           OR v.TextValue LIKE N'%scratchpad%'
           OR v.TextValue LIKE N'%rawprompt%'
           OR v.TextValue LIKE N'%raw prompt%'
           OR v.TextValue LIKE N'%rawcompletion%'
           OR v.TextValue LIKE N'%raw completion%'
           OR v.TextValue LIKE N'%rawtooloutput%'
           OR v.TextValue LIKE N'%raw tool output%'
           OR v.TextValue LIKE N'%entirepatch%'
           OR v.TextValue LIKE N'%entire patch%'
           OR v.TextValue LIKE N'%approval granted%'
           OR v.TextValue LIKE N'%execution allowed%'
           OR v.TextValue LIKE N'%source applied%'
           OR v.TextValue LIKE N'%memory promoted%'
           OR v.TextValue LIKE N'%resume workflow%'
           OR v.TextValue LIKE N'%tool executed%'
    )
        THROW 54232, 'Workflow checkpoint grounding contains forbidden private-reasoning or authority text.', 1;
END;
GO

CREATE OR ALTER TRIGGER workflow.TR_WorkflowCheckpointGroundingReference_BlockUpdateDelete
ON workflow.WorkflowCheckpointGroundingReference
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 54233, 'workflow.WorkflowCheckpointGroundingReference is append-only.', 1;
END;
GO

CREATE OR ALTER PROCEDURE workflow.usp_WorkflowCheckpoint_Get
    @ProjectId UNIQUEIDENTIFIER,
    @WorkflowRunId UNIQUEIDENTIFIER,
    @WorkflowCheckpointId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        WorkflowCheckpointId,
        WorkflowRunId,
        WorkflowRunStepId,
        ProjectId,
        CheckpointKey,
        CheckpointName,
        CheckpointType,
        Status,
        SubjectType,
        SubjectId,
        SafeSummary,
        StateVersion,
        StateJson,
        StateHashSha256,
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
        ResumesWorkflow,
        SatisfiesPolicy,
        TransfersAuthority,
        ApprovesRelease,
        CreatesAcceptedMemory,
        CreatedUtc
    FROM workflow.WorkflowCheckpoint
    WHERE ProjectId = @ProjectId
      AND WorkflowRunId = @WorkflowRunId
      AND WorkflowCheckpointId = @WorkflowCheckpointId;

    SELECT
        WorkflowCheckpointEvidenceReferenceId,
        WorkflowCheckpointId,
        WorkflowRunId,
        WorkflowRunStepId,
        ProjectId,
        EvidenceType,
        EvidenceId,
        EvidenceLabel,
        SafeSummary,
        AllowedUse,
        GovernanceEventId,
        HandoffRecordId,
        ThoughtLedgerEntryId,
        GroundingReferenceId,
        WorkflowRunEvidenceReferenceId,
        CreatedUtc
    FROM workflow.WorkflowCheckpointEvidenceReference
    WHERE ProjectId = @ProjectId
      AND WorkflowRunId = @WorkflowRunId
      AND WorkflowCheckpointId = @WorkflowCheckpointId
    ORDER BY CreatedUtc, WorkflowCheckpointEvidenceReferenceId;

    SELECT
        WorkflowCheckpointGroundingReferenceId,
        WorkflowCheckpointId,
        WorkflowRunId,
        WorkflowRunStepId,
        ProjectId,
        GroundingReferenceId,
        ClaimType,
        ClaimId,
        SafeSummary,
        CreatedUtc
    FROM workflow.WorkflowCheckpointGroundingReference
    WHERE ProjectId = @ProjectId
      AND WorkflowRunId = @WorkflowRunId
      AND WorkflowCheckpointId = @WorkflowCheckpointId
    ORDER BY CreatedUtc, WorkflowCheckpointGroundingReferenceId;
END;
GO

CREATE OR ALTER PROCEDURE workflow.usp_WorkflowCheckpoint_Create
    @WorkflowCheckpointId UNIQUEIDENTIFIER,
    @WorkflowRunId UNIQUEIDENTIFIER,
    @WorkflowRunStepId UNIQUEIDENTIFIER = NULL,
    @ProjectId UNIQUEIDENTIFIER,
    @CheckpointKey NVARCHAR(160),
    @CheckpointName NVARCHAR(200),
    @CheckpointType NVARCHAR(80),
    @Status NVARCHAR(80),
    @SubjectType NVARCHAR(120) = NULL,
    @SubjectId NVARCHAR(300) = NULL,
    @SafeSummary NVARCHAR(1000) = NULL,
    @StateVersion INT,
    @StateJson NVARCHAR(MAX),
    @StateHashSha256 NVARCHAR(128) = NULL,
    @CorrelationId UNIQUEIDENTIFIER = NULL,
    @CausationId UNIQUEIDENTIFIER = NULL,
    @CreatedByActorType NVARCHAR(80),
    @CreatedByActorId NVARCHAR(200),
    @MetadataVersion INT,
    @MetadataJson NVARCHAR(MAX),
    @EvidenceReferencesJson NVARCHAR(MAX) = N'[]',
    @GroundingReferencesJson NVARCHAR(MAX) = N'[]',
    @GrantsApproval BIT = 0,
    @GrantsExecution BIT = 0,
    @MutatesSource BIT = 0,
    @PromotesMemory BIT = 0,
    @StartsWorkflow BIT = 0,
    @ContinuesWorkflow BIT = 0,
    @ResumesWorkflow BIT = 0,
    @SatisfiesPolicy BIT = 0,
    @TransfersAuthority BIT = 0,
    @ApprovesRelease BIT = 0,
    @CreatesAcceptedMemory BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @WorkflowCheckpointId = '00000000-0000-0000-0000-000000000000'
        THROW 54240, 'WorkflowCheckpointId cannot be empty.', 1;

    IF ISJSON(@StateJson) <> 1 OR ISJSON(@MetadataJson) <> 1 OR ISJSON(@EvidenceReferencesJson) <> 1 OR ISJSON(@GroundingReferencesJson) <> 1
        THROW 54241, 'Workflow checkpoint JSON payloads must be valid JSON.', 1;

    IF @GrantsApproval = 1 OR @GrantsExecution = 1 OR @MutatesSource = 1 OR @PromotesMemory = 1 OR @StartsWorkflow = 1 OR @ContinuesWorkflow = 1 OR @ResumesWorkflow = 1 OR @SatisfiesPolicy = 1 OR @TransfersAuthority = 1 OR @ApprovesRelease = 1 OR @CreatesAcceptedMemory = 1
        THROW 54242, 'Workflow checkpoint cannot grant authority, resume workflow, mutate source, or promote memory.', 1;

    DECLARE @UnsafeText NVARCHAR(MAX) = LOWER(CONCAT_WS(N' ', @CheckpointKey, @CheckpointName, @CheckpointType, @Status, @SubjectType, @SubjectId, @SafeSummary, @StateJson, @StateHashSha256, @CreatedByActorType, @CreatedByActorId, @MetadataJson, @EvidenceReferencesJson, @GroundingReferencesJson));

    IF @UnsafeText LIKE N'%hiddenreasoning%'
        OR @UnsafeText LIKE N'%hidden reasoning%'
        OR @UnsafeText LIKE N'%chainofthought%'
        OR @UnsafeText LIKE N'%chain of thought%'
        OR @UnsafeText LIKE N'%chain-of-thought%'
        OR @UnsafeText LIKE N'%private reasoning%'
        OR @UnsafeText LIKE N'%scratchpad%'
        OR @UnsafeText LIKE N'%rawprompt%'
        OR @UnsafeText LIKE N'%raw prompt%'
        OR @UnsafeText LIKE N'%rawcompletion%'
        OR @UnsafeText LIKE N'%raw completion%'
        OR @UnsafeText LIKE N'%rawtooloutput%'
        OR @UnsafeText LIKE N'%raw tool output%'
        OR @UnsafeText LIKE N'%entirepatch%'
        OR @UnsafeText LIKE N'%entire patch%'
        OR @UnsafeText LIKE N'%approval granted%'
        OR @UnsafeText LIKE N'%approved for execution%'
        OR @UnsafeText LIKE N'%execution permission%'
        OR @UnsafeText LIKE N'%execution allowed%'
        OR @UnsafeText LIKE N'%can execute%'
        OR @UnsafeText LIKE N'%authorize execution%'
        OR @UnsafeText LIKE N'%policy satisfied%'
        OR @UnsafeText LIKE N'%satisfy policy%'
        OR @UnsafeText LIKE N'%source applied%'
        OR @UnsafeText LIKE N'%apply source%'
        OR @UnsafeText LIKE N'%apply patch%'
        OR @UnsafeText LIKE N'%memory promoted%'
        OR @UnsafeText LIKE N'%promote memory%'
        OR @UnsafeText LIKE N'%accepted memory%'
        OR @UnsafeText LIKE N'%release approved%'
        OR @UnsafeText LIKE N'%approve release%'
        OR @UnsafeText LIKE N'%ready to ship%'
        OR @UnsafeText LIKE N'%can ship%'
        OR @UnsafeText LIKE N'%authority transferred%'
        OR @UnsafeText LIKE N'%transfer authority%'
        OR @UnsafeText LIKE N'%workflow continued%'
        OR @UnsafeText LIKE N'%continue workflow%'
        OR @UnsafeText LIKE N'%workflow started%'
        OR @UnsafeText LIKE N'%start workflow%'
        OR @UnsafeText LIKE N'%resume workflow%'
        OR @UnsafeText LIKE N'%workflow resumed%'
        OR @UnsafeText LIKE N'%resume allowed%'
        OR @UnsafeText LIKE N'%restorable%'
        OR @UnsafeText LIKE N'%restore workflow%'
        OR @UnsafeText LIKE N'%dispatch agent%'
        OR @UnsafeText LIKE N'%tool executed%'
        OR @UnsafeText LIKE N'%tool ran%'
        THROW 54243, 'Workflow checkpoint contains forbidden private-reasoning or authority/resume text.', 1;

    IF NOT EXISTS (SELECT 1 FROM workflow.WorkflowRun WHERE WorkflowRunId = @WorkflowRunId AND ProjectId = @ProjectId)
        THROW 54244, 'Parent workflow run does not exist for this project.', 1;

    IF @WorkflowRunStepId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM workflow.WorkflowRunStep WHERE WorkflowRunStepId = @WorkflowRunStepId AND WorkflowRunId = @WorkflowRunId AND ProjectId = @ProjectId)
        THROW 54245, 'Parent workflow step does not exist for this workflow run.', 1;

    IF EXISTS (SELECT 1 FROM workflow.WorkflowCheckpoint WHERE WorkflowCheckpointId = @WorkflowCheckpointId)
        THROW 54246, 'Workflow checkpoint already exists.', 1;

    IF EXISTS (SELECT 1 FROM workflow.WorkflowCheckpoint WHERE WorkflowRunId = @WorkflowRunId AND CheckpointKey = LTRIM(RTRIM(@CheckpointKey)))
        THROW 54247, 'Workflow checkpoint key already exists within this workflow run.', 1;

    DECLARE @EvidenceRows TABLE
    (
        EvidenceType NVARCHAR(120) NOT NULL,
        EvidenceId NVARCHAR(300) NOT NULL,
        EvidenceLabel NVARCHAR(240) NULL,
        SafeSummary NVARCHAR(1000) NULL,
        AllowedUse NVARCHAR(120) NULL,
        GovernanceEventId UNIQUEIDENTIFIER NULL,
        HandoffRecordId UNIQUEIDENTIFIER NULL,
        ThoughtLedgerEntryId UNIQUEIDENTIFIER NULL,
        GroundingReferenceId UNIQUEIDENTIFIER NULL,
        WorkflowRunEvidenceReferenceId UNIQUEIDENTIFIER NULL
    );

    INSERT INTO @EvidenceRows
    SELECT
        COALESCE(JSON_VALUE([value], '$.evidenceType'), JSON_VALUE([value], '$.EvidenceType')),
        COALESCE(JSON_VALUE([value], '$.evidenceId'), JSON_VALUE([value], '$.EvidenceId')),
        COALESCE(JSON_VALUE([value], '$.evidenceLabel'), JSON_VALUE([value], '$.EvidenceLabel')),
        COALESCE(JSON_VALUE([value], '$.safeSummary'), JSON_VALUE([value], '$.SafeSummary')),
        COALESCE(JSON_VALUE([value], '$.allowedUse'), JSON_VALUE([value], '$.AllowedUse')),
        TRY_CONVERT(UNIQUEIDENTIFIER, COALESCE(JSON_VALUE([value], '$.governanceEventId'), JSON_VALUE([value], '$.GovernanceEventId'))),
        TRY_CONVERT(UNIQUEIDENTIFIER, COALESCE(JSON_VALUE([value], '$.handoffRecordId'), JSON_VALUE([value], '$.HandoffRecordId'))),
        TRY_CONVERT(UNIQUEIDENTIFIER, COALESCE(JSON_VALUE([value], '$.thoughtLedgerEntryId'), JSON_VALUE([value], '$.ThoughtLedgerEntryId'))),
        TRY_CONVERT(UNIQUEIDENTIFIER, COALESCE(JSON_VALUE([value], '$.groundingReferenceId'), JSON_VALUE([value], '$.GroundingReferenceId'))),
        TRY_CONVERT(UNIQUEIDENTIFIER, COALESCE(JSON_VALUE([value], '$.workflowRunEvidenceReferenceId'), JSON_VALUE([value], '$.WorkflowRunEvidenceReferenceId')))
    FROM OPENJSON(@EvidenceReferencesJson);

    IF EXISTS (SELECT 1 FROM @EvidenceRows WHERE EvidenceType IS NULL OR EvidenceId IS NULL OR LEN(LTRIM(RTRIM(EvidenceId))) = 0)
        THROW 54248, 'Workflow checkpoint evidence references require evidence type and evidence ID.', 1;

    DECLARE @GroundingRows TABLE
    (
        GroundingReferenceId UNIQUEIDENTIFIER NOT NULL,
        ClaimType NVARCHAR(120) NOT NULL,
        ClaimId NVARCHAR(300) NOT NULL,
        SafeSummary NVARCHAR(1000) NULL
    );

    INSERT INTO @GroundingRows
    SELECT
        TRY_CONVERT(UNIQUEIDENTIFIER, COALESCE(JSON_VALUE([value], '$.groundingReferenceId'), JSON_VALUE([value], '$.GroundingReferenceId'))),
        COALESCE(JSON_VALUE([value], '$.claimType'), JSON_VALUE([value], '$.ClaimType')),
        COALESCE(JSON_VALUE([value], '$.claimId'), JSON_VALUE([value], '$.ClaimId')),
        COALESCE(JSON_VALUE([value], '$.safeSummary'), JSON_VALUE([value], '$.SafeSummary'))
    FROM OPENJSON(@GroundingReferencesJson);

    IF EXISTS (SELECT 1 FROM @GroundingRows WHERE GroundingReferenceId IS NULL OR ClaimType IS NULL OR ClaimId IS NULL OR LEN(LTRIM(RTRIM(ClaimId))) = 0)
        THROW 54249, 'Workflow checkpoint grounding references require grounding ID, claim type, and claim ID.', 1;

    BEGIN TRANSACTION;

    INSERT INTO workflow.WorkflowCheckpoint
    (
        WorkflowCheckpointId,
        WorkflowRunId,
        WorkflowRunStepId,
        ProjectId,
        CheckpointKey,
        CheckpointName,
        CheckpointType,
        Status,
        SubjectType,
        SubjectId,
        SafeSummary,
        StateVersion,
        StateJson,
        StateHashSha256,
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
        ResumesWorkflow,
        SatisfiesPolicy,
        TransfersAuthority,
        ApprovesRelease,
        CreatesAcceptedMemory
    )
    VALUES
    (
        @WorkflowCheckpointId,
        @WorkflowRunId,
        @WorkflowRunStepId,
        @ProjectId,
        LTRIM(RTRIM(@CheckpointKey)),
        LTRIM(RTRIM(@CheckpointName)),
        LTRIM(RTRIM(@CheckpointType)),
        LTRIM(RTRIM(@Status)),
        NULLIF(LTRIM(RTRIM(@SubjectType)), N''),
        NULLIF(LTRIM(RTRIM(@SubjectId)), N''),
        NULLIF(LTRIM(RTRIM(@SafeSummary)), N''),
        @StateVersion,
        @StateJson,
        NULLIF(LTRIM(RTRIM(@StateHashSha256)), N''),
        COALESCE(@CorrelationId, @WorkflowRunId),
        @CausationId,
        LTRIM(RTRIM(@CreatedByActorType)),
        LTRIM(RTRIM(@CreatedByActorId)),
        @MetadataVersion,
        @MetadataJson,
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
        0
    );

    INSERT INTO workflow.WorkflowCheckpointEvidenceReference
    (
        WorkflowCheckpointEvidenceReferenceId,
        WorkflowCheckpointId,
        WorkflowRunId,
        WorkflowRunStepId,
        ProjectId,
        EvidenceType,
        EvidenceId,
        EvidenceLabel,
        SafeSummary,
        AllowedUse,
        GovernanceEventId,
        HandoffRecordId,
        ThoughtLedgerEntryId,
        GroundingReferenceId,
        WorkflowRunEvidenceReferenceId
    )
    SELECT
        NEWID(),
        @WorkflowCheckpointId,
        @WorkflowRunId,
        @WorkflowRunStepId,
        @ProjectId,
        LTRIM(RTRIM(EvidenceType)),
        LTRIM(RTRIM(EvidenceId)),
        NULLIF(LTRIM(RTRIM(EvidenceLabel)), N''),
        NULLIF(LTRIM(RTRIM(SafeSummary)), N''),
        NULLIF(LTRIM(RTRIM(AllowedUse)), N''),
        GovernanceEventId,
        HandoffRecordId,
        ThoughtLedgerEntryId,
        GroundingReferenceId,
        WorkflowRunEvidenceReferenceId
    FROM @EvidenceRows;

    INSERT INTO workflow.WorkflowCheckpointGroundingReference
    (
        WorkflowCheckpointGroundingReferenceId,
        WorkflowCheckpointId,
        WorkflowRunId,
        WorkflowRunStepId,
        ProjectId,
        GroundingReferenceId,
        ClaimType,
        ClaimId,
        SafeSummary
    )
    SELECT
        NEWID(),
        @WorkflowCheckpointId,
        @WorkflowRunId,
        @WorkflowRunStepId,
        @ProjectId,
        GroundingReferenceId,
        LTRIM(RTRIM(ClaimType)),
        LTRIM(RTRIM(ClaimId)),
        NULLIF(LTRIM(RTRIM(SafeSummary)), N'')
    FROM @GroundingRows;

    COMMIT TRANSACTION;

    EXEC workflow.usp_WorkflowCheckpoint_Get @ProjectId = @ProjectId, @WorkflowRunId = @WorkflowRunId, @WorkflowCheckpointId = @WorkflowCheckpointId;
END;
GO

CREATE OR ALTER PROCEDURE workflow.usp_WorkflowCheckpoint_ListByRun
    @ProjectId UNIQUEIDENTIFIER,
    @WorkflowRunId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    SET @Take = CASE WHEN @Take IS NULL OR @Take < 1 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@Take)
        c.WorkflowCheckpointId,
        c.WorkflowRunId,
        c.WorkflowRunStepId,
        c.ProjectId,
        c.CheckpointKey,
        c.CheckpointName,
        c.CheckpointType,
        c.Status,
        c.SubjectType,
        c.SubjectId,
        c.StateHashSha256,
        c.CorrelationId,
        c.CausationId,
        (SELECT COUNT(1) FROM workflow.WorkflowCheckpointEvidenceReference e WHERE e.WorkflowCheckpointId = c.WorkflowCheckpointId) AS EvidenceReferenceCount,
        (SELECT COUNT(1) FROM workflow.WorkflowCheckpointGroundingReference g WHERE g.WorkflowCheckpointId = c.WorkflowCheckpointId) AS GroundingReferenceCount,
        c.CreatedUtc
    FROM workflow.WorkflowCheckpoint c
    WHERE c.ProjectId = @ProjectId
      AND c.WorkflowRunId = @WorkflowRunId
    ORDER BY c.CreatedUtc DESC, c.WorkflowCheckpointId DESC;
END;
GO

CREATE OR ALTER PROCEDURE workflow.usp_WorkflowCheckpoint_ListByStep
    @ProjectId UNIQUEIDENTIFIER,
    @WorkflowRunId UNIQUEIDENTIFIER,
    @WorkflowRunStepId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    SET @Take = CASE WHEN @Take IS NULL OR @Take < 1 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@Take)
        c.WorkflowCheckpointId,
        c.WorkflowRunId,
        c.WorkflowRunStepId,
        c.ProjectId,
        c.CheckpointKey,
        c.CheckpointName,
        c.CheckpointType,
        c.Status,
        c.SubjectType,
        c.SubjectId,
        c.StateHashSha256,
        c.CorrelationId,
        c.CausationId,
        (SELECT COUNT(1) FROM workflow.WorkflowCheckpointEvidenceReference e WHERE e.WorkflowCheckpointId = c.WorkflowCheckpointId) AS EvidenceReferenceCount,
        (SELECT COUNT(1) FROM workflow.WorkflowCheckpointGroundingReference g WHERE g.WorkflowCheckpointId = c.WorkflowCheckpointId) AS GroundingReferenceCount,
        c.CreatedUtc
    FROM workflow.WorkflowCheckpoint c
    WHERE c.ProjectId = @ProjectId
      AND c.WorkflowRunId = @WorkflowRunId
      AND c.WorkflowRunStepId = @WorkflowRunStepId
    ORDER BY c.CreatedUtc DESC, c.WorkflowCheckpointId DESC;
END;
GO

CREATE OR ALTER PROCEDURE workflow.usp_WorkflowCheckpoint_ListByCorrelation
    @ProjectId UNIQUEIDENTIFIER,
    @CorrelationId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    SET @Take = CASE WHEN @Take IS NULL OR @Take < 1 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@Take)
        c.WorkflowCheckpointId,
        c.WorkflowRunId,
        c.WorkflowRunStepId,
        c.ProjectId,
        c.CheckpointKey,
        c.CheckpointName,
        c.CheckpointType,
        c.Status,
        c.SubjectType,
        c.SubjectId,
        c.StateHashSha256,
        c.CorrelationId,
        c.CausationId,
        (SELECT COUNT(1) FROM workflow.WorkflowCheckpointEvidenceReference e WHERE e.WorkflowCheckpointId = c.WorkflowCheckpointId) AS EvidenceReferenceCount,
        (SELECT COUNT(1) FROM workflow.WorkflowCheckpointGroundingReference g WHERE g.WorkflowCheckpointId = c.WorkflowCheckpointId) AS GroundingReferenceCount,
        c.CreatedUtc
    FROM workflow.WorkflowCheckpoint c
    WHERE c.ProjectId = @ProjectId
      AND c.CorrelationId = @CorrelationId
    ORDER BY c.CreatedUtc DESC, c.WorkflowCheckpointId DESC;
END;
GO

CREATE OR ALTER PROCEDURE workflow.usp_WorkflowCheckpoint_ListBySubject
    @ProjectId UNIQUEIDENTIFIER,
    @SubjectType NVARCHAR(120),
    @SubjectId NVARCHAR(300),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    SET @Take = CASE WHEN @Take IS NULL OR @Take < 1 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@Take)
        c.WorkflowCheckpointId,
        c.WorkflowRunId,
        c.WorkflowRunStepId,
        c.ProjectId,
        c.CheckpointKey,
        c.CheckpointName,
        c.CheckpointType,
        c.Status,
        c.SubjectType,
        c.SubjectId,
        c.StateHashSha256,
        c.CorrelationId,
        c.CausationId,
        (SELECT COUNT(1) FROM workflow.WorkflowCheckpointEvidenceReference e WHERE e.WorkflowCheckpointId = c.WorkflowCheckpointId) AS EvidenceReferenceCount,
        (SELECT COUNT(1) FROM workflow.WorkflowCheckpointGroundingReference g WHERE g.WorkflowCheckpointId = c.WorkflowCheckpointId) AS GroundingReferenceCount,
        c.CreatedUtc
    FROM workflow.WorkflowCheckpoint c
    WHERE c.ProjectId = @ProjectId
      AND c.SubjectType = LTRIM(RTRIM(@SubjectType))
      AND c.SubjectId = LTRIM(RTRIM(@SubjectId))
    ORDER BY c.CreatedUtc DESC, c.WorkflowCheckpointId DESC;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'IronDevGovernanceEventRuntimeRole') IS NOT NULL
BEGIN
    GRANT EXECUTE ON OBJECT::workflow.usp_WorkflowCheckpoint_Create TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::workflow.usp_WorkflowCheckpoint_Get TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::workflow.usp_WorkflowCheckpoint_ListByRun TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::workflow.usp_WorkflowCheckpoint_ListByStep TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::workflow.usp_WorkflowCheckpoint_ListByCorrelation TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::workflow.usp_WorkflowCheckpoint_ListBySubject TO IronDevGovernanceEventRuntimeRole;
    GRANT SELECT ON OBJECT::workflow.WorkflowCheckpoint TO IronDevGovernanceEventRuntimeRole;
    GRANT SELECT ON OBJECT::workflow.WorkflowCheckpointEvidenceReference TO IronDevGovernanceEventRuntimeRole;
    GRANT SELECT ON OBJECT::workflow.WorkflowCheckpointGroundingReference TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::workflow.WorkflowCheckpoint TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::workflow.WorkflowCheckpointEvidenceReference TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::workflow.WorkflowCheckpointGroundingReference TO IronDevGovernanceEventRuntimeRole;
END;
GO
