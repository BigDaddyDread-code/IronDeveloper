/* Workbench v0.1 PR-04A: trusted proposal-purpose AgentRuns and append-only proposal revisions. */

IF COL_LENGTH(N'dbo.WorkbenchAgentRuns', N'InvocationKind') IS NULL
    ALTER TABLE dbo.WorkbenchAgentRuns ADD InvocationKind NVARCHAR(64) NULL;
GO

UPDATE dbo.WorkbenchAgentRuns SET InvocationKind=N'Conversation' WHERE InvocationKind IS NULL;
GO

ALTER TABLE dbo.WorkbenchAgentRuns ALTER COLUMN InvocationKind NVARCHAR(64) NOT NULL;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.default_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.WorkbenchAgentRuns')
      AND name=N'DF_WorkbenchAgentRuns_InvocationKind'
)
    ALTER TABLE dbo.WorkbenchAgentRuns ADD CONSTRAINT DF_WorkbenchAgentRuns_InvocationKind
        DEFAULT N'Conversation' FOR InvocationKind;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.WorkbenchAgentRuns')
      AND name=N'CK_WorkbenchAgentRuns_InvocationKind'
)
    ALTER TABLE dbo.WorkbenchAgentRuns ADD CONSTRAINT CK_WorkbenchAgentRuns_InvocationKind
        CHECK (InvocationKind IN (N'Conversation', N'TicketProposalGeneration', N'TicketProposalRegeneration'));
GO

IF COL_LENGTH(N'dbo.WorkbenchAgentRuns', N'TicketProposalSetId') IS NULL
    ALTER TABLE dbo.WorkbenchAgentRuns ADD TicketProposalSetId UNIQUEIDENTIFIER NULL;
GO

IF COL_LENGTH(N'dbo.WorkbenchAgentRuns', N'TicketInstruction') IS NULL
    ALTER TABLE dbo.WorkbenchAgentRuns ADD TicketInstruction NVARCHAR(MAX) NULL;
GO

IF COL_LENGTH(N'dbo.WorkbenchAgentRuns', N'TicketProposalRevision') IS NULL
    ALTER TABLE dbo.WorkbenchAgentRuns ADD TicketProposalRevision BIGINT NULL;
GO

IF COL_LENGTH(N'dbo.WorkbenchAgentRuns', N'MaterializedTicketProposalRevision') IS NULL
    ALTER TABLE dbo.WorkbenchAgentRuns ADD MaterializedTicketProposalRevision BIGINT NULL;
GO

IF NOT EXISTS
(
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id=OBJECT_ID(N'dbo.WorkbenchAgentRuns')
      AND name=N'CK_WorkbenchAgentRuns_TicketProposalPurpose'
)
BEGIN
    ALTER TABLE dbo.WorkbenchAgentRuns ADD CONSTRAINT CK_WorkbenchAgentRuns_TicketProposalPurpose CHECK
    (
        (InvocationKind=N'Conversation' AND TicketInstruction IS NULL AND TicketProposalSetId IS NULL
            AND TicketProposalRevision IS NULL AND MaterializedTicketProposalRevision IS NULL)
        OR
        (InvocationKind=N'TicketProposalGeneration' AND TicketProposalRevision IS NULL)
        OR
        (InvocationKind=N'TicketProposalRegeneration' AND TicketProposalSetId IS NOT NULL
            AND TicketProposalRevision IS NOT NULL AND TicketProposalRevision > 0)
    );
END;
GO

IF OBJECT_ID(N'dbo.TicketProposalSets', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TicketProposalSets
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TicketProposalSets PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        WorkbenchSessionId BIGINT NOT NULL,
        LeaseEpoch BIGINT NOT NULL,
        CurrentRevision BIGINT NOT NULL,
        BasedOnUnderstandingRevision BIGINT NOT NULL,
        Status NVARCHAR(32) NOT NULL,
        SplitReason NVARCHAR(2000) NULL,
        SourceMessageIdsJson NVARCHAR(MAX) NOT NULL,
        CreatedByAgentRunId UNIQUEIDENTIFIER NOT NULL,
        CreatedByActorUserId INT NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL,
        UpdatedAtUtc DATETIME2(7) NOT NULL,
        RowVersion ROWVERSION NOT NULL,
        CONSTRAINT UQ_TicketProposalSets_TenantProjectId UNIQUE (TenantId, ProjectId, Id),
        CONSTRAINT UQ_TicketProposalSets_CreatingRun UNIQUE (CreatedByAgentRunId),
        CONSTRAINT FK_TicketProposalSets_Project
            FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_TicketProposalSets_Session
            FOREIGN KEY (TenantId, ProjectId, WorkbenchSessionId)
            REFERENCES dbo.WorkbenchSessions(TenantId, ProjectId, Id),
        CONSTRAINT FK_TicketProposalSets_ExactFence
            FOREIGN KEY (TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch)
            REFERENCES dbo.WorkbenchWriteLeases(TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch),
        CONSTRAINT FK_TicketProposalSets_Understanding
            FOREIGN KEY (TenantId, ProjectId, BasedOnUnderstandingRevision)
            REFERENCES dbo.ProjectUnderstandings(TenantId, ProjectId, Revision),
        CONSTRAINT FK_TicketProposalSets_CreatingRun
            FOREIGN KEY (CreatedByAgentRunId) REFERENCES dbo.WorkbenchAgentRuns(AgentRunId),
        CONSTRAINT FK_TicketProposalSets_CreatingActor
            FOREIGN KEY (CreatedByActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_TicketProposalSets_Revision CHECK
            (CurrentRevision > 0 AND BasedOnUnderstandingRevision > 0),
        CONSTRAINT CK_TicketProposalSets_Status CHECK
            (Status IN (N'Ready', N'NeedsInput')),
        CONSTRAINT CK_TicketProposalSets_SourceMessages CHECK
            (ISJSON(SourceMessageIdsJson)=1),
        CONSTRAINT CK_TicketProposalSets_Timestamps CHECK
            (UpdatedAtUtc >= CreatedAtUtc)
    );

    CREATE INDEX IX_TicketProposalSets_ProjectCurrent
        ON dbo.TicketProposalSets(TenantId, ProjectId, UpdatedAtUtc DESC, Id DESC);
END;
GO

IF OBJECT_ID(N'dbo.TicketProposalSetRevisions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TicketProposalSetRevisions
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TicketProposalSetRevisions PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        TicketProposalSetId UNIQUEIDENTIFIER NOT NULL,
        Revision BIGINT NOT NULL,
        SnapshotJson NVARCHAR(MAX) NOT NULL,
        SnapshotHash CHAR(64) NOT NULL,
        ActorUserId INT NOT NULL,
        AgentRunId UNIQUEIDENTIFIER NULL,
        ChangeKind NVARCHAR(32) NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_TicketProposalSetRevisions_CreatedAtUtc
            DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_TicketProposalSetRevisions_ProjectSetRevision UNIQUE
            (TenantId, ProjectId, TicketProposalSetId, Revision),
        CONSTRAINT FK_TicketProposalSetRevisions_Set
            FOREIGN KEY (TenantId, ProjectId, TicketProposalSetId)
            REFERENCES dbo.TicketProposalSets(TenantId, ProjectId, Id),
        CONSTRAINT FK_TicketProposalSetRevisions_Project
            FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_TicketProposalSetRevisions_Actor
            FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_TicketProposalSetRevisions_AgentRun
            FOREIGN KEY (AgentRunId) REFERENCES dbo.WorkbenchAgentRuns(AgentRunId),
        CONSTRAINT CK_TicketProposalSetRevisions_Revision CHECK (Revision > 0),
        CONSTRAINT CK_TicketProposalSetRevisions_Snapshot CHECK (ISJSON(SnapshotJson)=1),
        CONSTRAINT CK_TicketProposalSetRevisions_Hash CHECK
            (LEN(SnapshotHash)=64 AND SnapshotHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_TicketProposalSetRevisions_ChangeKind CHECK
            (ChangeKind IN (N'Generated', N'Regenerated', N'Edited', N'Reordered', N'Removed', N'IssueResolved'))
    );

    CREATE INDEX IX_TicketProposalSetRevisions_ProjectHistory
        ON dbo.TicketProposalSetRevisions(TenantId, ProjectId, TicketProposalSetId, Revision DESC);
END;
GO

/*
   This migration was exercised in LocalTest while PR-04A was still in development.
   Keep reapplication safe for those preview catalogs by replacing the earlier,
   tenant-only parent keys with the final project-scoped keys.
*/
IF OBJECT_ID(N'dbo.UQ_TicketProposalSets_TenantId_Id', N'UQ') IS NOT NULL
   OR OBJECT_ID(N'dbo.UQ_TicketProposalSetRevisions_SetRevision', N'UQ') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.FK_TicketProposalSetRevisions_Set', N'F') IS NOT NULL
        ALTER TABLE dbo.TicketProposalSetRevisions
            DROP CONSTRAINT FK_TicketProposalSetRevisions_Set;

    IF OBJECT_ID(N'dbo.UQ_TicketProposalSetRevisions_SetRevision', N'UQ') IS NOT NULL
        ALTER TABLE dbo.TicketProposalSetRevisions
            DROP CONSTRAINT UQ_TicketProposalSetRevisions_SetRevision;

    IF OBJECT_ID(N'dbo.UQ_TicketProposalSets_TenantId_Id', N'UQ') IS NOT NULL
        ALTER TABLE dbo.TicketProposalSets
            DROP CONSTRAINT UQ_TicketProposalSets_TenantId_Id;
END;
GO

IF OBJECT_ID(N'dbo.UQ_TicketProposalSets_TenantProjectId', N'UQ') IS NULL
    ALTER TABLE dbo.TicketProposalSets WITH CHECK
        ADD CONSTRAINT UQ_TicketProposalSets_TenantProjectId
        UNIQUE (TenantId, ProjectId, Id);
GO

IF OBJECT_ID(N'dbo.FK_TicketProposalSets_Understanding', N'F') IS NULL
    ALTER TABLE dbo.TicketProposalSets WITH CHECK
        ADD CONSTRAINT FK_TicketProposalSets_Understanding
        FOREIGN KEY (TenantId, ProjectId, BasedOnUnderstandingRevision)
        REFERENCES dbo.ProjectUnderstandings(TenantId, ProjectId, Revision);
GO

IF OBJECT_ID(N'dbo.UQ_TicketProposalSetRevisions_ProjectSetRevision', N'UQ') IS NULL
    ALTER TABLE dbo.TicketProposalSetRevisions WITH CHECK
        ADD CONSTRAINT UQ_TicketProposalSetRevisions_ProjectSetRevision
        UNIQUE (TenantId, ProjectId, TicketProposalSetId, Revision);
GO

IF OBJECT_ID(N'dbo.FK_TicketProposalSetRevisions_Set', N'F') IS NULL
    ALTER TABLE dbo.TicketProposalSetRevisions WITH CHECK
        ADD CONSTRAINT FK_TicketProposalSetRevisions_Set
        FOREIGN KEY (TenantId, ProjectId, TicketProposalSetId)
        REFERENCES dbo.TicketProposalSets(TenantId, ProjectId, Id);
GO

CREATE OR ALTER TRIGGER dbo.trg_TicketProposalSetRevisions_AppendOnly
ON dbo.TicketProposalSetRevisions
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51161, 'TicketProposalSetRevisions is append-only.', 1;
END;
GO
