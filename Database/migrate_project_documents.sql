USE [IronDeveloper];
GO

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
