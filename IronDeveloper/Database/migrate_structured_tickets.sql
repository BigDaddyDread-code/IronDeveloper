-- Migration: Add structured columns to ProjectTickets
-- Run this against an existing IronDeveloper database (no data loss)
USE [IronDeveloper];
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'Title')
BEGIN
    ALTER TABLE dbo.ProjectTickets ADD Title NVARCHAR(200) NOT NULL CONSTRAINT DF_ProjectTickets_Title DEFAULT '';
    ALTER TABLE dbo.ProjectTickets ADD TicketType NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectTickets_TicketType DEFAULT 'Task';
    ALTER TABLE dbo.ProjectTickets ADD Priority NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectTickets_Priority DEFAULT 'Medium';
    ALTER TABLE dbo.ProjectTickets ADD Summary NVARCHAR(MAX) NULL;
    ALTER TABLE dbo.ProjectTickets ADD Background NVARCHAR(MAX) NULL;
    ALTER TABLE dbo.ProjectTickets ADD Problem NVARCHAR(MAX) NULL;
    ALTER TABLE dbo.ProjectTickets ADD AcceptanceCriteria NVARCHAR(MAX) NULL;
    ALTER TABLE dbo.ProjectTickets ADD TechnicalNotes NVARCHAR(MAX) NULL;
    ALTER TABLE dbo.ProjectTickets ADD Status NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectTickets_Status DEFAULT 'Draft';
    PRINT 'Structured columns added to ProjectTickets.';
END
ELSE
BEGIN
    PRINT 'Structured columns already exist. No changes made.';
END
GO
