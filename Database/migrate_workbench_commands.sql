/* Workbench v0.1 PR-03: deterministic slash-command rejection audit. */

IF OBJECT_ID(N'dbo.WorkbenchCommandRejections', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkbenchCommandRejections
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WorkbenchCommandRejections PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        WorkbenchSessionId BIGINT NOT NULL,
        ActorUserId INT NOT NULL,
        LeaseEpoch BIGINT NOT NULL,
        ClientOperationRecordId BIGINT NOT NULL,
        ClientOperationId UNIQUEIDENTIFIER NOT NULL,
        RawCommandToken NVARCHAR(MAX) NOT NULL,
        PayloadHash CHAR(64) NOT NULL,
        ReasonCode NVARCHAR(100) NOT NULL,
        RejectedAtUtc DATETIME2(7) NOT NULL
            CONSTRAINT DF_WorkbenchCommandRejections_RejectedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_WorkbenchCommandRejections_Project
            FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_WorkbenchCommandRejections_Session
            FOREIGN KEY (TenantId, ProjectId, WorkbenchSessionId)
            REFERENCES dbo.WorkbenchSessions(TenantId, ProjectId, Id),
        CONSTRAINT FK_WorkbenchCommandRejections_ExactFence
            FOREIGN KEY (TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch)
            REFERENCES dbo.WorkbenchWriteLeases(TenantId, ProjectId, WorkbenchSessionId, LeaseEpoch),
        CONSTRAINT FK_WorkbenchCommandRejections_Actor
            FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_WorkbenchCommandRejections_ClientOperation
            FOREIGN KEY (ClientOperationRecordId) REFERENCES dbo.ClientOperations(Id),
        CONSTRAINT CK_WorkbenchCommandRejections_Token CHECK
            (LEN(RawCommandToken) BETWEEN 1 AND 20000 AND LEFT(RawCommandToken, 1)=N'/'),
        CONSTRAINT CK_WorkbenchCommandRejections_PayloadHash CHECK
            (LEN(PayloadHash)=64 AND PayloadHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'),
        CONSTRAINT CK_WorkbenchCommandRejections_Reason CHECK
            (ReasonCode=N'UnknownCommand'),
        CONSTRAINT UQ_WorkbenchCommandRejections_Operation UNIQUE
            (TenantId, ActorUserId, WorkbenchSessionId, ClientOperationId),
        CONSTRAINT UQ_WorkbenchCommandRejections_ClientOperationRecord UNIQUE
            (ClientOperationRecordId)
    );

    CREATE INDEX IX_WorkbenchCommandRejections_ProjectHistory
        ON dbo.WorkbenchCommandRejections(TenantId, ProjectId, RejectedAtUtc DESC, Id DESC);
END;
GO
