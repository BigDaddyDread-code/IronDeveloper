USE [IronDeveloper];
GO

IF OBJECT_ID('dbo.ChatMessages', 'U') IS NOT NULL
BEGIN
    ALTER TABLE dbo.ChatMessages ALTER COLUMN Tags NVARCHAR(MAX) NULL;
END
GO
