USE [IronDeveloper];
GO

IF OBJECT_ID('dbo.ProjectContextDocuments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectContextDocuments
    (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_ProjectContextDocuments_Tenant DEFAULT 1,
        ProjectId INT NOT NULL,
        DocumentType NVARCHAR(100) NOT NULL,
        AuthorityLevel NVARCHAR(50) NOT NULL,
        Status NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectContextDocuments_Status DEFAULT 'Active',
        Title NVARCHAR(200) NOT NULL,
        Content NVARCHAR(MAX) NOT NULL,
        Summary NVARCHAR(MAX) NULL,
        Tags NVARCHAR(MAX) NULL,
        AppliesToCapability NVARCHAR(200) NULL,
        AppliesToArea NVARCHAR(200) NULL,
        Source NVARCHAR(200) NULL,
        SupersedesDocumentId BIGINT NULL,
        SourceChatMessageId BIGINT NULL,
        CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectContextDocuments_CreatedDate DEFAULT SYSUTCDATETIME(),
        UpdatedDate DATETIME2 NULL,
        CONSTRAINT FK_ProjectContextDocuments_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ProjectContextDocuments_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id),
        CONSTRAINT FK_ProjectContextDocuments_ChatMessages FOREIGN KEY (SourceChatMessageId) REFERENCES dbo.ChatMessages(Id),
        CONSTRAINT FK_ProjectContextDocuments_Supersedes FOREIGN KEY (SupersedesDocumentId) REFERENCES dbo.ProjectContextDocuments(Id)
    );

    CREATE INDEX IX_ProjectContextDocuments_Project_Type_Authority
        ON dbo.ProjectContextDocuments(ProjectId, DocumentType, AuthorityLevel, Status, CreatedDate DESC);
END
GO

IF OBJECT_ID('dbo.ProjectObservableStates', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectObservableStates
    (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_ProjectObservableStates_Tenant DEFAULT 1,
        ProjectId INT NOT NULL,
        ActiveCapability NVARCHAR(200) NULL,
        ActiveMilestone NVARCHAR(200) NULL,
        CurrentFocus NVARCHAR(500) NULL,
        BuildReadiness NVARCHAR(100) NULL,
        IndexStatus NVARCHAR(100) NULL,
        BuilderMode NVARCHAR(100) NULL,
        OpenBlockers NVARCHAR(MAX) NULL,
        LastRecommendation NVARCHAR(MAX) NULL,
        CurrentTargetPath NVARCHAR(1000) NULL,
        KnownCurrentGaps NVARCHAR(MAX) NULL,
        SnapshotJson NVARCHAR(MAX) NULL,
        UpdatedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectObservableStates_UpdatedDate DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ProjectObservableStates_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ProjectObservableStates_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
    );

    CREATE UNIQUE INDEX UX_ProjectObservableStates_Tenant_Project
        ON dbo.ProjectObservableStates(TenantId, ProjectId);
END
GO

IF COL_LENGTH('dbo.ProjectTickets', 'SourceChatSessionId') IS NULL
BEGIN
    ALTER TABLE dbo.ProjectTickets ADD SourceChatSessionId BIGINT NULL;
END
GO

IF COL_LENGTH('dbo.ProjectTickets', 'SourceChatMessageId') IS NULL
BEGIN
    ALTER TABLE dbo.ProjectTickets ADD SourceChatMessageId BIGINT NULL;
END
GO

IF OBJECT_ID('dbo.ArtifactSourceReferences', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ArtifactSourceReferences
    (
        ArtifactSourceReferenceId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        ArtifactType NVARCHAR(100) NOT NULL,
        ArtifactId BIGINT NOT NULL,
        SourceType NVARCHAR(100) NOT NULL,
        SourceId BIGINT NULL,
        SourcePath NVARCHAR(1000) NULL,
        SourceSymbol NVARCHAR(500) NULL,
        SourceSection NVARCHAR(500) NULL,
        SourceAnchor NVARCHAR(500) NULL,
        ReferenceType NVARCHAR(100) NOT NULL,
        Summary NVARCHAR(MAX) NULL,
        RelevanceScore DECIMAL(9,4) NULL,
        IsRequired BIT NOT NULL CONSTRAINT DF_ArtifactSourceReferences_IsRequired DEFAULT 0,
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_ArtifactSourceReferences_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy NVARCHAR(200) NULL,
        CONSTRAINT FK_ArtifactSourceReferences_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ArtifactSourceReferences_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
    );

    CREATE INDEX IX_ArtifactSourceReferences_Artifact
        ON dbo.ArtifactSourceReferences(TenantId, ProjectId, ArtifactType, ArtifactId);

    CREATE INDEX IX_ArtifactSourceReferences_Source
        ON dbo.ArtifactSourceReferences(TenantId, ProjectId, SourceType, SourceId);
END
GO

DECLARE @BookSellerProjectId INT =
(
    SELECT TOP (1) Id
    FROM dbo.Projects
    WHERE Name IN ('BookSeller', 'Bookcase')
       OR LocalPath LIKE '%BookSeller%'
       OR LocalPath LIKE '%Bookcase%'
    ORDER BY Id
);

IF @BookSellerProjectId IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM dbo.ProjectContextDocuments
        WHERE ProjectId = @BookSellerProjectId
          AND Title = 'BookSeller currently uses in-memory storage')
    BEGIN
        INSERT INTO dbo.ProjectContextDocuments
            (TenantId, ProjectId, DocumentType, AuthorityLevel, Status, Title, Content, Summary, Tags, AppliesToCapability, AppliesToArea, Source)
        SELECT TenantId, Id, 'ProjectFact', 'ObservedFact', 'Active',
               'BookSeller currently uses in-memory storage',
               'BookSeller currently stores book data in memory. This is an observed project fact and should be rechecked after persistence work lands.',
               'BookSeller currently stores book data in memory.',
               'books,persistence,sandbox',
               'Book persistence',
               'Persistence',
               'Manual seed'
        FROM dbo.Projects
        WHERE Id = @BookSellerProjectId;
    END

    IF NOT EXISTS (
        SELECT 1 FROM dbo.ProjectContextDocuments
        WHERE ProjectId = @BookSellerProjectId
          AND Title = 'SQLite plus Dapper is recommended for BookSeller persistence')
    BEGIN
        INSERT INTO dbo.ProjectContextDocuments
            (TenantId, ProjectId, DocumentType, AuthorityLevel, Status, Title, Content, Summary, Tags, AppliesToCapability, AppliesToArea, Source)
        SELECT TenantId, Id, 'Recommendation', 'Pending', 'Pending',
               'SQLite plus Dapper is recommended for BookSeller persistence',
               'SQLite plus Dapper is the current recommendation for BookSeller persistence because it is lightweight, local, easy to reset in a sandbox, and consistent with IronDev''s Dapper-oriented style. This is not binding until promoted to an ArchitectureDecision.',
               'SQLite plus Dapper is recommended, but not yet binding.',
               'sqlite,dapper,persistence,sandbox',
               'Book persistence',
               'Persistence',
               'Manual seed'
        FROM dbo.Projects
        WHERE Id = @BookSellerProjectId;
    END

    IF NOT EXISTS (
        SELECT 1 FROM dbo.ProjectContextDocuments
        WHERE ProjectId = @BookSellerProjectId
          AND Title = 'BookSeller persistence choice is still open')
    BEGIN
        INSERT INTO dbo.ProjectContextDocuments
            (TenantId, ProjectId, DocumentType, AuthorityLevel, Status, Title, Content, Summary, Tags, AppliesToCapability, AppliesToArea, Source)
        SELECT TenantId, Id, 'OpenQuestion', 'Pending', 'Pending',
               'BookSeller persistence choice is still open',
               'BookSeller needs an accepted persistence decision before Builder should implement durable book storage. Current recommendation is SQLite plus Dapper, but the user must accept or revise it.',
               'Persistence implementation should wait for an accepted decision.',
               'sqlite,dapper,persistence,decision',
               'Book persistence',
               'Persistence',
               'Manual seed'
        FROM dbo.Projects
        WHERE Id = @BookSellerProjectId;
    END

    IF NOT EXISTS (
        SELECT 1 FROM dbo.ProjectObservableStates
        WHERE ProjectId = @BookSellerProjectId)
    BEGIN
        INSERT INTO dbo.ProjectObservableStates
            (TenantId, ProjectId, ActiveCapability, CurrentFocus, BuildReadiness, IndexStatus, BuilderMode, OpenBlockers, LastRecommendation, CurrentTargetPath, KnownCurrentGaps)
        SELECT TenantId, Id,
               'Book persistence',
               'Choose and accept the persistence approach for BookSeller books.',
               'BlockedByOpenQuestion',
               IndexingStatus,
               'Sandbox',
               'Persistence engine and data access style are not accepted yet.',
               'SQLite plus Dapper',
               LocalPath,
               'No durable database persistence has been accepted yet.'
        FROM dbo.Projects
        WHERE Id = @BookSellerProjectId;
    END
END
GO
