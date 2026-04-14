-- Migration: Add LocalPath to Projects and create ProjectFiles table

IF NOT EXISTS (
    SELECT * FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'dbo.Projects') AND name = N'LocalPath'
)
BEGIN
    ALTER TABLE dbo.Projects ADD LocalPath NVARCHAR(500) NULL;
END
GO

IF OBJECT_ID('dbo.ProjectFiles', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectFiles
    (
        Id BIGINT IDENTITY(1,1) PRIMARY KEY,
        ProjectId INT NOT NULL,
        FilePath NVARCHAR(1000) NOT NULL,
        FileExtension NVARCHAR(50) NOT NULL,
        ContentHash NVARCHAR(100) NOT NULL,
        Content NVARCHAR(MAX) NOT NULL,
        LastIndexedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectFiles_LastIndexedDate DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ProjectFiles_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
    );

    CREATE INDEX IX_ProjectFiles_ProjectId_FilePath
        ON dbo.ProjectFiles(ProjectId, FilePath);

    CREATE INDEX IX_ProjectFiles_ProjectId_FileExtension
        ON dbo.ProjectFiles(ProjectId, FileExtension);

    CREATE UNIQUE INDEX UX_ProjectFiles_ProjectId_FilePath
        ON dbo.ProjectFiles(ProjectId, FilePath);
END
GO
