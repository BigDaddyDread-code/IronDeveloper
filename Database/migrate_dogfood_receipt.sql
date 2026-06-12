IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance')
BEGIN
    EXEC(N'CREATE SCHEMA governance');
END;
GO

IF OBJECT_ID(N'governance.GovernanceEvent', N'U') IS NULL
BEGIN
    THROW 52500, 'governance.GovernanceEvent must exist before governance.DogfoodReceipt migration runs.', 1;
END;
GO

IF OBJECT_ID(N'governance.ToolRequest', N'U') IS NULL
BEGIN
    THROW 52501, 'governance.ToolRequest must exist before governance.DogfoodReceipt migration runs.', 1;
END;
GO

IF OBJECT_ID(N'governance.ToolGateDecision', N'U') IS NULL
BEGIN
    THROW 52502, 'governance.ToolGateDecision must exist before governance.DogfoodReceipt migration runs.', 1;
END;
GO

IF OBJECT_ID(N'governance.ApprovalDecision', N'U') IS NULL
BEGIN
    THROW 52503, 'governance.ApprovalDecision must exist before governance.DogfoodReceipt migration runs.', 1;
END;
GO

IF OBJECT_ID(N'governance.PolicyDecisionEvent', N'U') IS NULL
BEGIN
    THROW 52504, 'governance.PolicyDecisionEvent must exist before governance.DogfoodReceipt migration runs.', 1;
END;
GO

IF OBJECT_ID(N'governance.DogfoodReceipt', N'U') IS NULL
BEGIN
    CREATE TABLE governance.DogfoodReceipt
    (
        DogfoodReceiptId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        GovernanceEventId UNIQUEIDENTIFIER NOT NULL,
        ReceiptType NVARCHAR(120) NOT NULL,
        SubjectType NVARCHAR(120) NOT NULL,
        SubjectId NVARCHAR(200) NOT NULL,
        Outcome NVARCHAR(40) NOT NULL,
        SummaryCode NVARCHAR(160) NOT NULL,
        Summary NVARCHAR(1000) NULL,
        RecordedByActorType NVARCHAR(80) NOT NULL,
        RecordedByActorId NVARCHAR(200) NOT NULL,
        RelatedToolRequestId UNIQUEIDENTIFIER NULL,
        RelatedToolGateDecisionId UNIQUEIDENTIFIER NULL,
        RelatedApprovalDecisionId UNIQUEIDENTIFIER NULL,
        RelatedPolicyDecisionEventId UNIQUEIDENTIFIER NULL,
        CorrelationId UNIQUEIDENTIFIER NULL,
        CausationId UNIQUEIDENTIFIER NULL,
        EvidenceVersion INT NOT NULL,
        EvidenceJson NVARCHAR(MAX) NOT NULL,
        CreatedUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_DogfoodReceipt_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_DogfoodReceipt PRIMARY KEY CLUSTERED (DogfoodReceiptId),
        CONSTRAINT FK_DogfoodReceipt_GovernanceEvent FOREIGN KEY (GovernanceEventId) REFERENCES governance.GovernanceEvent(EventId),
        CONSTRAINT FK_DogfoodReceipt_ToolRequest FOREIGN KEY (RelatedToolRequestId) REFERENCES governance.ToolRequest(ToolRequestId),
        CONSTRAINT FK_DogfoodReceipt_ToolGateDecision FOREIGN KEY (RelatedToolGateDecisionId) REFERENCES governance.ToolGateDecision(ToolGateDecisionId),
        CONSTRAINT FK_DogfoodReceipt_ApprovalDecision FOREIGN KEY (RelatedApprovalDecisionId) REFERENCES governance.ApprovalDecision(ApprovalDecisionId),
        CONSTRAINT FK_DogfoodReceipt_PolicyDecisionEvent FOREIGN KEY (RelatedPolicyDecisionEventId) REFERENCES governance.PolicyDecisionEvent(PolicyDecisionEventId),
        CONSTRAINT CK_DogfoodReceipt_ReceiptType_NotBlank CHECK (LEN(LTRIM(RTRIM(ReceiptType))) > 0),
        CONSTRAINT CK_DogfoodReceipt_SubjectType_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectType))) > 0),
        CONSTRAINT CK_DogfoodReceipt_SubjectId_NotBlank CHECK (LEN(LTRIM(RTRIM(SubjectId))) > 0),
        CONSTRAINT CK_DogfoodReceipt_Outcome_Allowed CHECK (Outcome IN (N'Passed', N'Failed', N'Partial', N'Inconclusive', N'NotRun')),
        CONSTRAINT CK_DogfoodReceipt_Outcome_NotAuthority CHECK (Outcome NOT IN (N'Approved', N'ReleaseApproved', N'ReadyToRelease', N'ReleaseReady', N'Authorized', N'Accepted', N'Promoted', N'Certified', N'CanShip')),
        CONSTRAINT CK_DogfoodReceipt_SummaryCode_NotBlank CHECK (LEN(LTRIM(RTRIM(SummaryCode))) > 0),
        CONSTRAINT CK_DogfoodReceipt_RecordedByActorType_NotBlank CHECK (LEN(LTRIM(RTRIM(RecordedByActorType))) > 0),
        CONSTRAINT CK_DogfoodReceipt_RecordedByActorId_NotBlank CHECK (LEN(LTRIM(RTRIM(RecordedByActorId))) > 0),
        CONSTRAINT CK_DogfoodReceipt_EvidenceVersion_Positive CHECK (EvidenceVersion > 0),
        CONSTRAINT CK_DogfoodReceipt_EvidenceJson_IsJson CHECK (ISJSON(EvidenceJson) = 1),
        CONSTRAINT CK_DogfoodReceipt_EvidenceJson_Versioned CHECK (JSON_VALUE(EvidenceJson, '$.schema') IS NOT NULL OR JSON_VALUE(EvidenceJson, '$.schemaVersion') IS NOT NULL)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DogfoodReceipt_Project_CreatedUtc' AND object_id = OBJECT_ID(N'governance.DogfoodReceipt'))
BEGIN
    CREATE INDEX IX_DogfoodReceipt_Project_CreatedUtc
    ON governance.DogfoodReceipt(ProjectId, CreatedUtc DESC, DogfoodReceiptId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DogfoodReceipt_Subject_CreatedUtc' AND object_id = OBJECT_ID(N'governance.DogfoodReceipt'))
BEGIN
    CREATE INDEX IX_DogfoodReceipt_Subject_CreatedUtc
    ON governance.DogfoodReceipt(ProjectId, ReceiptType, SubjectType, SubjectId, CreatedUtc DESC, DogfoodReceiptId DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DogfoodReceipt_Correlation_CreatedUtc' AND object_id = OBJECT_ID(N'governance.DogfoodReceipt'))
BEGIN
    CREATE INDEX IX_DogfoodReceipt_Correlation_CreatedUtc
    ON governance.DogfoodReceipt(CorrelationId, CreatedUtc DESC, DogfoodReceiptId DESC)
    WHERE CorrelationId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DogfoodReceipt_GovernanceEventId' AND object_id = OBJECT_ID(N'governance.DogfoodReceipt'))
BEGIN
    CREATE INDEX IX_DogfoodReceipt_GovernanceEventId
    ON governance.DogfoodReceipt(GovernanceEventId);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DogfoodReceipt_RelatedToolRequest' AND object_id = OBJECT_ID(N'governance.DogfoodReceipt'))
BEGIN
    CREATE INDEX IX_DogfoodReceipt_RelatedToolRequest
    ON governance.DogfoodReceipt(RelatedToolRequestId)
    WHERE RelatedToolRequestId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DogfoodReceipt_RelatedToolGateDecision' AND object_id = OBJECT_ID(N'governance.DogfoodReceipt'))
BEGIN
    CREATE INDEX IX_DogfoodReceipt_RelatedToolGateDecision
    ON governance.DogfoodReceipt(RelatedToolGateDecisionId)
    WHERE RelatedToolGateDecisionId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DogfoodReceipt_RelatedApprovalDecision' AND object_id = OBJECT_ID(N'governance.DogfoodReceipt'))
BEGIN
    CREATE INDEX IX_DogfoodReceipt_RelatedApprovalDecision
    ON governance.DogfoodReceipt(RelatedApprovalDecisionId)
    WHERE RelatedApprovalDecisionId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DogfoodReceipt_RelatedPolicyDecisionEvent' AND object_id = OBJECT_ID(N'governance.DogfoodReceipt'))
BEGIN
    CREATE INDEX IX_DogfoodReceipt_RelatedPolicyDecisionEvent
    ON governance.DogfoodReceipt(RelatedPolicyDecisionEventId)
    WHERE RelatedPolicyDecisionEventId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DogfoodReceipt_Project_Outcome_CreatedUtc' AND object_id = OBJECT_ID(N'governance.DogfoodReceipt'))
BEGIN
    CREATE INDEX IX_DogfoodReceipt_Project_Outcome_CreatedUtc
    ON governance.DogfoodReceipt(ProjectId, Outcome, CreatedUtc DESC, DogfoodReceiptId DESC);
END;
GO

IF OBJECT_ID(N'governance.TR_DogfoodReceipt_ValidateInsert', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_DogfoodReceipt_ValidateInsert;
END;
GO

CREATE TRIGGER governance.TR_DogfoodReceipt_ValidateInsert
ON governance.DogfoodReceipt
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        JOIN governance.ToolRequest request
            ON request.ToolRequestId = i.RelatedToolRequestId
        WHERE request.ProjectId <> i.ProjectId
    )
    BEGIN
        THROW 52590, 'Dogfood receipt related tool request belongs to a different project.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        JOIN governance.ToolGateDecision gateDecision
            ON gateDecision.ToolGateDecisionId = i.RelatedToolGateDecisionId
        WHERE gateDecision.ProjectId <> i.ProjectId
    )
    BEGIN
        THROW 52591, 'Dogfood receipt related tool gate decision belongs to a different project.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        JOIN governance.ApprovalDecision approvalDecision
            ON approvalDecision.ApprovalDecisionId = i.RelatedApprovalDecisionId
        WHERE approvalDecision.ProjectId <> i.ProjectId
    )
    BEGIN
        THROW 52592, 'Dogfood receipt related approval decision belongs to a different project.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        JOIN governance.PolicyDecisionEvent policyDecision
            ON policyDecision.PolicyDecisionEventId = i.RelatedPolicyDecisionEventId
        WHERE policyDecision.ProjectId <> i.ProjectId
    )
    BEGIN
        THROW 52593, 'Dogfood receipt related policy decision belongs to a different project.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        WHERE JSON_VALUE(i.EvidenceJson, '$.approvesRelease') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.releaseApproved') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.releaseReady') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.grantsApproval') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.approvalGranted') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.grantsExecution') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.executionAllowed') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.mutatesSource') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.sourceApplied') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.promotesMemory') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.memoryPromoted') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.startsWorkflow') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.workflowStarted') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.satisfiesPolicy') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.transfersAuthority') = N'true'
           OR JSON_VALUE(i.EvidenceJson, '$.containsRawPrivateReasoning') = N'true'
    )
    BEGIN
        THROW 52594, 'Dogfood receipt evidence must not claim release approval, approval, execution, source mutation, workflow, memory promotion, policy satisfaction, private reasoning, or authority transfer.', 1;
    END;

    IF EXISTS
    (
        SELECT 1
        FROM inserted i
        WHERE LOWER(i.EvidenceJson) LIKE N'%rawprompt%'
           OR LOWER(i.EvidenceJson) LIKE N'%raw prompt%'
           OR LOWER(i.EvidenceJson) LIKE N'%rawcompletion%'
           OR LOWER(i.EvidenceJson) LIKE N'%raw completion%'
           OR LOWER(i.EvidenceJson) LIKE N'%chainofthought%'
           OR LOWER(i.EvidenceJson) LIKE N'%chain-of-thought%'
           OR LOWER(i.EvidenceJson) LIKE N'%chain of thought%'
           OR LOWER(i.EvidenceJson) LIKE N'%scratchpad%'
           OR LOWER(i.EvidenceJson) LIKE N'%privatereasoning%'
           OR LOWER(i.EvidenceJson) LIKE N'%private reasoning%'
           OR LOWER(i.EvidenceJson) LIKE N'%hiddenreasoning%'
           OR LOWER(i.EvidenceJson) LIKE N'%hidden reasoning%'
           OR LOWER(i.EvidenceJson) LIKE N'%system prompt%'
           OR LOWER(i.EvidenceJson) LIKE N'%developer prompt%'
           OR LOWER(i.EvidenceJson) LIKE N'%rawtooloutput%'
           OR LOWER(i.EvidenceJson) LIKE N'%raw tool output%'
           OR LOWER(i.EvidenceJson) LIKE N'%entirepatch%'
           OR LOWER(i.EvidenceJson) LIKE N'%entire patch%'
    )
    BEGIN
        THROW 52595, 'Dogfood receipt evidence must not contain hidden/private reasoning, raw tool output, or entire patch markers.', 1;
    END;
END;
GO

IF OBJECT_ID(N'governance.TR_DogfoodReceipt_BlockUpdateDelete', N'TR') IS NOT NULL
BEGIN
    DROP TRIGGER governance.TR_DogfoodReceipt_BlockUpdateDelete;
END;
GO

CREATE TRIGGER governance.TR_DogfoodReceipt_BlockUpdateDelete
ON governance.DogfoodReceipt
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 52596, 'Dogfood receipts are append-only.', 1;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_DogfoodReceipt_Record
    @DogfoodReceiptId UNIQUEIDENTIFIER,
    @ProjectId UNIQUEIDENTIFIER,
    @GovernanceEventId UNIQUEIDENTIFIER,
    @ReceiptType NVARCHAR(120),
    @SubjectType NVARCHAR(120),
    @SubjectId NVARCHAR(200),
    @Outcome NVARCHAR(40),
    @SummaryCode NVARCHAR(160),
    @Summary NVARCHAR(1000) = NULL,
    @RecordedByActorType NVARCHAR(80),
    @RecordedByActorId NVARCHAR(200),
    @RelatedToolRequestId UNIQUEIDENTIFIER = NULL,
    @RelatedToolGateDecisionId UNIQUEIDENTIFIER = NULL,
    @RelatedApprovalDecisionId UNIQUEIDENTIFIER = NULL,
    @RelatedPolicyDecisionEventId UNIQUEIDENTIFIER = NULL,
    @CorrelationId UNIQUEIDENTIFIER = NULL,
    @CausationId UNIQUEIDENTIFIER = NULL,
    @EvidenceVersion INT,
    @EvidenceJson NVARCHAR(MAX),
    @GovernanceEventPayloadJson NVARCHAR(MAX),
    @CreatedUtc DATETIMEOFFSET(7) = NULL
AS
BEGIN
    SET XACT_ABORT ON;
    SET NOCOUNT ON;

    IF @RelatedToolRequestId IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM governance.ToolRequest WHERE ToolRequestId = @RelatedToolRequestId AND ProjectId = @ProjectId)
    BEGIN
        THROW 52510, 'Related tool request does not exist for this project.', 1;
    END;

    IF @RelatedToolGateDecisionId IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM governance.ToolGateDecision WHERE ToolGateDecisionId = @RelatedToolGateDecisionId AND ProjectId = @ProjectId)
    BEGIN
        THROW 52511, 'Related tool gate decision does not exist for this project.', 1;
    END;

    IF @RelatedApprovalDecisionId IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM governance.ApprovalDecision WHERE ApprovalDecisionId = @RelatedApprovalDecisionId AND ProjectId = @ProjectId)
    BEGIN
        THROW 52512, 'Related approval decision does not exist for this project.', 1;
    END;

    IF @RelatedPolicyDecisionEventId IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM governance.PolicyDecisionEvent WHERE PolicyDecisionEventId = @RelatedPolicyDecisionEventId AND ProjectId = @ProjectId)
    BEGIN
        THROW 52513, 'Related policy decision does not exist for this project.', 1;
    END;

    DECLARE @EffectiveCorrelationId UNIQUEIDENTIFIER = COALESCE(@CorrelationId, @DogfoodReceiptId);
    DECLARE @EffectiveCreatedUtc DATETIMEOFFSET(7) = COALESCE(@CreatedUtc, SYSUTCDATETIME());

    BEGIN TRY
        BEGIN TRANSACTION;

        IF EXISTS (SELECT 1 FROM governance.DogfoodReceipt WHERE DogfoodReceiptId = @DogfoodReceiptId)
        BEGIN
            THROW 52514, 'Dogfood receipt already exists.', 1;
        END;

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
            N'dogfood.receipt.recorded',
            @RecordedByActorType,
            @RecordedByActorId,
            @EffectiveCorrelationId,
            @CausationId,
            N'dogfood_receipt',
            CONVERT(NVARCHAR(36), @DogfoodReceiptId),
            1,
            @GovernanceEventPayloadJson,
            @EffectiveCreatedUtc
        );

        INSERT INTO governance.DogfoodReceipt
        (
            DogfoodReceiptId,
            ProjectId,
            GovernanceEventId,
            ReceiptType,
            SubjectType,
            SubjectId,
            Outcome,
            SummaryCode,
            Summary,
            RecordedByActorType,
            RecordedByActorId,
            RelatedToolRequestId,
            RelatedToolGateDecisionId,
            RelatedApprovalDecisionId,
            RelatedPolicyDecisionEventId,
            CorrelationId,
            CausationId,
            EvidenceVersion,
            EvidenceJson,
            CreatedUtc
        )
        VALUES
        (
            @DogfoodReceiptId,
            @ProjectId,
            @GovernanceEventId,
            @ReceiptType,
            @SubjectType,
            @SubjectId,
            @Outcome,
            @SummaryCode,
            @Summary,
            @RecordedByActorType,
            @RecordedByActorId,
            @RelatedToolRequestId,
            @RelatedToolGateDecisionId,
            @RelatedApprovalDecisionId,
            @RelatedPolicyDecisionEventId,
            @EffectiveCorrelationId,
            @CausationId,
            @EvidenceVersion,
            @EvidenceJson,
            @EffectiveCreatedUtc
        );

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;

    EXEC governance.usp_DogfoodReceipt_GetById @DogfoodReceiptId = @DogfoodReceiptId;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_DogfoodReceipt_GetById
    @DogfoodReceiptId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        DogfoodReceiptId,
        ProjectId,
        GovernanceEventId,
        ReceiptType,
        SubjectType,
        SubjectId,
        Outcome,
        SummaryCode,
        Summary,
        RecordedByActorType,
        RecordedByActorId,
        RelatedToolRequestId,
        RelatedToolGateDecisionId,
        RelatedApprovalDecisionId,
        RelatedPolicyDecisionEventId,
        CorrelationId,
        CausationId,
        EvidenceVersion,
        EvidenceJson,
        CreatedUtc
    FROM governance.DogfoodReceipt
    WHERE DogfoodReceiptId = @DogfoodReceiptId;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_DogfoodReceipt_ListForSubject
    @ProjectId UNIQUEIDENTIFIER,
    @ReceiptType NVARCHAR(120),
    @SubjectType NVARCHAR(120),
    @SubjectId NVARCHAR(200),
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake)
        DogfoodReceiptId,
        ProjectId,
        GovernanceEventId,
        ReceiptType,
        SubjectType,
        SubjectId,
        Outcome,
        SummaryCode,
        RecordedByActorType,
        RecordedByActorId,
        CorrelationId,
        CausationId,
        CreatedUtc
    FROM governance.DogfoodReceipt
    WHERE ProjectId = @ProjectId
      AND ReceiptType = @ReceiptType
      AND SubjectType = @SubjectType
      AND SubjectId = @SubjectId
    ORDER BY CreatedUtc DESC, DogfoodReceiptId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_DogfoodReceipt_ListForProject
    @ProjectId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake)
        DogfoodReceiptId,
        ProjectId,
        GovernanceEventId,
        ReceiptType,
        SubjectType,
        SubjectId,
        Outcome,
        SummaryCode,
        RecordedByActorType,
        RecordedByActorId,
        CorrelationId,
        CausationId,
        CreatedUtc
    FROM governance.DogfoodReceipt
    WHERE ProjectId = @ProjectId
    ORDER BY CreatedUtc DESC, DogfoodReceiptId DESC;
END;
GO

CREATE OR ALTER PROCEDURE governance.usp_DogfoodReceipt_ListForCorrelation
    @ProjectId UNIQUEIDENTIFIER,
    @CorrelationId UNIQUEIDENTIFIER,
    @Take INT = 100
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @EffectiveTake INT = CASE WHEN @Take IS NULL OR @Take <= 0 THEN 100 WHEN @Take > 500 THEN 500 ELSE @Take END;

    SELECT TOP (@EffectiveTake)
        DogfoodReceiptId,
        ProjectId,
        GovernanceEventId,
        ReceiptType,
        SubjectType,
        SubjectId,
        Outcome,
        SummaryCode,
        RecordedByActorType,
        RecordedByActorId,
        CorrelationId,
        CausationId,
        CreatedUtc
    FROM governance.DogfoodReceipt
    WHERE ProjectId = @ProjectId
      AND CorrelationId = @CorrelationId
    ORDER BY CreatedUtc DESC, DogfoodReceiptId DESC;
END;
GO

IF DATABASE_PRINCIPAL_ID(N'IronDevGovernanceEventRuntimeRole') IS NOT NULL
BEGIN
    GRANT EXECUTE ON OBJECT::governance.usp_DogfoodReceipt_Record TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_DogfoodReceipt_GetById TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_DogfoodReceipt_ListForSubject TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_DogfoodReceipt_ListForProject TO IronDevGovernanceEventRuntimeRole;
    GRANT EXECUTE ON OBJECT::governance.usp_DogfoodReceipt_ListForCorrelation TO IronDevGovernanceEventRuntimeRole;
    GRANT SELECT ON OBJECT::governance.DogfoodReceipt TO IronDevGovernanceEventRuntimeRole;
    DENY INSERT, UPDATE, DELETE ON OBJECT::governance.DogfoodReceipt TO IronDevGovernanceEventRuntimeRole;
    DENY ALTER ON SCHEMA::governance TO IronDevGovernanceEventRuntimeRole;
END;
GO
