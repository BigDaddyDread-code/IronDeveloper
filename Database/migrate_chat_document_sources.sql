SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.ChatMessages', 'ReplyToMessageId') IS NULL
    ALTER TABLE dbo.ChatMessages ADD ReplyToMessageId BIGINT NULL;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID('dbo.ChatMessages')
      AND name = 'FK_ChatMessages_ReplyToMessage'
)
BEGIN
    ALTER TABLE dbo.ChatMessages WITH CHECK ADD CONSTRAINT FK_ChatMessages_ReplyToMessage
        FOREIGN KEY (ReplyToMessageId) REFERENCES dbo.ChatMessages(Id);
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.ChatMessages')
      AND name = 'IX_ChatMessages_ReplyToMessageId'
)
BEGIN
    CREATE INDEX IX_ChatMessages_ReplyToMessageId
        ON dbo.ChatMessages(ReplyToMessageId)
        WHERE ReplyToMessageId IS NOT NULL;
END
GO

PRINT 'migrate_chat_document_sources.sql complete.';
GO
