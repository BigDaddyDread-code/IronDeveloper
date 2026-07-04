/*
  Project channels are collaboration state.
  Channel rows, messages, links, reads, pins, and assistant turns do not grant
  approval, authority, policy satisfaction, source apply, workflow continuation,
  memory promotion, release readiness, or deployment readiness.
*/

IF OBJECT_ID(N'dbo.Projects', N'U') IS NULL
BEGIN
    THROW 52900, 'dbo.Projects must exist before project channel migration runs.', 1;
END;
GO

IF OBJECT_ID(N'dbo.Tenants', N'U') IS NULL
BEGIN
    THROW 52901, 'dbo.Tenants must exist before project channel migration runs.', 1;
END;
GO

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
BEGIN
    THROW 52902, 'dbo.Users must exist before project channel migration runs.', 1;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.Projects') AND name = N'UQ_Projects_IdTenant')
BEGIN
    ALTER TABLE dbo.Projects
    ADD CONSTRAINT UQ_Projects_IdTenant UNIQUE (Id, TenantId);
END;
GO

IF OBJECT_ID(N'dbo.ProjectChannels', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectChannels
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        Name NVARCHAR(100) NOT NULL,
        Slug NVARCHAR(120) NOT NULL,
        Description NVARCHAR(500) NULL,
        ChannelKind NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectChannels_ChannelKind DEFAULT N'Custom',
        Visibility NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectChannels_Visibility DEFAULT N'Project',
        Status NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectChannels_Status DEFAULT N'Active',
        CreatedByUserId INT NOT NULL,
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_ProjectChannels_CreatedUtc DEFAULT SYSUTCDATETIME(),
        UpdatedUtc DATETIME2 NULL,
        ArchivedUtc DATETIME2 NULL,
        LinkedTicketId BIGINT NULL,
        LinkedRunId NVARCHAR(200) NULL,
        LinkedBatchId NVARCHAR(200) NULL,
        LinkedReviewId NVARCHAR(200) NULL,
        LinkedReleaseCandidateRef NVARCHAR(200) NULL,
        Boundary NVARCHAR(500) NOT NULL CONSTRAINT DF_ProjectChannels_Boundary DEFAULT N'A project channel is collaboration state. It is not approval, authority, evidence, policy satisfaction, source apply, workflow continuation, release readiness, or deployment readiness.',
        CONSTRAINT PK_ProjectChannels PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_ProjectChannels_IdTenantProject UNIQUE (Id, TenantId, ProjectId),
        CONSTRAINT FK_ProjectChannels_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ProjectChannels_Projects FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_ProjectChannels_CreatedBy FOREIGN KEY (CreatedByUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_ProjectChannels_Name_NotBlank CHECK (LEN(LTRIM(RTRIM(Name))) > 0),
        CONSTRAINT CK_ProjectChannels_Slug_NotBlank CHECK (LEN(LTRIM(RTRIM(Slug))) > 0),
        CONSTRAINT CK_ProjectChannels_Status CHECK (Status IN (N'Active', N'Archived')),
        CONSTRAINT CK_ProjectChannels_Visibility CHECK (Visibility IN (N'Project', N'MembersOnly')),
        CONSTRAINT CK_ProjectChannels_ChannelKind CHECK (ChannelKind IN (N'General', N'Architecture', N'Tickets', N'BuildRuns', N'Review', N'Release', N'Custom', N'WorkItem', N'Run', N'Batch')),
        CONSTRAINT CK_ProjectChannels_Boundary_NotBlank CHECK (LEN(LTRIM(RTRIM(Boundary))) > 0)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ProjectChannels_TenantProjectSlug_Active' AND object_id = OBJECT_ID(N'dbo.ProjectChannels'))
BEGIN
    CREATE UNIQUE INDEX UX_ProjectChannels_TenantProjectSlug_Active
    ON dbo.ProjectChannels (TenantId, ProjectId, Slug)
    WHERE Status = N'Active';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjectChannels_TenantProject_Status' AND object_id = OBJECT_ID(N'dbo.ProjectChannels'))
BEGIN
    CREATE INDEX IX_ProjectChannels_TenantProject_Status
    ON dbo.ProjectChannels (TenantId, ProjectId, Status, UpdatedUtc DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjectChannels_LinkedTicket' AND object_id = OBJECT_ID(N'dbo.ProjectChannels'))
BEGIN
    CREATE INDEX IX_ProjectChannels_LinkedTicket
    ON dbo.ProjectChannels (TenantId, ProjectId, LinkedTicketId)
    WHERE LinkedTicketId IS NOT NULL;
END;
GO

IF OBJECT_ID(N'dbo.ProjectChannelMembers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectChannelMembers
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        ChannelId BIGINT NOT NULL,
        UserId INT NOT NULL,
        ChannelRole NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectChannelMembers_ChannelRole DEFAULT N'Member',
        NotificationLevel NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectChannelMembers_NotificationLevel DEFAULT N'Mentions',
        Status NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectChannelMembers_Status DEFAULT N'Active',
        AddedByUserId INT NOT NULL,
        AddedUtc DATETIME2 NOT NULL CONSTRAINT DF_ProjectChannelMembers_AddedUtc DEFAULT SYSUTCDATETIME(),
        RemovedUtc DATETIME2 NULL,
        Boundary NVARCHAR(500) NOT NULL CONSTRAINT DF_ProjectChannelMembers_Boundary DEFAULT N'Channel membership controls channel visibility and moderation only. It is not approval, authority, policy satisfaction, source apply, workflow continuation, release readiness, or deployment readiness.',
        CONSTRAINT PK_ProjectChannelMembers PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_ProjectChannelMembers_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ProjectChannelMembers_Projects FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_ProjectChannelMembers_Channels FOREIGN KEY (ChannelId, TenantId, ProjectId) REFERENCES dbo.ProjectChannels(Id, TenantId, ProjectId),
        CONSTRAINT FK_ProjectChannelMembers_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_ProjectChannelMembers_AddedBy FOREIGN KEY (AddedByUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_ProjectChannelMembers_ChannelRole CHECK (ChannelRole IN (N'Owner', N'Moderator', N'Member', N'ReadOnly')),
        CONSTRAINT CK_ProjectChannelMembers_NotificationLevel CHECK (NotificationLevel IN (N'All', N'Mentions', N'None')),
        CONSTRAINT CK_ProjectChannelMembers_Status CHECK (Status IN (N'Active', N'Removed')),
        CONSTRAINT CK_ProjectChannelMembers_Boundary_NotBlank CHECK (LEN(LTRIM(RTRIM(Boundary))) > 0)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ProjectChannelMembers_ChannelUser_Active' AND object_id = OBJECT_ID(N'dbo.ProjectChannelMembers'))
BEGIN
    CREATE UNIQUE INDEX UX_ProjectChannelMembers_ChannelUser_Active
    ON dbo.ProjectChannelMembers (TenantId, ProjectId, ChannelId, UserId)
    WHERE Status = N'Active';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjectChannelMembers_User' AND object_id = OBJECT_ID(N'dbo.ProjectChannelMembers'))
BEGIN
    CREATE INDEX IX_ProjectChannelMembers_User
    ON dbo.ProjectChannelMembers (TenantId, ProjectId, UserId, Status);
END;
GO

IF OBJECT_ID(N'dbo.ProjectChannelMessages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectChannelMessages
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        ChannelId BIGINT NOT NULL,
        AuthorUserId INT NULL,
        Role NVARCHAR(50) NOT NULL,
        Message NVARCHAR(MAX) NOT NULL,
        MessageFormat NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectChannelMessages_MessageFormat DEFAULT N'Markdown',
        Status NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectChannelMessages_Status DEFAULT N'Active',
        ReplyToMessageId BIGINT NULL,
        ThreadRootMessageId BIGINT NULL,
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_ProjectChannelMessages_CreatedUtc DEFAULT SYSUTCDATETIME(),
        EditedUtc DATETIME2 NULL,
        DeletedUtc DATETIME2 NULL,
        CorrelationId NVARCHAR(200) NULL,
        CausationId NVARCHAR(200) NULL,
        Boundary NVARCHAR(500) NOT NULL CONSTRAINT DF_ProjectChannelMessages_Boundary DEFAULT N'A channel message is conversation. It is not approval, authority, evidence, policy satisfaction, source apply, workflow continuation, release readiness, or deployment readiness.',
        CONSTRAINT PK_ProjectChannelMessages PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_ProjectChannelMessages_IdTenantProjectChannel UNIQUE (Id, TenantId, ProjectId, ChannelId),
        CONSTRAINT FK_ProjectChannelMessages_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ProjectChannelMessages_Projects FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_ProjectChannelMessages_Channels FOREIGN KEY (ChannelId, TenantId, ProjectId) REFERENCES dbo.ProjectChannels(Id, TenantId, ProjectId),
        CONSTRAINT FK_ProjectChannelMessages_Author FOREIGN KEY (AuthorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_ProjectChannelMessages_ReplyTo FOREIGN KEY (ReplyToMessageId, TenantId, ProjectId, ChannelId) REFERENCES dbo.ProjectChannelMessages(Id, TenantId, ProjectId, ChannelId),
        CONSTRAINT FK_ProjectChannelMessages_ThreadRoot FOREIGN KEY (ThreadRootMessageId, TenantId, ProjectId, ChannelId) REFERENCES dbo.ProjectChannelMessages(Id, TenantId, ProjectId, ChannelId),
        CONSTRAINT CK_ProjectChannelMessages_Role CHECK (Role IN (N'User', N'Assistant', N'SystemNotice', N'EventLink')),
        CONSTRAINT CK_ProjectChannelMessages_UserRequiresAuthor CHECK (Role <> N'User' OR AuthorUserId IS NOT NULL),
        CONSTRAINT CK_ProjectChannelMessages_Format CHECK (MessageFormat IN (N'PlainText', N'Markdown')),
        CONSTRAINT CK_ProjectChannelMessages_Status CHECK (Status IN (N'Active', N'Edited', N'Deleted')),
        CONSTRAINT CK_ProjectChannelMessages_Message_NotBlank CHECK (LEN(LTRIM(RTRIM(Message))) > 0),
        CONSTRAINT CK_ProjectChannelMessages_Boundary_NotBlank CHECK (LEN(LTRIM(RTRIM(Boundary))) > 0)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjectChannelMessages_ChannelCreated' AND object_id = OBJECT_ID(N'dbo.ProjectChannelMessages'))
BEGIN
    CREATE INDEX IX_ProjectChannelMessages_ChannelCreated
    ON dbo.ProjectChannelMessages (TenantId, ProjectId, ChannelId, CreatedUtc DESC, Id DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjectChannelMessages_Author' AND object_id = OBJECT_ID(N'dbo.ProjectChannelMessages'))
BEGIN
    CREATE INDEX IX_ProjectChannelMessages_Author
    ON dbo.ProjectChannelMessages (TenantId, ProjectId, AuthorUserId, CreatedUtc DESC)
    WHERE AuthorUserId IS NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjectChannelMessages_Thread' AND object_id = OBJECT_ID(N'dbo.ProjectChannelMessages'))
BEGIN
    CREATE INDEX IX_ProjectChannelMessages_Thread
    ON dbo.ProjectChannelMessages (TenantId, ProjectId, ChannelId, ThreadRootMessageId, CreatedUtc)
    WHERE ThreadRootMessageId IS NOT NULL;
END;
GO

IF OBJECT_ID(N'dbo.ProjectChannelMessageContextLinks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectChannelMessageContextLinks
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        ChannelId BIGINT NOT NULL,
        MessageId BIGINT NOT NULL,
        LinkKind NVARCHAR(50) NOT NULL,
        LinkId NVARCHAR(200) NOT NULL,
        LinkLabel NVARCHAR(300) NULL,
        Source NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectChannelMessageContextLinks_Source DEFAULT N'UserLinked',
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_ProjectChannelMessageContextLinks_CreatedUtc DEFAULT SYSUTCDATETIME(),
        Boundary NVARCHAR(500) NOT NULL CONSTRAINT DF_ProjectChannelMessageContextLinks_Boundary DEFAULT N'A context link is a pointer for navigation and grounding. It is not approval, evidence validation, authority, or permission to mutate the linked object.',
        CONSTRAINT PK_ProjectChannelMessageContextLinks PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_ProjectChannelMessageContextLinks_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ProjectChannelMessageContextLinks_Projects FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_ProjectChannelMessageContextLinks_Channels FOREIGN KEY (ChannelId, TenantId, ProjectId) REFERENCES dbo.ProjectChannels(Id, TenantId, ProjectId),
        CONSTRAINT FK_ProjectChannelMessageContextLinks_Messages FOREIGN KEY (MessageId, TenantId, ProjectId, ChannelId) REFERENCES dbo.ProjectChannelMessages(Id, TenantId, ProjectId, ChannelId),
        CONSTRAINT CK_ProjectChannelMessageContextLinks_LinkKind CHECK (LinkKind IN (N'Ticket', N'Run', N'Batch', N'BatchMap', N'BatchPlan', N'CriticPackage', N'Finding', N'ApprovalPackage', N'AcceptedApproval', N'PatchArtifact', N'DryRunReceipt', N'SourceApplyReview', N'ReleaseCandidate', N'Document', N'Decision', N'File', N'Symbol')),
        CONSTRAINT CK_ProjectChannelMessageContextLinks_Source CHECK (Source IN (N'UserLinked', N'AssistantLinked', N'SystemLinked')),
        CONSTRAINT CK_ProjectChannelMessageContextLinks_LinkId_NotBlank CHECK (LEN(LTRIM(RTRIM(LinkId))) > 0),
        CONSTRAINT CK_ProjectChannelMessageContextLinks_Boundary_NotBlank CHECK (LEN(LTRIM(RTRIM(Boundary))) > 0)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjectChannelMessageContextLinks_Message' AND object_id = OBJECT_ID(N'dbo.ProjectChannelMessageContextLinks'))
BEGIN
    CREATE INDEX IX_ProjectChannelMessageContextLinks_Message
    ON dbo.ProjectChannelMessageContextLinks (TenantId, ProjectId, MessageId);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjectChannelMessageContextLinks_Link' AND object_id = OBJECT_ID(N'dbo.ProjectChannelMessageContextLinks'))
BEGIN
    CREATE INDEX IX_ProjectChannelMessageContextLinks_Link
    ON dbo.ProjectChannelMessageContextLinks (TenantId, ProjectId, LinkKind, LinkId);
END;
GO

IF OBJECT_ID(N'dbo.ProjectChannelAssistantTurns', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectChannelAssistantTurns
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        ChannelId BIGINT NOT NULL,
        RequestMessageId BIGINT NOT NULL,
        ResponseMessageId BIGINT NULL,
        RequestedByUserId INT NOT NULL,
        Prompt NVARCHAR(MAX) NOT NULL,
        Answer NVARCHAR(MAX) NULL,
        Mode NVARCHAR(50) NULL,
        ModeConfidence FLOAT NULL,
        ModeReason NVARCHAR(MAX) NULL,
        ContextSummary NVARCHAR(MAX) NULL,
        LinkedFilePaths NVARCHAR(MAX) NULL,
        LinkedSymbols NVARCHAR(MAX) NULL,
        LinkedDocumentIds NVARCHAR(MAX) NULL,
        LinkedTicketIds NVARCHAR(MAX) NULL,
        LinkedRunIds NVARCHAR(MAX) NULL,
        RouteTraceId NVARCHAR(200) NULL,
        DogfoodTraceId NVARCHAR(200) NULL,
        TraceId BIGINT NULL,
        Status NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectChannelAssistantTurns_Status DEFAULT N'Requested',
        FailureReason NVARCHAR(MAX) NULL,
        CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_ProjectChannelAssistantTurns_CreatedUtc DEFAULT SYSUTCDATETIME(),
        CompletedUtc DATETIME2 NULL,
        Boundary NVARCHAR(500) NOT NULL CONSTRAINT DF_ProjectChannelAssistantTurns_Boundary DEFAULT N'A channel assistant answer is advisory project context. It is not approval, authority, evidence, policy satisfaction, source apply, workflow continuation, release readiness, or deployment readiness.',
        CONSTRAINT PK_ProjectChannelAssistantTurns PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_ProjectChannelAssistantTurns_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ProjectChannelAssistantTurns_Projects FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_ProjectChannelAssistantTurns_Channels FOREIGN KEY (ChannelId, TenantId, ProjectId) REFERENCES dbo.ProjectChannels(Id, TenantId, ProjectId),
        CONSTRAINT FK_ProjectChannelAssistantTurns_RequestMessage FOREIGN KEY (RequestMessageId, TenantId, ProjectId, ChannelId) REFERENCES dbo.ProjectChannelMessages(Id, TenantId, ProjectId, ChannelId),
        CONSTRAINT FK_ProjectChannelAssistantTurns_ResponseMessage FOREIGN KEY (ResponseMessageId, TenantId, ProjectId, ChannelId) REFERENCES dbo.ProjectChannelMessages(Id, TenantId, ProjectId, ChannelId),
        CONSTRAINT FK_ProjectChannelAssistantTurns_RequestedBy FOREIGN KEY (RequestedByUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_ProjectChannelAssistantTurns_Status CHECK (Status IN (N'Requested', N'Answered', N'Failed', N'Refused')),
        CONSTRAINT CK_ProjectChannelAssistantTurns_Prompt_NotBlank CHECK (LEN(LTRIM(RTRIM(Prompt))) > 0),
        CONSTRAINT CK_ProjectChannelAssistantTurns_Boundary_NotBlank CHECK (LEN(LTRIM(RTRIM(Boundary))) > 0)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjectChannelAssistantTurns_ChannelCreated' AND object_id = OBJECT_ID(N'dbo.ProjectChannelAssistantTurns'))
BEGIN
    CREATE INDEX IX_ProjectChannelAssistantTurns_ChannelCreated
    ON dbo.ProjectChannelAssistantTurns (TenantId, ProjectId, ChannelId, CreatedUtc DESC);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjectChannelAssistantTurns_RequestMessage' AND object_id = OBJECT_ID(N'dbo.ProjectChannelAssistantTurns'))
BEGIN
    CREATE INDEX IX_ProjectChannelAssistantTurns_RequestMessage
    ON dbo.ProjectChannelAssistantTurns (TenantId, ProjectId, RequestMessageId);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjectChannelAssistantTurns_ResponseMessage' AND object_id = OBJECT_ID(N'dbo.ProjectChannelAssistantTurns'))
BEGIN
    CREATE INDEX IX_ProjectChannelAssistantTurns_ResponseMessage
    ON dbo.ProjectChannelAssistantTurns (TenantId, ProjectId, ResponseMessageId)
    WHERE ResponseMessageId IS NOT NULL;
END;
GO

IF OBJECT_ID(N'dbo.ProjectChannelMessageReads', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectChannelMessageReads
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        ChannelId BIGINT NOT NULL,
        UserId INT NOT NULL,
        LastReadMessageId BIGINT NULL,
        LastReadUtc DATETIME2 NOT NULL CONSTRAINT DF_ProjectChannelMessageReads_LastReadUtc DEFAULT SYSUTCDATETIME(),
        Boundary NVARCHAR(500) NOT NULL CONSTRAINT DF_ProjectChannelMessageReads_Boundary DEFAULT N'A channel read marker is unread-count convenience. It is not approval, authority, evidence, policy satisfaction, source apply, workflow continuation, release readiness, or deployment readiness.',
        CONSTRAINT PK_ProjectChannelMessageReads PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_ProjectChannelMessageReads_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ProjectChannelMessageReads_Projects FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_ProjectChannelMessageReads_Channels FOREIGN KEY (ChannelId, TenantId, ProjectId) REFERENCES dbo.ProjectChannels(Id, TenantId, ProjectId),
        CONSTRAINT FK_ProjectChannelMessageReads_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_ProjectChannelMessageReads_LastReadMessage FOREIGN KEY (LastReadMessageId, TenantId, ProjectId, ChannelId) REFERENCES dbo.ProjectChannelMessages(Id, TenantId, ProjectId, ChannelId),
        CONSTRAINT CK_ProjectChannelMessageReads_Boundary_NotBlank CHECK (LEN(LTRIM(RTRIM(Boundary))) > 0)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ProjectChannelMessageReads_ChannelUser' AND object_id = OBJECT_ID(N'dbo.ProjectChannelMessageReads'))
BEGIN
    CREATE UNIQUE INDEX UX_ProjectChannelMessageReads_ChannelUser
    ON dbo.ProjectChannelMessageReads (TenantId, ProjectId, ChannelId, UserId);
END;
GO

IF OBJECT_ID(N'dbo.ProjectChannelPins', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProjectChannelPins
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        TenantId INT NOT NULL,
        ProjectId INT NOT NULL,
        ChannelId BIGINT NOT NULL,
        MessageId BIGINT NOT NULL,
        PinnedByUserId INT NOT NULL,
        PinnedUtc DATETIME2 NOT NULL CONSTRAINT DF_ProjectChannelPins_PinnedUtc DEFAULT SYSUTCDATETIME(),
        UnpinnedUtc DATETIME2 NULL,
        Boundary NVARCHAR(500) NOT NULL CONSTRAINT DF_ProjectChannelPins_Boundary DEFAULT N'A pinned channel message is navigation convenience. It is not approval, policy, authority, evidence, source apply, workflow continuation, release readiness, or deployment readiness.',
        CONSTRAINT PK_ProjectChannelPins PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_ProjectChannelPins_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
        CONSTRAINT FK_ProjectChannelPins_Projects FOREIGN KEY (ProjectId, TenantId) REFERENCES dbo.Projects(Id, TenantId),
        CONSTRAINT FK_ProjectChannelPins_Channels FOREIGN KEY (ChannelId, TenantId, ProjectId) REFERENCES dbo.ProjectChannels(Id, TenantId, ProjectId),
        CONSTRAINT FK_ProjectChannelPins_Messages FOREIGN KEY (MessageId, TenantId, ProjectId, ChannelId) REFERENCES dbo.ProjectChannelMessages(Id, TenantId, ProjectId, ChannelId),
        CONSTRAINT FK_ProjectChannelPins_PinnedBy FOREIGN KEY (PinnedByUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_ProjectChannelPins_Boundary_NotBlank CHECK (LEN(LTRIM(RTRIM(Boundary))) > 0)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ProjectChannelPins_Message_Active' AND object_id = OBJECT_ID(N'dbo.ProjectChannelPins'))
BEGIN
    CREATE UNIQUE INDEX UX_ProjectChannelPins_Message_Active
    ON dbo.ProjectChannelPins (TenantId, ProjectId, ChannelId, MessageId)
    WHERE UnpinnedUtc IS NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProjectChannelPins_Channel' AND object_id = OBJECT_ID(N'dbo.ProjectChannelPins'))
BEGIN
    CREATE INDEX IX_ProjectChannelPins_Channel
    ON dbo.ProjectChannelPins (TenantId, ProjectId, ChannelId, PinnedUtc DESC);
END;
GO
