IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance')
BEGIN
    EXEC(N'CREATE SCHEMA governance');
END;
GO

IF OBJECT_ID(N'governance.WorkflowTransitionRecord', N'U') IS NULL
BEGIN
    CREATE TABLE governance.WorkflowTransitionRecord
    (
        WorkflowTransitionRecordId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        WorkflowRunId NVARCHAR(300) NOT NULL,
        WorkflowStepId NVARCHAR(300) NOT NULL,
        TransitionKind NVARCHAR(100) NOT NULL,
        PreviousWorkflowStateHash NVARCHAR(256) NOT NULL,
        NewWorkflowStateHash NVARCHAR(256) NOT NULL,
        PreviousStepStateHash NVARCHAR(256) NOT NULL,
        NewStepStateHash NVARCHAR(256) NOT NULL,
        PreviousStepId NVARCHAR(300) NULL,
        NextStepId NVARCHAR(300) NULL,
        WorkflowContinuationGateEvaluationId UNIQUEIDENTIFIER NOT NULL,
        WorkflowContinuationGateEvaluationHash NVARCHAR(256) NOT NULL,
        SourceApplyRequestId UNIQUEIDENTIFIER NOT NULL,
        SourceApplyRequestHash NVARCHAR(256) NOT NULL,
        SourceApplyReceiptId UNIQUEIDENTIFIER NOT NULL,
        SourceApplyReceiptHash NVARCHAR(256) NOT NULL,
        RollbackExecutionReceiptId UNIQUEIDENTIFIER NULL,
        RollbackExecutionReceiptHash NVARCHAR(256) NULL,
        RollbackExecutionAuditReportId UNIQUEIDENTIFIER NULL,
        RollbackExecutionAuditReportHash NVARCHAR(256) NULL,
        WorkflowStateMutated BIT NOT NULL,
        StepCompleted BIT NOT NULL,
        NextStepStarted BIT NOT NULL,
        ReleaseReadinessInferred BIT NOT NULL,
        ReleaseApproved BIT NOT NULL,
        SourceApplyExecuted BIT NOT NULL,
        RollbackExecuted BIT NOT NULL,
        TransitionedAtUtc DATETIMEOFFSET(7) NOT NULL,
        WorkflowTransitionRecordHash NVARCHAR(256) NOT NULL,
        EvidenceReferencesJson NVARCHAR(MAX) NOT NULL,
        BoundaryMaximsJson NVARCHAR(MAX) NOT NULL,
        BoundaryText NVARCHAR(MAX) NOT NULL,
        StoredAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_WorkflowTransitionRecord_StoredAtUtc DEFAULT SYSUTCDATETIME(),
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT PK_WorkflowTransitionRecord PRIMARY KEY CLUSTERED (WorkflowTransitionRecordId),
        CONSTRAINT CK_WorkflowTransitionRecord_TransitionKind CHECK (TransitionKind IN (N'ContinueToNextStep', N'MarkStepComplete', N'BlockedNoTransition')),
        CONSTRAINT CK_WorkflowTransitionRecord_WorkflowRunId_NotBlank CHECK (LEN(LTRIM(RTRIM(WorkflowRunId))) > 0),
        CONSTRAINT CK_WorkflowTransitionRecord_WorkflowStepId_NotBlank CHECK (LEN(LTRIM(RTRIM(WorkflowStepId))) > 0),
        CONSTRAINT CK_WorkflowTransitionRecord_PreviousWorkflowHash CHECK (LEN(LTRIM(RTRIM(PreviousWorkflowStateHash))) > 0 AND PreviousWorkflowStateHash LIKE N'sha256:%'),
        CONSTRAINT CK_WorkflowTransitionRecord_NewWorkflowHash CHECK (LEN(LTRIM(RTRIM(NewWorkflowStateHash))) > 0 AND NewWorkflowStateHash LIKE N'sha256:%'),
        CONSTRAINT CK_WorkflowTransitionRecord_PreviousStepHash CHECK (LEN(LTRIM(RTRIM(PreviousStepStateHash))) > 0 AND PreviousStepStateHash LIKE N'sha256:%'),
        CONSTRAINT CK_WorkflowTransitionRecord_NewStepHash CHECK (LEN(LTRIM(RTRIM(NewStepStateHash))) > 0 AND NewStepStateHash LIKE N'sha256:%'),
        CONSTRAINT CK_WorkflowTransitionRecord_GateHash CHECK (LEN(LTRIM(RTRIM(WorkflowContinuationGateEvaluationHash))) > 0 AND WorkflowContinuationGateEvaluationHash LIKE N'sha256:%'),
        CONSTRAINT CK_WorkflowTransitionRecord_SourceApplyRequestHash CHECK (LEN(LTRIM(RTRIM(SourceApplyRequestHash))) > 0 AND SourceApplyRequestHash LIKE N'sha256:%'),
        CONSTRAINT CK_WorkflowTransitionRecord_SourceApplyReceiptHash CHECK (LEN(LTRIM(RTRIM(SourceApplyReceiptHash))) > 0 AND SourceApplyReceiptHash LIKE N'sha256:%'),
        CONSTRAINT CK_WorkflowTransitionRecord_RecordHash CHECK (LEN(LTRIM(RTRIM(WorkflowTransitionRecordHash))) > 0 AND WorkflowTransitionRecordHash LIKE N'sha256:%'),
        CONSTRAINT CK_WorkflowTransitionRecord_EvidenceJson CHECK (ISJSON(EvidenceReferencesJson) = 1 AND LEN(LTRIM(RTRIM(EvidenceReferencesJson))) > 2),
        CONSTRAINT CK_WorkflowTransitionRecord_BoundaryJson CHECK (ISJSON(BoundaryMaximsJson) = 1 AND LEN(LTRIM(RTRIM(BoundaryMaximsJson))) > 2),
        CONSTRAINT CK_WorkflowTransitionRecord_BoundaryText CHECK (LEN(LTRIM(RTRIM(BoundaryText))) > 0),
        CONSTRAINT CK_WorkflowTransitionRecord_NoReleaseReadiness CHECK (ReleaseReadinessInferred = 0),
        CONSTRAINT CK_WorkflowTransitionRecord_NoReleaseApproval CHECK (ReleaseApproved = 0),
        CONSTRAINT CK_WorkflowTransitionRecord_NoSourceApplyExecution CHECK (SourceApplyExecuted = 0),
        CONSTRAINT CK_WorkflowTransitionRecord_NoRollbackExecution CHECK (RollbackExecuted = 0),
        CONSTRAINT CK_WorkflowTransitionRecord_RollbackReceiptPair CHECK ((RollbackExecutionReceiptId IS NULL AND RollbackExecutionReceiptHash IS NULL) OR (RollbackExecutionReceiptId IS NOT NULL AND RollbackExecutionReceiptHash IS NOT NULL AND RollbackExecutionReceiptHash LIKE N'sha256:%')),
        CONSTRAINT CK_WorkflowTransitionRecord_RollbackAuditPair CHECK ((RollbackExecutionAuditReportId IS NULL AND RollbackExecutionAuditReportHash IS NULL) OR (RollbackExecutionAuditReportId IS NOT NULL AND RollbackExecutionAuditReportHash IS NOT NULL AND RollbackExecutionAuditReportHash LIKE N'sha256:%')),
        CONSTRAINT CK_WorkflowTransitionRecord_RollbackAuditRequiresReceipt CHECK (RollbackExecutionAuditReportId IS NULL OR RollbackExecutionReceiptId IS NOT NULL),
        CONSTRAINT CK_WorkflowTransitionRecord_ContinueTruth CHECK (TransitionKind <> N'ContinueToNextStep' OR (WorkflowStateMutated = 1 AND StepCompleted = 1 AND NextStepStarted = 1 AND PreviousStepId IS NOT NULL AND NextStepId IS NOT NULL AND PreviousWorkflowStateHash <> NewWorkflowStateHash AND PreviousStepStateHash <> NewStepStateHash)),
        CONSTRAINT CK_WorkflowTransitionRecord_MarkCompleteTruth CHECK (TransitionKind <> N'MarkStepComplete' OR (WorkflowStateMutated = 1 AND StepCompleted = 1 AND NextStepStarted = 0 AND PreviousStepId IS NOT NULL AND PreviousWorkflowStateHash <> NewWorkflowStateHash AND PreviousStepStateHash <> NewStepStateHash)),
        CONSTRAINT CK_WorkflowTransitionRecord_BlockedTruth CHECK (TransitionKind <> N'BlockedNoTransition' OR (WorkflowStateMutated = 0 AND StepCompleted = 0 AND NextStepStarted = 0 AND PreviousWorkflowStateHash = NewWorkflowStateHash AND PreviousStepStateHash = NewStepStateHash))
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_WorkflowTransitionRecord_Project_Hash' AND object_id = OBJECT_ID(N'governance.WorkflowTransitionRecord'))
BEGIN
    CREATE UNIQUE INDEX UX_WorkflowTransitionRecord_Project_Hash
    ON governance.WorkflowTransitionRecord(ProjectId, WorkflowTransitionRecordHash);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowTransitionRecord_Project_WorkflowRun' AND object_id = OBJECT_ID(N'governance.WorkflowTransitionRecord'))
BEGIN
    CREATE INDEX IX_WorkflowTransitionRecord_Project_WorkflowRun
    ON governance.WorkflowTransitionRecord(ProjectId, WorkflowRunId, TransitionedAtUtc DESC, WorkflowTransitionRecordId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowTransitionRecord_Project_WorkflowStep' AND object_id = OBJECT_ID(N'governance.WorkflowTransitionRecord'))
BEGIN
    CREATE INDEX IX_WorkflowTransitionRecord_Project_WorkflowStep
    ON governance.WorkflowTransitionRecord(ProjectId, WorkflowRunId, WorkflowStepId, TransitionedAtUtc DESC, WorkflowTransitionRecordId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowTransitionRecord_Project_Gate' AND object_id = OBJECT_ID(N'governance.WorkflowTransitionRecord'))
BEGIN
    CREATE INDEX IX_WorkflowTransitionRecord_Project_Gate
    ON governance.WorkflowTransitionRecord(ProjectId, WorkflowContinuationGateEvaluationId, TransitionedAtUtc DESC, WorkflowTransitionRecordId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowTransitionRecord_Project_SourceApplyReceipt' AND object_id = OBJECT_ID(N'governance.WorkflowTransitionRecord'))
BEGIN
    CREATE INDEX IX_WorkflowTransitionRecord_Project_SourceApplyReceipt
    ON governance.WorkflowTransitionRecord(ProjectId, SourceApplyReceiptId, TransitionedAtUtc DESC, WorkflowTransitionRecordId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowTransitionRecord_Project_RollbackReceipt' AND object_id = OBJECT_ID(N'governance.WorkflowTransitionRecord'))
BEGIN
    CREATE INDEX IX_WorkflowTransitionRecord_Project_RollbackReceipt
    ON governance.WorkflowTransitionRecord(ProjectId, RollbackExecutionReceiptId, TransitionedAtUtc DESC, WorkflowTransitionRecordId DESC)
    WHERE RollbackExecutionReceiptId IS NOT NULL;
END;
GO

IF OBJECT_ID(N'governance.TR_WorkflowTransitionRecord_ValidateInsert', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_WorkflowTransitionRecord_ValidateInsert;
END;
GO

CREATE TRIGGER governance.TR_WorkflowTransitionRecord_ValidateInsert
ON governance.WorkflowTransitionRecord
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        CROSS APPLY (SELECT LOWER(CONCAT(
            i.WorkflowRunId, N' ', i.WorkflowStepId, N' ', i.TransitionKind, N' ',
            i.PreviousWorkflowStateHash, N' ', i.NewWorkflowStateHash, N' ', i.PreviousStepStateHash, N' ', i.NewStepStateHash, N' ',
            COALESCE(i.PreviousStepId, N''), N' ', COALESCE(i.NextStepId, N''), N' ',
            i.WorkflowContinuationGateEvaluationHash, N' ', i.SourceApplyRequestHash, N' ', i.SourceApplyReceiptHash, N' ',
            COALESCE(i.RollbackExecutionReceiptHash, N''), N' ', COALESCE(i.RollbackExecutionAuditReportHash, N''), N' ',
            i.WorkflowTransitionRecordHash, N' ', i.EvidenceReferencesJson, N' ', i.BoundaryMaximsJson, N' ', i.BoundaryText
        )) AS TextToCheck) c
        WHERE c.TextToCheck LIKE N'%rawprompt%'
           OR c.TextToCheck LIKE N'%raw prompt%'
           OR c.TextToCheck LIKE N'%rawcompletion%'
           OR c.TextToCheck LIKE N'%raw completion%'
           OR c.TextToCheck LIKE N'%rawtooloutput%'
           OR c.TextToCheck LIKE N'%raw tool output%'
           OR c.TextToCheck LIKE N'%chainofthought%'
           OR c.TextToCheck LIKE N'%chain-of-thought%'
           OR c.TextToCheck LIKE N'%chain of thought%'
           OR c.TextToCheck LIKE N'%scratchpad%'
           OR c.TextToCheck LIKE N'%private reasoning%'
           OR c.TextToCheck LIKE N'%hidden reasoning%'
           OR c.TextToCheck LIKE N'%system prompt%'
           OR c.TextToCheck LIKE N'%developer prompt%'
           OR c.TextToCheck LIKE N'%password%'
           OR c.TextToCheck LIKE N'%api_key%'
           OR c.TextToCheck LIKE N'%secret%'
           OR c.TextToCheck LIKE N'%private key%'
           OR c.TextToCheck LIKE N'%bearer%'
           OR c.TextToCheck LIKE N'%safe to continue%'
           OR c.TextToCheck LIKE N'%release approved%'
           OR (c.TextToCheck LIKE N'%release ready%' AND c.TextToCheck NOT LIKE N'%not release ready%')
           OR (c.TextToCheck LIKE N'%release readiness%' AND c.TextToCheck NOT LIKE N'%not release readiness%' AND c.TextToCheck NOT LIKE N'%does not infer release readiness%')
           OR c.TextToCheck LIKE N'%source applied by transition record%'
           OR c.TextToCheck LIKE N'%rollback executed by transition record%'
           OR c.TextToCheck LIKE N'%rollback cleaned up%'
           OR c.TextToCheck LIKE N'%crash cleaned up%'
           OR c.TextToCheck LIKE N'%gitcommitted%'
           OR c.TextToCheck LIKE N'%gitpushed%'
           OR c.TextToCheck LIKE N'%pull request created%'
           OR c.TextToCheck LIKE N'%memory promoted%'
           OR c.TextToCheck LIKE N'%retrieval activated%'
           OR c.TextToCheck LIKE N'%workflow continued by gate alone%'
           OR c.TextToCheck LIKE N'%workflow continued automatically%'
           OR c.TextToCheck LIKE N'%workflow transition grants release%'
    )
    BEGIN
        THROW 53501, 'WorkflowTransitionRecord must not contain raw/private material or authority claims.', 1;
    END;
END;
GO

IF OBJECT_ID(N'governance.TR_WorkflowTransitionRecord_BlockUpdateDelete', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_WorkflowTransitionRecord_BlockUpdateDelete;
END;
GO

CREATE TRIGGER governance.TR_WorkflowTransitionRecord_BlockUpdateDelete
ON governance.WorkflowTransitionRecord
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 53502, 'WorkflowTransitionRecord is append-only.', 1;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_WorkflowTransitionRecord_Save
    @WorkflowTransitionRecordId UNIQUEIDENTIFIER,
    @ProjectId UNIQUEIDENTIFIER,
    @WorkflowRunId NVARCHAR(300),
    @WorkflowStepId NVARCHAR(300),
    @TransitionKind NVARCHAR(100),
    @PreviousWorkflowStateHash NVARCHAR(256),
    @NewWorkflowStateHash NVARCHAR(256),
    @PreviousStepStateHash NVARCHAR(256),
    @NewStepStateHash NVARCHAR(256),
    @PreviousStepId NVARCHAR(300) = NULL,
    @NextStepId NVARCHAR(300) = NULL,
    @WorkflowContinuationGateEvaluationId UNIQUEIDENTIFIER,
    @WorkflowContinuationGateEvaluationHash NVARCHAR(256),
    @SourceApplyRequestId UNIQUEIDENTIFIER,
    @SourceApplyRequestHash NVARCHAR(256),
    @SourceApplyReceiptId UNIQUEIDENTIFIER,
    @SourceApplyReceiptHash NVARCHAR(256),
    @RollbackExecutionReceiptId UNIQUEIDENTIFIER = NULL,
    @RollbackExecutionReceiptHash NVARCHAR(256) = NULL,
    @RollbackExecutionAuditReportId UNIQUEIDENTIFIER = NULL,
    @RollbackExecutionAuditReportHash NVARCHAR(256) = NULL,
    @WorkflowStateMutated BIT,
    @StepCompleted BIT,
    @NextStepStarted BIT,
    @ReleaseReadinessInferred BIT,
    @ReleaseApproved BIT,
    @SourceApplyExecuted BIT,
    @RollbackExecuted BIT,
    @TransitionedAtUtc DATETIMEOFFSET(7),
    @WorkflowTransitionRecordHash NVARCHAR(256),
    @EvidenceReferencesJson NVARCHAR(MAX),
    @BoundaryMaximsJson NVARCHAR(MAX),
    @BoundaryText NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM governance.WorkflowTransitionRecord WHERE WorkflowTransitionRecordId = @WorkflowTransitionRecordId AND WorkflowTransitionRecordHash = @WorkflowTransitionRecordHash)
        RETURN;

    IF EXISTS (SELECT 1 FROM governance.WorkflowTransitionRecord WHERE WorkflowTransitionRecordId = @WorkflowTransitionRecordId AND WorkflowTransitionRecordHash <> @WorkflowTransitionRecordHash)
        THROW 53503, 'WorkflowTransitionRecordId already exists with different hash.', 1;

    INSERT INTO governance.WorkflowTransitionRecord
    (
        WorkflowTransitionRecordId, ProjectId, WorkflowRunId, WorkflowStepId, TransitionKind,
        PreviousWorkflowStateHash, NewWorkflowStateHash, PreviousStepStateHash, NewStepStateHash,
        PreviousStepId, NextStepId, WorkflowContinuationGateEvaluationId, WorkflowContinuationGateEvaluationHash,
        SourceApplyRequestId, SourceApplyRequestHash, SourceApplyReceiptId, SourceApplyReceiptHash,
        RollbackExecutionReceiptId, RollbackExecutionReceiptHash, RollbackExecutionAuditReportId, RollbackExecutionAuditReportHash,
        WorkflowStateMutated, StepCompleted, NextStepStarted, ReleaseReadinessInferred, ReleaseApproved,
        SourceApplyExecuted, RollbackExecuted, TransitionedAtUtc, WorkflowTransitionRecordHash,
        EvidenceReferencesJson, BoundaryMaximsJson, BoundaryText
    )
    VALUES
    (
        @WorkflowTransitionRecordId, @ProjectId, LTRIM(RTRIM(@WorkflowRunId)), LTRIM(RTRIM(@WorkflowStepId)), LTRIM(RTRIM(@TransitionKind)),
        LTRIM(RTRIM(@PreviousWorkflowStateHash)), LTRIM(RTRIM(@NewWorkflowStateHash)), LTRIM(RTRIM(@PreviousStepStateHash)), LTRIM(RTRIM(@NewStepStateHash)),
        NULLIF(LTRIM(RTRIM(@PreviousStepId)), N''), NULLIF(LTRIM(RTRIM(@NextStepId)), N''), @WorkflowContinuationGateEvaluationId, LTRIM(RTRIM(@WorkflowContinuationGateEvaluationHash)),
        @SourceApplyRequestId, LTRIM(RTRIM(@SourceApplyRequestHash)), @SourceApplyReceiptId, LTRIM(RTRIM(@SourceApplyReceiptHash)),
        @RollbackExecutionReceiptId, NULLIF(LTRIM(RTRIM(@RollbackExecutionReceiptHash)), N''), @RollbackExecutionAuditReportId, NULLIF(LTRIM(RTRIM(@RollbackExecutionAuditReportHash)), N''),
        @WorkflowStateMutated, @StepCompleted, @NextStepStarted, @ReleaseReadinessInferred, @ReleaseApproved,
        @SourceApplyExecuted, @RollbackExecuted, @TransitionedAtUtc, LTRIM(RTRIM(@WorkflowTransitionRecordHash)),
        @EvidenceReferencesJson, @BoundaryMaximsJson, @BoundaryText
    );
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_WorkflowTransitionRecord_Get
    @ProjectId UNIQUEIDENTIFIER,
    @WorkflowTransitionRecordId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM governance.WorkflowTransitionRecord WHERE ProjectId = @ProjectId AND WorkflowTransitionRecordId = @WorkflowTransitionRecordId;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_WorkflowTransitionRecord_GetByRecordHash
    @ProjectId UNIQUEIDENTIFIER,
    @WorkflowTransitionRecordHash NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM governance.WorkflowTransitionRecord WHERE ProjectId = @ProjectId AND WorkflowTransitionRecordHash = LTRIM(RTRIM(@WorkflowTransitionRecordHash));
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_WorkflowTransitionRecord_ListByWorkflowRun
    @ProjectId UNIQUEIDENTIFIER,
    @WorkflowRunId NVARCHAR(300),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;
    SELECT TOP (@EffectiveTake) * FROM governance.WorkflowTransitionRecord
    WHERE ProjectId = @ProjectId AND WorkflowRunId = LTRIM(RTRIM(@WorkflowRunId))
    ORDER BY TransitionedAtUtc DESC, WorkflowTransitionRecordId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_WorkflowTransitionRecord_ListByWorkflowStep
    @ProjectId UNIQUEIDENTIFIER,
    @WorkflowRunId NVARCHAR(300),
    @WorkflowStepId NVARCHAR(300),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;
    SELECT TOP (@EffectiveTake) * FROM governance.WorkflowTransitionRecord
    WHERE ProjectId = @ProjectId AND WorkflowRunId = LTRIM(RTRIM(@WorkflowRunId)) AND WorkflowStepId = LTRIM(RTRIM(@WorkflowStepId))
    ORDER BY TransitionedAtUtc DESC, WorkflowTransitionRecordId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_WorkflowTransitionRecord_ListByContinuationGateEvaluation
    @ProjectId UNIQUEIDENTIFIER,
    @WorkflowContinuationGateEvaluationId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;
    SELECT TOP (@EffectiveTake) * FROM governance.WorkflowTransitionRecord
    WHERE ProjectId = @ProjectId AND WorkflowContinuationGateEvaluationId = @WorkflowContinuationGateEvaluationId
    ORDER BY TransitionedAtUtc DESC, WorkflowTransitionRecordId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_WorkflowTransitionRecord_ListBySourceApplyReceipt
    @ProjectId UNIQUEIDENTIFIER,
    @SourceApplyReceiptId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;
    SELECT TOP (@EffectiveTake) * FROM governance.WorkflowTransitionRecord
    WHERE ProjectId = @ProjectId AND SourceApplyReceiptId = @SourceApplyReceiptId
    ORDER BY TransitionedAtUtc DESC, WorkflowTransitionRecordId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_WorkflowTransitionRecord_ListByRollbackExecutionReceipt
    @ProjectId UNIQUEIDENTIFIER,
    @RollbackExecutionReceiptId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;
    SELECT TOP (@EffectiveTake) * FROM governance.WorkflowTransitionRecord
    WHERE ProjectId = @ProjectId AND RollbackExecutionReceiptId = @RollbackExecutionReceiptId
    ORDER BY TransitionedAtUtc DESC, WorkflowTransitionRecordId DESC;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'IronDevGovernanceEventRuntimeRole') IS NOT NULL
BEGIN
    GRANT EXECUTE ON OBJECT::governance.usp_WorkflowTransitionRecord_Save TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_WorkflowTransitionRecord_Get TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_WorkflowTransitionRecord_GetByRecordHash TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_WorkflowTransitionRecord_ListByWorkflowRun TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_WorkflowTransitionRecord_ListByWorkflowStep TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_WorkflowTransitionRecord_ListByContinuationGateEvaluation TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_WorkflowTransitionRecord_ListBySourceApplyReceipt TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_WorkflowTransitionRecord_ListByRollbackExecutionReceipt TO IronDevGovernanceEventRuntimeRole;
    GRANT SELECT ON OBJECT::governance.WorkflowTransitionRecord TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::governance.WorkflowTransitionRecord TO IronDevGovernanceEventRuntimeRole;
    DENY ALTER ON SCHEMA::governance TO IronDevGovernanceEventRuntimeRole;
END;
GO
