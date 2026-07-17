/* Workbench v0.1 project-first startup state. No repository path or filesystem work is performed here. */

IF OBJECT_ID(N'dbo.ProjectLifecyclePhases', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectLifecyclePhases
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProjectLifecyclePhases PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        Revision BIGINT NOT NULL,
        Phase NVARCHAR(50) NOT NULL,
        ChangedByActorUserId INT NOT NULL,
        ChangedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_ProjectLifecyclePhases_ChangedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ProjectLifecyclePhases_Project FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_ProjectLifecyclePhases_Actor FOREIGN KEY (ChangedByActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_ProjectLifecyclePhases_Phase CHECK (Phase IN (N'Shaping', N'Planning', N'Executing', N'Closed')),
        CONSTRAINT UQ_ProjectLifecyclePhases_Revision UNIQUE (TenantId, ProjectId, Revision)
    );
END;
GO

IF OBJECT_ID(N'dbo.ProjectUnderstandings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectUnderstandings
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProjectUnderstandings PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        Revision BIGINT NOT NULL,
        Status NVARCHAR(50) NOT NULL,
        UnderstandingJson NVARCHAR(MAX) NOT NULL,
        CreatedByActorUserId INT NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_ProjectUnderstandings_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ProjectUnderstandings_Project FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_ProjectUnderstandings_Actor FOREIGN KEY (CreatedByActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_ProjectUnderstandings_Status CHECK (Status IN (N'Draft', N'Confirmed', N'Superseded')),
        CONSTRAINT CK_ProjectUnderstandings_Json CHECK (ISJSON(UnderstandingJson) = 1),
        CONSTRAINT UQ_ProjectUnderstandings_Revision UNIQUE (TenantId, ProjectId, Revision)
    );
END;
GO

IF OBJECT_ID(N'dbo.ProjectReadinessAssessments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectReadinessAssessments
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProjectReadinessAssessments PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        Revision BIGINT NOT NULL,
        ExecutionReadiness NVARCHAR(50) NOT NULL,
        ReasonCode NVARCHAR(100) NOT NULL,
        Summary NVARCHAR(500) NOT NULL,
        AssessedByActorUserId INT NOT NULL,
        AssessedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_ProjectReadinessAssessments_AssessedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ProjectReadinessAssessments_Project FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_ProjectReadinessAssessments_Actor FOREIGN KEY (AssessedByActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_ProjectReadinessAssessments_State CHECK (ExecutionReadiness IN (N'NotConfigured', N'Blocked', N'Ready', N'Stale')),
        CONSTRAINT UQ_ProjectReadinessAssessments_Revision UNIQUE (TenantId, ProjectId, Revision)
    );
END;
GO

IF OBJECT_ID(N'dbo.WorkbenchSessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkbenchSessions
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_WorkbenchSessions PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        Status NVARCHAR(50) NOT NULL,
        CreatedByActorUserId INT NOT NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_WorkbenchSessions_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        ClosedAtUtc DATETIME2(7) NULL,
        CONSTRAINT FK_WorkbenchSessions_Project FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_WorkbenchSessions_Actor FOREIGN KEY (CreatedByActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_WorkbenchSessions_Status CHECK (Status IN (N'Active', N'Historical'))
    );
    CREATE INDEX IX_WorkbenchSessions_Project ON dbo.WorkbenchSessions(TenantId, ProjectId, CreatedAtUtc DESC);
END;
GO

IF OBJECT_ID(N'dbo.WorkbenchWriteLeases', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkbenchWriteLeases
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WorkbenchWriteLeases PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        WorkbenchSessionId UNIQUEIDENTIFIER NOT NULL,
        HolderActorUserId INT NOT NULL,
        LeaseEpoch BIGINT NOT NULL,
        LeaseTokenHash CHAR(64) NOT NULL,
        AcquiredAtUtc DATETIME2(7) NOT NULL,
        HeartbeatAtUtc DATETIME2(7) NOT NULL,
        ExpiresAtUtc DATETIME2(7) NOT NULL,
        RevokedAtUtc DATETIME2(7) NULL,
        CONSTRAINT FK_WorkbenchWriteLeases_Project FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_WorkbenchWriteLeases_Session FOREIGN KEY (WorkbenchSessionId) REFERENCES dbo.WorkbenchSessions(Id),
        CONSTRAINT FK_WorkbenchWriteLeases_Holder FOREIGN KEY (HolderActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_WorkbenchWriteLeases_Epoch CHECK (LeaseEpoch > 0),
        CONSTRAINT UQ_WorkbenchWriteLeases_ProjectEpoch UNIQUE (TenantId, ProjectId, LeaseEpoch)
    );
    CREATE UNIQUE INDEX UX_WorkbenchWriteLeases_ActiveProject
        ON dbo.WorkbenchWriteLeases(TenantId, ProjectId)
        WHERE RevokedAtUtc IS NULL;
END;
GO

IF OBJECT_ID(N'dbo.ClientOperations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClientOperations
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ClientOperations PRIMARY KEY,
        TenantId INT NOT NULL,
        ActorUserId INT NOT NULL,
        OperationKind NVARCHAR(100) NOT NULL,
        ResourceScopeId NVARCHAR(200) NOT NULL,
        ClientOperationId UNIQUEIDENTIFIER NOT NULL,
        PayloadHash CHAR(64) NOT NULL,
        Status NVARCHAR(30) NOT NULL,
        ResultProjectId INT NULL,
        ResultWorkbenchSessionId UNIQUEIDENTIFIER NULL,
        CreatedAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_ClientOperations_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CompletedAtUtc DATETIME2(7) NULL,
        CONSTRAINT FK_ClientOperations_Tenant FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ClientOperations_Actor FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_ClientOperations_ResultProject FOREIGN KEY (ResultProjectId) REFERENCES dbo.Projects(Id),
        CONSTRAINT FK_ClientOperations_ResultSession FOREIGN KEY (ResultWorkbenchSessionId) REFERENCES dbo.WorkbenchSessions(Id),
        CONSTRAINT CK_ClientOperations_Status CHECK (Status IN (N'Pending', N'Completed', N'Failed')),
        CONSTRAINT CK_ClientOperations_PayloadHash CHECK (LEN(PayloadHash) = 64),
        CONSTRAINT UQ_ClientOperations_Scope UNIQUE
            (TenantId, ActorUserId, OperationKind, ResourceScopeId, ClientOperationId)
    );
END;
GO

IF OBJECT_ID(N'dbo.WorkbenchOutboxEvents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorkbenchOutboxEvents
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WorkbenchOutboxEvents PRIMARY KEY,
        EventId UNIQUEIDENTIFIER NOT NULL,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        WorkbenchSessionId UNIQUEIDENTIFIER NULL,
        EventKind NVARCHAR(100) NOT NULL,
        PayloadJson NVARCHAR(MAX) NOT NULL,
        ClientOperationId UNIQUEIDENTIFIER NOT NULL,
        OccurredAtUtc DATETIME2(7) NOT NULL CONSTRAINT DF_WorkbenchOutboxEvents_OccurredAtUtc DEFAULT SYSUTCDATETIME(),
        PublishedAtUtc DATETIME2(7) NULL,
        CONSTRAINT FK_WorkbenchOutboxEvents_Project FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_WorkbenchOutboxEvents_Session FOREIGN KEY (WorkbenchSessionId) REFERENCES dbo.WorkbenchSessions(Id),
        CONSTRAINT CK_WorkbenchOutboxEvents_Payload CHECK (ISJSON(PayloadJson) = 1),
        CONSTRAINT UQ_WorkbenchOutboxEvents_Event UNIQUE (EventId)
    );
    CREATE INDEX IX_WorkbenchOutboxEvents_Unpublished
        ON dbo.WorkbenchOutboxEvents(OccurredAtUtc, Id)
        WHERE PublishedAtUtc IS NULL;
END;
GO
