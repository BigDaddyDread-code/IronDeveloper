IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance')
BEGIN
    EXEC(N'CREATE SCHEMA governance');
END;
GO

IF OBJECT_ID(N'governance.ReleaseReadinessDecisionRecord', N'U') IS NULL
BEGIN
    CREATE TABLE governance.ReleaseReadinessDecisionRecord
    (
        ReleaseReadinessDecisionRecordId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        ReleaseReadinessReportId UNIQUEIDENTIFIER NOT NULL,
        ReleaseReadinessReportHash NVARCHAR(128) NOT NULL,
        WorkflowRunId NVARCHAR(200) NOT NULL,
        WorkflowStepId NVARCHAR(200) NOT NULL,
        SubjectKind NVARCHAR(120) NOT NULL,
        SubjectId NVARCHAR(300) NOT NULL,
        SubjectHash NVARCHAR(128) NOT NULL,
        DecisionStatus NVARCHAR(120) NOT NULL,
        ReleaseReadinessEvidenceSatisfied BIT NOT NULL,
        ReleaseApproved BIT NOT NULL,
        DeploymentApproved BIT NOT NULL,
        MergeApproved BIT NOT NULL,
        SourceApplyExecutedByDecision BIT NOT NULL,
        RollbackExecutedByDecision BIT NOT NULL,
        WorkflowMutatedByDecision BIT NOT NULL,
        GitOperationExecutedByDecision BIT NOT NULL,
        ReleaseExecutedByDecision BIT NOT NULL,
        HumanReviewRequiredForReleaseApproval BIT NOT NULL,
        HumanReviewRequiredForDeployment BIT NOT NULL,
        HumanReviewRequiredForMerge BIT NOT NULL,
        ReasonsJson NVARCHAR(MAX) NOT NULL,
        EvidenceReferencesJson NVARCHAR(MAX) NOT NULL,
        BoundaryMaximsJson NVARCHAR(MAX) NOT NULL,
        DecidedAtUtc DATETIMEOFFSET(7) NOT NULL,
        ReleaseReadinessDecisionRecordHash NVARCHAR(128) NOT NULL,
        Boundary NVARCHAR(MAX) NOT NULL,
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_ReleaseReadinessDecisionRecord_CreatedUtc DEFAULT SYSUTCDATETIME(),
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT PK_ReleaseReadinessDecisionRecord PRIMARY KEY CLUSTERED (ProjectId, ReleaseReadinessDecisionRecordId),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_ProjectId_NotEmpty CHECK (ProjectId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_RecordId_NotEmpty CHECK (ReleaseReadinessDecisionRecordId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_ReportId_NotEmpty CHECK (ReleaseReadinessReportId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_ReportHash CHECK (LEN(LTRIM(RTRIM(ReleaseReadinessReportHash))) = 64 AND LTRIM(RTRIM(ReleaseReadinessReportHash)) NOT LIKE N'%[^0-9a-fA-F]%'),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_SubjectHash CHECK (LEN(LTRIM(RTRIM(SubjectHash))) = 64 AND LTRIM(RTRIM(SubjectHash)) NOT LIKE N'%[^0-9a-fA-F]%'),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_RecordHash CHECK (LEN(LTRIM(RTRIM(ReleaseReadinessDecisionRecordHash))) = 64 AND LTRIM(RTRIM(ReleaseReadinessDecisionRecordHash)) NOT LIKE N'%[^0-9a-fA-F]%'),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_WorkflowRunId_NotBlank CHECK (LEN(LTRIM(RTRIM(WorkflowRunId))) > 0),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_WorkflowStepId_NotBlank CHECK (LEN(LTRIM(RTRIM(WorkflowStepId))) > 0),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_SubjectKind_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectKind))) > 0),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_SubjectId_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectId))) > 0),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_Status_Allowed CHECK (DecisionStatus IN (N'ReadyEvidenceSatisfied', N'BlockedByMissingEvidence', N'BlockedByFailedEvidence', N'BlockedByHumanReviewRequired')),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_ReasonsJson CHECK (ISJSON(ReasonsJson) = 1 AND LEN(LTRIM(RTRIM(ReasonsJson))) > 2),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_EvidenceJson CHECK (ISJSON(EvidenceReferencesJson) = 1 AND LEN(LTRIM(RTRIM(EvidenceReferencesJson))) > 2),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_BoundaryMaximsJson CHECK (ISJSON(BoundaryMaximsJson) = 1 AND LEN(LTRIM(RTRIM(BoundaryMaximsJson))) > 2),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_Boundary_NotBlank CHECK (LEN(LTRIM(RTRIM(Boundary))) > 0),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_NoReleaseApproval CHECK (ReleaseApproved = 0),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_NoDeploymentApproval CHECK (DeploymentApproved = 0),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_NoMergeApproval CHECK (MergeApproved = 0),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_NoSourceApplyExecution CHECK (SourceApplyExecutedByDecision = 0),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_NoRollbackExecution CHECK (RollbackExecutedByDecision = 0),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_NoWorkflowMutation CHECK (WorkflowMutatedByDecision = 0),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_NoGitOperation CHECK (GitOperationExecutedByDecision = 0),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_NoReleaseExecution CHECK (ReleaseExecutedByDecision = 0),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_HumanReviewRelease CHECK (HumanReviewRequiredForReleaseApproval = 1),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_HumanReviewDeployment CHECK (HumanReviewRequiredForDeployment = 1),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_HumanReviewMerge CHECK (HumanReviewRequiredForMerge = 1),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_ReadyTruth CHECK (DecisionStatus <> N'ReadyEvidenceSatisfied' OR ReleaseReadinessEvidenceSatisfied = 1),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_MissingTruth CHECK (DecisionStatus <> N'BlockedByMissingEvidence' OR ReleaseReadinessEvidenceSatisfied = 0),
        CONSTRAINT CK_ReleaseReadinessDecisionRecord_FailedTruth CHECK (DecisionStatus <> N'BlockedByFailedEvidence' OR ReleaseReadinessEvidenceSatisfied = 0)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ReleaseReadinessDecisionRecord_Project_Hash' AND object_id = OBJECT_ID(N'governance.ReleaseReadinessDecisionRecord'))
BEGIN
    CREATE UNIQUE INDEX UX_ReleaseReadinessDecisionRecord_Project_Hash
    ON governance.ReleaseReadinessDecisionRecord(ProjectId, ReleaseReadinessDecisionRecordHash);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ReleaseReadinessDecisionRecord_Project_Report' AND object_id = OBJECT_ID(N'governance.ReleaseReadinessDecisionRecord'))
BEGIN
    CREATE INDEX IX_ReleaseReadinessDecisionRecord_Project_Report
    ON governance.ReleaseReadinessDecisionRecord(ProjectId, ReleaseReadinessReportId, DecidedAtUtc DESC, ReleaseReadinessDecisionRecordId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ReleaseReadinessDecisionRecord_Project_WorkflowRun' AND object_id = OBJECT_ID(N'governance.ReleaseReadinessDecisionRecord'))
BEGIN
    CREATE INDEX IX_ReleaseReadinessDecisionRecord_Project_WorkflowRun
    ON governance.ReleaseReadinessDecisionRecord(ProjectId, WorkflowRunId, DecidedAtUtc DESC, ReleaseReadinessDecisionRecordId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ReleaseReadinessDecisionRecord_Project_Subject' AND object_id = OBJECT_ID(N'governance.ReleaseReadinessDecisionRecord'))
BEGIN
    CREATE INDEX IX_ReleaseReadinessDecisionRecord_Project_Subject
    ON governance.ReleaseReadinessDecisionRecord(ProjectId, SubjectKind, SubjectId, DecidedAtUtc DESC, ReleaseReadinessDecisionRecordId DESC);
END;
GO

IF OBJECT_ID(N'governance.TR_ReleaseReadinessDecisionRecord_ValidateInsert', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_ReleaseReadinessDecisionRecord_ValidateInsert;
END;
GO

CREATE TRIGGER governance.TR_ReleaseReadinessDecisionRecord_ValidateInsert
ON governance.ReleaseReadinessDecisionRecord
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        CROSS APPLY (SELECT LOWER(CONCAT(
            i.ReleaseReadinessReportHash, N' ', i.WorkflowRunId, N' ', i.WorkflowStepId, N' ',
            i.SubjectKind, N' ', i.SubjectId, N' ', i.SubjectHash, N' ', i.DecisionStatus, N' ',
            i.ReasonsJson, N' ', i.EvidenceReferencesJson, N' ', i.BoundaryMaximsJson, N' ',
            i.ReleaseReadinessDecisionRecordHash, N' ', i.Boundary
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
           OR c.TextToCheck LIKE N'%entirepatch%'
           OR c.TextToCheck LIKE N'%entire patch%'
           OR c.TextToCheck LIKE N'%patchpayload%'
           OR c.TextToCheck LIKE N'%patch payload%'
           OR c.TextToCheck LIKE N'%password%'
           OR c.TextToCheck LIKE N'%api_key%'
           OR c.TextToCheck LIKE N'%secret%'
           OR c.TextToCheck LIKE N'%private key%'
           OR c.TextToCheck LIKE N'%bearer%'
           OR (c.TextToCheck LIKE N'%release approved%' AND c.TextToCheck NOT LIKE N'%not release approved%' AND c.TextToCheck NOT LIKE N'%not release approval%' AND c.TextToCheck NOT LIKE N'%does not approve release%')
           OR c.TextToCheck LIKE N'%approved for release%'
           OR (c.TextToCheck LIKE N'%deployment approved%' AND c.TextToCheck NOT LIKE N'%not deployment approved%' AND c.TextToCheck NOT LIKE N'%not deployment approval%' AND c.TextToCheck NOT LIKE N'%does not approve deployment%')
           OR (c.TextToCheck LIKE N'%merge approved%' AND c.TextToCheck NOT LIKE N'%not merge approved%' AND c.TextToCheck NOT LIKE N'%not merge approval%' AND c.TextToCheck NOT LIKE N'%does not approve merge%')
           OR c.TextToCheck LIKE N'%safe to deploy%'
           OR c.TextToCheck LIKE N'%safe to merge%'
           OR c.TextToCheck LIKE N'%can deploy%'
           OR c.TextToCheck LIKE N'%can merge%'
           OR c.TextToCheck LIKE N'%green to ship%'
           OR (c.TextToCheck LIKE N'%release executed%' AND c.TextToCheck NOT LIKE N'%does not execute release%')
           OR c.TextToCheck LIKE N'%deployed by decision%'
           OR c.TextToCheck LIKE N'%merged by decision%'
           OR c.TextToCheck LIKE N'%source applied by decision%'
           OR c.TextToCheck LIKE N'%rollback executed by decision%'
           OR c.TextToCheck LIKE N'%workflow continued by decision%'
           OR c.TextToCheck LIKE N'%workflow mutated by decision%'
           OR c.TextToCheck LIKE N'%git ' + N'committed%'
           OR c.TextToCheck LIKE N'%git ' + N'pushed%'
           OR c.TextToCheck LIKE N'%tag created%'
           OR c.TextToCheck LIKE N'%pull request created%'
           OR c.TextToCheck LIKE N'%memory promoted%'
           OR c.TextToCheck LIKE N'%retrieval activated%'
           OR c.TextToCheck LIKE N'%agent dispatched%'
           OR c.TextToCheck LIKE N'%tool executed%'
           OR c.TextToCheck LIKE N'%model called%'
    )
    BEGIN
        THROW 53601, 'ReleaseReadinessDecisionRecord must not contain raw/private material or authority claims.', 1;
    END;
END;
GO

IF OBJECT_ID(N'governance.TR_ReleaseReadinessDecisionRecord_BlockUpdateDelete', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_ReleaseReadinessDecisionRecord_BlockUpdateDelete;
END;
GO

CREATE TRIGGER governance.TR_ReleaseReadinessDecisionRecord_BlockUpdateDelete
ON governance.ReleaseReadinessDecisionRecord
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 53602, 'ReleaseReadinessDecisionRecord is append-only.', 1;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ReleaseReadinessDecisionRecord_Save
    @ReleaseReadinessDecisionRecordId UNIQUEIDENTIFIER,
    @ProjectId UNIQUEIDENTIFIER,
    @ReleaseReadinessReportId UNIQUEIDENTIFIER,
    @ReleaseReadinessReportHash NVARCHAR(128),
    @WorkflowRunId NVARCHAR(200),
    @WorkflowStepId NVARCHAR(200),
    @SubjectKind NVARCHAR(120),
    @SubjectId NVARCHAR(300),
    @SubjectHash NVARCHAR(128),
    @DecisionStatus NVARCHAR(120),
    @ReleaseReadinessEvidenceSatisfied BIT,
    @ReleaseApproved BIT,
    @DeploymentApproved BIT,
    @MergeApproved BIT,
    @SourceApplyExecutedByDecision BIT,
    @RollbackExecutedByDecision BIT,
    @WorkflowMutatedByDecision BIT,
    @GitOperationExecutedByDecision BIT,
    @ReleaseExecutedByDecision BIT,
    @HumanReviewRequiredForReleaseApproval BIT,
    @HumanReviewRequiredForDeployment BIT,
    @HumanReviewRequiredForMerge BIT,
    @ReasonsJson NVARCHAR(MAX),
    @EvidenceReferencesJson NVARCHAR(MAX),
    @BoundaryMaximsJson NVARCHAR(MAX),
    @DecidedAtUtc DATETIMEOFFSET(7),
    @ReleaseReadinessDecisionRecordHash NVARCHAR(128),
    @Boundary NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    SET @ReleaseReadinessReportHash = LTRIM(RTRIM(@ReleaseReadinessReportHash));
    SET @WorkflowRunId = LTRIM(RTRIM(@WorkflowRunId));
    SET @WorkflowStepId = LTRIM(RTRIM(@WorkflowStepId));
    SET @SubjectKind = LTRIM(RTRIM(@SubjectKind));
    SET @SubjectId = LTRIM(RTRIM(@SubjectId));
    SET @SubjectHash = LTRIM(RTRIM(@SubjectHash));
    SET @DecisionStatus = LTRIM(RTRIM(@DecisionStatus));
    SET @ReleaseReadinessDecisionRecordHash = LTRIM(RTRIM(@ReleaseReadinessDecisionRecordHash));

    IF EXISTS
    (
        SELECT 1
        FROM governance.ReleaseReadinessDecisionRecord
        WHERE ProjectId = @ProjectId
          AND ReleaseReadinessDecisionRecordId = @ReleaseReadinessDecisionRecordId
          AND ReleaseReadinessDecisionRecordHash = @ReleaseReadinessDecisionRecordHash
    )
        RETURN;

    IF EXISTS
    (
        SELECT 1
        FROM governance.ReleaseReadinessDecisionRecord
        WHERE ProjectId = @ProjectId
          AND ReleaseReadinessDecisionRecordId = @ReleaseReadinessDecisionRecordId
          AND ReleaseReadinessDecisionRecordHash <> @ReleaseReadinessDecisionRecordHash
    )
        THROW 53603, 'ReleaseReadinessDecisionRecordId already exists with different hash.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM governance.ReleaseReadinessDecisionRecord
        WHERE ProjectId = @ProjectId
          AND ReleaseReadinessDecisionRecordHash = @ReleaseReadinessDecisionRecordHash
          AND ReleaseReadinessDecisionRecordId <> @ReleaseReadinessDecisionRecordId
    )
        THROW 53604, 'ReleaseReadinessDecisionRecordHash already exists with different id.', 1;

    INSERT INTO governance.ReleaseReadinessDecisionRecord
    (
        ReleaseReadinessDecisionRecordId, ProjectId, ReleaseReadinessReportId, ReleaseReadinessReportHash,
        WorkflowRunId, WorkflowStepId, SubjectKind, SubjectId, SubjectHash, DecisionStatus,
        ReleaseReadinessEvidenceSatisfied, ReleaseApproved, DeploymentApproved, MergeApproved,
        SourceApplyExecutedByDecision, RollbackExecutedByDecision, WorkflowMutatedByDecision,
        GitOperationExecutedByDecision, ReleaseExecutedByDecision,
        HumanReviewRequiredForReleaseApproval, HumanReviewRequiredForDeployment, HumanReviewRequiredForMerge,
        ReasonsJson, EvidenceReferencesJson, BoundaryMaximsJson, DecidedAtUtc,
        ReleaseReadinessDecisionRecordHash, Boundary
    )
    VALUES
    (
        @ReleaseReadinessDecisionRecordId, @ProjectId, @ReleaseReadinessReportId, @ReleaseReadinessReportHash,
        @WorkflowRunId, @WorkflowStepId, @SubjectKind, @SubjectId, @SubjectHash, @DecisionStatus,
        @ReleaseReadinessEvidenceSatisfied, @ReleaseApproved, @DeploymentApproved, @MergeApproved,
        @SourceApplyExecutedByDecision, @RollbackExecutedByDecision, @WorkflowMutatedByDecision,
        @GitOperationExecutedByDecision, @ReleaseExecutedByDecision,
        @HumanReviewRequiredForReleaseApproval, @HumanReviewRequiredForDeployment, @HumanReviewRequiredForMerge,
        @ReasonsJson, @EvidenceReferencesJson, @BoundaryMaximsJson, @DecidedAtUtc,
        @ReleaseReadinessDecisionRecordHash, @Boundary
    );
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ReleaseReadinessDecisionRecord_Get
    @ProjectId UNIQUEIDENTIFIER,
    @ReleaseReadinessDecisionRecordId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM governance.ReleaseReadinessDecisionRecord
    WHERE ProjectId = @ProjectId AND ReleaseReadinessDecisionRecordId = @ReleaseReadinessDecisionRecordId;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ReleaseReadinessDecisionRecord_GetByHash
    @ProjectId UNIQUEIDENTIFIER,
    @ReleaseReadinessDecisionRecordHash NVARCHAR(128)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM governance.ReleaseReadinessDecisionRecord
    WHERE ProjectId = @ProjectId AND ReleaseReadinessDecisionRecordHash = LTRIM(RTRIM(@ReleaseReadinessDecisionRecordHash));
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ReleaseReadinessDecisionRecord_ListByReleaseReadinessReport
    @ProjectId UNIQUEIDENTIFIER,
    @ReleaseReadinessReportId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;
    SELECT TOP (@EffectiveTake) * FROM governance.ReleaseReadinessDecisionRecord
    WHERE ProjectId = @ProjectId AND ReleaseReadinessReportId = @ReleaseReadinessReportId
    ORDER BY DecidedAtUtc DESC, ReleaseReadinessDecisionRecordId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ReleaseReadinessDecisionRecord_ListByWorkflowRun
    @ProjectId UNIQUEIDENTIFIER,
    @WorkflowRunId NVARCHAR(200),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;
    SELECT TOP (@EffectiveTake) * FROM governance.ReleaseReadinessDecisionRecord
    WHERE ProjectId = @ProjectId AND WorkflowRunId = LTRIM(RTRIM(@WorkflowRunId))
    ORDER BY DecidedAtUtc DESC, ReleaseReadinessDecisionRecordId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_ReleaseReadinessDecisionRecord_ListBySubject
    @ProjectId UNIQUEIDENTIFIER,
    @SubjectKind NVARCHAR(120),
    @SubjectId NVARCHAR(300),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;
    SELECT TOP (@EffectiveTake) * FROM governance.ReleaseReadinessDecisionRecord
    WHERE ProjectId = @ProjectId
      AND SubjectKind = LTRIM(RTRIM(@SubjectKind))
      AND SubjectId = LTRIM(RTRIM(@SubjectId))
    ORDER BY DecidedAtUtc DESC, ReleaseReadinessDecisionRecordId DESC;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'IronDevGovernanceEventRuntimeRole') IS NOT NULL
BEGIN
    GRANT EXECUTE ON OBJECT::governance.usp_ReleaseReadinessDecisionRecord_Save TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_ReleaseReadinessDecisionRecord_Get TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_ReleaseReadinessDecisionRecord_GetByHash TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_ReleaseReadinessDecisionRecord_ListByReleaseReadinessReport TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_ReleaseReadinessDecisionRecord_ListByWorkflowRun TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_ReleaseReadinessDecisionRecord_ListBySubject TO IronDevGovernanceEventRuntimeRole;
    GRANT SELECT ON OBJECT::governance.ReleaseReadinessDecisionRecord TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::governance.ReleaseReadinessDecisionRecord TO IronDevGovernanceEventRuntimeRole;
    DENY ALTER ON SCHEMA::governance TO IronDevGovernanceEventRuntimeRole;
END;
GO
