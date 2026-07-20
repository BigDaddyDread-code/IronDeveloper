/* Workbench v0.1 PR-02C-B: typed project intent, explicit conflicts, and rename proposals. */

IF COL_LENGTH(N'dbo.ProjectUnderstandings', N'DocumentSchemaVersion') IS NULL
    ALTER TABLE dbo.ProjectUnderstandings ADD DocumentSchemaVersion INT NOT NULL
        CONSTRAINT DF_ProjectUnderstandings_DocumentSchemaVersion DEFAULT (1) WITH VALUES;
GO

IF COL_LENGTH(N'dbo.ProjectUnderstandings', N'BasedOnRevision') IS NULL
    ALTER TABLE dbo.ProjectUnderstandings ADD BasedOnRevision BIGINT NULL;
GO

IF COL_LENGTH(N'dbo.ProjectUnderstandings', N'CreatedByAgentRunId') IS NULL
    ALTER TABLE dbo.ProjectUnderstandings ADD CreatedByAgentRunId UNIQUEIDENTIFIER NULL;
GO

UPDATE dbo.ProjectUnderstandings
SET UnderstandingJson=N'{"schemaVersion":1,"facts":[],"conflicts":[],"openQuestions":[]}'
WHERE JSON_VALUE(UnderstandingJson, N'$.schemaVersion') IS NULL;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.ProjectUnderstandings')
      AND name=N'CK_ProjectUnderstandings_DocumentSchemaVersion'
)
    ALTER TABLE dbo.ProjectUnderstandings WITH CHECK
        ADD CONSTRAINT CK_ProjectUnderstandings_DocumentSchemaVersion CHECK (DocumentSchemaVersion=1);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.ProjectUnderstandings')
      AND name=N'CK_ProjectUnderstandings_BasedOnRevision'
)
    ALTER TABLE dbo.ProjectUnderstandings WITH CHECK
        ADD CONSTRAINT CK_ProjectUnderstandings_BasedOnRevision CHECK
        (BasedOnRevision IS NULL OR (BasedOnRevision > 0 AND BasedOnRevision < Revision));
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id=OBJECT_ID(N'dbo.ProjectUnderstandings')
      AND name=N'FK_ProjectUnderstandings_AgentRun'
)
    ALTER TABLE dbo.ProjectUnderstandings WITH CHECK
        ADD CONSTRAINT FK_ProjectUnderstandings_AgentRun
        FOREIGN KEY (CreatedByAgentRunId) REFERENCES dbo.WorkbenchAgentRuns(AgentRunId);
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.ProjectUnderstandings')
      AND name=N'UX_ProjectUnderstandings_AgentRun'
)
    CREATE UNIQUE INDEX UX_ProjectUnderstandings_AgentRun
        ON dbo.ProjectUnderstandings(CreatedByAgentRunId)
        WHERE CreatedByAgentRunId IS NOT NULL;
GO

IF OBJECT_ID(N'dbo.ProjectRenameProposals', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectRenameProposals
    (
        ProposalId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ProjectRenameProposals PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        ProposedName NVARCHAR(200) NOT NULL,
        Status NVARCHAR(30) NOT NULL,
        BasedOnProjectName NVARCHAR(200) NOT NULL,
        BasedOnUnderstandingRevision BIGINT NOT NULL,
        ProposedByAgentRunId UNIQUEIDENTIFIER NOT NULL,
        InitiatingActorUserId INT NOT NULL,
        SourceMessageIdsJson NVARCHAR(MAX) NOT NULL,
        EvidenceSummary NVARCHAR(1000) NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_ProjectRenameProposals_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        DecisionByActorUserId INT NULL,
        DecisionClientOperationRecordId BIGINT NULL,
        DecisionAtUtc DATETIME2(7) NULL,
        CONSTRAINT FK_ProjectRenameProposals_Project
            FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_ProjectRenameProposals_Understanding
            FOREIGN KEY (TenantId, ProjectId, BasedOnUnderstandingRevision)
            REFERENCES dbo.ProjectUnderstandings(TenantId, ProjectId, Revision),
        CONSTRAINT FK_ProjectRenameProposals_AgentRun
            FOREIGN KEY (ProposedByAgentRunId) REFERENCES dbo.WorkbenchAgentRuns(AgentRunId),
        CONSTRAINT FK_ProjectRenameProposals_InitiatingActor
            FOREIGN KEY (InitiatingActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_ProjectRenameProposals_DecisionActor
            FOREIGN KEY (DecisionByActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_ProjectRenameProposals_DecisionOperation
            FOREIGN KEY (DecisionClientOperationRecordId) REFERENCES dbo.ClientOperations(Id),
        CONSTRAINT CK_ProjectRenameProposals_Status CHECK
            (Status IN (N'Pending', N'Accepted', N'Rejected', N'Superseded')),
        CONSTRAINT CK_ProjectRenameProposals_Name CHECK
            (LEN(LTRIM(RTRIM(ProposedName))) BETWEEN 1 AND 200),
        CONSTRAINT CK_ProjectRenameProposals_Sources CHECK (ISJSON(SourceMessageIdsJson)=1),
        CONSTRAINT CK_ProjectRenameProposals_Decision CHECK
            ((Status=N'Pending' AND DecisionByActorUserId IS NULL
              AND DecisionClientOperationRecordId IS NULL AND DecisionAtUtc IS NULL)
             OR
             (Status<>N'Pending' AND DecisionAtUtc IS NOT NULL))
    );

    CREATE UNIQUE INDEX UX_ProjectRenameProposals_AgentRun
        ON dbo.ProjectRenameProposals(ProposedByAgentRunId);
    CREATE UNIQUE INDEX UX_ProjectRenameProposals_PendingProject
        ON dbo.ProjectRenameProposals(TenantId, ProjectId)
        WHERE Status=N'Pending';
    CREATE INDEX IX_ProjectRenameProposals_ProjectHistory
        ON dbo.ProjectRenameProposals(TenantId, ProjectId, CreatedAtUtc DESC);
END;
GO
