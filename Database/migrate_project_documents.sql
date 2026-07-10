-- =====================================================================
-- migrate_project_documents.sql
--
-- Adds versioned Markdown project document storage.
-- Safe to rerun. All blocks are idempotent.
--
-- Tables:
--   dbo.ProjectDocuments        - Stable document identity
--   dbo.ProjectDocumentVersions - Immutable Markdown snapshots
--   dbo.ProjectDocumentLinks    - Trace links to other artefacts
-- =====================================================================

-- --------------------------------------------------------------------
-- 1. ProjectDocuments
--    The stable document record. One row per document.
--    CurrentVersionId is a soft pointer (no FK) to avoid circular deps.
-- --------------------------------------------------------------------
IF OBJECT_ID('dbo.ProjectDocuments', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectDocuments
    (
        Id              BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProjectDocuments PRIMARY KEY,
        TenantId        INT NOT NULL CONSTRAINT DF_ProjectDocuments_TenantId DEFAULT 1,
        ProjectId       INT NOT NULL,

        Title           NVARCHAR(300) NOT NULL,
        Slug            NVARCHAR(300) NOT NULL,
        DocumentType    NVARCHAR(100) NOT NULL,

        -- Soft pointer to the latest version. No FK to avoid circular dependency.
        -- Managed by ProjectDocumentService.
        CurrentVersionId BIGINT NULL,

        Status          NVARCHAR(50)  NOT NULL CONSTRAINT DF_ProjectDocuments_Status DEFAULT 'Active',

        Origin          NVARCHAR(50)  NOT NULL CONSTRAINT DF_ProjectDocuments_Origin DEFAULT 'CreatedInIronDev',
        ProcessingStatus NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectDocuments_ProcessingStatus DEFAULT 'Draft',
        Description     NVARCHAR(1000) NULL,
        Visibility      NVARCHAR(50)  NOT NULL CONSTRAINT DF_ProjectDocuments_Visibility DEFAULT 'Project',
        OriginalFileName NVARCHAR(260) NULL,
        MediaType       NVARCHAR(100) NULL,
        ByteSize        BIGINT NULL,

        CreatedAtUtc    DATETIME2(7) NOT NULL CONSTRAINT DF_ProjectDocuments_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        UpdatedAtUtc    DATETIME2(7) NULL,
        CreatedBy       NVARCHAR(200) NULL,
        UpdatedBy       NVARCHAR(200) NULL,

        CONSTRAINT FK_ProjectDocuments_Tenants  FOREIGN KEY (TenantId)  REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ProjectDocuments_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
    );

    CREATE INDEX IX_ProjectDocuments_Project_Status
        ON dbo.ProjectDocuments(TenantId, ProjectId, Status);

    CREATE INDEX IX_ProjectDocuments_Project_Type
        ON dbo.ProjectDocuments(TenantId, ProjectId, DocumentType, Status);

    CREATE UNIQUE INDEX UX_ProjectDocuments_Project_Slug
        ON dbo.ProjectDocuments(TenantId, ProjectId, Slug);

    PRINT 'Created dbo.ProjectDocuments';
END
ELSE
BEGIN
    PRINT 'dbo.ProjectDocuments already exists — skipped';
END
GO

-- Existing document stores gain upload metadata without changing their
-- current identity or implying retrieval readiness.
IF COL_LENGTH('dbo.ProjectDocuments', 'Origin') IS NULL
BEGIN
    ALTER TABLE dbo.ProjectDocuments ADD Origin NVARCHAR(50) NOT NULL
        CONSTRAINT DF_ProjectDocuments_Origin DEFAULT 'CreatedInIronDev' WITH VALUES;
END
GO

IF COL_LENGTH('dbo.ProjectDocuments', 'ProcessingStatus') IS NULL
BEGIN
    ALTER TABLE dbo.ProjectDocuments ADD ProcessingStatus NVARCHAR(50) NOT NULL
        CONSTRAINT DF_ProjectDocuments_ProcessingStatus DEFAULT 'Draft' WITH VALUES;
END
GO

IF COL_LENGTH('dbo.ProjectDocuments', 'Description') IS NULL
    ALTER TABLE dbo.ProjectDocuments ADD Description NVARCHAR(1000) NULL;
GO

IF COL_LENGTH('dbo.ProjectDocuments', 'Visibility') IS NULL
BEGIN
    ALTER TABLE dbo.ProjectDocuments ADD Visibility NVARCHAR(50) NOT NULL
        CONSTRAINT DF_ProjectDocuments_Visibility DEFAULT 'Project' WITH VALUES;
END
GO

IF COL_LENGTH('dbo.ProjectDocuments', 'OriginalFileName') IS NULL
    ALTER TABLE dbo.ProjectDocuments ADD OriginalFileName NVARCHAR(260) NULL;
GO

IF COL_LENGTH('dbo.ProjectDocuments', 'MediaType') IS NULL
    ALTER TABLE dbo.ProjectDocuments ADD MediaType NVARCHAR(100) NULL;
GO

IF COL_LENGTH('dbo.ProjectDocuments', 'ByteSize') IS NULL
    ALTER TABLE dbo.ProjectDocuments ADD ByteSize BIGINT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ProjectDocuments_Origin_Allowed')
BEGIN
    ALTER TABLE dbo.ProjectDocuments WITH CHECK ADD CONSTRAINT CK_ProjectDocuments_Origin_Allowed
        CHECK (Origin IN ('CreatedInIronDev', 'Uploaded'));
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ProjectDocuments_ProcessingStatus_Allowed')
BEGIN
    ALTER TABLE dbo.ProjectDocuments WITH CHECK ADD CONSTRAINT CK_ProjectDocuments_ProcessingStatus_Allowed
        CHECK (ProcessingStatus IN ('Uploading', 'Processing', 'Draft', 'Ready', 'ProcessingFailed', 'Unsupported', 'Superseded', 'Unavailable'));
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ProjectDocuments_Visibility_Allowed')
BEGIN
    ALTER TABLE dbo.ProjectDocuments WITH CHECK ADD CONSTRAINT CK_ProjectDocuments_Visibility_Allowed
        CHECK (Visibility IN ('Project', 'MembersOnly'));
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ProjectDocuments_ByteSize_NonNegative')
BEGIN
    ALTER TABLE dbo.ProjectDocuments WITH CHECK ADD CONSTRAINT CK_ProjectDocuments_ByteSize_NonNegative
        CHECK (ByteSize IS NULL OR ByteSize >= 0);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.ProjectDocuments') AND name = 'IX_ProjectDocuments_Project_Origin_Processing')
BEGIN
    CREATE INDEX IX_ProjectDocuments_Project_Origin_Processing
        ON dbo.ProjectDocuments(TenantId, ProjectId, Origin, ProcessingStatus);
END
GO

-- --------------------------------------------------------------------
-- 2. ProjectDocumentVersions
--    Immutable Markdown snapshots. Never update ContentMarkdown.
--    ParentVersionId tracks lineage.
-- --------------------------------------------------------------------
IF OBJECT_ID('dbo.ProjectDocumentVersions', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectDocumentVersions
    (
        Id              BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProjectDocumentVersions PRIMARY KEY,

        DocumentId      BIGINT NOT NULL,

        VersionMajor    INT  NOT NULL CONSTRAINT DF_ProjectDocumentVersions_VersionMajor DEFAULT 0,
        VersionMinor    INT  NOT NULL CONSTRAINT DF_ProjectDocumentVersions_VersionMinor DEFAULT 1,

        -- The canonical content. NEVER updated after creation.
        ContentMarkdown NVARCHAR(MAX) NOT NULL,

        ChangeSummary   NVARCHAR(MAX) NULL,
        ParentVersionId BIGINT NULL,

        -- Draft | Approved | Superseded | Archived
        Status          NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectDocumentVersions_Status DEFAULT 'Draft',

        -- SHA-256 hex of ContentMarkdown. Prevents duplicate saves.
        ContentHash     NVARCHAR(128) NULL,

        CreatedAtUtc    DATETIME2(7) NOT NULL CONSTRAINT DF_ProjectDocumentVersions_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy       NVARCHAR(200) NULL,

        CONSTRAINT FK_ProjectDocumentVersions_Documents
            FOREIGN KEY (DocumentId)      REFERENCES dbo.ProjectDocuments(Id),

        CONSTRAINT FK_ProjectDocumentVersions_ParentVersion
            FOREIGN KEY (ParentVersionId) REFERENCES dbo.ProjectDocumentVersions(Id)
    );

    CREATE INDEX IX_ProjectDocumentVersions_Document_Version
        ON dbo.ProjectDocumentVersions(DocumentId, VersionMajor DESC, VersionMinor DESC);

    CREATE INDEX IX_ProjectDocumentVersions_Document_Status
        ON dbo.ProjectDocumentVersions(DocumentId, Status);

    CREATE UNIQUE INDEX UX_ProjectDocumentVersions_Document_VersionNumber
        ON dbo.ProjectDocumentVersions(DocumentId, VersionMajor, VersionMinor);

    PRINT 'Created dbo.ProjectDocumentVersions';
END
ELSE
BEGIN
    PRINT 'dbo.ProjectDocumentVersions already exists — skipped';
END
GO

-- --------------------------------------------------------------------
-- 3. ProjectDocumentLinks
--    Polymorphic trace table linking a document version to any artefact.
--    LinkedEntityType / LinkedEntityId uses same pattern as ArtifactSourceReferences.
-- --------------------------------------------------------------------
IF OBJECT_ID('dbo.ProjectDocumentLinks', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectDocumentLinks
    (
        Id                BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProjectDocumentLinks PRIMARY KEY,

        DocumentVersionId BIGINT NOT NULL,

        -- e.g. Discussion | ChatMessage | ProjectMemory | Decision | Ticket | BuildTrace | LlmTrace
        LinkedEntityType  NVARCHAR(100) NOT NULL,
        LinkedEntityId    BIGINT NOT NULL,

        -- e.g. CreatedFrom | GeneratedTicket | References | Supersedes | RefinedBy | BuildTrace | DecisionSource
        LinkType          NVARCHAR(100) NOT NULL,

        CreatedAtUtc      DATETIME2(7) NOT NULL CONSTRAINT DF_ProjectDocumentLinks_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CreatedBy         NVARCHAR(200) NULL,

        CONSTRAINT FK_ProjectDocumentLinks_DocumentVersions
            FOREIGN KEY (DocumentVersionId) REFERENCES dbo.ProjectDocumentVersions(Id)
    );

    CREATE INDEX IX_ProjectDocumentLinks_DocumentVersionId
        ON dbo.ProjectDocumentLinks(DocumentVersionId);

    CREATE INDEX IX_ProjectDocumentLinks_LinkedEntity
        ON dbo.ProjectDocumentLinks(LinkedEntityType, LinkedEntityId);

    PRINT 'Created dbo.ProjectDocumentLinks';
END
ELSE
BEGIN
    PRINT 'dbo.ProjectDocumentLinks already exists — skipped';
END
GO

-- --------------------------------------------------------------------
-- 4. Add SourceDocumentVersionId to ProjectTickets (optional traceability)
--    Keeps querying simple alongside the link table approach.
-- --------------------------------------------------------------------
IF COL_LENGTH('dbo.ProjectTickets', 'SourceDocumentVersionId') IS NULL
BEGIN
    ALTER TABLE dbo.ProjectTickets ADD SourceDocumentVersionId BIGINT NULL;
    PRINT 'Added ProjectTickets.SourceDocumentVersionId';
END
GO

PRINT 'migrate_project_documents.sql complete.';
GO
