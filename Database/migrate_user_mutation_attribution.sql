IF OBJECT_ID(N'dbo.UserMutationAttribution', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserMutationAttribution
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserMutationAttribution PRIMARY KEY,
        ActorUserId INT NOT NULL,
        TenantId INT NULL,
        ProjectId NVARCHAR(128) NULL,
        CorrelationId NVARCHAR(128) NOT NULL,
        CausationId NVARCHAR(128) NULL,
        TimestampUtc DATETIME2(7) NOT NULL,
        SourceSurface NVARCHAR(80) NOT NULL,
        SourceClient NVARCHAR(120) NOT NULL,
        Method NVARCHAR(12) NOT NULL,
        Route NVARCHAR(500) NOT NULL,
        Phase NVARCHAR(24) NOT NULL,
        StatusCode INT NULL,
        CONSTRAINT FK_UserMutationAttribution_Actor FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_UserMutationAttribution_Phase CHECK (Phase IN (N'Attempted', N'Completed', N'Refused', N'Failed'))
    );

    CREATE INDEX IX_UserMutationAttribution_ScopeTime
        ON dbo.UserMutationAttribution(TenantId, ProjectId, TimestampUtc DESC);

    CREATE INDEX IX_UserMutationAttribution_Correlation
        ON dbo.UserMutationAttribution(CorrelationId, TimestampUtc);
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_UserMutationAttribution_BlockUpdateDelete
ON dbo.UserMutationAttribution
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    THROW 53200, 'User mutation attribution is append-only.', 1;
END;
GO
