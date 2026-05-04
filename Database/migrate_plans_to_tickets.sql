-- ============================================================================
-- Migration: Link Implementation Plans to Tickets
-- Date: 2026-04-21
-- Purpose: Support 1:1 relationship between Ticket and Plan
-- ============================================================================

USE [IronDeveloper];
GO

IF NOT EXISTS (
    SELECT * FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'dbo.ProjectImplementationPlans') AND name = N'TicketId'
)
BEGIN
    ALTER TABLE dbo.ProjectImplementationPlans ADD TicketId BIGINT NULL;
    
    -- Add foreign key
    ALTER TABLE dbo.ProjectImplementationPlans 
    ADD CONSTRAINT FK_ProjectImplementationPlans_Tickets FOREIGN KEY (TicketId) 
    REFERENCES dbo.ProjectTickets(Id);
    
    PRINT 'Added TicketId to ProjectImplementationPlans and linked FK.';
END
GO
