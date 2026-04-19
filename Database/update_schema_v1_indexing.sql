USE [IronDeveloper];
GO

IF OBJECT_ID('dbo.CodeIndexEntries', 'U') IS NOT NULL DROP TABLE dbo.CodeIndexEntries;

CREATE TABLE dbo.CodeIndexEntries
(
    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    TenantId INT NOT NULL,
    ProjectId INT NOT NULL,
    FileId BIGINT NOT NULL,
    Namespace NVARCHAR(500) NULL,
    SymbolName NVARCHAR(500) NULL,
    SymbolType NVARCHAR(50) NULL, -- e.g. Class, Method, Property
    Summary NVARCHAR(MAX) NULL,
    ChunkText NVARCHAR(MAX) NOT NULL,
    CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_CodeIndexEntries_CreatedDate DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_CodeIndexEntries_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
    CONSTRAINT FK_CodeIndexEntries_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id),
    CONSTRAINT FK_CodeIndexEntries_ProjectFiles FOREIGN KEY (FileId) REFERENCES dbo.ProjectFiles(Id) ON DELETE CASCADE
);

CREATE INDEX IX_CodeIndexEntries_ProjectId_SymbolName
    ON dbo.CodeIndexEntries(ProjectId, SymbolName);

CREATE INDEX IX_CodeIndexEntries_ProjectId_Namespace
    ON dbo.CodeIndexEntries(ProjectId, Namespace);
GO
