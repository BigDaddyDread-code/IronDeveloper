-- ============================================================
-- Migration 007: Chat Message Feedback
-- Stores per-message thumbs-up / thumbs-down feedback so IronDev
-- can adapt future prompt context to project response preferences.
-- ============================================================

IF NOT EXISTS (
    SELECT 1 FROM sys.tables t
    JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = 'dbo' AND t.name = 'ChatMessageFeedback'
)
BEGIN
    CREATE TABLE dbo.ChatMessageFeedback (
        Id              BIGINT          IDENTITY(1,1)   NOT NULL,
        TenantId        INT             NOT NULL,
        ProjectId       INT             NOT NULL,
        ChatSessionId   BIGINT          NULL,
        ChatMessageId   BIGINT          NOT NULL,
        Rating          NVARCHAR(20)    NOT NULL,   -- 'Useful' | 'Weak'
        Reason          NVARCHAR(100)   NULL,
        Comment         NVARCHAR(MAX)   NULL,
        CreatedDate     DATETIME2       NOT NULL    DEFAULT SYSUTCDATETIME(),

        CONSTRAINT PK_ChatMessageFeedback PRIMARY KEY CLUSTERED (Id)
    );

    -- Efficient look-up for preference summary queries
    CREATE NONCLUSTERED INDEX IX_ChatMessageFeedback_Project
        ON dbo.ChatMessageFeedback (TenantId, ProjectId, CreatedDate DESC);
END
GO
