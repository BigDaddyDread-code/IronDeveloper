/* Project visibility and Work Item ownership are collaboration state, not workflow authority. */

IF OBJECT_ID(N'dbo.ProjectMembers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectMembers
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProjectMembers PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        UserId INT NOT NULL,
        ProjectRole NVARCHAR(50) NOT NULL,
        Status NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectMembers_Status DEFAULT N'Active',
        AddedByUserId INT NOT NULL,
        AddedUtc DATETIME2 NOT NULL CONSTRAINT DF_ProjectMembers_AddedUtc DEFAULT SYSUTCDATETIME(),
        RemovedByUserId INT NULL,
        RemovedUtc DATETIME2 NULL,
        CONSTRAINT FK_ProjectMembers_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ProjectMembers_Projects FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_ProjectMembers_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_ProjectMembers_AddedBy FOREIGN KEY (AddedByUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_ProjectMembers_RemovedBy FOREIGN KEY (RemovedByUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_ProjectMembers_Role CHECK (ProjectRole IN (N'Owner', N'Contributor', N'Viewer')),
        CONSTRAINT CK_ProjectMembers_Status CHECK (Status IN (N'Active', N'Removed')),
        CONSTRAINT UQ_ProjectMembers_ProjectUser UNIQUE (TenantId, ProjectId, UserId)
    );
END;
GO

INSERT INTO dbo.ProjectMembers (TenantId, ProjectId, UserId, ProjectRole, AddedByUserId)
SELECT p.TenantId, p.Id, tu.UserId,
       CASE WHEN tu.Role = N'Owner' THEN N'Owner' WHEN tu.Role = N'Viewer' THEN N'Viewer' ELSE N'Contributor' END,
       COALESCE(ownerUser.UserId, tu.UserId)
FROM dbo.Projects p
INNER JOIN dbo.TenantUsers tu ON tu.TenantId = p.TenantId
INNER JOIN dbo.Users u ON u.Id = tu.UserId AND u.IsActive = 1
OUTER APPLY (SELECT TOP (1) ownerTu.UserId FROM dbo.TenantUsers ownerTu WHERE ownerTu.TenantId=p.TenantId AND ownerTu.Role=N'Owner' ORDER BY ownerTu.Id) ownerUser
WHERE NOT EXISTS (SELECT 1 FROM dbo.ProjectMembers pm WHERE pm.TenantId=p.TenantId AND pm.ProjectId=p.Id AND pm.UserId=tu.UserId);
GO

IF OBJECT_ID(N'dbo.ProjectWorkItemCollaboration', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectWorkItemCollaboration
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProjectWorkItemCollaboration PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        WorkItemId BIGINT NOT NULL,
        AssigneeUserId INT NULL,
        WaitingOnUserId INT NULL,
        WaitingOnKind NVARCHAR(50) NULL,
        WaitingOnLabel NVARCHAR(200) NULL,
        Revision BIGINT NOT NULL CONSTRAINT DF_ProjectWorkItemCollaboration_Revision DEFAULT 1,
        UpdatedByUserId INT NOT NULL,
        UpdatedUtc DATETIME2 NOT NULL CONSTRAINT DF_ProjectWorkItemCollaboration_UpdatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ProjectWorkItemCollaboration_Project FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_ProjectWorkItemCollaboration_WorkItem FOREIGN KEY (WorkItemId) REFERENCES dbo.ProjectTickets(Id),
        CONSTRAINT FK_ProjectWorkItemCollaboration_Assignee FOREIGN KEY (AssigneeUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_ProjectWorkItemCollaboration_WaitingOn FOREIGN KEY (WaitingOnUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_ProjectWorkItemCollaboration_UpdatedBy FOREIGN KEY (UpdatedByUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT UQ_ProjectWorkItemCollaboration_WorkItem UNIQUE (TenantId, ProjectId, WorkItemId)
    );
END;
GO

IF OBJECT_ID(N'dbo.ProjectWorkItemFollowers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectWorkItemFollowers
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProjectWorkItemFollowers PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        WorkItemId BIGINT NOT NULL,
        UserId INT NOT NULL,
        AddedByUserId INT NOT NULL,
        AddedUtc DATETIME2 NOT NULL CONSTRAINT DF_ProjectWorkItemFollowers_AddedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ProjectWorkItemFollowers_Project FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_ProjectWorkItemFollowers_WorkItem FOREIGN KEY (WorkItemId) REFERENCES dbo.ProjectTickets(Id),
        CONSTRAINT FK_ProjectWorkItemFollowers_User FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_ProjectWorkItemFollowers_AddedBy FOREIGN KEY (AddedByUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT UQ_ProjectWorkItemFollowers_User UNIQUE (TenantId, ProjectId, WorkItemId, UserId)
    );
END;
GO

IF OBJECT_ID(N'dbo.ProjectWorkItemActivity', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectWorkItemActivity
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProjectWorkItemActivity PRIMARY KEY,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        WorkItemId BIGINT NOT NULL,
        EventKind NVARCHAR(100) NOT NULL,
        Summary NVARCHAR(500) NOT NULL,
        ActorUserId INT NULL,
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_ProjectWorkItemActivity_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ProjectWorkItemActivity_Project FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_ProjectWorkItemActivity_WorkItem FOREIGN KEY (WorkItemId) REFERENCES dbo.ProjectTickets(Id),
        CONSTRAINT FK_ProjectWorkItemActivity_Actor FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(Id)
    );
    CREATE INDEX IX_ProjectWorkItemActivity_WorkItem ON dbo.ProjectWorkItemActivity(TenantId, ProjectId, WorkItemId, CreatedUtc DESC);
END;
GO
