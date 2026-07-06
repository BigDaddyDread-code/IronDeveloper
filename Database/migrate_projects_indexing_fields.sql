-- Catalog-agnostic by design: the CONNECTION chooses the database, never the
-- migration. A USE statement here once hijacked test-host provisioning onto the
-- real IronDeveloper catalog (see Docs/receipts/HERO2_LIVE_MODEL_REAL_LOOP.md).

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
