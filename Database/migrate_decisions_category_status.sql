-- ============================================================================
-- Migration: Add Category and Status columns to ProjectDecisions
--            Create DecisionCategories and DecisionStatuses lookup tables
-- Date: 2026-04-21
-- Purpose: Support decision categorization and lifecycle status tracking
-- ============================================================================

USE [IronDeveloper];
GO

-- ── 1. Add columns to ProjectDecisions ──────────────────────────────────────

IF NOT EXISTS (
    SELECT 1 FROM sys.columns 
    WHERE object_id = OBJECT_ID('dbo.ProjectDecisions') AND name = 'Category'
)
BEGIN
    ALTER TABLE dbo.ProjectDecisions
    ADD Category NVARCHAR(100) NULL;
    PRINT 'Added Category column to ProjectDecisions.';
END
ELSE
    PRINT 'Category column already exists.';

IF NOT EXISTS (
    SELECT 1 FROM sys.columns 
    WHERE object_id = OBJECT_ID('dbo.ProjectDecisions') AND name = 'Status'
)
BEGIN
    ALTER TABLE dbo.ProjectDecisions
    ADD Status NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectDecisions_Status DEFAULT 'Accepted';
    PRINT 'Added Status column to ProjectDecisions.';
END
ELSE
    PRINT 'Status column already exists.';

GO

-- ── 2. Create lookup tables ─────────────────────────────────────────────────

IF OBJECT_ID('dbo.DecisionCategories', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DecisionCategories
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name NVARCHAR(100) NOT NULL,
        SortOrder INT NOT NULL CONSTRAINT DF_DecisionCategories_SortOrder DEFAULT 0,
        IsActive BIT NOT NULL CONSTRAINT DF_DecisionCategories_IsActive DEFAULT 1
    );
    PRINT 'Created DecisionCategories table.';

    INSERT INTO dbo.DecisionCategories (Name, SortOrder) VALUES
        ('Architecture',       1),
        ('Code Standards',     2),
        ('Product',            3),
        ('Data',               4),
        ('Infrastructure',     5),
        ('AI / Prompting',     6),
        ('UX / UI',            7),
        ('Workflow / Process', 8),
        ('Integration',        9),
        ('Security',          10);
    PRINT 'Seeded DecisionCategories.';
END
ELSE
    PRINT 'DecisionCategories table already exists.';

IF OBJECT_ID('dbo.DecisionStatuses', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DecisionStatuses
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Name NVARCHAR(50) NOT NULL,
        SortOrder INT NOT NULL CONSTRAINT DF_DecisionStatuses_SortOrder DEFAULT 0,
        IsActive BIT NOT NULL CONSTRAINT DF_DecisionStatuses_IsActive DEFAULT 1
    );
    PRINT 'Created DecisionStatuses table.';

    INSERT INTO dbo.DecisionStatuses (Name, SortOrder) VALUES
        ('Proposed',    1),
        ('Accepted',    2),
        ('Superseded',  3),
        ('Rejected',    4);
    PRINT 'Seeded DecisionStatuses.';
END
ELSE
    PRINT 'DecisionStatuses table already exists.';

GO
