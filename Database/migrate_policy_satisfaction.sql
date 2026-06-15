IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance')
BEGIN
    EXEC(N'CREATE SCHEMA governance');
END;
GO

IF OBJECT_ID(N'governance.PolicySatisfaction', N'U') IS NULL
BEGIN
    CREATE TABLE governance.PolicySatisfaction
    (
        PolicySatisfactionId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        PolicyCode NVARCHAR(128) NOT NULL,
        PolicyVersion NVARCHAR(128) NOT NULL,
        SubjectKind NVARCHAR(128) NOT NULL,
        SubjectId NVARCHAR(256) NOT NULL,
        SubjectHash NVARCHAR(256) NOT NULL,
        CapabilityCode NVARCHAR(128) NOT NULL,
        AcceptedApprovalId UNIQUEIDENTIFIER NOT NULL,
        ApprovalRequirementHash NVARCHAR(256) NOT NULL,
        ApprovalEvaluatedAtUtc DATETIMEOFFSET(7) NOT NULL,
        SatisfiedAtUtc DATETIMEOFFSET(7) NOT NULL,
        ExpiresAtUtc DATETIMEOFFSET(7) NULL,
        CorrelationId NVARCHAR(256) NOT NULL,
        CausationId NVARCHAR(256) NOT NULL,
        EvidenceReferencesJson NVARCHAR(MAX) NOT NULL,
        BoundaryMaximsJson NVARCHAR(MAX) NOT NULL,
        CreatedAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_PolicySatisfaction_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT PK_PolicySatisfaction PRIMARY KEY CLUSTERED (PolicySatisfactionId),
        CONSTRAINT CK_PolicySatisfaction_PolicyCode_NotBlank CHECK (LEN(LTRIM(RTRIM(PolicyCode))) > 0),
        CONSTRAINT CK_PolicySatisfaction_PolicyVersion_NotBlank CHECK (LEN(LTRIM(RTRIM(PolicyVersion))) > 0),
        CONSTRAINT CK_PolicySatisfaction_SubjectKind_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectKind))) > 0),
        CONSTRAINT CK_PolicySatisfaction_SubjectId_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectId))) > 0),
        CONSTRAINT CK_PolicySatisfaction_SubjectHash_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectHash))) > 0),
        CONSTRAINT CK_PolicySatisfaction_CapabilityCode_NotBlank CHECK (LEN(LTRIM(RTRIM(CapabilityCode))) > 0),
        CONSTRAINT CK_PolicySatisfaction_ApprovalRequirementHash_NotBlank CHECK (LEN(LTRIM(RTRIM(ApprovalRequirementHash))) > 0),
        CONSTRAINT CK_PolicySatisfaction_CorrelationId_NotBlank CHECK (LEN(LTRIM(RTRIM(CorrelationId))) > 0),
        CONSTRAINT CK_PolicySatisfaction_CausationId_NotBlank CHECK (LEN(LTRIM(RTRIM(CausationId))) > 0),
        CONSTRAINT CK_PolicySatisfaction_Expiry_AfterSatisfied CHECK (ExpiresAtUtc IS NULL OR ExpiresAtUtc > SatisfiedAtUtc),
        CONSTRAINT CK_PolicySatisfaction_EvidenceJson_IsJson CHECK (ISJSON(EvidenceReferencesJson) = 1),
        CONSTRAINT CK_PolicySatisfaction_EvidenceJson_NotEmpty CHECK (JSON_VALUE(EvidenceReferencesJson, '$[0]') IS NOT NULL),
        CONSTRAINT CK_PolicySatisfaction_BoundaryJson_IsJson CHECK (ISJSON(BoundaryMaximsJson) = 1),
        CONSTRAINT CK_PolicySatisfaction_BoundaryJson_NotEmpty CHECK (JSON_VALUE(BoundaryMaximsJson, '$[0]') IS NOT NULL)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PolicySatisfaction_Project_SatisfiedAt' AND object_id = OBJECT_ID(N'governance.PolicySatisfaction'))
BEGIN
    CREATE INDEX IX_PolicySatisfaction_Project_SatisfiedAt
    ON governance.PolicySatisfaction(ProjectId, SatisfiedAtUtc DESC, PolicySatisfactionId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PolicySatisfaction_Project_Policy' AND object_id = OBJECT_ID(N'governance.PolicySatisfaction'))
BEGIN
    CREATE INDEX IX_PolicySatisfaction_Project_Policy
    ON governance.PolicySatisfaction(ProjectId, PolicyCode, PolicyVersion, SatisfiedAtUtc DESC, PolicySatisfactionId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PolicySatisfaction_Project_Subject' AND object_id = OBJECT_ID(N'governance.PolicySatisfaction'))
BEGIN
    CREATE INDEX IX_PolicySatisfaction_Project_Subject
    ON governance.PolicySatisfaction(ProjectId, SubjectKind, SubjectId, SatisfiedAtUtc DESC, PolicySatisfactionId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PolicySatisfaction_Project_SubjectHash' AND object_id = OBJECT_ID(N'governance.PolicySatisfaction'))
BEGIN
    CREATE INDEX IX_PolicySatisfaction_Project_SubjectHash
    ON governance.PolicySatisfaction(ProjectId, SubjectHash, SatisfiedAtUtc DESC, PolicySatisfactionId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PolicySatisfaction_Project_Capability' AND object_id = OBJECT_ID(N'governance.PolicySatisfaction'))
BEGIN
    CREATE INDEX IX_PolicySatisfaction_Project_Capability
    ON governance.PolicySatisfaction(ProjectId, CapabilityCode, SatisfiedAtUtc DESC, PolicySatisfactionId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PolicySatisfaction_Project_AcceptedApproval' AND object_id = OBJECT_ID(N'governance.PolicySatisfaction'))
BEGIN
    CREATE INDEX IX_PolicySatisfaction_Project_AcceptedApproval
    ON governance.PolicySatisfaction(ProjectId, AcceptedApprovalId, SatisfiedAtUtc DESC, PolicySatisfactionId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PolicySatisfaction_Project_Correlation' AND object_id = OBJECT_ID(N'governance.PolicySatisfaction'))
BEGIN
    CREATE INDEX IX_PolicySatisfaction_Project_Correlation
    ON governance.PolicySatisfaction(ProjectId, CorrelationId, SatisfiedAtUtc DESC, PolicySatisfactionId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PolicySatisfaction_Project_Causation' AND object_id = OBJECT_ID(N'governance.PolicySatisfaction'))
BEGIN
    CREATE INDEX IX_PolicySatisfaction_Project_Causation
    ON governance.PolicySatisfaction(ProjectId, CausationId, SatisfiedAtUtc DESC, PolicySatisfactionId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PolicySatisfaction_ExpiresAt' AND object_id = OBJECT_ID(N'governance.PolicySatisfaction'))
BEGIN
    CREATE INDEX IX_PolicySatisfaction_ExpiresAt
    ON governance.PolicySatisfaction(ExpiresAtUtc, ProjectId, PolicySatisfactionId)
    WHERE ExpiresAtUtc IS NOT NULL;
END;
GO

IF OBJECT_ID(N'governance.TR_PolicySatisfaction_ValidateInsert', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_PolicySatisfaction_ValidateInsert;
END;
GO

CREATE TRIGGER governance.TR_PolicySatisfaction_ValidateInsert
ON governance.PolicySatisfaction
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        CROSS APPLY (SELECT LOWER(CONCAT(i.EvidenceReferencesJson, N' ', i.BoundaryMaximsJson)) AS TextToCheck) c
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
           OR c.TextToCheck LIKE N'%runs dry-run%'
           OR c.TextToCheck LIKE N'%creates patch artifact%'
           OR c.TextToCheck LIKE N'%applies source%'
           OR c.TextToCheck LIKE N'%continues workflow%'
           OR c.TextToCheck LIKE N'%approves release%'
           OR c.TextToCheck LIKE N'%release ready%'
           OR c.TextToCheck LIKE N'%source applied%'
           OR c.TextToCheck LIKE N'%workflow continued%'
    )
    BEGIN
        THROW 52760, 'Policy satisfaction records must not contain raw/private material or action-authority claims.', 1;
    END;
END;
GO

IF OBJECT_ID(N'governance.TR_PolicySatisfaction_BlockUpdateDelete', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_PolicySatisfaction_BlockUpdateDelete;
END;
GO

CREATE TRIGGER governance.TR_PolicySatisfaction_BlockUpdateDelete
ON governance.PolicySatisfaction
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 52761, 'Policy satisfaction records are append-only.', 1;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_PolicySatisfaction_Save
    @PolicySatisfactionId UNIQUEIDENTIFIER,
    @ProjectId UNIQUEIDENTIFIER,
    @PolicyCode NVARCHAR(128),
    @PolicyVersion NVARCHAR(128),
    @SubjectKind NVARCHAR(128),
    @SubjectId NVARCHAR(256),
    @SubjectHash NVARCHAR(256),
    @CapabilityCode NVARCHAR(128),
    @AcceptedApprovalId UNIQUEIDENTIFIER,
    @ApprovalRequirementHash NVARCHAR(256),
    @ApprovalEvaluatedAtUtc DATETIMEOFFSET(7),
    @SatisfiedAtUtc DATETIMEOFFSET(7),
    @ExpiresAtUtc DATETIMEOFFSET(7) = NULL,
    @CorrelationId NVARCHAR(256),
    @CausationId NVARCHAR(256),
    @EvidenceReferencesJson NVARCHAR(MAX),
    @BoundaryMaximsJson NVARCHAR(MAX),
    @CreatedAtUtc DATETIMEOFFSET(7) = NULL
AS
BEGIN
    SET XACT_ABORT ON;
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM governance.PolicySatisfaction WHERE PolicySatisfactionId = @PolicySatisfactionId)
    BEGIN
        THROW 52762, 'Policy satisfaction already exists.', 1;
    END;

    INSERT INTO governance.PolicySatisfaction
    (
        PolicySatisfactionId,
        ProjectId,
        PolicyCode,
        PolicyVersion,
        SubjectKind,
        SubjectId,
        SubjectHash,
        CapabilityCode,
        AcceptedApprovalId,
        ApprovalRequirementHash,
        ApprovalEvaluatedAtUtc,
        SatisfiedAtUtc,
        ExpiresAtUtc,
        CorrelationId,
        CausationId,
        EvidenceReferencesJson,
        BoundaryMaximsJson,
        CreatedAtUtc
    )
    VALUES
    (
        @PolicySatisfactionId,
        @ProjectId,
        LTRIM(RTRIM(@PolicyCode)),
        LTRIM(RTRIM(@PolicyVersion)),
        LTRIM(RTRIM(@SubjectKind)),
        LTRIM(RTRIM(@SubjectId)),
        LTRIM(RTRIM(@SubjectHash)),
        LTRIM(RTRIM(@CapabilityCode)),
        @AcceptedApprovalId,
        LTRIM(RTRIM(@ApprovalRequirementHash)),
        @ApprovalEvaluatedAtUtc,
        @SatisfiedAtUtc,
        @ExpiresAtUtc,
        LTRIM(RTRIM(@CorrelationId)),
        LTRIM(RTRIM(@CausationId)),
        @EvidenceReferencesJson,
        @BoundaryMaximsJson,
        COALESCE(@CreatedAtUtc, SYSUTCDATETIME())
    );
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_PolicySatisfaction_Get
    @ProjectId UNIQUEIDENTIFIER,
    @PolicySatisfactionId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        PolicySatisfactionId,
        ProjectId,
        PolicyCode,
        PolicyVersion,
        SubjectKind,
        SubjectId,
        SubjectHash,
        CapabilityCode,
        AcceptedApprovalId,
        ApprovalRequirementHash,
        ApprovalEvaluatedAtUtc,
        SatisfiedAtUtc,
        ExpiresAtUtc,
        CorrelationId,
        CausationId,
        EvidenceReferencesJson,
        BoundaryMaximsJson,
        CreatedAtUtc
    FROM governance.PolicySatisfaction
    WHERE ProjectId = @ProjectId
      AND PolicySatisfactionId = @PolicySatisfactionId;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_PolicySatisfaction_ListBySubject
    @ProjectId UNIQUEIDENTIFIER,
    @SubjectKind NVARCHAR(128),
    @SubjectId NVARCHAR(256),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake)
        PolicySatisfactionId,
        ProjectId,
        PolicyCode,
        PolicyVersion,
        SubjectKind,
        SubjectId,
        SubjectHash,
        CapabilityCode,
        AcceptedApprovalId,
        ApprovalRequirementHash,
        ApprovalEvaluatedAtUtc,
        SatisfiedAtUtc,
        ExpiresAtUtc,
        CorrelationId,
        CausationId,
        EvidenceReferencesJson,
        BoundaryMaximsJson,
        CreatedAtUtc
    FROM governance.PolicySatisfaction
    WHERE ProjectId = @ProjectId
      AND SubjectKind = LTRIM(RTRIM(@SubjectKind))
      AND SubjectId = LTRIM(RTRIM(@SubjectId))
    ORDER BY SatisfiedAtUtc DESC, PolicySatisfactionId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_PolicySatisfaction_ListByAcceptedApproval
    @ProjectId UNIQUEIDENTIFIER,
    @AcceptedApprovalId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake)
        PolicySatisfactionId,
        ProjectId,
        PolicyCode,
        PolicyVersion,
        SubjectKind,
        SubjectId,
        SubjectHash,
        CapabilityCode,
        AcceptedApprovalId,
        ApprovalRequirementHash,
        ApprovalEvaluatedAtUtc,
        SatisfiedAtUtc,
        ExpiresAtUtc,
        CorrelationId,
        CausationId,
        EvidenceReferencesJson,
        BoundaryMaximsJson,
        CreatedAtUtc
    FROM governance.PolicySatisfaction
    WHERE ProjectId = @ProjectId
      AND AcceptedApprovalId = @AcceptedApprovalId
    ORDER BY SatisfiedAtUtc DESC, PolicySatisfactionId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_PolicySatisfaction_ListByProjectAndCorrelation
    @ProjectId UNIQUEIDENTIFIER,
    @CorrelationId NVARCHAR(256),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake)
        PolicySatisfactionId,
        ProjectId,
        PolicyCode,
        PolicyVersion,
        SubjectKind,
        SubjectId,
        SubjectHash,
        CapabilityCode,
        AcceptedApprovalId,
        ApprovalRequirementHash,
        ApprovalEvaluatedAtUtc,
        SatisfiedAtUtc,
        ExpiresAtUtc,
        CorrelationId,
        CausationId,
        EvidenceReferencesJson,
        BoundaryMaximsJson,
        CreatedAtUtc
    FROM governance.PolicySatisfaction
    WHERE ProjectId = @ProjectId
      AND CorrelationId = LTRIM(RTRIM(@CorrelationId))
    ORDER BY SatisfiedAtUtc DESC, PolicySatisfactionId DESC;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'IronDevGovernanceEventRuntimeRole') IS NOT NULL
BEGIN
    GRANT EXECUTE ON OBJECT::governance.usp_PolicySatisfaction_Save TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_PolicySatisfaction_Get TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_PolicySatisfaction_ListBySubject TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_PolicySatisfaction_ListByAcceptedApproval TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_PolicySatisfaction_ListByProjectAndCorrelation TO IronDevGovernanceEventRuntimeRole;
    GRANT SELECT ON OBJECT::governance.PolicySatisfaction TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::governance.PolicySatisfaction TO IronDevGovernanceEventRuntimeRole;
    DENY ALTER ON SCHEMA::governance TO IronDevGovernanceEventRuntimeRole;
END;
GO
