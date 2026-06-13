IF SCHEMA_ID(N'workflow') IS NULL
    THROW 54100, 'workflow schema must exist before applying workflow step store migration.', 1;
GO

IF OBJECT_ID(N'workflow.WorkflowRunStep', N'U') IS NULL
    THROW 54101, 'workflow.WorkflowRunStep must exist before applying workflow step store migration.', 1;
GO

IF COL_LENGTH(N'workflow.WorkflowRunStep', N'SequenceNumber') IS NULL
BEGIN
    ALTER TABLE workflow.WorkflowRunStep
        ADD SequenceNumber INT NOT NULL CONSTRAINT DF_WorkflowRunStep_SequenceNumber DEFAULT 1;
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'workflow.WorkflowRunStep')
      AND name = N'CK_WorkflowRunStep_SequenceNumber_Positive'
)
BEGIN
    ALTER TABLE workflow.WorkflowRunStep
        ADD CONSTRAINT CK_WorkflowRunStep_SequenceNumber_Positive CHECK (SequenceNumber > 0);
END;
GO

IF EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'workflow.WorkflowRunStep')
      AND name = N'CK_WorkflowRunStep_StepType_Allowed'
)
BEGIN
    ALTER TABLE workflow.WorkflowRunStep DROP CONSTRAINT CK_WorkflowRunStep_StepType_Allowed;
END;
GO

ALTER TABLE workflow.WorkflowRunStep
    ADD CONSTRAINT CK_WorkflowRunStep_StepType_Allowed CHECK (StepType IN (N'Planning', N'Review', N'Validation', N'HandoffSummary', N'GroundingSummary', N'HumanDecisionSupport', N'EvidenceCollection', N'Receipt', N'PolicyEvaluationInput', N'ApprovalRequirementEvaluation', N'DebugFinding', N'ReviewFinding'));
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkflowRunStep_Project_Subject_CreatedUtc' AND object_id = OBJECT_ID(N'workflow.WorkflowRunStep'))
    CREATE INDEX IX_WorkflowRunStep_Project_Subject_CreatedUtc ON workflow.WorkflowRunStep(ProjectId, SubjectType, SubjectId, CreatedUtc DESC, WorkflowRunStepId DESC) WHERE SubjectType IS NOT NULL AND SubjectId IS NOT NULL;
GO

CREATE OR ALTER PROCEDURE workflow.usp_WorkflowStep_Get
    @ProjectId UNIQUEIDENTIFIER,
    @WorkflowRunId UNIQUEIDENTIFIER,
    @WorkflowRunStepId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        s.WorkflowRunStepId,
        s.WorkflowRunId,
        s.ProjectId,
        s.StepKey,
        s.StepName,
        s.StepType,
        s.Status,
        s.AgentRole,
        s.AgentId,
        s.SubjectType,
        s.SubjectId,
        s.SafeSummary,
        s.SequenceNumber,
        r.CorrelationId,
        r.CausationId,
        s.MetadataVersion,
        s.MetadataJson,
        s.GrantsApproval,
        s.GrantsExecution,
        s.MutatesSource,
        s.PromotesMemory,
        s.StartsWorkflow,
        s.ContinuesWorkflow,
        s.SatisfiesPolicy,
        s.TransfersAuthority,
        s.ApprovesRelease,
        s.CreatesAcceptedMemory,
        s.CreatedUtc
    FROM workflow.WorkflowRunStep s
    JOIN workflow.WorkflowRun r ON r.WorkflowRunId = s.WorkflowRunId
    WHERE s.ProjectId = @ProjectId
      AND s.WorkflowRunId = @WorkflowRunId
      AND s.WorkflowRunStepId = @WorkflowRunStepId;

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
    JOIN workflow.WorkflowRunStep s ON s.WorkflowRunStepId = e.WorkflowRunStepId
    WHERE e.ProjectId = @ProjectId
      AND e.WorkflowRunId = @WorkflowRunId
      AND e.WorkflowRunStepId = @WorkflowRunStepId
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
    JOIN workflow.WorkflowRunStep s ON s.WorkflowRunStepId = g.WorkflowRunStepId
    WHERE g.ProjectId = @ProjectId
      AND g.WorkflowRunId = @WorkflowRunId
      AND g.WorkflowRunStepId = @WorkflowRunStepId
    ORDER BY g.CreatedUtc, g.WorkflowRunGroundingReferenceId;
END;
GO

CREATE OR ALTER PROCEDURE workflow.usp_WorkflowStep_Create
    @WorkflowRunStepId UNIQUEIDENTIFIER,
    @WorkflowRunId UNIQUEIDENTIFIER,
    @ProjectId UNIQUEIDENTIFIER,
    @StepKey NVARCHAR(160),
    @StepName NVARCHAR(200),
    @StepType NVARCHAR(120),
    @Status NVARCHAR(80),
    @AgentRole NVARCHAR(80) = NULL,
    @AgentId NVARCHAR(200) = NULL,
    @SubjectType NVARCHAR(120) = NULL,
    @SubjectId NVARCHAR(300) = NULL,
    @SafeSummary NVARCHAR(1000) = NULL,
    @SequenceNumber INT,
    @CorrelationId UNIQUEIDENTIFIER = NULL,
    @CausationId UNIQUEIDENTIFIER = NULL,
    @MetadataVersion INT,
    @MetadataJson NVARCHAR(MAX),
    @EvidenceReferencesJson NVARCHAR(MAX),
    @GroundingReferencesJson NVARCHAR(MAX),
    @CreatedUtc DATETIMEOFFSET(7) = NULL
AS
BEGIN
    SET XACT_ABORT ON;
    SET NOCOUNT ON;

    IF ISJSON(@EvidenceReferencesJson) <> 1
        THROW 54110, 'EvidenceReferencesJson must be valid JSON.', 1;

    IF ISJSON(@GroundingReferencesJson) <> 1
        THROW 54111, 'GroundingReferencesJson must be valid JSON.', 1;

    DECLARE @UnsafeText NVARCHAR(MAX) = LOWER(CONCAT(
        COALESCE(@StepKey, N''), N' ',
        COALESCE(@StepName, N''), N' ',
        COALESCE(@StepType, N''), N' ',
        COALESCE(@Status, N''), N' ',
        COALESCE(@AgentRole, N''), N' ',
        COALESCE(@AgentId, N''), N' ',
        COALESCE(@SubjectType, N''), N' ',
        COALESCE(@SubjectId, N''), N' ',
        COALESCE(@SafeSummary, N''), N' ',
        COALESCE(@MetadataJson, N''), N' ',
        COALESCE(@EvidenceReferencesJson, N''), N' ',
        COALESCE(@GroundingReferencesJson, N'')
    ));

    IF @UnsafeText LIKE N'%private reasoning%'
        OR @UnsafeText LIKE N'%hidden reasoning%'
        OR @UnsafeText LIKE N'%chain-of-thought%'
        OR @UnsafeText LIKE N'%scratchpad%'
        OR @UnsafeText LIKE N'%approval granted%'
        OR @UnsafeText LIKE N'%execution allowed%'
        OR @UnsafeText LIKE N'%tool executed%'
        OR @UnsafeText LIKE N'%source mutated%'
        OR @UnsafeText LIKE N'%memory promoted%'
        OR @UnsafeText LIKE N'%authority transferred%'
        OR @UnsafeText LIKE N'%release approved%'
        THROW 54116, 'Workflow step text contains unsafe private-reasoning or authority language.', 1;

    IF NOT EXISTS (SELECT 1 FROM workflow.WorkflowRun WHERE WorkflowRunId = @WorkflowRunId AND ProjectId = @ProjectId)
        THROW 54112, 'Parent workflow run does not exist for this project.', 1;

    IF EXISTS (SELECT 1 FROM workflow.WorkflowRunStep WHERE WorkflowRunId = @WorkflowRunId AND StepKey = LTRIM(RTRIM(@StepKey)))
        THROW 54113, 'Workflow step key already exists within this workflow run.', 1;

    DECLARE @EffectiveCreatedUtc DATETIMEOFFSET(7) = COALESCE(@CreatedUtc, SYSUTCDATETIME());

    DECLARE @EvidenceRows TABLE
    (
        WorkflowRunEvidenceReferenceId UNIQUEIDENTIFIER NOT NULL,
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
    SELECT
        NEWID(),
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

    IF EXISTS (SELECT 1 FROM @EvidenceRows WHERE EvidenceType IS NULL OR EvidenceId IS NULL OR LEN(EvidenceId) = 0)
        THROW 54114, 'Workflow step evidence references must include type and id.', 1;

    DECLARE @GroundingRows TABLE
    (
        WorkflowRunGroundingReferenceId UNIQUEIDENTIFIER NOT NULL,
        GroundingEvidenceReferenceId UNIQUEIDENTIFIER NOT NULL,
        ClaimType NVARCHAR(120) NOT NULL,
        ClaimId NVARCHAR(300) NOT NULL,
        SafeSummary NVARCHAR(1000) NULL
    );

    INSERT INTO @GroundingRows
    SELECT
        NEWID(),
        TRY_CONVERT(UNIQUEIDENTIFIER, JSON_VALUE(j.value, '$.GroundingEvidenceReferenceId')),
        LTRIM(RTRIM(JSON_VALUE(j.value, '$.claimType'))),
        LTRIM(RTRIM(JSON_VALUE(j.value, '$.ClaimId'))),
        NULLIF(LTRIM(RTRIM(COALESCE(JSON_VALUE(j.value, '$.SafeSummary'), N''))), N'')
    FROM OPENJSON(@GroundingReferencesJson) j;

    IF EXISTS (SELECT 1 FROM @GroundingRows WHERE GroundingEvidenceReferenceId IS NULL OR ClaimType IS NULL OR ClaimId IS NULL OR LEN(ClaimId) = 0)
        THROW 54115, 'Workflow step grounding references must include grounding id, claim type, and claim id.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;

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
            SequenceNumber,
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
            @WorkflowRunStepId,
            @WorkflowRunId,
            @ProjectId,
            LTRIM(RTRIM(@StepKey)),
            LTRIM(RTRIM(@StepName)),
            LTRIM(RTRIM(@StepType)),
            LTRIM(RTRIM(@Status)),
            NULLIF(LTRIM(RTRIM(COALESCE(@AgentRole, N''))), N''),
            NULLIF(LTRIM(RTRIM(COALESCE(@AgentId, N''))), N''),
            NULLIF(LTRIM(RTRIM(COALESCE(@SubjectType, N''))), N''),
            NULLIF(LTRIM(RTRIM(COALESCE(@SubjectId, N''))), N''),
            NULLIF(LTRIM(RTRIM(COALESCE(@SafeSummary, N''))), N''),
            @SequenceNumber,
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
            @WorkflowRunStepId,
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
            @WorkflowRunStepId,
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

    EXEC workflow.usp_WorkflowStep_Get @ProjectId = @ProjectId, @WorkflowRunId = @WorkflowRunId, @WorkflowRunStepId = @WorkflowRunStepId;
END;
GO

CREATE OR ALTER PROCEDURE workflow.usp_WorkflowStep_ListByRun
    @ProjectId UNIQUEIDENTIFIER,
    @WorkflowRunId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @BoundedTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@BoundedTake)
        s.WorkflowRunStepId,
        s.WorkflowRunId,
        s.ProjectId,
        s.StepKey,
        s.StepName,
        s.StepType,
        s.Status,
        s.AgentRole,
        s.AgentId,
        s.SubjectType,
        s.SubjectId,
        s.SequenceNumber,
        r.CorrelationId,
        r.CausationId,
        (SELECT COUNT(1) FROM workflow.WorkflowRunEvidenceReference e WHERE e.WorkflowRunStepId = s.WorkflowRunStepId) AS EvidenceReferenceCount,
        (SELECT COUNT(1) FROM workflow.WorkflowRunGroundingReference g WHERE g.WorkflowRunStepId = s.WorkflowRunStepId) AS GroundingReferenceCount,
        s.CreatedUtc
    FROM workflow.WorkflowRunStep s
    JOIN workflow.WorkflowRun r ON r.WorkflowRunId = s.WorkflowRunId
    WHERE s.ProjectId = @ProjectId
      AND s.WorkflowRunId = @WorkflowRunId
    ORDER BY s.SequenceNumber, s.CreatedUtc, s.WorkflowRunStepId;
END;
GO

CREATE OR ALTER PROCEDURE workflow.usp_WorkflowStep_ListByCorrelation
    @ProjectId UNIQUEIDENTIFIER,
    @CorrelationId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @BoundedTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@BoundedTake)
        s.WorkflowRunStepId,
        s.WorkflowRunId,
        s.ProjectId,
        s.StepKey,
        s.StepName,
        s.StepType,
        s.Status,
        s.AgentRole,
        s.AgentId,
        s.SubjectType,
        s.SubjectId,
        s.SequenceNumber,
        r.CorrelationId,
        r.CausationId,
        (SELECT COUNT(1) FROM workflow.WorkflowRunEvidenceReference e WHERE e.WorkflowRunStepId = s.WorkflowRunStepId) AS EvidenceReferenceCount,
        (SELECT COUNT(1) FROM workflow.WorkflowRunGroundingReference g WHERE g.WorkflowRunStepId = s.WorkflowRunStepId) AS GroundingReferenceCount,
        s.CreatedUtc
    FROM workflow.WorkflowRunStep s
    JOIN workflow.WorkflowRun r ON r.WorkflowRunId = s.WorkflowRunId
    WHERE s.ProjectId = @ProjectId
      AND r.CorrelationId = @CorrelationId
    ORDER BY r.CreatedUtc DESC, s.SequenceNumber, s.CreatedUtc, s.WorkflowRunStepId;
END;
GO

CREATE OR ALTER PROCEDURE workflow.usp_WorkflowStep_ListBySubject
    @ProjectId UNIQUEIDENTIFIER,
    @SubjectType NVARCHAR(120),
    @SubjectId NVARCHAR(300),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @BoundedTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@BoundedTake)
        s.WorkflowRunStepId,
        s.WorkflowRunId,
        s.ProjectId,
        s.StepKey,
        s.StepName,
        s.StepType,
        s.Status,
        s.AgentRole,
        s.AgentId,
        s.SubjectType,
        s.SubjectId,
        s.SequenceNumber,
        r.CorrelationId,
        r.CausationId,
        (SELECT COUNT(1) FROM workflow.WorkflowRunEvidenceReference e WHERE e.WorkflowRunStepId = s.WorkflowRunStepId) AS EvidenceReferenceCount,
        (SELECT COUNT(1) FROM workflow.WorkflowRunGroundingReference g WHERE g.WorkflowRunStepId = s.WorkflowRunStepId) AS GroundingReferenceCount,
        s.CreatedUtc
    FROM workflow.WorkflowRunStep s
    JOIN workflow.WorkflowRun r ON r.WorkflowRunId = s.WorkflowRunId
    WHERE s.ProjectId = @ProjectId
      AND s.SubjectType = LTRIM(RTRIM(@SubjectType))
      AND s.SubjectId = LTRIM(RTRIM(@SubjectId))
    ORDER BY r.CreatedUtc DESC, s.SequenceNumber, s.CreatedUtc, s.WorkflowRunStepId;
END;
GO

GRANT EXECUTE ON OBJECT::workflow.usp_WorkflowStep_Create TO IronDevGovernanceEventRuntimeRole;
GRANT EXECUTE ON OBJECT::workflow.usp_WorkflowStep_Get TO IronDevGovernanceEventRuntimeRole;
GRANT EXECUTE ON OBJECT::workflow.usp_WorkflowStep_ListByRun TO IronDevGovernanceEventRuntimeRole;
GRANT EXECUTE ON OBJECT::workflow.usp_WorkflowStep_ListByCorrelation TO IronDevGovernanceEventRuntimeRole;
GRANT EXECUTE ON OBJECT::workflow.usp_WorkflowStep_ListBySubject TO IronDevGovernanceEventRuntimeRole;
GO
