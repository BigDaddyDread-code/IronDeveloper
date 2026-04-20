USE [IronDeveloper];
GO

IF NOT EXISTS (
    SELECT * FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'dbo.Projects') AND name = N'LastIndexedUtc'
)
BEGIN
    ALTER TABLE dbo.Projects ADD LastIndexedUtc DATETIME2 NULL;
END
GO

IF NOT EXISTS (
    SELECT * FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'dbo.Projects') AND name = N'IndexingStatus'
)
BEGIN
    ALTER TABLE dbo.Projects ADD IndexingStatus NVARCHAR(50) NULL;
END
GO
