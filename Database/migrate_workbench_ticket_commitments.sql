/* Workbench v0.1 PR-04B: atomic permanent-ticket commitment receipts and mappings. */

IF COL_LENGTH(N'dbo.TicketProposalSets', N'CommittedRevision') IS NULL
    ALTER TABLE dbo.TicketProposalSets ADD CommittedRevision BIGINT NULL;
GO

IF COL_LENGTH(N'dbo.TicketProposalSets', N'CommittedByActorUserId') IS NULL
    ALTER TABLE dbo.TicketProposalSets ADD CommittedByActorUserId INT NULL;
GO

IF COL_LENGTH(N'dbo.TicketProposalSets', N'CommittedByClientOperationId') IS NULL
    ALTER TABLE dbo.TicketProposalSets ADD CommittedByClientOperationId UNIQUEIDENTIFIER NULL;
GO

IF COL_LENGTH(N'dbo.TicketProposalSets', N'CommittedAtUtc') IS NULL
    ALTER TABLE dbo.TicketProposalSets ADD CommittedAtUtc DATETIME2(7) NULL;
GO

IF OBJECT_ID(N'dbo.CK_TicketProposalSets_Status', N'C') IS NOT NULL
    ALTER TABLE dbo.TicketProposalSets DROP CONSTRAINT CK_TicketProposalSets_Status;
GO

ALTER TABLE dbo.TicketProposalSets WITH CHECK
    ADD CONSTRAINT CK_TicketProposalSets_Status
    CHECK (Status IN (N'Ready', N'NeedsInput', N'Committed'));
GO

IF OBJECT_ID(N'dbo.CK_TicketProposalSets_CommitmentState', N'C') IS NOT NULL
    ALTER TABLE dbo.TicketProposalSets DROP CONSTRAINT CK_TicketProposalSets_CommitmentState;
GO

ALTER TABLE dbo.TicketProposalSets WITH CHECK
    ADD CONSTRAINT CK_TicketProposalSets_CommitmentState CHECK
    (
        (
            Status=N'Committed'
            AND CommittedRevision IS NOT NULL
            AND CommittedRevision=CurrentRevision
            AND CommittedByActorUserId IS NOT NULL
            AND CommittedByClientOperationId IS NOT NULL
            AND CommittedAtUtc IS NOT NULL
        )
        OR
        (
            Status<>N'Committed'
            AND CommittedRevision IS NULL
            AND CommittedByActorUserId IS NULL
            AND CommittedByClientOperationId IS NULL
            AND CommittedAtUtc IS NULL
        )
    );
GO

IF OBJECT_ID(N'dbo.FK_TicketProposalSets_CommittingActor', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.TicketProposalSets WITH CHECK
        ADD CONSTRAINT FK_TicketProposalSets_CommittingActor
        FOREIGN KEY (CommittedByActorUserId) REFERENCES dbo.Users(Id);
    ALTER TABLE dbo.TicketProposalSets CHECK CONSTRAINT FK_TicketProposalSets_CommittingActor;
END;
GO

IF OBJECT_ID(N'dbo.CK_TicketProposalSetRevisions_ChangeKind', N'C') IS NOT NULL
    ALTER TABLE dbo.TicketProposalSetRevisions
        DROP CONSTRAINT CK_TicketProposalSetRevisions_ChangeKind;
GO

ALTER TABLE dbo.TicketProposalSetRevisions WITH CHECK
    ADD CONSTRAINT CK_TicketProposalSetRevisions_ChangeKind CHECK
    (
        ChangeKind IN
        (
            N'Generated', N'Regenerated', N'Edited', N'Reordered',
            N'Removed', N'IssueResolved', N'Committed'
        )
    );
GO

IF OBJECT_ID(N'dbo.UQ_TicketProposalSetRevisions_ProjectSetRevisionHash', N'UQ') IS NULL
    ALTER TABLE dbo.TicketProposalSetRevisions WITH CHECK
        ADD CONSTRAINT UQ_TicketProposalSetRevisions_ProjectSetRevisionHash
        UNIQUE (TenantId, ProjectId, TicketProposalSetId, Revision, SnapshotHash);
GO

/*
   ProjectTickets historically had independent project and tenant foreign keys.
   The Workbench mappings require one checked project scope and one exact composite
   candidate key so a ticket from another project or tenant cannot be substituted.
*/
IF OBJECT_ID(N'dbo.UQ_ProjectTickets_TenantProjectId', N'UQ') IS NULL
    ALTER TABLE dbo.ProjectTickets WITH CHECK
        ADD CONSTRAINT UQ_ProjectTickets_TenantProjectId
        UNIQUE (TenantId, ProjectId, Id);
GO

IF OBJECT_ID(N'dbo.FK_ProjectTickets_ProjectScope', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.ProjectTickets WITH CHECK
        ADD CONSTRAINT FK_ProjectTickets_ProjectScope
        FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId);
    ALTER TABLE dbo.ProjectTickets CHECK CONSTRAINT FK_ProjectTickets_ProjectScope;
END;
GO

/* Keep this migration safe when exercised directly by a focused SQL fixture. */
IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id=OBJECT_ID(N'dbo.WorkbenchWriteLeases')
      AND name=N'UX_WorkbenchWriteLeases_ExactFence'
)
    CREATE UNIQUE INDEX UX_WorkbenchWriteLeases_ExactFence
        ON dbo.WorkbenchWriteLeases(TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch);
GO

IF OBJECT_ID(N'dbo.TicketProposalCommitments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TicketProposalCommitments
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TicketProposalCommitments PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        TicketProposalSetId UNIQUEIDENTIFIER NOT NULL,
        ReviewedRevision BIGINT NOT NULL,
        CommittedRevision BIGINT NOT NULL,
        ReviewedSnapshotHash CHAR(64) NOT NULL,
        ActorUserId INT NOT NULL,
        WorkbenchSessionId BIGINT NOT NULL,
        LeaseEpoch BIGINT NOT NULL,
        ClientOperationId UNIQUEIDENTIFIER NOT NULL,
        PayloadHash CHAR(64) NOT NULL,
        TicketCount TINYINT NOT NULL,
        CommittedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_TicketProposalCommitments_CommittedAtUtc
            DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_TicketProposalCommitments_ProjectId UNIQUE
            (TenantId, ProjectId, Id),
        CONSTRAINT UQ_TicketProposalCommitments_ProposalSet UNIQUE
            (TenantId, ProjectId, TicketProposalSetId),
        CONSTRAINT UQ_TicketProposalCommitments_ClientOperation UNIQUE
            (TenantId, ActorUserId, ClientOperationId),
        CONSTRAINT FK_TicketProposalCommitments_Set
            FOREIGN KEY (TenantId, ProjectId, TicketProposalSetId)
            REFERENCES dbo.TicketProposalSets(TenantId, ProjectId, Id),
        CONSTRAINT FK_TicketProposalCommitments_ReviewedRevision
            FOREIGN KEY
                (TenantId, ProjectId, TicketProposalSetId, ReviewedRevision, ReviewedSnapshotHash)
            REFERENCES dbo.TicketProposalSetRevisions
                (TenantId, ProjectId, TicketProposalSetId, Revision, SnapshotHash),
        CONSTRAINT FK_TicketProposalCommitments_CommittedRevision
            FOREIGN KEY (TenantId, ProjectId, TicketProposalSetId, CommittedRevision)
            REFERENCES dbo.TicketProposalSetRevisions
                (TenantId, ProjectId, TicketProposalSetId, Revision),
        CONSTRAINT FK_TicketProposalCommitments_ExactFence
            FOREIGN KEY (TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch)
            REFERENCES dbo.WorkbenchWriteLeases
                (TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch),
        CONSTRAINT FK_TicketProposalCommitments_Actor
            FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_TicketProposalCommitments_Revisions CHECK
            (ReviewedRevision > 0 AND CommittedRevision=ReviewedRevision+1),
        CONSTRAINT CK_TicketProposalCommitments_ReviewedSnapshotHash CHECK
            (LEN(ReviewedSnapshotHash)=64
             AND ReviewedSnapshotHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_TicketProposalCommitments_PayloadHash CHECK
            (LEN(PayloadHash)=64
             AND PayloadHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_TicketProposalCommitments_TicketCount CHECK
            (TicketCount BETWEEN 1 AND 5)
    );

    CREATE INDEX IX_TicketProposalCommitments_ProjectTime
        ON dbo.TicketProposalCommitments(TenantId, ProjectId, CommittedAtUtc DESC, Id);
END;
GO

IF OBJECT_ID(N'dbo.TicketProposalCommitmentTickets', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TicketProposalCommitmentTickets
    (
        Id BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TicketProposalCommitmentTickets PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        TicketProposalCommitmentId UNIQUEIDENTIFIER NOT NULL,
        TicketProposalId UNIQUEIDENTIFIER NOT NULL,
        ProjectTicketId BIGINT NOT NULL,
        SuggestedOrder INT NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL
            CONSTRAINT DF_TicketProposalCommitmentTickets_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_TicketProposalCommitmentTickets_ExactMapping UNIQUE
            (TenantId, ProjectId, TicketProposalCommitmentId, TicketProposalId, ProjectTicketId),
        CONSTRAINT UQ_TicketProposalCommitmentTickets_Proposal UNIQUE
            (TenantId, ProjectId, TicketProposalCommitmentId, TicketProposalId),
        CONSTRAINT UQ_TicketProposalCommitmentTickets_ProjectTicket UNIQUE
            (TenantId, ProjectId, TicketProposalCommitmentId, ProjectTicketId),
        CONSTRAINT UQ_TicketProposalCommitmentTickets_Order UNIQUE
            (TenantId, ProjectId, TicketProposalCommitmentId, SuggestedOrder),
        CONSTRAINT FK_TicketProposalCommitmentTickets_Commitment
            FOREIGN KEY (TenantId, ProjectId, TicketProposalCommitmentId)
            REFERENCES dbo.TicketProposalCommitments(TenantId, ProjectId, Id),
        CONSTRAINT FK_TicketProposalCommitmentTickets_ProjectTicket
            FOREIGN KEY (TenantId, ProjectId, ProjectTicketId)
            REFERENCES dbo.ProjectTickets(TenantId, ProjectId, Id),
        CONSTRAINT CK_TicketProposalCommitmentTickets_Order CHECK
            (SuggestedOrder BETWEEN 1 AND 5)
    );
END;
GO

IF OBJECT_ID(N'dbo.TicketProposalCommitmentDependencies', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TicketProposalCommitmentDependencies
    (
        Id BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TicketProposalCommitmentDependencies PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        TicketProposalCommitmentId UNIQUEIDENTIFIER NOT NULL,
        DependentTicketProposalId UNIQUEIDENTIFIER NOT NULL,
        DependentProjectTicketId BIGINT NOT NULL,
        DependsOnTicketProposalId UNIQUEIDENTIFIER NOT NULL,
        DependsOnProjectTicketId BIGINT NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL
            CONSTRAINT DF_TicketProposalCommitmentDependencies_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_TicketProposalCommitmentDependencies_Edge UNIQUE
            (
                TenantId, ProjectId, TicketProposalCommitmentId,
                DependentTicketProposalId, DependsOnTicketProposalId
            ),
        CONSTRAINT FK_TicketProposalCommitmentDependencies_DependentMapping
            FOREIGN KEY
            (
                TenantId, ProjectId, TicketProposalCommitmentId,
                DependentTicketProposalId, DependentProjectTicketId
            )
            REFERENCES dbo.TicketProposalCommitmentTickets
            (
                TenantId, ProjectId, TicketProposalCommitmentId,
                TicketProposalId, ProjectTicketId
            ),
        CONSTRAINT FK_TicketProposalCommitmentDependencies_DependsOnMapping
            FOREIGN KEY
            (
                TenantId, ProjectId, TicketProposalCommitmentId,
                DependsOnTicketProposalId, DependsOnProjectTicketId
            )
            REFERENCES dbo.TicketProposalCommitmentTickets
            (
                TenantId, ProjectId, TicketProposalCommitmentId,
                TicketProposalId, ProjectTicketId
            ),
        CONSTRAINT CK_TicketProposalCommitmentDependencies_NoSelfDependency CHECK
            (
                DependentTicketProposalId<>DependsOnTicketProposalId
                AND DependentProjectTicketId<>DependsOnProjectTicketId
            )
    );
END;
GO

CREATE OR ALTER TRIGGER dbo.trg_TicketProposalCommitments_AppendOnly
ON dbo.TicketProposalCommitments
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51162, 'TicketProposalCommitments is append-only.', 1;
END;
GO

CREATE OR ALTER TRIGGER dbo.trg_TicketProposalCommitmentTickets_AppendOnly
ON dbo.TicketProposalCommitmentTickets
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51163, 'TicketProposalCommitmentTickets is append-only.', 1;
END;
GO

CREATE OR ALTER TRIGGER dbo.trg_TicketProposalCommitmentDependencies_AppendOnly
ON dbo.TicketProposalCommitmentDependencies
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51164, 'TicketProposalCommitmentDependencies is append-only.', 1;
END;
GO
