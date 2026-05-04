USE [IronDeveloper];
GO

ALTER TABLE dbo.ProjectDecisions
ADD LinkedFilePaths NVARCHAR(MAX) NULL,
    LinkedCodeIndexEntryIds NVARCHAR(MAX) NULL,
    LinkedSymbols NVARCHAR(MAX) NULL;

ALTER TABLE dbo.ProjectTickets
ADD LinkedFilePaths NVARCHAR(MAX) NULL,
    LinkedCodeIndexEntryIds NVARCHAR(MAX) NULL,
    LinkedSymbols NVARCHAR(MAX) NULL;
GO
