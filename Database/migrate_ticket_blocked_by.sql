USE [IronDeveloper];
GO

-- P2-1 (Phase 2, widen to batch): a ticket can declare the tickets it is
-- blocked by — the human's explicit ordering, one evidence source for the
-- batch dependency map. Comma-separated ticket ids. Declaring a dependency
-- schedules nothing by itself: the map is advisory evidence.
IF COL_LENGTH('dbo.ProjectTickets', 'BlockedByTicketIds') IS NULL
BEGIN
    ALTER TABLE dbo.ProjectTickets ADD BlockedByTicketIds NVARCHAR(MAX) NULL;
END
GO
