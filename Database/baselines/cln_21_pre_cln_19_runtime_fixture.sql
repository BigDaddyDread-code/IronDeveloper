/*
  CLN-21 supported upgrade fixture.

  Starting schema: main commit 7f0e1058, immediately before CLN-19 moved
  request-path schema ownership into Database/migrate_runtime_schema_ownership.sql.

  This test-only fixture recreates the exact runtime-owned table shapes and
  compatibility columns from that baseline, then inserts representative rows
  whose identities and payloads must survive the current migration manifest.
*/

IF DB_NAME() NOT LIKE N'%Test%'
    THROW 51000, N'Refusing CLN-21 baseline fixture outside a test database.', 1;
GO

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
    CREATE INDEX IX_SemanticChunks_Project_Artefact ON dbo.SemanticChunks(ProjectId, ArtefactId, IsStale);
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

SET IDENTITY_INSERT dbo.Tenants ON;
INSERT dbo.Tenants (Id, Name, Slug, IsActive, CreatedDate)
VALUES (2101, N'CLN-21 Upgrade Tenant', N'cln-21-upgrade', 1, '2026-07-01T01:02:03');
SET IDENTITY_INSERT dbo.Tenants OFF;

SET IDENTITY_INSERT dbo.Projects ON;
INSERT dbo.Projects (Id, TenantId, Name, Description, LocalPath, CreatedDate, LastIndexedUtc, IndexingStatus, IndexedFileCount)
VALUES (2101, 2101, N'CLN-21 Upgrade Project', N'Preservation fixture', N'C:\IronDevUpgradeTest\Project', '2026-07-01T01:03:03', '2026-07-01T01:04:03', N'Ready', 12);
SET IDENTITY_INSERT dbo.Projects OFF;

SET IDENTITY_INSERT dbo.ProjectDocuments ON;
INSERT dbo.ProjectDocuments
    (Id, TenantId, ProjectId, Title, Slug, DocumentType, Status, CreatedAtUtc, CreatedBy)
VALUES
    (210107, 2101, 2101, N'CLN-21 preserved source document', N'cln-21-preserved-source', N'Architecture', N'Active', '2026-07-01T01:04:13', N'cln-21-fixture');
SET IDENTITY_INSERT dbo.ProjectDocuments OFF;

SET IDENTITY_INSERT dbo.ProjectDocumentVersions ON;
INSERT dbo.ProjectDocumentVersions
    (Id, DocumentId, VersionMajor, VersionMinor, ContentMarkdown, ChangeSummary, Status, ContentHash, CreatedAtUtc, CreatedBy)
VALUES
    (210105, 210107, 1, 0, N'# CLN-21 preserved source', N'Preservation fixture', N'Approved', N'cln21-source-hash-210105', '2026-07-01T01:04:23', N'cln-21-fixture');
SET IDENTITY_INSERT dbo.ProjectDocumentVersions OFF;

UPDATE dbo.ProjectDocuments SET CurrentVersionId = 210105 WHERE Id = 210107;

SET IDENTITY_INSERT dbo.ProjectContextDocuments ON;
INSERT dbo.ProjectContextDocuments
    (Id, TenantId, ProjectId, DocumentType, AuthorityLevel, Status, Title, Content, Summary, Tags, AppliesToCapability, AppliesToArea, Source, CreatedDate)
VALUES
    (210101, 2101, 2101, N'Architecture', N'Accepted', N'Active', N'CLN-21 preserved context', N'context-payload-2101', N'preserve exactly', N'["upgrade"]', N'Database', N'Migrations', N'pre-cln-19-runtime', '2026-07-01T01:05:03');
SET IDENTITY_INSERT dbo.ProjectContextDocuments OFF;

SET IDENTITY_INSERT dbo.ProjectTickets ON;
INSERT dbo.ProjectTickets
    (Id, TenantId, ProjectId, Title, TicketType, Priority, Status, Content, SourceDocumentVersionId, BlockedByTicketIds, Revision, CreatedDate)
VALUES
    (210103, 2101, 2101, N'CLN-21 preserved ticket', N'Task', N'High', N'Ready', N'ticket-payload-2101', 210105, N'[210104]', 7, '2026-07-01T01:06:03');
SET IDENTITY_INSERT dbo.ProjectTickets OFF;

SET IDENTITY_INSERT dbo.ArtifactSourceReferences ON;
INSERT dbo.ArtifactSourceReferences
    (ArtifactSourceReferenceId, TenantId, ProjectId, ArtifactType, ArtifactId, SourceType, SourceId, SourcePath, SourceSymbol, SourceSection, SourceAnchor, ReferenceType, Summary, RelevanceScore, IsRequired, CreatedUtc, CreatedBy)
VALUES
    (210102, 2101, 2101, N'Ticket', 210103, N'ProjectContextDocument', 210101, N'Docs/upgrade.md', N'UpgradeProof', N'Preservation', N'cln-21', N'CreatedFrom', N'preserve-source-reference', 0.8750, 1, '2026-07-01T01:07:03', N'cln-21-fixture');
SET IDENTITY_INSERT dbo.ArtifactSourceReferences OFF;

SET IDENTITY_INSERT dbo.Runs ON;
INSERT dbo.Runs
    (Id, RunId, ProjectId, TicketId, State, IsDisposable, Summary, FailureReason, WorkspacePath, CreatedUtc, UpdatedUtc, StartedUtc, CompletedUtc)
VALUES
    (210104, N'cln21-upgrade-run', 2101, 210103, N'Completed', 1, N'preserve-run-summary', NULL, N'C:\IronDevUpgradeTest\Workspace', '2026-07-01T01:08:03', '2026-07-01T01:09:03', '2026-07-01T01:08:13', '2026-07-01T01:09:03');
SET IDENTITY_INSERT dbo.Runs OFF;

SET IDENTITY_INSERT dbo.RunEvents ON;
INSERT dbo.RunEvents (Id, EventId, RunId, TimestampUtc, EventType, Message, PayloadJson, CreatedUtc)
VALUES (210105, '21000000-0000-0000-0000-000000000001', N'cln21-upgrade-run', '2026-07-01T01:08:23', N'UpgradeFixtureRecorded', N'preserve-run-event', N'{"version":1,"value":"preserve"}', '2026-07-01T01:08:23');
SET IDENTITY_INSERT dbo.RunEvents OFF;

INSERT dbo.SemanticArtefacts
    (Id, TenantId, ProjectId, SourceEntityType, SourceEntityId, SourceVersionId, ArtefactType, AuthorityLevel, Title, Summary, ContentHash, IsStale, CreatedUtc, UpdatedUtc)
VALUES
    ('21000000-0000-0000-0000-000000000002', 2101, 2101, N'ProjectContextDocument', N'210101', N'v7', N'Architecture', N'Accepted', N'Preserved semantic artefact', N'preserve-semantic-summary', N'hash-artefact-2101', 0, '2026-07-01T01:10:03', '2026-07-01T01:11:03');

INSERT dbo.SemanticChunks
    (Id, ArtefactId, ProjectId, ChunkIndex, ChunkText, TokenEstimate, ContentHash, WeaviateObjectId, EmbeddedAtUtc, EmbeddingModel, IsStale, CreatedUtc)
VALUES
    ('21000000-0000-0000-0000-000000000003', '21000000-0000-0000-0000-000000000002', 2101, 3, N'preserve-semantic-chunk', 17, N'hash-chunk-2101', N'weaviate-2101', '2026-07-01T01:12:03', N'fixture-model', 0, '2026-07-01T01:11:03');

INSERT dbo.EmbeddingJobs
    (Id, TenantId, ProjectId, SourceEntityType, SourceEntityId, SourceVersionId, JobType, Status, Attempts, LastError, CreatedUtc, StartedUtc, CompletedUtc)
VALUES
    ('21000000-0000-0000-0000-000000000004', 2101, 2101, N'ProjectContextDocument', N'210101', N'v7', N'Upsert', N'Completed', 2, NULL, '2026-07-01T01:12:03', '2026-07-01T01:12:13', '2026-07-01T01:12:23');

INSERT dbo.SemanticSearchTraces (Id, ProjectId, QueryText, Consumer, CreatedUtc)
VALUES ('21000000-0000-0000-0000-000000000005', 2101, N'preserve query', N'CLN21', '2026-07-01T01:13:03');

INSERT dbo.SemanticSearchTraceResults
    (Id, SearchTraceId, ArtefactId, ChunkId, VectorSimilarity, FinalScore, AuthorityBoost, RecencyBoost, SourceTypeBoost, ExplicitLinkBoost, StalePenalty, MatchReason)
VALUES
    ('21000000-0000-0000-0000-000000000006', '21000000-0000-0000-0000-000000000005', '21000000-0000-0000-0000-000000000002', '21000000-0000-0000-0000-000000000003', 0.91, 0.93, 0.10, 0.02, 0.03, 0.04, 0.01, N'preserve-match-reason');

INSERT dbo.SemanticEmbeddings
    (Id, ProjectId, ArtefactId, ArtefactType, DocumentId, SourceDocumentVersionId, ContentHash, ModelVersion, VectorDimensions, VectorData, Provider, CollectionName, WeaviateObjectId, EmbeddedAtUtc, CreatedUtc, UpdatedUtc)
VALUES
    ('21000000-0000-0000-0000-000000000007', 2101, '21000000-0000-0000-0000-000000000002', N'Architecture', 210101, 7, N'hash-embedding-2101', N'fixture-model-v1', 4, 0x01020304, N'Fixture', N'cln21_fixture', '21000000-0000-0000-0000-000000000008', '2026-07-01T01:14:03', '2026-07-01T01:14:03', '2026-07-01T01:15:03');

SET IDENTITY_INSERT dbo.SemanticIndexRuns ON;
INSERT dbo.SemanticIndexRuns (Id, ProjectId, StartedAtUtc, CompletedAtUtc, Status, TotalDocuments, ProcessedDocuments, ErrorMessage)
VALUES (210106, 2101, '2026-07-01T01:16:03', '2026-07-01T01:17:03', N'Completed', 12, 12, NULL);
SET IDENTITY_INSERT dbo.SemanticIndexRuns OFF;
GO

PRINT 'PASS CLN-21 pre-CLN-19 baseline fixture created.';
