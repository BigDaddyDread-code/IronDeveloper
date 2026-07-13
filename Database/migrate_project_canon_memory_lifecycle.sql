/* CLN-25: append-only Project Canon identity, versions, and lifecycle. */

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID(N'memory') AND name = N'ProjectCanonMemory')
BEGIN
    CREATE TABLE memory.ProjectCanonMemory
    (
        StableMemoryId UNIQUEIDENTIFIER NOT NULL,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        Title NVARCHAR(300) NOT NULL,
        CreatedByUserId INT NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        CONSTRAINT PK_ProjectCanonMemory PRIMARY KEY (StableMemoryId),
        CONSTRAINT FK_ProjectCanonMemory_Project FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_ProjectCanonMemory_CreatedBy FOREIGN KEY (CreatedByUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_ProjectCanonMemory_Title CHECK (LEN(LTRIM(RTRIM(Title))) > 0),
        CONSTRAINT UX_ProjectCanonMemory_Scope UNIQUE (StableMemoryId, TenantId, ProjectId)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID(N'memory') AND name = N'ProjectCanonMemoryVersion')
BEGIN
    CREATE TABLE memory.ProjectCanonMemoryVersion
    (
        VersionId UNIQUEIDENTIFIER NOT NULL,
        StableMemoryId UNIQUEIDENTIFIER NOT NULL,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        Content NVARCHAR(MAX) NOT NULL,
        ContentHash CHAR(64) NOT NULL,
        Status NVARCHAR(32) NOT NULL,
        CreatedByUserId INT NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        SourceEvidence NVARCHAR(MAX) NOT NULL,
        SupersedesVersionId UNIQUEIDENTIFIER NULL,
        EffectiveFromUtc DATETIME2(7) NULL,
        RetiredAtUtc DATETIME2(7) NULL,
        PromotionReceiptId UNIQUEIDENTIFIER NOT NULL,
        CONSTRAINT PK_ProjectCanonMemoryVersion PRIMARY KEY (VersionId),
        CONSTRAINT FK_ProjectCanonMemoryVersion_MemoryScope FOREIGN KEY (StableMemoryId, TenantId, ProjectId)
            REFERENCES memory.ProjectCanonMemory(StableMemoryId, TenantId, ProjectId),
        CONSTRAINT FK_ProjectCanonMemoryVersion_SupersedesScope
            FOREIGN KEY (SupersedesVersionId, StableMemoryId, TenantId, ProjectId)
            REFERENCES memory.ProjectCanonMemoryVersion(VersionId, StableMemoryId, TenantId, ProjectId),
        CONSTRAINT FK_ProjectCanonMemoryVersion_CreatedBy FOREIGN KEY (CreatedByUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_ProjectCanonMemoryVersion_Content CHECK (LEN(LTRIM(RTRIM(Content))) > 0),
        CONSTRAINT CK_ProjectCanonMemoryVersion_ContentHash CHECK (LEN(ContentHash) = 64 AND ContentHash NOT LIKE '%[^0-9A-Fa-f]%'),
        CONSTRAINT CK_ProjectCanonMemoryVersion_Status CHECK (Status IN (N'Current', N'Superseded', N'Archived')),
        CONSTRAINT CK_ProjectCanonMemoryVersion_SourceEvidence CHECK (ISJSON(SourceEvidence) = 1),
        CONSTRAINT CK_ProjectCanonMemoryVersion_Lifecycle CHECK
        (
            (Status = N'Current' AND EffectiveFromUtc IS NOT NULL AND RetiredAtUtc IS NULL)
            OR
            (Status IN (N'Superseded', N'Archived') AND SupersedesVersionId IS NOT NULL AND RetiredAtUtc IS NOT NULL)
        ),
        CONSTRAINT UX_ProjectCanonMemoryVersion_Scope UNIQUE (VersionId, StableMemoryId, TenantId, ProjectId)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'memory.ProjectCanonMemoryVersion') AND name = N'IX_ProjectCanonMemoryVersion_History')
    CREATE INDEX IX_ProjectCanonMemoryVersion_History
        ON memory.ProjectCanonMemoryVersion(StableMemoryId, CreatedAtUtc DESC, VersionId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'memory.ProjectCanonMemoryVersion') AND name = N'IX_ProjectCanonMemoryVersion_Supersedes')
    CREATE UNIQUE INDEX IX_ProjectCanonMemoryVersion_Supersedes
        ON memory.ProjectCanonMemoryVersion(SupersedesVersionId) WHERE SupersedesVersionId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'memory.ProjectCanonMemory') AND name = N'IX_ProjectCanonMemory_Project_Title')
    CREATE INDEX IX_ProjectCanonMemory_Project_Title ON memory.ProjectCanonMemory(TenantId, ProjectId, Title);
GO

CREATE OR ALTER VIEW memory.vw_CurrentProjectCanonMemory
AS
    SELECT
        m.StableMemoryId, v.VersionId, m.TenantId, m.ProjectId, m.Title,
        v.Content, v.ContentHash, v.Status, v.CreatedByUserId, v.CreatedAtUtc,
        v.SourceEvidence, v.EffectiveFromUtc, v.PromotionReceiptId
    FROM memory.ProjectCanonMemory m
    INNER JOIN memory.ProjectCanonMemoryVersion v ON v.StableMemoryId = m.StableMemoryId
    WHERE v.Status = N'Current'
      AND NOT EXISTS
      (
          SELECT 1 FROM memory.ProjectCanonMemoryVersion successor
          WHERE successor.SupersedesVersionId = v.VersionId
      );
GO

CREATE OR ALTER TRIGGER memory.TR_ProjectCanonMemory_BlockUpdateDelete
ON memory.ProjectCanonMemory
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51000, 'Project Canon stable identities are append-only.', 1;
END;
GO

CREATE OR ALTER TRIGGER memory.TR_ProjectCanonMemoryVersion_BlockUpdateDelete
ON memory.ProjectCanonMemoryVersion
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51000, 'Project Canon versions are append-only; add a superseding lifecycle version.', 1;
END;
GO

CREATE OR ALTER PROCEDURE memory.usp_ProjectCanonMemory_CreateIdentity
    @StableMemoryId UNIQUEIDENTIFIER,
    @TenantId INT,
    @ProjectId INT,
    @Title NVARCHAR(300),
    @CreatedByUserId INT,
    @CreatedAtUtc DATETIME2(7)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF EXISTS (SELECT 1 FROM memory.ProjectCanonMemory WHERE StableMemoryId = @StableMemoryId)
        THROW 51000, 'StableMemoryId already exists; title-based overwrite is forbidden.', 1;
    INSERT memory.ProjectCanonMemory(StableMemoryId, TenantId, ProjectId, Title, CreatedByUserId, CreatedAtUtc)
    VALUES (@StableMemoryId, @TenantId, @ProjectId, @Title, @CreatedByUserId, @CreatedAtUtc);
END;
GO

CREATE OR ALTER PROCEDURE memory.usp_ProjectCanonMemory_AppendVersion
    @VersionId UNIQUEIDENTIFIER,
    @StableMemoryId UNIQUEIDENTIFIER,
    @TenantId INT,
    @ProjectId INT,
    @Content NVARCHAR(MAX),
    @ContentHash CHAR(64),
    @Status NVARCHAR(32),
    @CreatedByUserId INT,
    @CreatedAtUtc DATETIME2(7),
    @SourceEvidence NVARCHAR(MAX),
    @SupersedesVersionId UNIQUEIDENTIFIER = NULL,
    @EffectiveFromUtc DATETIME2(7) = NULL,
    @RetiredAtUtc DATETIME2(7) = NULL,
    @PromotionReceiptId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        IF NOT EXISTS
        (
            SELECT 1
            FROM memory.ProjectCanonMemory WITH (UPDLOCK, HOLDLOCK)
            WHERE StableMemoryId = @StableMemoryId
              AND TenantId = @TenantId
              AND ProjectId = @ProjectId
        )
            THROW 51000, 'Stable memory identity does not exist in the requested scope.', 1;

        IF EXISTS (SELECT 1 FROM memory.ProjectCanonMemoryVersion WHERE VersionId = @VersionId)
            THROW 51000, 'VersionId already exists.', 1;

        DECLARE @VersionCount INT;
        DECLARE @CurrentLeafCount INT;
        DECLARE @CurrentLeafVersionId UNIQUEIDENTIFIER;

        SELECT @VersionCount = COUNT(*)
        FROM memory.ProjectCanonMemoryVersion WITH (UPDLOCK, HOLDLOCK, INDEX(IX_ProjectCanonMemoryVersion_History))
        WHERE StableMemoryId = @StableMemoryId
          AND TenantId = @TenantId
          AND ProjectId = @ProjectId;

        SELECT
            @CurrentLeafCount = COUNT(*),
            @CurrentLeafVersionId = MIN(v.VersionId)
        FROM memory.ProjectCanonMemoryVersion v WITH (UPDLOCK, HOLDLOCK)
        WHERE v.StableMemoryId = @StableMemoryId
          AND v.TenantId = @TenantId
          AND v.ProjectId = @ProjectId
          AND v.Status = N'Current'
          AND NOT EXISTS
          (
              SELECT 1
              FROM memory.ProjectCanonMemoryVersion successor
              WHERE successor.SupersedesVersionId = v.VersionId
          );

        IF @VersionCount = 0
        BEGIN
            IF @Status <> N'Current' OR @SupersedesVersionId IS NOT NULL
                THROW 51000, 'The first version must be Current and cannot supersede another version.', 1;
        END
        ELSE
        BEGIN
            IF @SupersedesVersionId IS NULL
                THROW 51000, 'A successor must identify the current leaf it supersedes.', 1;
            IF @CurrentLeafCount <> 1 OR @CurrentLeafVersionId <> @SupersedesVersionId
                THROW 51000, 'The supplied predecessor is not the single current leaf.', 1;
            IF @Status NOT IN (N'Current', N'Archived')
                THROW 51000, 'A successor must be Current or Archived.', 1;
        END;

        INSERT memory.ProjectCanonMemoryVersion
        (
            VersionId, StableMemoryId, TenantId, ProjectId, Content, ContentHash, Status,
            CreatedByUserId, CreatedAtUtc, SourceEvidence, SupersedesVersionId,
            EffectiveFromUtc, RetiredAtUtc, PromotionReceiptId
        )
        VALUES
        (
            @VersionId, @StableMemoryId, @TenantId, @ProjectId, @Content, @ContentHash, @Status,
            @CreatedByUserId, @CreatedAtUtc, @SourceEvidence, @SupersedesVersionId,
            @EffectiveFromUtc, @RetiredAtUtc, @PromotionReceiptId
        );

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE memory.usp_ProjectCanonMemory_GetCurrent
    @TenantId INT, @ProjectId INT, @StableMemoryId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM memory.vw_CurrentProjectCanonMemory
    WHERE TenantId = @TenantId AND ProjectId = @ProjectId
      AND (@StableMemoryId IS NULL OR StableMemoryId = @StableMemoryId)
    ORDER BY EffectiveFromUtc DESC, VersionId DESC;
END;
GO

CREATE OR ALTER PROCEDURE memory.usp_ProjectCanonMemory_ListHistory
    @TenantId INT, @ProjectId INT, @StableMemoryId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT m.Title, v.*
    FROM memory.ProjectCanonMemory m
    INNER JOIN memory.ProjectCanonMemoryVersion v ON v.StableMemoryId = m.StableMemoryId
    WHERE m.TenantId = @TenantId AND m.ProjectId = @ProjectId AND m.StableMemoryId = @StableMemoryId
    ORDER BY v.CreatedAtUtc DESC, v.VersionId DESC;
END;
GO
