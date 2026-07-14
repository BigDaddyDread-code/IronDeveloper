/* CLN-27: durable SQL authority for rebuildable semantic/vector index lifecycle. */

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo') AND name = N'MemoryIndexLifecycleEvents')
BEGIN
    CREATE TABLE dbo.MemoryIndexLifecycleEvents
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        SourceEntityType NVARCHAR(80) NOT NULL,
        SourceEntityId NVARCHAR(200) NOT NULL,
        SourceVersionId NVARCHAR(200) NOT NULL,
        EventType NVARCHAR(40) NOT NULL,
        SourceContentHash CHAR(64) NULL,
        ProviderName NVARCHAR(80) NULL,
        CorrelationId UNIQUEIDENTIFIER NOT NULL,
        ActorUserId INT NULL,
        OccurredAtUtc DATETIME2(7) NOT NULL,
        DetailJson NVARCHAR(MAX) NULL,
        CONSTRAINT PK_MemoryIndexLifecycleEvents PRIMARY KEY (Id),
        CONSTRAINT FK_MemoryIndexLifecycleEvents_Project FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_MemoryIndexLifecycleEvents_Actor FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_MemoryIndexLifecycleEvents_EventType CHECK (EventType IN
        (
            N'SourceCreated', N'SourceUpdated', N'EmbeddingQueued', N'EmbeddingCompleted',
            N'StaleDetected', N'ReindexRequested', N'ReindexCompleted', N'SourceArchived',
            N'DerivedIndexDeleted', N'DerivedIndexRebuilt'
        )),
        CONSTRAINT CK_MemoryIndexLifecycleEvents_SourceHash CHECK
            (SourceContentHash IS NULL OR (LEN(SourceContentHash) = 64 AND SourceContentHash NOT LIKE '%[^0-9A-Fa-f]%')),
        CONSTRAINT CK_MemoryIndexLifecycleEvents_DetailJson CHECK (DetailJson IS NULL OR ISJSON(DetailJson) = 1)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.MemoryIndexLifecycleEvents') AND name = N'IX_MemoryIndexLifecycleEvents_Source')
    CREATE INDEX IX_MemoryIndexLifecycleEvents_Source
        ON dbo.MemoryIndexLifecycleEvents(TenantId, ProjectId, SourceEntityType, SourceEntityId, Id DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.MemoryIndexLifecycleEvents') AND name = N'IX_MemoryIndexLifecycleEvents_Project_Event_Time')
    CREATE INDEX IX_MemoryIndexLifecycleEvents_Project_Event_Time
        ON dbo.MemoryIndexLifecycleEvents(TenantId, ProjectId, EventType, OccurredAtUtc DESC, Id DESC);
GO

CREATE OR ALTER TRIGGER dbo.TR_MemoryIndexLifecycleEvents_BlockUpdateDelete
ON dbo.MemoryIndexLifecycleEvents
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51000, 'Memory index lifecycle events are append-only.', 1;
END;
GO

CREATE OR ALTER PROCEDURE dbo.usp_MemoryIndexLifecycleEvent_Record
    @TenantId INT,
    @ProjectId INT,
    @SourceEntityType NVARCHAR(80),
    @SourceEntityId NVARCHAR(200),
    @SourceVersionId NVARCHAR(200),
    @EventType NVARCHAR(40),
    @SourceContentHash CHAR(64) = NULL,
    @ProviderName NVARCHAR(80) = NULL,
    @CorrelationId UNIQUEIDENTIFIER,
    @ActorUserId INT = NULL,
    @OccurredAtUtc DATETIME2(7),
    @DetailJson NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @TenantId <= 0 OR @ProjectId <= 0
       OR NULLIF(LTRIM(RTRIM(@SourceEntityType)), N'') IS NULL
       OR NULLIF(LTRIM(RTRIM(@SourceEntityId)), N'') IS NULL
       OR NULLIF(LTRIM(RTRIM(@SourceVersionId)), N'') IS NULL
       OR @CorrelationId = '00000000-0000-0000-0000-000000000000'
       OR @OccurredAtUtc > DATEADD(MINUTE, 5, SYSUTCDATETIME())
        THROW 51000, 'Invalid memory index lifecycle event envelope.', 1;

    IF @EventType IN
       (N'EmbeddingQueued', N'EmbeddingCompleted', N'StaleDetected', N'ReindexRequested',
        N'ReindexCompleted', N'DerivedIndexDeleted', N'DerivedIndexRebuilt')
       AND NULLIF(LTRIM(RTRIM(@ProviderName)), N'') IS NULL
        THROW 51000, 'Derived index lifecycle events require a provider.', 1;

    BEGIN TRANSACTION;

    DECLARE @PreviousEvent NVARCHAR(40);
    DECLARE @PreviousSourceVersionId NVARCHAR(200);
    DECLARE @PreviousOccurredAtUtc DATETIME2(7);
    SELECT TOP (1)
        @PreviousEvent = EventType,
        @PreviousSourceVersionId = SourceVersionId,
        @PreviousOccurredAtUtc = OccurredAtUtc
    FROM dbo.MemoryIndexLifecycleEvents WITH (UPDLOCK, HOLDLOCK)
    WHERE TenantId = @TenantId AND ProjectId = @ProjectId
      AND SourceEntityType = @SourceEntityType AND SourceEntityId = @SourceEntityId
    ORDER BY Id DESC;

    IF @PreviousOccurredAtUtc IS NOT NULL AND @OccurredAtUtc < @PreviousOccurredAtUtc
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 51000, 'Memory index lifecycle timestamps must be monotonic.', 1;
    END;

    IF (@EventType = N'SourceUpdated' AND @SourceVersionId = @PreviousSourceVersionId)
       OR (@PreviousEvent IS NOT NULL AND @EventType <> N'SourceUpdated' AND @SourceVersionId <> @PreviousSourceVersionId)
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 51000, 'Memory index lifecycle event does not match the active source version.', 1;
    END;

    IF NOT
    (
        (@EventType = N'SourceCreated' AND @PreviousEvent IS NULL)
        OR (@EventType = N'SourceUpdated' AND @PreviousEvent IN (N'SourceCreated', N'EmbeddingCompleted', N'ReindexCompleted', N'DerivedIndexRebuilt'))
        OR (@EventType = N'EmbeddingQueued' AND @PreviousEvent IN (N'SourceCreated', N'SourceUpdated', N'ReindexRequested'))
        OR (@EventType = N'EmbeddingCompleted' AND @PreviousEvent = N'EmbeddingQueued')
        OR (@EventType = N'StaleDetected' AND @PreviousEvent IN (N'EmbeddingCompleted', N'ReindexCompleted', N'DerivedIndexRebuilt'))
        OR (@EventType = N'ReindexRequested' AND @PreviousEvent IN (N'SourceUpdated', N'EmbeddingCompleted', N'StaleDetected'))
        OR (@EventType = N'ReindexCompleted' AND @PreviousEvent = N'EmbeddingCompleted')
        OR (@EventType = N'SourceArchived' AND @PreviousEvent IN
            (N'SourceCreated', N'SourceUpdated', N'EmbeddingQueued', N'EmbeddingCompleted',
             N'StaleDetected', N'ReindexRequested', N'ReindexCompleted', N'DerivedIndexRebuilt'))
        OR (@EventType = N'DerivedIndexDeleted' AND @PreviousEvent = N'SourceArchived')
        OR (@EventType = N'DerivedIndexRebuilt' AND @PreviousEvent = N'ReindexCompleted')
    )
    BEGIN
        ROLLBACK TRANSACTION;
        THROW 51000, 'Invalid memory index lifecycle transition.', 1;
    END;

    IF @EventType = N'ReindexCompleted'
    BEGIN
        DECLARE @LatestReindexRequestId BIGINT;
        DECLARE @LatestReindexCompletionId BIGINT;
        SELECT @LatestReindexRequestId = MAX(CASE WHEN EventType = N'ReindexRequested' THEN Id END),
               @LatestReindexCompletionId = MAX(CASE WHEN EventType = N'ReindexCompleted' THEN Id END)
        FROM dbo.MemoryIndexLifecycleEvents
        WHERE TenantId = @TenantId AND ProjectId = @ProjectId
          AND SourceEntityType = @SourceEntityType AND SourceEntityId = @SourceEntityId
          AND SourceVersionId = @SourceVersionId;

        IF @LatestReindexRequestId IS NULL OR @LatestReindexRequestId <= ISNULL(@LatestReindexCompletionId, 0)
        BEGIN
            ROLLBACK TRANSACTION;
            THROW 51000, 'Reindex completion requires an unmatched reindex request for the active source version.', 1;
        END;
    END;

    INSERT dbo.MemoryIndexLifecycleEvents
    (
        TenantId, ProjectId, SourceEntityType, SourceEntityId, SourceVersionId, EventType,
        SourceContentHash, ProviderName, CorrelationId, ActorUserId, OccurredAtUtc, DetailJson
    )
    VALUES
    (
        @TenantId, @ProjectId, @SourceEntityType, @SourceEntityId, @SourceVersionId, @EventType,
        @SourceContentHash, @ProviderName, @CorrelationId, @ActorUserId, @OccurredAtUtc, @DetailJson
    );
    COMMIT TRANSACTION;
END;
GO

CREATE OR ALTER VIEW dbo.vw_CurrentMemoryIndexLifecycle
AS
    WITH ranked AS
    (
        SELECT *, ROW_NUMBER() OVER
        (
            PARTITION BY TenantId, ProjectId, SourceEntityType, SourceEntityId ORDER BY Id DESC
        ) AS Position
        FROM dbo.MemoryIndexLifecycleEvents
    )
    SELECT Id, TenantId, ProjectId, SourceEntityType, SourceEntityId, SourceVersionId,
           EventType, SourceContentHash, ProviderName, CorrelationId, ActorUserId, OccurredAtUtc, DetailJson
    FROM ranked WHERE Position = 1;
GO
