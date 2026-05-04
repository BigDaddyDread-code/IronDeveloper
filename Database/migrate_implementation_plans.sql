-- ============================================================================
-- Migration: Create ProjectImplementationPlans table
-- Date: 2026-04-21
-- Purpose: Support implementation planning with linked code context
-- ============================================================================

USE [IronDeveloper];
GO

IF OBJECT_ID('dbo.ProjectImplementationPlans', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectImplementationPlans
    (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        TenantId INT NOT NULL CONSTRAINT DF_ProjectImplementationPlans_Tenant DEFAULT 1,
        ProjectId INT NOT NULL,
        Title NVARCHAR(200) NOT NULL,
        Goal NVARCHAR(MAX) NOT NULL,
        Scope NVARCHAR(MAX) NULL,
        ProposedSteps NVARCHAR(MAX) NULL,
        AffectedContext NVARCHAR(MAX) NULL,
        RisksNotes NVARCHAR(MAX) NULL,
        Status NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectImplementationPlans_Status DEFAULT 'Draft',
        
        -- Linked Context
        LinkedFilePaths NVARCHAR(MAX) NULL,
        LinkedCodeIndexEntryIds NVARCHAR(MAX) NULL,
        LinkedSymbols NVARCHAR(MAX) NULL,
        
        -- AI Context
        SourceChatMessageId BIGINT NULL,
        
        CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectImplementationPlans_CreatedDate DEFAULT SYSUTCDATETIME(),
        UpdatedDate DATETIME2 NULL,

        CONSTRAINT FK_ProjectImplementationPlans_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ProjectImplementationPlans_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id),
        CONSTRAINT FK_ProjectImplementationPlans_ChatMessages FOREIGN KEY (SourceChatMessageId) REFERENCES dbo.ChatMessages(Id)
    );

    CREATE INDEX IX_ProjectImplementationPlans_ProjectId_CreatedDate
        ON dbo.ProjectImplementationPlans(ProjectId, CreatedDate DESC);

    PRINT 'Created ProjectImplementationPlans table.';
END
ELSE
    PRINT 'ProjectImplementationPlans table already exists.';

GO
