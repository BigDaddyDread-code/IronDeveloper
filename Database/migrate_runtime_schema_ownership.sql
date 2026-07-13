/* CLN-19: controlled ownership for schema previously created by request-path services. */

IF OBJECT_ID(N'dbo.ProjectContextDocuments', N'U') IS NULL
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
END;
GO

IF OBJECT_ID(N'dbo.ArtifactSourceReferences', N'U') IS NULL
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
END;
GO

IF COL_LENGTH(N'dbo.ArtifactSourceReferences', N'SourcePath') IS NULL
    ALTER TABLE dbo.ArtifactSourceReferences ADD SourcePath NVARCHAR(1000) NULL;
IF COL_LENGTH(N'dbo.ArtifactSourceReferences', N'SourceSymbol') IS NULL
    ALTER TABLE dbo.ArtifactSourceReferences ADD SourceSymbol NVARCHAR(500) NULL;
IF COL_LENGTH(N'dbo.ArtifactSourceReferences', N'SourceSection') IS NULL
    ALTER TABLE dbo.ArtifactSourceReferences ADD SourceSection NVARCHAR(500) NULL;
IF COL_LENGTH(N'dbo.ArtifactSourceReferences', N'SourceAnchor') IS NULL
    ALTER TABLE dbo.ArtifactSourceReferences ADD SourceAnchor NVARCHAR(500) NULL;
IF COL_LENGTH(N'dbo.ArtifactSourceReferences', N'ReferenceType') IS NULL
    ALTER TABLE dbo.ArtifactSourceReferences ADD ReferenceType NVARCHAR(100) NOT NULL
        CONSTRAINT DF_ArtifactSourceReferences_ReferenceType DEFAULT 'References';
IF COL_LENGTH(N'dbo.ArtifactSourceReferences', N'Summary') IS NULL
    ALTER TABLE dbo.ArtifactSourceReferences ADD Summary NVARCHAR(MAX) NULL;
IF COL_LENGTH(N'dbo.ArtifactSourceReferences', N'RelevanceScore') IS NULL
    ALTER TABLE dbo.ArtifactSourceReferences ADD RelevanceScore DECIMAL(9,4) NULL;
IF COL_LENGTH(N'dbo.ArtifactSourceReferences', N'IsRequired') IS NULL
    ALTER TABLE dbo.ArtifactSourceReferences ADD IsRequired BIT NOT NULL
        CONSTRAINT DF_ArtifactSourceReferences_IsRequired DEFAULT 0;
IF COL_LENGTH(N'dbo.ArtifactSourceReferences', N'CreatedBy') IS NULL
    ALTER TABLE dbo.ArtifactSourceReferences ADD CreatedBy NVARCHAR(200) NULL;
GO

IF COL_LENGTH(N'dbo.ProjectTickets', N'SourceChatSessionId') IS NULL
    ALTER TABLE dbo.ProjectTickets ADD SourceChatSessionId BIGINT NULL;
IF COL_LENGTH(N'dbo.ProjectTickets', N'SourceChatMessageId') IS NULL
    ALTER TABLE dbo.ProjectTickets ADD SourceChatMessageId BIGINT NULL;
IF COL_LENGTH(N'dbo.ProjectTickets', N'SourceDocumentVersionId') IS NULL
    ALTER TABLE dbo.ProjectTickets ADD SourceDocumentVersionId BIGINT NULL;
IF COL_LENGTH(N'dbo.ProjectTickets', N'BlockedByTicketIds') IS NULL
    ALTER TABLE dbo.ProjectTickets ADD BlockedByTicketIds NVARCHAR(MAX) NULL;
IF COL_LENGTH(N'dbo.ProjectTickets', N'Revision') IS NULL
    ALTER TABLE dbo.ProjectTickets ADD Revision BIGINT NOT NULL CONSTRAINT DF_ProjectTickets_Revision DEFAULT 1;
GO

IF OBJECT_ID(N'dbo.Runs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Runs
    (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RunId NVARCHAR(100) NOT NULL,
        ProjectId INT NULL,
        TicketId BIGINT NULL,
        State NVARCHAR(50) NOT NULL,
        IsDisposable BIT NOT NULL CONSTRAINT DF_Runs_IsDisposable DEFAULT 0,
        Summary NVARCHAR(MAX) NOT NULL CONSTRAINT DF_Runs_Summary DEFAULT '',
        FailureReason NVARCHAR(MAX) NULL,
        WorkspacePath NVARCHAR(1000) NULL,
        CreatedUtc DATETIME2 NOT NULL,
        UpdatedUtc DATETIME2 NOT NULL,
        StartedUtc DATETIME2 NULL,
        CompletedUtc DATETIME2 NULL
    );

    CREATE UNIQUE INDEX UX_Runs_RunId ON dbo.Runs(RunId);
    CREATE INDEX IX_Runs_ProjectTicketUpdated ON dbo.Runs(ProjectId, TicketId, UpdatedUtc DESC);
END;
GO

IF OBJECT_ID(N'dbo.RunEvents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RunEvents
    (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        EventId UNIQUEIDENTIFIER NOT NULL,
        RunId NVARCHAR(100) NOT NULL,
        TimestampUtc DATETIME2 NOT NULL,
        EventType NVARCHAR(100) NOT NULL,
        Message NVARCHAR(MAX) NOT NULL,
        PayloadJson NVARCHAR(MAX) NULL,
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_RunEvents_CreatedUtc DEFAULT SYSUTCDATETIME()
    );

    CREATE UNIQUE INDEX UX_RunEvents_EventId ON dbo.RunEvents(EventId);
    CREATE INDEX IX_RunEvents_RunId_Timestamp ON dbo.RunEvents(RunId, TimestampUtc, Id);
END;
GO

IF OBJECT_ID(N'dbo.SemanticArtefacts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SemanticArtefacts
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        TenantId INT NULL,
        ProjectId INT NOT NULL,
        SourceEntityType NVARCHAR(100) NOT NULL,
        SourceEntityId NVARCHAR(100) NOT NULL,
        SourceVersionId NVARCHAR(100) NULL,
        ArtefactType NVARCHAR(100) NOT NULL,
        AuthorityLevel NVARCHAR(50) NOT NULL,
        Title NVARCHAR(500) NOT NULL,
        Summary NVARCHAR(MAX) NULL,
        ContentHash NVARCHAR(128) NOT NULL,
        IsStale BIT NOT NULL CONSTRAINT DF_SemanticArtefacts_IsStale DEFAULT 0,
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_SemanticArtefacts_CreatedUtc DEFAULT SYSUTCDATETIME(),
        UpdatedUtc DATETIME2 NOT NULL CONSTRAINT DF_SemanticArtefacts_UpdatedUtc DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_SemanticArtefacts_Project_Source
        ON dbo.SemanticArtefacts(ProjectId, SourceEntityType, SourceEntityId, SourceVersionId);
END;
GO

IF OBJECT_ID(N'dbo.SemanticChunks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SemanticChunks
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        ArtefactId UNIQUEIDENTIFIER NOT NULL,
        ProjectId INT NOT NULL,
        ChunkIndex INT NOT NULL,
        ChunkText NVARCHAR(MAX) NOT NULL,
        TokenEstimate INT NULL,
        ContentHash NVARCHAR(128) NOT NULL,
        WeaviateObjectId NVARCHAR(100) NULL,
        EmbeddedAtUtc DATETIME2 NULL,
        EmbeddingModel NVARCHAR(200) NULL,
        IsStale BIT NOT NULL CONSTRAINT DF_SemanticChunks_IsStale DEFAULT 0,
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_SemanticChunks_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_SemanticChunks_SemanticArtefacts FOREIGN KEY (ArtefactId) REFERENCES dbo.SemanticArtefacts(Id)
    );
    CREATE INDEX IX_SemanticChunks_Project_Artefact
        ON dbo.SemanticChunks(ProjectId, ArtefactId, IsStale);
END;
GO

IF OBJECT_ID(N'dbo.EmbeddingJobs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EmbeddingJobs
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        TenantId INT NULL,
        ProjectId INT NOT NULL,
        SourceEntityType NVARCHAR(100) NOT NULL,
        SourceEntityId NVARCHAR(100) NOT NULL,
        SourceVersionId NVARCHAR(100) NULL,
        JobType NVARCHAR(50) NOT NULL,
        Status NVARCHAR(50) NOT NULL,
        Attempts INT NOT NULL CONSTRAINT DF_EmbeddingJobs_Attempts DEFAULT 0,
        LastError NVARCHAR(MAX) NULL,
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_EmbeddingJobs_CreatedUtc DEFAULT SYSUTCDATETIME(),
        StartedUtc DATETIME2 NULL,
        CompletedUtc DATETIME2 NULL
    );
    CREATE INDEX IX_EmbeddingJobs_Status ON dbo.EmbeddingJobs(Status, CreatedUtc);
END;
GO

/* Search traces are derived diagnostics. Replace only the unsupported pre-GUID shape. */
IF OBJECT_ID(N'dbo.SemanticSearchTraces', N'U') IS NOT NULL
   AND EXISTS
   (
       SELECT 1
       FROM INFORMATION_SCHEMA.COLUMNS
       WHERE TABLE_SCHEMA = N'dbo'
         AND TABLE_NAME = N'SemanticSearchTraces'
         AND COLUMN_NAME = N'Id'
         AND DATA_TYPE <> N'uniqueidentifier'
   )
BEGIN
    IF OBJECT_ID(N'dbo.SemanticSearchTraceResults', N'U') IS NOT NULL
        DROP TABLE dbo.SemanticSearchTraceResults;
    DROP TABLE dbo.SemanticSearchTraces;
END;
GO

IF OBJECT_ID(N'dbo.SemanticSearchTraces', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SemanticSearchTraces
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        ProjectId INT NOT NULL,
        QueryText NVARCHAR(MAX) NOT NULL,
        Consumer NVARCHAR(100) NOT NULL,
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_SemanticSearchTraces_CreatedUtc DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'dbo.SemanticSearchTraceResults', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SemanticSearchTraceResults
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        SearchTraceId UNIQUEIDENTIFIER NOT NULL,
        ArtefactId UNIQUEIDENTIFIER NOT NULL,
        ChunkId UNIQUEIDENTIFIER NOT NULL,
        VectorSimilarity FLOAT NOT NULL,
        FinalScore FLOAT NOT NULL,
        AuthorityBoost FLOAT NOT NULL,
        RecencyBoost FLOAT NOT NULL,
        SourceTypeBoost FLOAT NOT NULL,
        ExplicitLinkBoost FLOAT NOT NULL,
        StalePenalty FLOAT NOT NULL,
        MatchReason NVARCHAR(MAX) NULL,
        CONSTRAINT FK_SemanticSearchTraceResults_SemanticSearchTraces
            FOREIGN KEY (SearchTraceId) REFERENCES dbo.SemanticSearchTraces(Id)
    );
END;
GO

IF OBJECT_ID(N'dbo.SemanticEmbeddings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SemanticEmbeddings
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        ProjectId INT NOT NULL,
        ArtefactId UNIQUEIDENTIFIER NOT NULL,
        ArtefactType NVARCHAR(100) NOT NULL,
        DocumentId BIGINT NOT NULL,
        SourceDocumentVersionId INT NULL,
        ContentHash NVARCHAR(128) NOT NULL,
        ModelVersion NVARCHAR(100) NOT NULL,
        VectorDimensions INT NOT NULL,
        VectorData VARBINARY(MAX) NULL,
        Provider NVARCHAR(100) NOT NULL,
        CollectionName NVARCHAR(255) NULL,
        WeaviateObjectId UNIQUEIDENTIFIER NULL,
        EmbeddedAtUtc DATETIME2 NOT NULL,
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_SemanticEmbeddings_CreatedUtc DEFAULT SYSUTCDATETIME(),
        UpdatedUtc DATETIME2 NULL
    );
    CREATE UNIQUE INDEX UX_SemanticEmbeddings_ArtefactId ON dbo.SemanticEmbeddings(ArtefactId);
    CREATE INDEX IX_SemanticEmbeddings_ProjectId ON dbo.SemanticEmbeddings(ProjectId, ArtefactType);
END;
GO

IF OBJECT_ID(N'dbo.SemanticIndexRuns', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SemanticIndexRuns
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ProjectId INT NOT NULL,
        StartedAtUtc DATETIME2 NOT NULL,
        CompletedAtUtc DATETIME2 NULL,
        Status NVARCHAR(50) NOT NULL,
        TotalDocuments INT NOT NULL,
        ProcessedDocuments INT NOT NULL,
        ErrorMessage NVARCHAR(MAX) NULL
    );
END;
GO
