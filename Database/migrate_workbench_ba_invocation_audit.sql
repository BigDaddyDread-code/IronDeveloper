/* Workbench v0.1 PR-02C-A: safe append-only Business Analyst invocation metadata. */

IF OBJECT_ID(N'dbo.WorkbenchBusinessAnalystPreparations', N'U') IS NULL
    THROW 51023, 'Workbench Business Analyst invocation audit requires preparation provenance.', 1;
GO

IF OBJECT_ID(N'dbo.WorkbenchBusinessAnalystInvocationAudits', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkbenchBusinessAnalystInvocationAudits
    (
        Id BIGINT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_WorkbenchBusinessAnalystInvocationAudits PRIMARY KEY,
        PreparationId BIGINT NOT NULL,
        AgentRunId UNIQUEIDENTIFIER NOT NULL,
        ClaimToken UNIQUEIDENTIFIER NOT NULL,
        AttemptNumber INT NOT NULL,
        SafeRequestId NVARCHAR(100) NOT NULL,
        ProviderRequestId NVARCHAR(200) NULL,
        UsageReported BIT NOT NULL,
        InputTokens INT NULL,
        OutputTokens INT NULL,
        DurationMilliseconds BIGINT NOT NULL,
        Outcome NVARCHAR(20) NOT NULL,
        FailureCategory NVARCHAR(100) NULL,
        InvocationHash CHAR(64) NOT NULL,
        CompletedAtUtc DATETIME2(7) NOT NULL,
        RecordedAtUtc DATETIME2(7) NOT NULL
            CONSTRAINT DF_WorkbenchBusinessAnalystInvocationAudits_RecordedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_WorkbenchBusinessAnalystInvocationAudits_ExactPreparation
            FOREIGN KEY (PreparationId, AgentRunId, ClaimToken, AttemptNumber)
            REFERENCES dbo.WorkbenchBusinessAnalystPreparations(Id, AgentRunId, ClaimToken, AttemptNumber),
        CONSTRAINT UQ_WorkbenchBusinessAnalystInvocationAudits_Preparation UNIQUE (PreparationId),
        CONSTRAINT CK_WorkbenchBusinessAnalystInvocationAudits_AttemptNumber CHECK (AttemptNumber > 0),
        CONSTRAINT CK_WorkbenchBusinessAnalystInvocationAudits_SafeRequestId CHECK
            (LEN(SafeRequestId) BETWEEN 1 AND 100 AND
             SafeRequestId NOT LIKE N'%' + NCHAR(10) + N'%' AND
             SafeRequestId NOT LIKE N'%' + NCHAR(13) + N'%'),
        CONSTRAINT CK_WorkbenchBusinessAnalystInvocationAudits_ProviderRequestId CHECK
            (ProviderRequestId IS NULL OR
             (LEN(ProviderRequestId) BETWEEN 1 AND 200 AND
              ProviderRequestId NOT LIKE N'%' + NCHAR(10) + N'%' AND
              ProviderRequestId NOT LIKE N'%' + NCHAR(13) + N'%')),
        CONSTRAINT CK_WorkbenchBusinessAnalystInvocationAudits_Usage CHECK
            ((UsageReported=1 AND InputTokens IS NOT NULL AND OutputTokens IS NOT NULL
                              AND InputTokens>=0 AND OutputTokens>=0)
             OR
             (UsageReported=0 AND InputTokens IS NULL AND OutputTokens IS NULL)),
        CONSTRAINT CK_WorkbenchBusinessAnalystInvocationAudits_Duration CHECK
            (DurationMilliseconds BETWEEN 0 AND 3600000),
        CONSTRAINT CK_WorkbenchBusinessAnalystInvocationAudits_Outcome CHECK
            (Outcome IN (N'Succeeded', N'Failed')),
        CONSTRAINT CK_WorkbenchBusinessAnalystInvocationAudits_Failure CHECK
            ((Outcome=N'Succeeded' AND FailureCategory IS NULL)
             OR (Outcome=N'Failed' AND FailureCategory IS NOT NULL
                                    AND LEN(FailureCategory) BETWEEN 1 AND 100
                                    AND FailureCategory NOT LIKE N'%' + NCHAR(10) + N'%'
                                    AND FailureCategory NOT LIKE N'%' + NCHAR(13) + N'%')),
        CONSTRAINT CK_WorkbenchBusinessAnalystInvocationAudits_Timeline CHECK
            (CompletedAtUtc <= DATEADD(MINUTE, 5, RecordedAtUtc)),
        CONSTRAINT CK_WorkbenchBusinessAnalystInvocationAudits_Hash CHECK
            (LEN(InvocationHash)=64 AND
             InvocationHash COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%')
    );
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_WorkbenchBusinessAnalystInvocationAudits_BlockUpdateDelete
ON dbo.WorkbenchBusinessAnalystInvocationAudits
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51024, 'Workbench Business Analyst invocation provenance is append-only.', 1;
END;
GO
