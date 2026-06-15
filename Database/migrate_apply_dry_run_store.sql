IF SCHEMA_ID(N'workflow') IS NULL
    EXEC(N'CREATE SCHEMA workflow');
GO

IF OBJECT_ID(N'workflow.ApplyDryRunRecord', N'U') IS NULL
BEGIN
    CREATE TABLE workflow.ApplyDryRunRecord
    (
        DryRunId NVARCHAR(160) NOT NULL,
        WorkflowRunId NVARCHAR(160) NOT NULL,
        WorkflowStepId NVARCHAR(160) NOT NULL,
        ControlledApplyPlanReferenceId NVARCHAR(160) NOT NULL,
        SourceApplyApprovalRequirementReferenceId NVARCHAR(160) NOT NULL,
        PatchProposalEvidencePackageReferenceId NVARCHAR(160) NOT NULL,
        ProjectReferenceId NVARCHAR(160) NOT NULL,
        TargetReferenceId NVARCHAR(160) NOT NULL,
        Status NVARCHAR(64) NOT NULL,
        OutcomeKind NVARCHAR(64) NOT NULL,
        SafeSummary NVARCHAR(1000) NOT NULL,
        EvidenceReferencesJson NVARCHAR(MAX) NOT NULL,
        GateReferencesJson NVARCHAR(MAX) NOT NULL,
        ValidationReferencesJson NVARCHAR(MAX) NOT NULL,
        RollbackReferencesJson NVARCHAR(MAX) NOT NULL,
        RisksJson NVARCHAR(MAX) NOT NULL,
        MissingEvidenceJson NVARCHAR(MAX) NOT NULL,
        CorrelationId NVARCHAR(160) NOT NULL CONSTRAINT DF_ApplyDryRunRecord_CorrelationId DEFAULT N'',
        MetadataJson NVARCHAR(MAX) NOT NULL,
        IsStoreRecordOnly BIT NOT NULL CONSTRAINT DF_ApplyDryRunRecord_IsStoreRecordOnly DEFAULT 1,
        IsDryRunPerformed BIT NOT NULL CONSTRAINT DF_ApplyDryRunRecord_IsDryRunPerformed DEFAULT 0,
        IsSourceApply BIT NOT NULL CONSTRAINT DF_ApplyDryRunRecord_IsSourceApply DEFAULT 0,
        IsPatchApplication BIT NOT NULL CONSTRAINT DF_ApplyDryRunRecord_IsPatchApplication DEFAULT 0,
        IsApproval BIT NOT NULL CONSTRAINT DF_ApplyDryRunRecord_IsApproval DEFAULT 0,
        IsApprovalSatisfied BIT NOT NULL CONSTRAINT DF_ApplyDryRunRecord_IsApprovalSatisfied DEFAULT 0,
        CanPerformDryRun BIT NOT NULL CONSTRAINT DF_ApplyDryRunRecord_CanPerformDryRun DEFAULT 0,
        CanApplySource BIT NOT NULL CONSTRAINT DF_ApplyDryRunRecord_CanApplySource DEFAULT 0,
        CanMutateFiles BIT NOT NULL CONSTRAINT DF_ApplyDryRunRecord_CanMutateFiles DEFAULT 0,
        CanReadSourceFiles BIT NOT NULL CONSTRAINT DF_ApplyDryRunRecord_CanReadSourceFiles DEFAULT 0,
        CanRunCommand BIT NOT NULL CONSTRAINT DF_ApplyDryRunRecord_CanRunCommand DEFAULT 0,
        CanInvokeTool BIT NOT NULL CONSTRAINT DF_ApplyDryRunRecord_CanInvokeTool DEFAULT 0,
        CanRunValidation BIT NOT NULL CONSTRAINT DF_ApplyDryRunRecord_CanRunValidation DEFAULT 0,
        CanRollback BIT NOT NULL CONSTRAINT DF_ApplyDryRunRecord_CanRollback DEFAULT 0,
        CanSatisfyPolicy BIT NOT NULL CONSTRAINT DF_ApplyDryRunRecord_CanSatisfyPolicy DEFAULT 0,
        CanTransitionWorkflow BIT NOT NULL CONSTRAINT DF_ApplyDryRunRecord_CanTransitionWorkflow DEFAULT 0,
        CanPromoteMemory BIT NOT NULL CONSTRAINT DF_ApplyDryRunRecord_CanPromoteMemory DEFAULT 0,
        CanActivateRetrieval BIT NOT NULL CONSTRAINT DF_ApplyDryRunRecord_CanActivateRetrieval DEFAULT 0,
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_ApplyDryRunRecord_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_ApplyDryRunRecord PRIMARY KEY (DryRunId),
        CONSTRAINT CK_ApplyDryRunRecord_Status_Allowed CHECK (Status IN (N'Stored', N'RejectedUnsafeMaterial', N'InvalidRecord')),
        CONSTRAINT CK_ApplyDryRunRecord_OutcomeKind_Allowed CHECK (OutcomeKind IN (N'NotPerformed', N'PreviewOnly', N'BlockedByMissingEvidence', N'BlockedByUnsafeMaterial')),
        CONSTRAINT CK_ApplyDryRunRecord_EvidenceReferencesJson_IsJson CHECK (ISJSON(EvidenceReferencesJson) = 1),
        CONSTRAINT CK_ApplyDryRunRecord_GateReferencesJson_IsJson CHECK (ISJSON(GateReferencesJson) = 1),
        CONSTRAINT CK_ApplyDryRunRecord_ValidationReferencesJson_IsJson CHECK (ISJSON(ValidationReferencesJson) = 1),
        CONSTRAINT CK_ApplyDryRunRecord_RollbackReferencesJson_IsJson CHECK (ISJSON(RollbackReferencesJson) = 1),
        CONSTRAINT CK_ApplyDryRunRecord_RisksJson_IsJson CHECK (ISJSON(RisksJson) = 1),
        CONSTRAINT CK_ApplyDryRunRecord_MissingEvidenceJson_IsJson CHECK (ISJSON(MissingEvidenceJson) = 1),
        CONSTRAINT CK_ApplyDryRunRecord_MetadataJson_IsJson CHECK (ISJSON(MetadataJson) = 1),
        CONSTRAINT CK_ApplyDryRunRecord_RecordOnly CHECK (IsStoreRecordOnly = 1),
        CONSTRAINT CK_ApplyDryRunRecord_NoDryRunPerformed CHECK (IsDryRunPerformed = 0),
        CONSTRAINT CK_ApplyDryRunRecord_NoSourceApply CHECK (IsSourceApply = 0),
        CONSTRAINT CK_ApplyDryRunRecord_NoPatchApplication CHECK (IsPatchApplication = 0),
        CONSTRAINT CK_ApplyDryRunRecord_NoApproval CHECK (IsApproval = 0),
        CONSTRAINT CK_ApplyDryRunRecord_NoApprovalSatisfied CHECK (IsApprovalSatisfied = 0),
        CONSTRAINT CK_ApplyDryRunRecord_NoDryRunAction CHECK (CanPerformDryRun = 0),
        CONSTRAINT CK_ApplyDryRunRecord_NoApplySource CHECK (CanApplySource = 0),
        CONSTRAINT CK_ApplyDryRunRecord_NoMutateFiles CHECK (CanMutateFiles = 0),
        CONSTRAINT CK_ApplyDryRunRecord_NoReadSourceFiles CHECK (CanReadSourceFiles = 0),
        CONSTRAINT CK_ApplyDryRunRecord_NoCommand CHECK (CanRunCommand = 0),
        CONSTRAINT CK_ApplyDryRunRecord_NoTool CHECK (CanInvokeTool = 0),
        CONSTRAINT CK_ApplyDryRunRecord_NoValidationRun CHECK (CanRunValidation = 0),
        CONSTRAINT CK_ApplyDryRunRecord_NoRollback CHECK (CanRollback = 0),
        CONSTRAINT CK_ApplyDryRunRecord_NoPolicySatisfied CHECK (CanSatisfyPolicy = 0),
        CONSTRAINT CK_ApplyDryRunRecord_NoWorkflowTransition CHECK (CanTransitionWorkflow = 0),
        CONSTRAINT CK_ApplyDryRunRecord_NoMemoryPromotion CHECK (CanPromoteMemory = 0),
        CONSTRAINT CK_ApplyDryRunRecord_NoRetrievalActivation CHECK (CanActivateRetrieval = 0)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ApplyDryRunRecord_WorkflowRun' AND object_id = OBJECT_ID(N'workflow.ApplyDryRunRecord'))
    CREATE INDEX IX_ApplyDryRunRecord_WorkflowRun ON workflow.ApplyDryRunRecord (WorkflowRunId, CreatedUtc DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ApplyDryRunRecord_ControlledApplyPlan' AND object_id = OBJECT_ID(N'workflow.ApplyDryRunRecord'))
    CREATE INDEX IX_ApplyDryRunRecord_ControlledApplyPlan ON workflow.ApplyDryRunRecord (ControlledApplyPlanReferenceId, CreatedUtc DESC);
GO

CREATE OR ALTER TRIGGER workflow.TR_ApplyDryRunRecord_BlockUpdateDelete
ON workflow.ApplyDryRunRecord
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 52040, 'Apply dry-run records are append-only.', 1;
END;
GO

CREATE OR ALTER TRIGGER workflow.TR_ApplyDryRunRecord_ValidateInsert
ON workflow.ApplyDryRunRecord
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (
        SELECT 1
        FROM inserted
        WHERE IsStoreRecordOnly <> 1
           OR IsDryRunPerformed <> 0
           OR IsSourceApply <> 0
           OR IsPatchApplication <> 0
           OR IsApproval <> 0
           OR IsApprovalSatisfied <> 0
           OR CanPerformDryRun <> 0
           OR CanApplySource <> 0
           OR CanMutateFiles <> 0
           OR CanReadSourceFiles <> 0
           OR CanRunCommand <> 0
           OR CanInvokeTool <> 0
           OR CanRunValidation <> 0
           OR CanRollback <> 0
           OR CanSatisfyPolicy <> 0
           OR CanTransitionWorkflow <> 0
           OR CanPromoteMemory <> 0
           OR CanActivateRetrieval <> 0
    )
    BEGIN
        THROW 52041, 'Apply dry-run records cannot grant authority or perform actions.', 1;
    END;

    IF EXISTS (
        SELECT 1
        FROM inserted
        CROSS APPLY (VALUES (LOWER(CONCAT(
            DryRunId, N' ', WorkflowRunId, N' ', WorkflowStepId, N' ',
            ControlledApplyPlanReferenceId, N' ', SourceApplyApprovalRequirementReferenceId, N' ',
            PatchProposalEvidencePackageReferenceId, N' ', ProjectReferenceId, N' ', TargetReferenceId, N' ',
            SafeSummary, N' ', EvidenceReferencesJson, N' ', GateReferencesJson, N' ',
            ValidationReferencesJson, N' ', RollbackReferencesJson, N' ', RisksJson, N' ',
            MissingEvidenceJson, N' ', CorrelationId, N' ', MetadataJson
        )))) AS scan(UnsafeText)
        WHERE scan.UnsafeText LIKE N'%private reasoning%'
           OR scan.UnsafeText LIKE N'%hidden reasoning%'
           OR scan.UnsafeText LIKE N'%chain-of-thought%'
           OR scan.UnsafeText LIKE N'%chain of thought%'
           OR scan.UnsafeText LIKE N'%chainofthought%'
           OR scan.UnsafeText LIKE N'%raw prompt%'
           OR scan.UnsafeText LIKE N'%rawprompt%'
           OR scan.UnsafeText LIKE N'%raw completion%'
           OR scan.UnsafeText LIKE N'%rawcompletion%'
           OR scan.UnsafeText LIKE N'%raw tool output%'
           OR scan.UnsafeText LIKE N'%rawtooloutput%'
           OR scan.UnsafeText LIKE N'%entire patch%'
           OR scan.UnsafeText LIKE N'%entirepatch%'
           OR scan.UnsafeText LIKE N'%patch payload%'
           OR scan.UnsafeText LIKE N'%patch applied%'
           OR scan.UnsafeText LIKE N'%patchapplied%'
           OR scan.UnsafeText LIKE N'%ready to apply%'
           OR scan.UnsafeText LIKE N'%readytoapply%'
           OR scan.UnsafeText LIKE N'%validation passed%'
           OR scan.UnsafeText LIKE N'%validationpassed%'
           OR scan.UnsafeText LIKE N'%rollback completed%'
           OR scan.UnsafeText LIKE N'%rollbackcompleted%'
           OR scan.UnsafeText LIKE N'%approval granted%'
           OR scan.UnsafeText LIKE N'%approvalgranted%'
           OR scan.UnsafeText LIKE N'%policy satisfied%'
           OR scan.UnsafeText LIKE N'%policysatisfied%'
           OR scan.UnsafeText LIKE N'%execution allowed%'
           OR scan.UnsafeText LIKE N'%executionallowed%'
           OR scan.UnsafeText LIKE N'%tool executed%'
           OR scan.UnsafeText LIKE N'%toolexecuted%'
           OR scan.UnsafeText LIKE N'%source mutated%'
           OR scan.UnsafeText LIKE N'%sourcemutated%'
           OR scan.UnsafeText LIKE N'%memory promoted%'
           OR scan.UnsafeText LIKE N'%memorypromoted%'
           OR scan.UnsafeText LIKE N'%authority transferred%'
           OR scan.UnsafeText LIKE N'%authoritytransferred%'
           OR scan.UnsafeText LIKE N'%release approved%'
           OR scan.UnsafeText LIKE N'%releaseapproved%'
    )
    BEGIN
        THROW 52042, 'Apply dry-run records cannot contain unsafe material or authority-claiming text.', 1;
    END;
END;
GO

CREATE OR ALTER PROCEDURE workflow.usp_ApplyDryRun_Create
    @DryRunId NVARCHAR(160),
    @WorkflowRunId NVARCHAR(160),
    @WorkflowStepId NVARCHAR(160),
    @ControlledApplyPlanReferenceId NVARCHAR(160),
    @SourceApplyApprovalRequirementReferenceId NVARCHAR(160),
    @PatchProposalEvidencePackageReferenceId NVARCHAR(160),
    @ProjectReferenceId NVARCHAR(160),
    @TargetReferenceId NVARCHAR(160),
    @Status NVARCHAR(64),
    @OutcomeKind NVARCHAR(64),
    @SafeSummary NVARCHAR(1000),
    @EvidenceReferencesJson NVARCHAR(MAX),
    @GateReferencesJson NVARCHAR(MAX),
    @ValidationReferencesJson NVARCHAR(MAX),
    @RollbackReferencesJson NVARCHAR(MAX),
    @RisksJson NVARCHAR(MAX),
    @MissingEvidenceJson NVARCHAR(MAX),
    @CorrelationId NVARCHAR(160),
    @MetadataJson NVARCHAR(MAX),
    @IsStoreRecordOnly BIT,
    @IsDryRunPerformed BIT,
    @IsSourceApply BIT,
    @IsPatchApplication BIT,
    @IsApproval BIT,
    @IsApprovalSatisfied BIT,
    @CanPerformDryRun BIT,
    @CanApplySource BIT,
    @CanMutateFiles BIT,
    @CanReadSourceFiles BIT,
    @CanRunCommand BIT,
    @CanInvokeTool BIT,
    @CanRunValidation BIT,
    @CanRollback BIT,
    @CanSatisfyPolicy BIT,
    @CanTransitionWorkflow BIT,
    @CanPromoteMemory BIT,
    @CanActivateRetrieval BIT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM workflow.ApplyDryRunRecord WHERE DryRunId = LTRIM(RTRIM(@DryRunId)))
        THROW 52043, 'Apply dry-run record already exists.', 1;

    IF NULLIF(LTRIM(RTRIM(@DryRunId)), N'') IS NULL
        OR NULLIF(LTRIM(RTRIM(@WorkflowRunId)), N'') IS NULL
        OR NULLIF(LTRIM(RTRIM(@WorkflowStepId)), N'') IS NULL
        OR NULLIF(LTRIM(RTRIM(@ControlledApplyPlanReferenceId)), N'') IS NULL
        OR NULLIF(LTRIM(RTRIM(@ProjectReferenceId)), N'') IS NULL
        OR NULLIF(LTRIM(RTRIM(@TargetReferenceId)), N'') IS NULL
        OR NULLIF(LTRIM(RTRIM(@SafeSummary)), N'') IS NULL
        THROW 52044, 'Apply dry-run record identifiers and summary are required.', 1;

    IF ISJSON(@EvidenceReferencesJson) <> 1
        OR ISJSON(@GateReferencesJson) <> 1
        OR ISJSON(@ValidationReferencesJson) <> 1
        OR ISJSON(@RollbackReferencesJson) <> 1
        OR ISJSON(@RisksJson) <> 1
        OR ISJSON(@MissingEvidenceJson) <> 1
        OR ISJSON(@MetadataJson) <> 1
        THROW 52045, 'Apply dry-run JSON payloads must be valid JSON.', 1;

    IF @Status NOT IN (N'Stored', N'RejectedUnsafeMaterial', N'InvalidRecord')
        OR @OutcomeKind NOT IN (N'NotPerformed', N'PreviewOnly', N'BlockedByMissingEvidence', N'BlockedByUnsafeMaterial')
        THROW 52046, 'Apply dry-run status or outcome is invalid.', 1;

    DECLARE @UnsafeText NVARCHAR(MAX) = LOWER(CONCAT(
        @DryRunId, N' ', @WorkflowRunId, N' ', @WorkflowStepId, N' ',
        @ControlledApplyPlanReferenceId, N' ', @SourceApplyApprovalRequirementReferenceId, N' ',
        @PatchProposalEvidencePackageReferenceId, N' ', @ProjectReferenceId, N' ', @TargetReferenceId, N' ',
        @SafeSummary, N' ', @EvidenceReferencesJson, N' ', @GateReferencesJson, N' ',
        @ValidationReferencesJson, N' ', @RollbackReferencesJson, N' ', @RisksJson, N' ',
        @MissingEvidenceJson, N' ', @CorrelationId, N' ', @MetadataJson
    ));

    IF @UnsafeText LIKE N'%private reasoning%'
        OR @UnsafeText LIKE N'%hidden reasoning%'
        OR @UnsafeText LIKE N'%chain-of-thought%'
        OR @UnsafeText LIKE N'%chain of thought%'
        OR @UnsafeText LIKE N'%chainofthought%'
        OR @UnsafeText LIKE N'%raw prompt%'
        OR @UnsafeText LIKE N'%rawprompt%'
        OR @UnsafeText LIKE N'%raw completion%'
        OR @UnsafeText LIKE N'%rawcompletion%'
        OR @UnsafeText LIKE N'%raw tool output%'
        OR @UnsafeText LIKE N'%rawtooloutput%'
        OR @UnsafeText LIKE N'%entire patch%'
        OR @UnsafeText LIKE N'%entirepatch%'
        OR @UnsafeText LIKE N'%patch payload%'
        OR @UnsafeText LIKE N'%patch applied%'
        OR @UnsafeText LIKE N'%patchapplied%'
        OR @UnsafeText LIKE N'%ready to apply%'
        OR @UnsafeText LIKE N'%readytoapply%'
        OR @UnsafeText LIKE N'%validation passed%'
        OR @UnsafeText LIKE N'%validationpassed%'
        OR @UnsafeText LIKE N'%rollback completed%'
        OR @UnsafeText LIKE N'%rollbackcompleted%'
        OR @UnsafeText LIKE N'%approval granted%'
        OR @UnsafeText LIKE N'%approvalgranted%'
        OR @UnsafeText LIKE N'%policy satisfied%'
        OR @UnsafeText LIKE N'%policysatisfied%'
        OR @UnsafeText LIKE N'%execution allowed%'
        OR @UnsafeText LIKE N'%executionallowed%'
        OR @UnsafeText LIKE N'%tool executed%'
        OR @UnsafeText LIKE N'%toolexecuted%'
        OR @UnsafeText LIKE N'%source mutated%'
        OR @UnsafeText LIKE N'%sourcemutated%'
        OR @UnsafeText LIKE N'%memory promoted%'
        OR @UnsafeText LIKE N'%memorypromoted%'
        OR @UnsafeText LIKE N'%authority transferred%'
        OR @UnsafeText LIKE N'%authoritytransferred%'
        OR @UnsafeText LIKE N'%release approved%'
        OR @UnsafeText LIKE N'%releaseapproved%'
        THROW 52047, 'Apply dry-run record contains unsafe material or authority-claiming text.', 1;

    INSERT INTO workflow.ApplyDryRunRecord
    (
        DryRunId, WorkflowRunId, WorkflowStepId, ControlledApplyPlanReferenceId,
        SourceApplyApprovalRequirementReferenceId, PatchProposalEvidencePackageReferenceId,
        ProjectReferenceId, TargetReferenceId, Status, OutcomeKind, SafeSummary,
        EvidenceReferencesJson, GateReferencesJson, ValidationReferencesJson, RollbackReferencesJson,
        RisksJson, MissingEvidenceJson, CorrelationId, MetadataJson, IsStoreRecordOnly,
        IsDryRunPerformed, IsSourceApply, IsPatchApplication, IsApproval, IsApprovalSatisfied,
        CanPerformDryRun, CanApplySource, CanMutateFiles, CanReadSourceFiles, CanRunCommand,
        CanInvokeTool, CanRunValidation, CanRollback, CanSatisfyPolicy, CanTransitionWorkflow,
        CanPromoteMemory, CanActivateRetrieval
    )
    VALUES
    (
        LTRIM(RTRIM(@DryRunId)), LTRIM(RTRIM(@WorkflowRunId)), LTRIM(RTRIM(@WorkflowStepId)),
        LTRIM(RTRIM(@ControlledApplyPlanReferenceId)), LTRIM(RTRIM(@SourceApplyApprovalRequirementReferenceId)),
        LTRIM(RTRIM(@PatchProposalEvidencePackageReferenceId)), LTRIM(RTRIM(@ProjectReferenceId)),
        LTRIM(RTRIM(@TargetReferenceId)), @Status, @OutcomeKind, LTRIM(RTRIM(@SafeSummary)),
        @EvidenceReferencesJson, @GateReferencesJson, @ValidationReferencesJson, @RollbackReferencesJson,
        @RisksJson, @MissingEvidenceJson, COALESCE(LTRIM(RTRIM(@CorrelationId)), N''), @MetadataJson,
        @IsStoreRecordOnly, @IsDryRunPerformed, @IsSourceApply, @IsPatchApplication, @IsApproval,
        @IsApprovalSatisfied, @CanPerformDryRun, @CanApplySource, @CanMutateFiles, @CanReadSourceFiles,
        @CanRunCommand, @CanInvokeTool, @CanRunValidation, @CanRollback, @CanSatisfyPolicy,
        @CanTransitionWorkflow, @CanPromoteMemory, @CanActivateRetrieval
    );

    SELECT *
    FROM workflow.ApplyDryRunRecord
    WHERE DryRunId = LTRIM(RTRIM(@DryRunId));
END;
GO

CREATE OR ALTER PROCEDURE workflow.usp_ApplyDryRun_Get
    @DryRunId NVARCHAR(160)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT *
    FROM workflow.ApplyDryRunRecord
    WHERE DryRunId = LTRIM(RTRIM(@DryRunId));
END;
GO

CREATE OR ALTER PROCEDURE workflow.usp_ApplyDryRun_ListByWorkflowRun
    @WorkflowRunId NVARCHAR(160),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (CASE WHEN @Take < 1 THEN 1 WHEN @Take > 500 THEN 500 ELSE @Take END)
        DryRunId, WorkflowRunId, WorkflowStepId, ControlledApplyPlanReferenceId, ProjectReferenceId,
        TargetReferenceId, Status, OutcomeKind,
        ISNULL(JSON_QUERY(EvidenceReferencesJson), N'[]') AS EvidenceReferencesJson,
        ISNULL(JSON_QUERY(GateReferencesJson), N'[]') AS GateReferencesJson,
        ISNULL(JSON_QUERY(ValidationReferencesJson), N'[]') AS ValidationReferencesJson,
        ISNULL(JSON_QUERY(RollbackReferencesJson), N'[]') AS RollbackReferencesJson,
        ISNULL(JSON_QUERY(RisksJson), N'[]') AS RisksJson,
        ISNULL(JSON_QUERY(MissingEvidenceJson), N'[]') AS MissingEvidenceJson,
        (SELECT COUNT(1) FROM OPENJSON(EvidenceReferencesJson)) AS EvidenceReferenceCount,
        (SELECT COUNT(1) FROM OPENJSON(GateReferencesJson)) AS GateReferenceCount,
        (SELECT COUNT(1) FROM OPENJSON(ValidationReferencesJson)) AS ValidationReferenceCount,
        (SELECT COUNT(1) FROM OPENJSON(RollbackReferencesJson)) AS RollbackReferenceCount,
        (SELECT COUNT(1) FROM OPENJSON(RisksJson)) AS RiskCount,
        (SELECT COUNT(1) FROM OPENJSON(MissingEvidenceJson)) AS MissingEvidenceCount,
        CreatedUtc
    FROM workflow.ApplyDryRunRecord
    WHERE WorkflowRunId = LTRIM(RTRIM(@WorkflowRunId))
    ORDER BY CreatedUtc DESC;
END;
GO

CREATE OR ALTER PROCEDURE workflow.usp_ApplyDryRun_ListByControlledApplyPlan
    @ControlledApplyPlanReferenceId NVARCHAR(160),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (CASE WHEN @Take < 1 THEN 1 WHEN @Take > 500 THEN 500 ELSE @Take END)
        DryRunId, WorkflowRunId, WorkflowStepId, ControlledApplyPlanReferenceId, ProjectReferenceId,
        TargetReferenceId, Status, OutcomeKind,
        (SELECT COUNT(1) FROM OPENJSON(EvidenceReferencesJson)) AS EvidenceReferenceCount,
        (SELECT COUNT(1) FROM OPENJSON(GateReferencesJson)) AS GateReferenceCount,
        (SELECT COUNT(1) FROM OPENJSON(ValidationReferencesJson)) AS ValidationReferenceCount,
        (SELECT COUNT(1) FROM OPENJSON(RollbackReferencesJson)) AS RollbackReferenceCount,
        (SELECT COUNT(1) FROM OPENJSON(RisksJson)) AS RiskCount,
        (SELECT COUNT(1) FROM OPENJSON(MissingEvidenceJson)) AS MissingEvidenceCount,
        CreatedUtc
    FROM workflow.ApplyDryRunRecord
    WHERE ControlledApplyPlanReferenceId = LTRIM(RTRIM(@ControlledApplyPlanReferenceId))
    ORDER BY CreatedUtc DESC;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'IronDevGovernanceEventRuntimeRole') IS NOT NULL
BEGIN
    GRANT EXECUTE ON OBJECT::workflow.usp_ApplyDryRun_Create TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::workflow.usp_ApplyDryRun_Get TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::workflow.usp_ApplyDryRun_ListByWorkflowRun TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::workflow.usp_ApplyDryRun_ListByControlledApplyPlan TO IronDevGovernanceEventRuntimeRole;
    GRANT SELECT ON OBJECT::workflow.ApplyDryRunRecord TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::workflow.ApplyDryRunRecord TO IronDevGovernanceEventRuntimeRole;
END;
GO
