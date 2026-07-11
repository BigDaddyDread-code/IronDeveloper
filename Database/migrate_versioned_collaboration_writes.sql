/* Optimistic revisions prevent silent overwrite on shared mutable collaboration state. */

IF COL_LENGTH(N'dbo.ProjectTickets', N'Revision') IS NULL
BEGIN
    ALTER TABLE dbo.ProjectTickets ADD Revision BIGINT NOT NULL CONSTRAINT DF_ProjectTickets_Revision DEFAULT 1;
END;
GO

IF COL_LENGTH(N'dbo.ProjectChannelMembers', N'Revision') IS NULL
BEGIN
    ALTER TABLE dbo.ProjectChannelMembers ADD Revision BIGINT NOT NULL CONSTRAINT DF_ProjectChannelMembers_Revision DEFAULT 1;
END;
GO
