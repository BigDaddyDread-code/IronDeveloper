IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance')
BEGIN
    EXEC(N'CREATE SCHEMA governance');
END;
GO

IF OBJECT_ID(N'governance.AcceptedApproval', N'U') IS NULL
BEGIN
    CREATE TABLE governance.AcceptedApproval
    (
        AcceptedApprovalId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        ApprovalTargetKind NVARCHAR(128) NOT NULL,
        ApprovalTargetId NVARCHAR(256) NOT NULL,
        ApprovalTargetHash NVARCHAR(256) NOT NULL,
        CapabilityCode NVARCHAR(128) NOT NULL,
        ApprovalPurpose NVARCHAR(128) NOT NULL,
        ApprovedByActorId NVARCHAR(256) NOT NULL,
        ApprovedByActorDisplayName NVARCHAR(256) NULL,
        AcceptedAtUtc DATETIMEOFFSET(7) NOT NULL,
        ExpiresAtUtc DATETIMEOFFSET(7) NULL,
        CorrelationId NVARCHAR(256) NOT NULL,
        CausationId NVARCHAR(256) NOT NULL,
        EvidenceReferencesJson NVARCHAR(MAX) NOT NULL,
        BoundaryMaximsJson NVARCHAR(MAX) NOT NULL,
        CreatedAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_AcceptedApproval_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT PK_AcceptedApproval PRIMARY KEY CLUSTERED (AcceptedApprovalId),
        CONSTRAINT CK_AcceptedApproval_TargetKind_NotBlank CHECK (LEN(LTRIM(RTRIM(ApprovalTargetKind))) > 0),
        CONSTRAINT CK_AcceptedApproval_TargetId_NotBlank CHECK (LEN(LTRIM(RTRIM(ApprovalTargetId))) > 0),
        CONSTRAINT CK_AcceptedApproval_TargetHash_NotBlank CHECK (LEN(LTRIM(RTRIM(ApprovalTargetHash))) > 0),
        CONSTRAINT CK_AcceptedApproval_CapabilityCode_NotBlank CHECK (LEN(LTRIM(RTRIM(CapabilityCode))) > 0),
        CONSTRAINT CK_AcceptedApproval_Purpose_NotBlank CHECK (LEN(LTRIM(RTRIM(ApprovalPurpose))) > 0),
        CONSTRAINT CK_AcceptedApproval_ActorId_NotBlank CHECK (LEN(LTRIM(RTRIM(ApprovedByActorId))) > 0),
        CONSTRAINT CK_AcceptedApproval_CorrelationId_NotBlank CHECK (LEN(LTRIM(RTRIM(CorrelationId))) > 0),
        CONSTRAINT CK_AcceptedApproval_CausationId_NotBlank CHECK (LEN(LTRIM(RTRIM(CausationId))) > 0),
        CONSTRAINT CK_AcceptedApproval_Expiry_AfterAccepted CHECK (ExpiresAtUtc IS NULL OR ExpiresAtUtc > AcceptedAtUtc),
        CONSTRAINT CK_AcceptedApproval_EvidenceJson_IsJson CHECK (ISJSON(EvidenceReferencesJson) = 1),
        CONSTRAINT CK_AcceptedApproval_EvidenceJson_NotEmpty CHECK (JSON_VALUE(EvidenceReferencesJson, '$[0]') IS NOT NULL),
        CONSTRAINT CK_AcceptedApproval_BoundaryJson_IsJson CHECK (ISJSON(BoundaryMaximsJson) = 1),
        CONSTRAINT CK_AcceptedApproval_BoundaryJson_NotEmpty CHECK (JSON_VALUE(BoundaryMaximsJson, '$[0]') IS NOT NULL)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AcceptedApproval_Project_AcceptedAt' AND object_id = OBJECT_ID(N'governance.AcceptedApproval'))
BEGIN
    CREATE INDEX IX_AcceptedApproval_Project_AcceptedAt
    ON governance.AcceptedApproval(ProjectId, AcceptedAtUtc DESC, AcceptedApprovalId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AcceptedApproval_Project_Target' AND object_id = OBJECT_ID(N'governance.AcceptedApproval'))
BEGIN
    CREATE INDEX IX_AcceptedApproval_Project_Target
    ON governance.AcceptedApproval(ProjectId, ApprovalTargetKind, ApprovalTargetId, AcceptedAtUtc DESC, AcceptedApprovalId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AcceptedApproval_Project_TargetHash' AND object_id = OBJECT_ID(N'governance.AcceptedApproval'))
BEGIN
    CREATE INDEX IX_AcceptedApproval_Project_TargetHash
    ON governance.AcceptedApproval(ProjectId, ApprovalTargetHash, AcceptedAtUtc DESC, AcceptedApprovalId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AcceptedApproval_Project_Capability' AND object_id = OBJECT_ID(N'governance.AcceptedApproval'))
BEGIN
    CREATE INDEX IX_AcceptedApproval_Project_Capability
    ON governance.AcceptedApproval(ProjectId, CapabilityCode, AcceptedAtUtc DESC, AcceptedApprovalId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AcceptedApproval_Project_Correlation' AND object_id = OBJECT_ID(N'governance.AcceptedApproval'))
BEGIN
    CREATE INDEX IX_AcceptedApproval_Project_Correlation
    ON governance.AcceptedApproval(ProjectId, CorrelationId, AcceptedAtUtc DESC, AcceptedApprovalId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AcceptedApproval_Correlation' AND object_id = OBJECT_ID(N'governance.AcceptedApproval'))
BEGIN
    CREATE INDEX IX_AcceptedApproval_Correlation
    ON governance.AcceptedApproval(CorrelationId, AcceptedAtUtc DESC, AcceptedApprovalId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AcceptedApproval_Causation' AND object_id = OBJECT_ID(N'governance.AcceptedApproval'))
BEGIN
    CREATE INDEX IX_AcceptedApproval_Causation
    ON governance.AcceptedApproval(CausationId, AcceptedAtUtc DESC, AcceptedApprovalId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AcceptedApproval_ExpiresAt' AND object_id = OBJECT_ID(N'governance.AcceptedApproval'))
BEGIN
    CREATE INDEX IX_AcceptedApproval_ExpiresAt
    ON governance.AcceptedApproval(ExpiresAtUtc, ProjectId, AcceptedApprovalId)
    WHERE ExpiresAtUtc IS NOT NULL;
END;
GO

IF OBJECT_ID(N'governance.TR_AcceptedApproval_ValidateInsert', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_AcceptedApproval_ValidateInsert;
END;
GO

CREATE TRIGGER governance.TR_AcceptedApproval_ValidateInsert
ON governance.AcceptedApproval
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        WHERE LOWER(i.EvidenceReferencesJson) LIKE N'%rawprompt%'
           OR LOWER(i.EvidenceReferencesJson) LIKE N'%raw prompt%'
           OR LOWER(i.EvidenceReferencesJson) LIKE N'%rawcompletion%'
           OR LOWER(i.EvidenceReferencesJson) LIKE N'%raw completion%'
           OR LOWER(i.EvidenceReferencesJson) LIKE N'%rawtooloutput%'
           OR LOWER(i.EvidenceReferencesJson) LIKE N'%raw tool output%'
           OR LOWER(i.EvidenceReferencesJson) LIKE N'%chainofthought%'
           OR LOWER(i.EvidenceReferencesJson) LIKE N'%chain-of-thought%'
           OR LOWER(i.EvidenceReferencesJson) LIKE N'%chain of thought%'
           OR LOWER(i.EvidenceReferencesJson) LIKE N'%scratchpad%'
           OR LOWER(i.EvidenceReferencesJson) LIKE N'%private reasoning%'
           OR LOWER(i.EvidenceReferencesJson) LIKE N'%hidden reasoning%'
           OR LOWER(i.BoundaryMaximsJson) LIKE N'%grants execution%'
           OR LOWER(i.BoundaryMaximsJson) LIKE N'%runs dry-run%'
           OR LOWER(i.BoundaryMaximsJson) LIKE N'%creates patch artifact%'
           OR LOWER(i.BoundaryMaximsJson) LIKE N'%applies source%'
           OR LOWER(i.BoundaryMaximsJson) LIKE N'%continues workflow%'
           OR LOWER(i.BoundaryMaximsJson) LIKE N'%approves release%'
    )
    BEGIN
        THROW 52740, 'Accepted approval records must not contain raw/private material or action-authority claims.', 1;
    END;
END;
GO

IF OBJECT_ID(N'governance.TR_AcceptedApproval_BlockUpdateDelete', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_AcceptedApproval_BlockUpdateDelete;
END;
GO

CREATE TRIGGER governance.TR_AcceptedApproval_BlockUpdateDelete
ON governance.AcceptedApproval
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 52741, 'Accepted approvals are append-only.', 1;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_AcceptedApproval_Save
    @AcceptedApprovalId UNIQUEIDENTIFIER,
    @ProjectId UNIQUEIDENTIFIER,
    @ApprovalTargetKind NVARCHAR(128),
    @ApprovalTargetId NVARCHAR(256),
    @ApprovalTargetHash NVARCHAR(256),
    @CapabilityCode NVARCHAR(128),
    @ApprovalPurpose NVARCHAR(128),
    @ApprovedByActorId NVARCHAR(256),
    @ApprovedByActorDisplayName NVARCHAR(256) = NULL,
    @AcceptedAtUtc DATETIMEOFFSET(7),
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

    IF EXISTS (SELECT 1 FROM governance.AcceptedApproval WHERE AcceptedApprovalId = @AcceptedApprovalId)
    BEGIN
        THROW 52742, 'Accepted approval already exists.', 1;
    END;

    INSERT INTO governance.AcceptedApproval
    (
        AcceptedApprovalId,
        ProjectId,
        ApprovalTargetKind,
        ApprovalTargetId,
        ApprovalTargetHash,
        CapabilityCode,
        ApprovalPurpose,
        ApprovedByActorId,
        ApprovedByActorDisplayName,
        AcceptedAtUtc,
        ExpiresAtUtc,
        CorrelationId,
        CausationId,
        EvidenceReferencesJson,
        BoundaryMaximsJson,
        CreatedAtUtc
    )
    VALUES
    (
        @AcceptedApprovalId,
        @ProjectId,
        LTRIM(RTRIM(@ApprovalTargetKind)),
        LTRIM(RTRIM(@ApprovalTargetId)),
        LTRIM(RTRIM(@ApprovalTargetHash)),
        LTRIM(RTRIM(@CapabilityCode)),
        LTRIM(RTRIM(@ApprovalPurpose)),
        LTRIM(RTRIM(@ApprovedByActorId)),
        NULLIF(LTRIM(RTRIM(@ApprovedByActorDisplayName)), N''),
        @AcceptedAtUtc,
        @ExpiresAtUtc,
        LTRIM(RTRIM(@CorrelationId)),
        LTRIM(RTRIM(@CausationId)),
        @EvidenceReferencesJson,
        @BoundaryMaximsJson,
        COALESCE(@CreatedAtUtc, SYSUTCDATETIME())
    );
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_AcceptedApproval_Get
    @ProjectId UNIQUEIDENTIFIER,
    @AcceptedApprovalId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        AcceptedApprovalId,
        ProjectId,
        ApprovalTargetKind,
        ApprovalTargetId,
        ApprovalTargetHash,
        CapabilityCode,
        ApprovalPurpose,
        ApprovedByActorId,
        ApprovedByActorDisplayName,
        AcceptedAtUtc,
        ExpiresAtUtc,
        CorrelationId,
        CausationId,
        EvidenceReferencesJson,
        BoundaryMaximsJson,
        CreatedAtUtc
    FROM governance.AcceptedApproval
    WHERE ProjectId = @ProjectId
      AND AcceptedApprovalId = @AcceptedApprovalId;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_AcceptedApproval_ListByTarget
    @ProjectId UNIQUEIDENTIFIER,
    @ApprovalTargetKind NVARCHAR(128),
    @ApprovalTargetId NVARCHAR(256),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake)
        AcceptedApprovalId,
        ProjectId,
        ApprovalTargetKind,
        ApprovalTargetId,
        ApprovalTargetHash,
        CapabilityCode,
        ApprovalPurpose,
        ApprovedByActorId,
        ApprovedByActorDisplayName,
        AcceptedAtUtc,
        ExpiresAtUtc,
        CorrelationId,
        CausationId,
        EvidenceReferencesJson,
        BoundaryMaximsJson,
        CreatedAtUtc
    FROM governance.AcceptedApproval
    WHERE ProjectId = @ProjectId
      AND ApprovalTargetKind = LTRIM(RTRIM(@ApprovalTargetKind))
      AND ApprovalTargetId = LTRIM(RTRIM(@ApprovalTargetId))
    ORDER BY AcceptedAtUtc DESC, AcceptedApprovalId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_AcceptedApproval_ListByCorrelation
    @CorrelationId NVARCHAR(256),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake)
        AcceptedApprovalId,
        ProjectId,
        ApprovalTargetKind,
        ApprovalTargetId,
        ApprovalTargetHash,
        CapabilityCode,
        ApprovalPurpose,
        ApprovedByActorId,
        ApprovedByActorDisplayName,
        AcceptedAtUtc,
        ExpiresAtUtc,
        CorrelationId,
        CausationId,
        EvidenceReferencesJson,
        BoundaryMaximsJson,
        CreatedAtUtc
    FROM governance.AcceptedApproval
    WHERE CorrelationId = LTRIM(RTRIM(@CorrelationId))
    ORDER BY AcceptedAtUtc DESC, AcceptedApprovalId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_AcceptedApproval_ListByProjectAndCorrelation
    @ProjectId UNIQUEIDENTIFIER,
    @CorrelationId NVARCHAR(256),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake)
        AcceptedApprovalId,
        ProjectId,
        ApprovalTargetKind,
        ApprovalTargetId,
        ApprovalTargetHash,
        CapabilityCode,
        ApprovalPurpose,
        ApprovedByActorId,
        ApprovedByActorDisplayName,
        AcceptedAtUtc,
        ExpiresAtUtc,
        CorrelationId,
        CausationId,
        EvidenceReferencesJson,
        BoundaryMaximsJson,
        CreatedAtUtc
    FROM governance.AcceptedApproval
    WHERE ProjectId = @ProjectId
      AND CorrelationId = LTRIM(RTRIM(@CorrelationId))
    ORDER BY AcceptedAtUtc DESC, AcceptedApprovalId DESC;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'IronDevGovernanceEventRuntimeRole') IS NOT NULL
BEGIN
    GRANT EXECUTE ON OBJECT::governance.usp_AcceptedApproval_Save TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_AcceptedApproval_Get TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_AcceptedApproval_ListByTarget TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_AcceptedApproval_ListByCorrelation TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_AcceptedApproval_ListByProjectAndCorrelation TO IronDevGovernanceEventRuntimeRole;
    GRANT SELECT ON OBJECT::governance.AcceptedApproval TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::governance.AcceptedApproval TO IronDevGovernanceEventRuntimeRole;
    DENY ALTER ON SCHEMA::governance TO IronDevGovernanceEventRuntimeRole;
END;
GO
