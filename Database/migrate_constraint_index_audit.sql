/* CLN-22: enforce product scope and add source, supersession, retrieval, and retention indexes. */

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ProjectProfiles_ProjectScope')
BEGIN
    ALTER TABLE dbo.ProjectProfiles WITH CHECK
        ADD CONSTRAINT FK_ProjectProfiles_ProjectScope
        FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId);
    ALTER TABLE dbo.ProjectProfiles CHECK CONSTRAINT FK_ProjectProfiles_ProjectScope;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ProjectCommands_ProjectScope')
BEGIN
    ALTER TABLE dbo.ProjectCommands WITH CHECK
        ADD CONSTRAINT FK_ProjectCommands_ProjectScope
        FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId);
    ALTER TABLE dbo.ProjectCommands CHECK CONSTRAINT FK_ProjectCommands_ProjectScope;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ProjectCommands') AND name = N'IX_ProjectCommands_Project_Type_Enabled')
    CREATE INDEX IX_ProjectCommands_Project_Type_Enabled
        ON dbo.ProjectCommands(TenantId, ProjectId, CommandType, IsEnabled);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Runs_Projects')
BEGIN
    ALTER TABLE dbo.Runs WITH CHECK
        ADD CONSTRAINT FK_Runs_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id);
    ALTER TABLE dbo.Runs CHECK CONSTRAINT FK_Runs_Projects;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ProjectTickets') AND name = N'UX_ProjectTickets_Id_ProjectId')
    CREATE UNIQUE INDEX UX_ProjectTickets_Id_ProjectId ON dbo.ProjectTickets(Id, ProjectId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Runs_ProjectTickets')
BEGIN
    ALTER TABLE dbo.Runs WITH CHECK
        ADD CONSTRAINT FK_Runs_ProjectTickets
        FOREIGN KEY (TicketId, ProjectId) REFERENCES dbo.ProjectTickets(Id, ProjectId);
    ALTER TABLE dbo.Runs CHECK CONSTRAINT FK_Runs_ProjectTickets;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_Runs_TicketRequiresProject')
    ALTER TABLE dbo.Runs WITH CHECK
        ADD CONSTRAINT CK_Runs_TicketRequiresProject CHECK (TicketId IS NULL OR ProjectId IS NOT NULL);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Runs') AND name = N'IX_Runs_State_UpdatedUtc')
    CREATE INDEX IX_Runs_State_UpdatedUtc ON dbo.Runs(State, UpdatedUtc DESC, Id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_SemanticArtefacts_Projects')
BEGIN
    ALTER TABLE dbo.SemanticArtefacts WITH CHECK
        ADD CONSTRAINT FK_SemanticArtefacts_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id);
    ALTER TABLE dbo.SemanticArtefacts CHECK CONSTRAINT FK_SemanticArtefacts_Projects;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_SemanticArtefacts_ProjectScope')
BEGIN
    ALTER TABLE dbo.SemanticArtefacts WITH CHECK
        ADD CONSTRAINT FK_SemanticArtefacts_ProjectScope
        FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId);
    ALTER TABLE dbo.SemanticArtefacts CHECK CONSTRAINT FK_SemanticArtefacts_ProjectScope;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_SemanticChunks_Projects')
BEGIN
    ALTER TABLE dbo.SemanticChunks WITH CHECK
        ADD CONSTRAINT FK_SemanticChunks_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id);
    ALTER TABLE dbo.SemanticChunks CHECK CONSTRAINT FK_SemanticChunks_Projects;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_EmbeddingJobs_Projects')
BEGIN
    ALTER TABLE dbo.EmbeddingJobs WITH CHECK
        ADD CONSTRAINT FK_EmbeddingJobs_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id);
    ALTER TABLE dbo.EmbeddingJobs CHECK CONSTRAINT FK_EmbeddingJobs_Projects;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_EmbeddingJobs_ProjectScope')
BEGIN
    ALTER TABLE dbo.EmbeddingJobs WITH CHECK
        ADD CONSTRAINT FK_EmbeddingJobs_ProjectScope
        FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId);
    ALTER TABLE dbo.EmbeddingJobs CHECK CONSTRAINT FK_EmbeddingJobs_ProjectScope;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_SemanticSearchTraces_Projects')
BEGIN
    ALTER TABLE dbo.SemanticSearchTraces WITH CHECK
        ADD CONSTRAINT FK_SemanticSearchTraces_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id);
    ALTER TABLE dbo.SemanticSearchTraces CHECK CONSTRAINT FK_SemanticSearchTraces_Projects;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_SemanticEmbeddings_Projects')
BEGIN
    ALTER TABLE dbo.SemanticEmbeddings WITH CHECK
        ADD CONSTRAINT FK_SemanticEmbeddings_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id);
    ALTER TABLE dbo.SemanticEmbeddings CHECK CONSTRAINT FK_SemanticEmbeddings_Projects;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_SemanticIndexRuns_Projects')
BEGIN
    ALTER TABLE dbo.SemanticIndexRuns WITH CHECK
        ADD CONSTRAINT FK_SemanticIndexRuns_Projects FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id);
    ALTER TABLE dbo.SemanticIndexRuns CHECK CONSTRAINT FK_SemanticIndexRuns_Projects;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.EmbeddingJobs') AND name = N'IX_EmbeddingJobs_Project_Source')
    CREATE INDEX IX_EmbeddingJobs_Project_Source
        ON dbo.EmbeddingJobs(ProjectId, SourceEntityType, SourceEntityId, SourceVersionId, CreatedUtc DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SemanticSearchTraces') AND name = N'IX_SemanticSearchTraces_Project_CreatedUtc')
    CREATE INDEX IX_SemanticSearchTraces_Project_CreatedUtc
        ON dbo.SemanticSearchTraces(ProjectId, CreatedUtc DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SemanticEmbeddings') AND name = N'IX_SemanticEmbeddings_SourceDocumentVersion')
    CREATE INDEX IX_SemanticEmbeddings_SourceDocumentVersion
        ON dbo.SemanticEmbeddings(SourceDocumentVersionId, ProjectId)
        WHERE SourceDocumentVersionId IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SemanticIndexRuns') AND name = N'IX_SemanticIndexRuns_Project_StartedAtUtc')
    CREATE INDEX IX_SemanticIndexRuns_Project_StartedAtUtc
        ON dbo.SemanticIndexRuns(ProjectId, StartedAtUtc DESC, Id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.SemanticIndexRuns') AND name = N'IX_SemanticIndexRuns_Status_StartedAtUtc')
    CREATE INDEX IX_SemanticIndexRuns_Status_StartedAtUtc
        ON dbo.SemanticIndexRuns(Status, StartedAtUtc DESC, Id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ProjectTickets_SourceChatSession')
BEGIN
    UPDATE ticket
    SET SourceChatSessionId = NULL
    FROM dbo.ProjectTickets AS ticket
    WHERE ticket.SourceChatSessionId IS NOT NULL
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.ProjectChatSessions AS source
          WHERE source.Id = ticket.SourceChatSessionId
      );

    ALTER TABLE dbo.ProjectTickets WITH CHECK
        ADD CONSTRAINT FK_ProjectTickets_SourceChatSession
        FOREIGN KEY (SourceChatSessionId) REFERENCES dbo.ProjectChatSessions(Id);
    ALTER TABLE dbo.ProjectTickets CHECK CONSTRAINT FK_ProjectTickets_SourceChatSession;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ProjectTickets_SourceChatMessage')
BEGIN
    UPDATE ticket
    SET SourceChatMessageId = NULL
    FROM dbo.ProjectTickets AS ticket
    WHERE ticket.SourceChatMessageId IS NOT NULL
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.ChatMessages AS source
          WHERE source.Id = ticket.SourceChatMessageId
      );

    ALTER TABLE dbo.ProjectTickets WITH CHECK
        ADD CONSTRAINT FK_ProjectTickets_SourceChatMessage
        FOREIGN KEY (SourceChatMessageId) REFERENCES dbo.ChatMessages(Id);
    ALTER TABLE dbo.ProjectTickets CHECK CONSTRAINT FK_ProjectTickets_SourceChatMessage;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_ProjectTickets_SourceDocumentVersion')
BEGIN
    UPDATE ticket
    SET SourceDocumentVersionId = NULL
    FROM dbo.ProjectTickets AS ticket
    WHERE ticket.SourceDocumentVersionId IS NOT NULL
      AND NOT EXISTS
      (
          SELECT 1
          FROM dbo.ProjectDocumentVersions AS source
          WHERE source.Id = ticket.SourceDocumentVersionId
      );

    ALTER TABLE dbo.ProjectTickets WITH CHECK
        ADD CONSTRAINT FK_ProjectTickets_SourceDocumentVersion
        FOREIGN KEY (SourceDocumentVersionId) REFERENCES dbo.ProjectDocumentVersions(Id);
    ALTER TABLE dbo.ProjectTickets CHECK CONSTRAINT FK_ProjectTickets_SourceDocumentVersion;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ProjectTickets') AND name = N'IX_ProjectTickets_SourceChatSession')
    CREATE INDEX IX_ProjectTickets_SourceChatSession ON dbo.ProjectTickets(SourceChatSessionId) WHERE SourceChatSessionId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ProjectTickets') AND name = N'IX_ProjectTickets_SourceChatMessage')
    CREATE INDEX IX_ProjectTickets_SourceChatMessage ON dbo.ProjectTickets(SourceChatMessageId) WHERE SourceChatMessageId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ProjectTickets') AND name = N'IX_ProjectTickets_SourceDocumentVersion')
    CREATE INDEX IX_ProjectTickets_SourceDocumentVersion ON dbo.ProjectTickets(SourceDocumentVersionId) WHERE SourceDocumentVersionId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ProjectTickets') AND name = N'IX_ProjectTickets_Status_CreatedDate')
    CREATE INDEX IX_ProjectTickets_Status_CreatedDate ON dbo.ProjectTickets(ProjectId, Status, CreatedDate DESC, Id);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ProjectContextDocuments') AND name = N'IX_ProjectContextDocuments_SourceChatMessage')
    CREATE INDEX IX_ProjectContextDocuments_SourceChatMessage ON dbo.ProjectContextDocuments(SourceChatMessageId) WHERE SourceChatMessageId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ProjectContextDocuments') AND name = N'IX_ProjectContextDocuments_Supersedes')
    CREATE INDEX IX_ProjectContextDocuments_Supersedes ON dbo.ProjectContextDocuments(SupersedesDocumentId) WHERE SupersedesDocumentId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ProjectDecisions') AND name = N'IX_ProjectDecisions_SourceChatMessage')
    CREATE INDEX IX_ProjectDecisions_SourceChatMessage ON dbo.ProjectDecisions(SourceChatMessageId) WHERE SourceChatMessageId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ProjectImplementationPlans') AND name = N'IX_ProjectImplementationPlans_SourceChatMessage')
    CREATE INDEX IX_ProjectImplementationPlans_SourceChatMessage ON dbo.ProjectImplementationPlans(SourceChatMessageId) WHERE SourceChatMessageId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ProjectSummaries') AND name = N'IX_ProjectSummaries_SourceChatMessage')
    CREATE INDEX IX_ProjectSummaries_SourceChatMessage ON dbo.ProjectSummaries(SourceChatMessageId) WHERE SourceChatMessageId IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WorkItemContracts') AND name = N'IX_WorkItemContracts_Supersedes')
    CREATE INDEX IX_WorkItemContracts_Supersedes ON dbo.WorkItemContracts(SupersedesContractId) WHERE SupersedesContractId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.WorkItemContracts') AND name = N'IX_WorkItemContracts_SourceWorkshopSession')
    CREATE INDEX IX_WorkItemContracts_SourceWorkshopSession ON dbo.WorkItemContracts(SourceWorkshopSessionId) WHERE SourceWorkshopSessionId IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_AgentHandoff_Supersedes')
BEGIN
    ALTER TABLE a2a.AgentHandoff WITH CHECK
        ADD CONSTRAINT FK_AgentHandoff_Supersedes
        FOREIGN KEY (SupersedesHandoffId) REFERENCES a2a.AgentHandoff(AgentHandoffId);
    ALTER TABLE a2a.AgentHandoff CHECK CONSTRAINT FK_AgentHandoff_Supersedes;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'a2a.AgentHandoff') AND name = N'IX_AgentHandoff_Supersedes')
    CREATE INDEX IX_AgentHandoff_Supersedes ON a2a.AgentHandoff(SupersedesHandoffId) WHERE SupersedesHandoffId IS NOT NULL;
GO

/* Existing catalogs can contain the lookup tables without their compatibility rows. */
WITH CanonicalDecisionCategories AS
(
    SELECT Name, SortOrder
    FROM (VALUES
        (N'Architecture', 1),
        (N'Code Standards', 2),
        (N'Product', 3),
        (N'Data', 4),
        (N'Infrastructure', 5),
        (N'AI / Prompting', 6),
        (N'UX / UI', 7),
        (N'Workflow / Process', 8),
        (N'Integration', 9),
        (N'Security', 10)
    ) AS defaults(Name, SortOrder)
)
UPDATE existing
SET SortOrder = defaults.SortOrder
FROM dbo.DecisionCategories AS existing
INNER JOIN CanonicalDecisionCategories AS defaults ON defaults.Name = existing.Name;

WITH CanonicalDecisionCategories AS
(
    SELECT Name, SortOrder
    FROM (VALUES
        (N'Architecture', 1),
        (N'Code Standards', 2),
        (N'Product', 3),
        (N'Data', 4),
        (N'Infrastructure', 5),
        (N'AI / Prompting', 6),
        (N'UX / UI', 7),
        (N'Workflow / Process', 8),
        (N'Integration', 9),
        (N'Security', 10)
    ) AS defaults(Name, SortOrder)
)
INSERT INTO dbo.DecisionCategories (Name, SortOrder)
SELECT defaults.Name, defaults.SortOrder
FROM CanonicalDecisionCategories AS defaults
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.DecisionCategories AS existing
    WHERE existing.Name = defaults.Name
);

WITH CanonicalDecisionStatuses AS
(
    SELECT Name, SortOrder
    FROM (VALUES
        (N'Proposed', 1),
        (N'Accepted', 2),
        (N'Superseded', 3),
        (N'Rejected', 4)
    ) AS defaults(Name, SortOrder)
)
UPDATE existing
SET SortOrder = defaults.SortOrder
FROM dbo.DecisionStatuses AS existing
INNER JOIN CanonicalDecisionStatuses AS defaults ON defaults.Name = existing.Name;

WITH CanonicalDecisionStatuses AS
(
    SELECT Name, SortOrder
    FROM (VALUES
        (N'Proposed', 1),
        (N'Accepted', 2),
        (N'Superseded', 3),
        (N'Rejected', 4)
    ) AS defaults(Name, SortOrder)
)
INSERT INTO dbo.DecisionStatuses (Name, SortOrder)
SELECT defaults.Name, defaults.SortOrder
FROM CanonicalDecisionStatuses AS defaults
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.DecisionStatuses AS existing
    WHERE existing.Name = defaults.Name
);
GO
