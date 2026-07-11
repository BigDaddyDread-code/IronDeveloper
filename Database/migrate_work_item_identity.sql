/*
  V25-01: Work Items get durable identity and versioned contracts.
  This migration preserves current ticket-backed routes by assigning every
  migrated WorkItem.Id from its legacy ProjectTickets.Id.
*/

IF OBJECT_ID(N'dbo.Projects', N'U') IS NULL
BEGIN
    THROW 53100, 'dbo.Projects must exist before Work Item identity migration runs.', 1;
END;
GO

IF OBJECT_ID(N'dbo.ProjectTickets', N'U') IS NULL
BEGIN
    THROW 53101, 'dbo.ProjectTickets must exist before Work Item identity migration runs.', 1;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.Projects') AND name = N'UQ_Projects_IdTenant')
BEGIN
    ALTER TABLE dbo.Projects
    ADD CONSTRAINT UQ_Projects_IdTenant UNIQUE (Id, TenantId);
END;
GO

IF COL_LENGTH(N'dbo.ProjectTickets', N'Revision') IS NULL
BEGIN
    ALTER TABLE dbo.ProjectTickets ADD Revision BIGINT NOT NULL CONSTRAINT DF_ProjectTickets_Revision DEFAULT 1;
END;
GO

IF COL_LENGTH(N'dbo.ProjectTickets', N'BlockedByTicketIds') IS NULL
BEGIN
    ALTER TABLE dbo.ProjectTickets ADD BlockedByTicketIds NVARCHAR(MAX) NULL;
END;
GO

IF COL_LENGTH(N'dbo.ProjectTickets', N'SourceDocumentVersionId') IS NULL
BEGIN
    ALTER TABLE dbo.ProjectTickets ADD SourceDocumentVersionId BIGINT NULL;
END;
GO

IF OBJECT_ID(N'dbo.WorkItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkItems
    (
        Id BIGINT NOT NULL CONSTRAINT PK_WorkItems PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        Title NVARCHAR(200) NOT NULL,
        OriginKind NVARCHAR(50) NOT NULL CONSTRAINT DF_WorkItems_OriginKind DEFAULT N'Ticket',
        OriginReference NVARCHAR(200) NOT NULL CONSTRAINT DF_WorkItems_OriginReference DEFAULT N'',
        LegacyTicketId BIGINT NULL,
        CurrentContractId BIGINT NULL,
        CurrentRunId NVARCHAR(160) NULL,
        CurrentStage NVARCHAR(50) NOT NULL CONSTRAINT DF_WorkItems_CurrentStage DEFAULT N'Ticket',
        CurrentState NVARCHAR(50) NOT NULL CONSTRAINT DF_WorkItems_CurrentState DEFAULT N'Draft',
        AssigneeUserId INT NULL,
        WaitingOnKind NVARCHAR(50) NULL,
        WaitingOnReference NVARCHAR(200) NULL,
        CreatedByUserId INT NULL,
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_WorkItems_CreatedUtc DEFAULT SYSUTCDATETIME(),
        UpdatedUtc DATETIME2 NOT NULL CONSTRAINT DF_WorkItems_UpdatedUtc DEFAULT SYSUTCDATETIME(),
        Version BIGINT NOT NULL CONSTRAINT DF_WorkItems_Version DEFAULT 1,
        CONSTRAINT FK_WorkItems_Project FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_WorkItems_LegacyTicket FOREIGN KEY (LegacyTicketId) REFERENCES dbo.ProjectTickets(Id),
        CONSTRAINT FK_WorkItems_Assignee FOREIGN KEY (AssigneeUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_WorkItems_CreatedBy FOREIGN KEY (CreatedByUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_WorkItems_OriginKind CHECK (OriginKind IN (N'Ticket', N'Workshop', N'Document', N'Imported')),
        CONSTRAINT CK_WorkItems_CurrentStage CHECK (CurrentStage IN (N'Shape', N'Ticket', N'Build', N'Review', N'Done'))
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkItems_Project_Updated' AND object_id = OBJECT_ID(N'dbo.WorkItems'))
BEGIN
    CREATE INDEX IX_WorkItems_Project_Updated
    ON dbo.WorkItems (TenantId, ProjectId, UpdatedUtc DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_WorkItems_LegacyTicket' AND object_id = OBJECT_ID(N'dbo.WorkItems'))
BEGIN
    CREATE UNIQUE INDEX UX_WorkItems_LegacyTicket
    ON dbo.WorkItems (LegacyTicketId)
    WHERE LegacyTicketId IS NOT NULL;
END;
GO

IF OBJECT_ID(N'dbo.WorkItemContracts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkItemContracts
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WorkItemContracts PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        WorkItemId BIGINT NOT NULL,
        ContractVersion INT NOT NULL,
        SourceTicketId BIGINT NULL,
        Title NVARCHAR(200) NOT NULL,
        Summary NVARCHAR(MAX) NULL,
        Problem NVARCHAR(MAX) NULL,
        AcceptanceCriteria NVARCHAR(MAX) NULL,
        TechnicalNotes NVARCHAR(MAX) NULL,
        TestExpectations NVARCHAR(MAX) NULL,
        LinkedFilePaths NVARCHAR(MAX) NULL,
        LinkedCodeIndexEntryIds NVARCHAR(MAX) NULL,
        LinkedSymbols NVARCHAR(MAX) NULL,
        SourceWorkshopSessionId BIGINT NULL,
        SourceWorkshopMessageIds NVARCHAR(MAX) NULL,
        SourceDocumentVersionIds NVARCHAR(MAX) NULL,
        CreatedByUserId INT NULL,
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_WorkItemContracts_CreatedUtc DEFAULT SYSUTCDATETIME(),
        SupersedesContractId BIGINT NULL,
        ContractHash NVARCHAR(64) NOT NULL,
        CONSTRAINT FK_WorkItemContracts_Project FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_WorkItemContracts_WorkItem FOREIGN KEY (WorkItemId) REFERENCES dbo.WorkItems(Id),
        CONSTRAINT FK_WorkItemContracts_SourceTicket FOREIGN KEY (SourceTicketId) REFERENCES dbo.ProjectTickets(Id),
        CONSTRAINT FK_WorkItemContracts_CreatedBy FOREIGN KEY (CreatedByUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_WorkItemContracts_Supersedes FOREIGN KEY (SupersedesContractId) REFERENCES dbo.WorkItemContracts(Id),
        CONSTRAINT UQ_WorkItemContracts_WorkItemVersion UNIQUE (TenantId, ProjectId, WorkItemId, ContractVersion)
    );
END;
GO

IF OBJECT_ID(N'dbo.FK_WorkItems_CurrentContract', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.WorkItems
    ADD CONSTRAINT FK_WorkItems_CurrentContract FOREIGN KEY (CurrentContractId) REFERENCES dbo.WorkItemContracts(Id);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkItemContracts_WorkItem' AND object_id = OBJECT_ID(N'dbo.WorkItemContracts'))
BEGIN
    CREATE INDEX IX_WorkItemContracts_WorkItem
    ON dbo.WorkItemContracts (TenantId, ProjectId, WorkItemId, ContractVersion DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkItemContracts_SourceTicket' AND object_id = OBJECT_ID(N'dbo.WorkItemContracts'))
BEGIN
    CREATE INDEX IX_WorkItemContracts_SourceTicket
    ON dbo.WorkItemContracts (SourceTicketId);
END;
GO

INSERT INTO dbo.WorkItems
    (Id, TenantId, ProjectId, Title, OriginKind, OriginReference, LegacyTicketId, CurrentStage, CurrentState, CreatedUtc, UpdatedUtc)
SELECT
    t.Id,
    t.TenantId,
    t.ProjectId,
    t.Title,
    N'Ticket',
    CONCAT(N'Ticket:', CONVERT(NVARCHAR(40), t.Id)),
    t.Id,
    CASE
        WHEN LOWER(t.Status) LIKE N'%applied%' OR LOWER(t.Status) LIKE N'%done%' OR LOWER(t.Status) LIKE N'%closed%' THEN N'Done'
        WHEN LOWER(t.Status) LIKE N'%approval%' OR LOWER(t.Status) LIKE N'%review%' THEN N'Review'
        WHEN LOWER(t.Status) LIKE N'%build%' OR LOWER(t.Status) LIKE N'%progress%' OR LOWER(t.Status) LIKE N'%failed%' OR LOWER(t.Status) LIKE N'%blocked%' THEN N'Build'
        WHEN LOWER(t.Status) LIKE N'%shape%' THEN N'Shape'
        ELSE N'Ticket'
    END,
    t.Status,
    t.CreatedDate,
    SYSUTCDATETIME()
FROM dbo.ProjectTickets t
WHERE t.IsDeleted = 0
  AND NOT EXISTS (SELECT 1 FROM dbo.WorkItems wi WHERE wi.Id = t.Id OR wi.LegacyTicketId = t.Id);
GO

INSERT INTO dbo.WorkItemContracts
    (TenantId, ProjectId, WorkItemId, ContractVersion, SourceTicketId, Title, Summary, Problem, AcceptanceCriteria,
     TechnicalNotes, TestExpectations, LinkedFilePaths, LinkedCodeIndexEntryIds, LinkedSymbols, SourceWorkshopSessionId,
     SourceWorkshopMessageIds, SourceDocumentVersionIds, ContractHash)
SELECT
    t.TenantId,
    t.ProjectId,
    t.Id,
    1,
    t.Id,
    t.Title,
    t.Summary,
    t.Problem,
    t.AcceptanceCriteria,
    t.TechnicalNotes,
    CONCAT(
        N'Unit:', COALESCE(t.UnitTests, N''), CHAR(10),
        N'Integration:', COALESCE(t.IntegrationTests, N''), CHAR(10),
        N'Manual:', COALESCE(t.ManualTests, N''), CHAR(10),
        N'Regression:', COALESCE(t.RegressionTests, N''), CHAR(10),
        N'Build:', COALESCE(t.BuildValidation, N'')),
    t.LinkedFilePaths,
    t.LinkedCodeIndexEntryIds,
    t.LinkedSymbols,
    t.SourceChatSessionId,
    CASE WHEN t.SourceChatMessageId IS NULL THEN NULL ELSE CONVERT(NVARCHAR(40), t.SourceChatMessageId) END,
    CASE WHEN t.SourceDocumentVersionId IS NULL THEN NULL ELSE CONVERT(NVARCHAR(40), t.SourceDocumentVersionId) END,
    CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', CONCAT(
        COALESCE(t.Title, N''), N'|',
        COALESCE(t.Summary, N''), N'|',
        COALESCE(t.Problem, N''), N'|',
        COALESCE(t.AcceptanceCriteria, N''), N'|',
        COALESCE(t.TechnicalNotes, N''), N'|',
        COALESCE(t.LinkedFilePaths, N''), N'|',
        COALESCE(t.LinkedCodeIndexEntryIds, N''), N'|',
        COALESCE(t.LinkedSymbols, N''), N'|',
        COALESCE(t.UnitTests, N''), N'|',
        COALESCE(t.IntegrationTests, N''), N'|',
        COALESCE(t.ManualTests, N''), N'|',
        COALESCE(t.RegressionTests, N''), N'|',
        COALESCE(t.BuildValidation, N''))), 2)
FROM dbo.ProjectTickets t
WHERE t.IsDeleted = 0
  AND EXISTS (SELECT 1 FROM dbo.WorkItems wi WHERE wi.Id = t.Id)
  AND NOT EXISTS (SELECT 1 FROM dbo.WorkItemContracts c WHERE c.WorkItemId = t.Id);
GO

UPDATE wi
SET CurrentContractId = latest.Id
FROM dbo.WorkItems wi
CROSS APPLY (
    SELECT TOP (1) c.Id
    FROM dbo.WorkItemContracts c
    WHERE c.WorkItemId = wi.Id
    ORDER BY c.ContractVersion DESC, c.Id DESC
) latest
WHERE wi.CurrentContractId IS NULL;
GO
