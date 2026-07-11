using Dapper;
using IronDev.Core.Channels;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.RegularExpressions;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("Governance")]
[TestCategory("Database")]
[TestCategory("RequiresRealDatabase")]
[TestCategory("LongRunning")]
[TestCategory("Boundary")]
[TestCategory("Contract")]
public sealed class ProjectChannelsSchemaTests : IntegrationTestBase
{
    private const string MigrationPath = "Database/migrate_project_channels.sql";
    private const string ReceiptPath = "Docs/receipts/M02_PROJECT_CHANNELS_SCHEMA.md";

    private static readonly string[] ExpectedTables =
    [
        "ProjectChannels",
        "ProjectChannelMembers",
        "ProjectChannelMessages",
        "ProjectChannelMessageMentions",
        "ProjectNotifications",
        "ProjectChannelMessageContextLinks",
        "ProjectChannelAssistantTurns",
        "ProjectChannelMessageReads",
        "ProjectChannelPins"
    ];

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropProjectChannelTablesAsync();
        await ApplySqlFileAsync("Database", "migrate_project_channels.sql");
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropProjectChannelTablesAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public void ProjectChannelContracts_ExposeConstantsEntitiesAndBoundaryText()
    {
        CollectionAssert.AreEquivalent(
            new[] { "General", "Architecture", "Tickets", "BuildRuns", "Review", "Release", "Custom", "WorkItem", "Run", "Batch" },
            ProjectChannelKinds.All.ToArray());

        CollectionAssert.AreEquivalent(new[] { "Project", "MembersOnly" }, ProjectChannelVisibility.All.ToArray());
        CollectionAssert.AreEquivalent(new[] { "Active", "Archived" }, ProjectChannelStatus.All.ToArray());
        CollectionAssert.AreEquivalent(new[] { "Owner", "Moderator", "Member", "ReadOnly" }, ProjectChannelRoles.All.ToArray());
        CollectionAssert.AreEquivalent(new[] { "User", "Assistant", "SystemNotice", "EventLink" }, ProjectChannelMessageRoles.All.ToArray());
        CollectionAssert.AreEquivalent(new[] { "Requested", "Answered", "Failed", "Refused" }, ProjectChannelAssistantTurnStatus.All.ToArray());

        AssertScopedEntity<ProjectChannel>();
        AssertScopedEntity<ProjectChannelMember>();
        AssertScopedEntity<ProjectChannelMessage>();
        AssertScopedEntity<ProjectChannelMessageMention>();
        AssertScopedEntity<ProjectNotification>();
        AssertScopedEntity<ProjectChannelMessageContextLink>();
        AssertScopedEntity<ProjectChannelAssistantTurn>();
        AssertScopedEntity<ProjectChannelMessageRead>();
        AssertScopedEntity<ProjectChannelPin>();

        AssertBoundary(ProjectChannelBoundaries.Channel);
        AssertBoundary(ProjectChannelBoundaries.Message);
        AssertBoundary(ProjectChannelBoundaries.ContextLink);
        AssertBoundary(ProjectChannelBoundaries.AssistantTurn);
        AssertBoundary(ProjectChannelBoundaries.ReadMarker);
        AssertBoundary(ProjectChannelBoundaries.Mention);
        AssertBoundary(ProjectChannelBoundaries.Notification);
        AssertBoundary(ProjectChannelBoundaries.Pin);
    }

    [TestMethod]
    public async Task ProjectChannelMigration_CreatesTablesForeignKeysChecksAndIndexes()
    {
        await using var connection = new SqlConnection(ConnectionString);

        var tables = await QueryNamesAsync(
            connection,
            """
            SELECT t.name
            FROM sys.tables t
            WHERE t.schema_id = SCHEMA_ID(N'dbo')
              AND t.name IN @Names
            """,
            new { Names = ExpectedTables });
        CollectionAssert.AreEquivalent(ExpectedTables, tables);

        var foreignKeys = await QueryNamesAsync(
            connection,
            """
            SELECT fk.name
            FROM sys.foreign_keys fk
            WHERE fk.parent_object_id IN
            (
                OBJECT_ID(N'dbo.ProjectChannels'),
                OBJECT_ID(N'dbo.ProjectChannelMembers'),
                OBJECT_ID(N'dbo.ProjectChannelMessages'),
                OBJECT_ID(N'dbo.ProjectChannelMessageMentions'),
                OBJECT_ID(N'dbo.ProjectNotifications'),
                OBJECT_ID(N'dbo.ProjectChannelMessageContextLinks'),
                OBJECT_ID(N'dbo.ProjectChannelAssistantTurns'),
                OBJECT_ID(N'dbo.ProjectChannelMessageReads'),
                OBJECT_ID(N'dbo.ProjectChannelPins')
            )
            """);
        AssertContainsAll(
            foreignKeys,
            "FK_ProjectChannels_Tenants",
            "FK_ProjectChannels_Projects",
            "FK_ProjectChannelMembers_Channels",
            "FK_ProjectChannelMessages_Channels",
            "FK_ProjectChannelMessageMentions_Messages",
            "FK_ProjectNotifications_Recipient",
            "FK_ProjectChannelMessageContextLinks_Messages",
            "FK_ProjectChannelAssistantTurns_RequestMessage",
            "FK_ProjectChannelMessageReads_LastReadMessage",
            "FK_ProjectChannelPins_Messages");

        var checks = await QueryNamesAsync(
            connection,
            """
            SELECT cc.name
            FROM sys.check_constraints cc
            WHERE cc.parent_object_id IN
            (
                OBJECT_ID(N'dbo.ProjectChannels'),
                OBJECT_ID(N'dbo.ProjectChannelMembers'),
                OBJECT_ID(N'dbo.ProjectChannelMessages'),
                OBJECT_ID(N'dbo.ProjectChannelMessageMentions'),
                OBJECT_ID(N'dbo.ProjectNotifications'),
                OBJECT_ID(N'dbo.ProjectChannelMessageContextLinks'),
                OBJECT_ID(N'dbo.ProjectChannelAssistantTurns')
            )
            """);
        AssertContainsAll(
            checks,
            "CK_ProjectChannels_ChannelKind",
            "CK_ProjectChannels_Visibility",
            "CK_ProjectChannels_Status",
            "CK_ProjectChannelMembers_ChannelRole",
            "CK_ProjectChannelMessages_Role",
            "CK_ProjectChannelMessages_UserRequiresAuthor",
            "CK_ProjectChannelMessageMentions_NotSelf",
            "CK_ProjectNotifications_Status",
            "CK_ProjectChannelMessageContextLinks_LinkKind",
            "CK_ProjectChannelAssistantTurns_Status");

        var indexes = await QueryNamesAsync(
            connection,
            """
            SELECT i.name
            FROM sys.indexes i
            WHERE i.object_id IN
            (
                OBJECT_ID(N'dbo.ProjectChannels'),
                OBJECT_ID(N'dbo.ProjectChannelMembers'),
                OBJECT_ID(N'dbo.ProjectChannelMessages'),
                OBJECT_ID(N'dbo.ProjectChannelMessageMentions'),
                OBJECT_ID(N'dbo.ProjectNotifications'),
                OBJECT_ID(N'dbo.ProjectChannelMessageContextLinks'),
                OBJECT_ID(N'dbo.ProjectChannelAssistantTurns'),
                OBJECT_ID(N'dbo.ProjectChannelMessageReads'),
                OBJECT_ID(N'dbo.ProjectChannelPins')
            )
              AND i.name IS NOT NULL
            """);
        AssertContainsAll(
            indexes,
            "UX_ProjectChannels_TenantProjectSlug_Active",
            "IX_ProjectChannels_TenantProject_Status",
            "IX_ProjectChannels_LinkedTicket",
            "UX_ProjectChannelMembers_ChannelUser_Active",
            "IX_ProjectChannelMessages_ChannelCreated",
            "UX_ProjectChannelMessageMentions_MessageUser",
            "IX_ProjectNotifications_RecipientStatusCreated",
            "IX_ProjectChannelMessageContextLinks_Link",
            "IX_ProjectChannelAssistantTurns_ChannelCreated",
            "UX_ProjectChannelMessageReads_ChannelUser",
            "UX_ProjectChannelPins_Message_Active");
    }

    [TestMethod]
    public async Task ProjectChannelMigration_DefaultBoundariesDenyAuthority()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var seed = await InsertSeedAsync(connection);
        var channelId = await InsertChannelAsync(connection, seed, slug: "general");
        var userMessageId = await InsertUserMessageAsync(connection, seed, channelId, "looks good, but not authority");
        var assistantMessageId = await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO dbo.ProjectChannelMessages (TenantId, ProjectId, ChannelId, Role, Message)
            OUTPUT INSERTED.Id
            VALUES (@TenantId, @ProjectId, @ChannelId, N'Assistant', N'I can explain, but not approve.');
            """,
            new { seed.TenantId, seed.ProjectId, ChannelId = channelId });

        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.ProjectChannelMembers (TenantId, ProjectId, ChannelId, UserId, ChannelRole, AddedByUserId)
            VALUES (@TenantId, @ProjectId, @ChannelId, @UserId, N'Member', @UserId);

            INSERT INTO dbo.ProjectChannelMessageContextLinks (TenantId, ProjectId, ChannelId, MessageId, LinkKind, LinkId)
            VALUES (@TenantId, @ProjectId, @ChannelId, @MessageId, N'AcceptedApproval', N'approval-record-001');

            INSERT INTO dbo.ProjectChannelAssistantTurns (TenantId, ProjectId, ChannelId, RequestMessageId, ResponseMessageId, RequestedByUserId, Prompt, Answer, Status)
            VALUES (@TenantId, @ProjectId, @ChannelId, @RequestMessageId, @ResponseMessageId, @UserId, N'@IronDev summarize blockers', N'No authority granted.', N'Answered');

            INSERT INTO dbo.ProjectChannelMessageReads (TenantId, ProjectId, ChannelId, UserId, LastReadMessageId)
            VALUES (@TenantId, @ProjectId, @ChannelId, @UserId, @MessageId);

            INSERT INTO dbo.ProjectChannelPins (TenantId, ProjectId, ChannelId, MessageId, PinnedByUserId)
            VALUES (@TenantId, @ProjectId, @ChannelId, @MessageId, @UserId);
            """,
            new
            {
                seed.TenantId,
                seed.ProjectId,
                ChannelId = channelId,
                seed.UserId,
                MessageId = userMessageId,
                RequestMessageId = userMessageId,
                ResponseMessageId = assistantMessageId
            });

        var boundaries = (await connection.QueryAsync<string>(
            """
            SELECT Boundary FROM dbo.ProjectChannels WHERE Id = @ChannelId
            UNION ALL SELECT Boundary FROM dbo.ProjectChannelMembers WHERE ChannelId = @ChannelId
            UNION ALL SELECT Boundary FROM dbo.ProjectChannelMessages WHERE ChannelId = @ChannelId
            UNION ALL SELECT Boundary FROM dbo.ProjectChannelMessageContextLinks WHERE ChannelId = @ChannelId
            UNION ALL SELECT Boundary FROM dbo.ProjectChannelAssistantTurns WHERE ChannelId = @ChannelId
            UNION ALL SELECT Boundary FROM dbo.ProjectChannelMessageReads WHERE ChannelId = @ChannelId
            UNION ALL SELECT Boundary FROM dbo.ProjectChannelPins WHERE ChannelId = @ChannelId
            """,
            new { ChannelId = channelId })).ToArray();

        Assert.AreEqual(8, boundaries.Length);
        foreach (var boundary in boundaries)
            AssertBoundary(boundary);
    }

    [TestMethod]
    public async Task ProjectChannelMigration_EnforcesUniqueActiveSlugPerTenantProject()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var seed = await InsertSeedAsync(connection);

        _ = await InsertChannelAsync(connection, seed, slug: "architecture");

        await AssertSqlFailsAsync(() => InsertChannelAsync(connection, seed, slug: "architecture"));
    }

    [TestMethod]
    public async Task ProjectChannelMigration_AllowsArchivedSlugReuseWithoutTreatingArchiveAsAuthority()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var seed = await InsertSeedAsync(connection);
        var archivedId = await InsertChannelAsync(connection, seed, slug: "review");

        await connection.ExecuteAsync(
            "UPDATE dbo.ProjectChannels SET Status = N'Archived', ArchivedUtc = SYSUTCDATETIME() WHERE Id = @Id",
            new { Id = archivedId });

        var activeId = await InsertChannelAsync(connection, seed, slug: "review");
        Assert.AreNotEqual(archivedId, activeId);

        var boundaries = (await connection.QueryAsync<string>(
            "SELECT Boundary FROM dbo.ProjectChannels WHERE Id IN @Ids",
            new { Ids = new[] { archivedId, activeId } })).ToArray();
        foreach (var boundary in boundaries)
            AssertBoundary(boundary);
    }

    [TestMethod]
    public async Task ProjectChannelMigration_EnforcesTenantProjectScopedChildRows()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var seed = await InsertSeedAsync(connection);
        var otherProjectId = await InsertProjectAsync(connection, seed.TenantId, "other-project");
        var channelId = await InsertChannelAsync(connection, seed, slug: "tickets");

        await AssertSqlFailsAsync(() => connection.ExecuteAsync(
            """
            INSERT INTO dbo.ProjectChannelMessages (TenantId, ProjectId, ChannelId, AuthorUserId, Role, Message)
            VALUES (@TenantId, @OtherProjectId, @ChannelId, @UserId, N'User', N'cross project leak');
            """,
            new { seed.TenantId, OtherProjectId = otherProjectId, ChannelId = channelId, seed.UserId }));

        var messageId = await InsertUserMessageAsync(connection, seed, channelId, "properly scoped");

        await AssertSqlFailsAsync(() => connection.ExecuteAsync(
            """
            INSERT INTO dbo.ProjectChannelMessageContextLinks (TenantId, ProjectId, ChannelId, MessageId, LinkKind, LinkId)
            VALUES (@TenantId, @OtherProjectId, @ChannelId, @MessageId, N'Ticket', N'ticket-1');
            """,
            new { seed.TenantId, OtherProjectId = otherProjectId, ChannelId = channelId, MessageId = messageId }));
    }

    [TestMethod]
    public async Task ProjectChannelMessages_EnforceRolesAndUserAuthorRule()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var seed = await InsertSeedAsync(connection);
        var channelId = await InsertChannelAsync(connection, seed, slug: "build-runs");

        await AssertSqlFailsAsync(() => connection.ExecuteAsync(
            """
            INSERT INTO dbo.ProjectChannelMessages (TenantId, ProjectId, ChannelId, Role, Message)
            VALUES (@TenantId, @ProjectId, @ChannelId, N'User', N'user without author');
            """,
            new { seed.TenantId, seed.ProjectId, ChannelId = channelId }));

        await AssertSqlFailsAsync(() => connection.ExecuteAsync(
            """
            INSERT INTO dbo.ProjectChannelMessages (TenantId, ProjectId, ChannelId, Role, Message)
            VALUES (@TenantId, @ProjectId, @ChannelId, N'Approval', N'not a valid role');
            """,
            new { seed.TenantId, seed.ProjectId, ChannelId = channelId }));

        var assistantId = await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO dbo.ProjectChannelMessages (TenantId, ProjectId, ChannelId, Role, Message)
            OUTPUT INSERTED.Id
            VALUES (@TenantId, @ProjectId, @ChannelId, N'Assistant', N'Advisory answer only.');
            """,
            new { seed.TenantId, seed.ProjectId, ChannelId = channelId });
        Assert.IsTrue(assistantId > 0);
    }

    [TestMethod]
    public async Task ProjectChannelContextPinsReads_AreConvenienceOnly()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var seed = await InsertSeedAsync(connection);
        var channelId = await InsertChannelAsync(connection, seed, slug: "release");
        var messageId = await InsertUserMessageAsync(connection, seed, channelId, "release discussion only");

        await connection.ExecuteAsync(
            """
            INSERT INTO dbo.ProjectChannelMessageContextLinks (TenantId, ProjectId, ChannelId, MessageId, LinkKind, LinkId, Source)
            VALUES (@TenantId, @ProjectId, @ChannelId, @MessageId, N'ReleaseCandidate', N'release-candidate:m02', N'UserLinked');

            INSERT INTO dbo.ProjectChannelMessageReads (TenantId, ProjectId, ChannelId, UserId, LastReadMessageId)
            VALUES (@TenantId, @ProjectId, @ChannelId, @UserId, @MessageId);

            INSERT INTO dbo.ProjectChannelPins (TenantId, ProjectId, ChannelId, MessageId, PinnedByUserId)
            VALUES (@TenantId, @ProjectId, @ChannelId, @MessageId, @UserId);
            """,
            new { seed.TenantId, seed.ProjectId, ChannelId = channelId, MessageId = messageId, seed.UserId });

        var linkBoundary = await connection.ExecuteScalarAsync<string>(
            "SELECT Boundary FROM dbo.ProjectChannelMessageContextLinks WHERE MessageId = @MessageId",
            new { MessageId = messageId });
        var readBoundary = await connection.ExecuteScalarAsync<string>(
            "SELECT Boundary FROM dbo.ProjectChannelMessageReads WHERE LastReadMessageId = @MessageId",
            new { MessageId = messageId });
        var pinBoundary = await connection.ExecuteScalarAsync<string>(
            "SELECT Boundary FROM dbo.ProjectChannelPins WHERE MessageId = @MessageId",
            new { MessageId = messageId });

        StringAssert.Contains(linkBoundary, "not approval");
        StringAssert.Contains(readBoundary, "unread-count convenience");
        StringAssert.Contains(pinBoundary, "navigation convenience");
    }

    [TestMethod]
    public void ProjectChannelSchema_AssistantInvocationRemainsExplicitAndNonAuthoritative()
    {
        var root = RepositoryRoot();
        Assert.IsFalse(File.Exists(Path.Combine(root, "IronDev.TauriShell", "src", "screens", "Channels.tsx")), "M02 must not add channel UI.");

        var channelController = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Controllers", "ProjectChannelsController.cs"));
        StringAssert.Contains(channelController, "Shared project conversation");
        StringAssert.Contains(channelController, "messages never grant approval");
        StringAssert.Contains(channelController, "CompleteAssistantTurn");
        StringAssert.Contains(channelController, "assistant-turns/{turnId:long}/complete");
        AssertDoesNotContainAny(
            channelController,
            "ApproveFromChannel",
            "AutoApprove",
            "AutoContinue",
            "AutoApply",
            "AutoAnswer",
            "ReleaseReadyFromChat");

        var migration = File.ReadAllText(Path.Combine(root, MigrationPath));
        AssertDoesNotContainAny(
            migration,
            "ProjectChannelMessageReactions",
            "EmojiApproval",
            "ApprovalFromReaction",
            "AutoApprove",
            "AutoContinue",
            "AutoApply",
            "ReleaseReadyFromChat",
            "MemoryPromoteFromChannel");

        var core = File.ReadAllText(Path.Combine(root, "IronDev.Core", "Channels", "ProjectChannelModels.cs"));
        AssertDoesNotContainAny(
            core,
            "ApproveFromChannel",
            "ChannelMessageGrantsAuthority",
            "ChatApproval",
            "CanApprove",
            "CanApply",
            "CanRelease",
            "CanDeploy",
            "ContinueWorkflow");
    }

    [TestMethod]
    public void Receipt_RecordsM02ScopeAndAuthorityBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), ReceiptPath));

        AssertContainsAll(
            receipt,
            "Project channels are collaboration state.",
            "Channel messages, pins, summaries, context links, and assistant answers do not create approval, authority, policy satisfaction, source apply, workflow continuation, memory promotion, release readiness, or deployment readiness.",
            "M02 does not add API endpoints.",
            "M02 does not add UI.",
            "M02 does not implement Ask IronDev.",
            "M02 does not add reactions.",
            "M02 does not migrate existing ProjectChatSessions.",
            "Channels are where people discuss.",
            "Gates are where authority happens.");
    }

    private static void AssertScopedEntity<T>()
    {
        var properties = typeof(T).GetProperties().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        CollectionAssert.Contains(properties.ToArray(), "TenantId");
        CollectionAssert.Contains(properties.ToArray(), "ProjectId");
        CollectionAssert.Contains(properties.ToArray(), "Boundary");
    }

    private static void AssertBoundary(string boundary)
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(boundary));
        StringAssert.Contains(boundary, "not");
        Assert.IsTrue(
            boundary.Contains("approval", StringComparison.OrdinalIgnoreCase)
            || boundary.Contains("policy", StringComparison.OrdinalIgnoreCase)
            || boundary.Contains("authority", StringComparison.OrdinalIgnoreCase));
    }

    private async Task DropProjectChannelTablesAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(
            """
            IF OBJECT_ID(N'dbo.ProjectChannelPins', N'U') IS NOT NULL DROP TABLE dbo.ProjectChannelPins;
            IF OBJECT_ID(N'dbo.ProjectChannelAssistantTurns', N'U') IS NOT NULL DROP TABLE dbo.ProjectChannelAssistantTurns;
            IF OBJECT_ID(N'dbo.ProjectChannelMessageContextLinks', N'U') IS NOT NULL DROP TABLE dbo.ProjectChannelMessageContextLinks;
            IF OBJECT_ID(N'dbo.ProjectChannelMessageReads', N'U') IS NOT NULL DROP TABLE dbo.ProjectChannelMessageReads;
            IF OBJECT_ID(N'dbo.ProjectNotifications', N'U') IS NOT NULL DROP TABLE dbo.ProjectNotifications;
            IF OBJECT_ID(N'dbo.ProjectChannelMessageMentions', N'U') IS NOT NULL DROP TABLE dbo.ProjectChannelMessageMentions;
            IF OBJECT_ID(N'dbo.ProjectChannelMembers', N'U') IS NOT NULL DROP TABLE dbo.ProjectChannelMembers;
            IF OBJECT_ID(N'dbo.ProjectChannelMessages', N'U') IS NOT NULL DROP TABLE dbo.ProjectChannelMessages;
            IF OBJECT_ID(N'dbo.ProjectChannels', N'U') IS NOT NULL DROP TABLE dbo.ProjectChannels;
            IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.Projects') AND name = N'UQ_Projects_IdTenant')
                ALTER TABLE dbo.Projects DROP CONSTRAINT UQ_Projects_IdTenant;
            """);
    }

    private async Task ApplySqlFileAsync(params string[] pathParts)
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), Path.Combine(pathParts)));
        await using var connection = new SqlConnection(ConnectionString);

        foreach (var batch in SplitSqlBatches(sql))
            await connection.ExecuteAsync(batch);
    }

    private static IReadOnlyList<string> SplitSqlBatches(string sql)
    {
        return Regex
            .Split(sql, @"(?im)^\s*GO\s*;?\s*$")
            .Select(batch => batch.Trim())
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToArray();
    }

    private static async Task<string[]> QueryNamesAsync(SqlConnection connection, string sql, object? parameters = null)
    {
        return (await connection.QueryAsync<string>(sql, parameters)).OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    private async Task<SeedIds> InsertSeedAsync(SqlConnection connection)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var tenantId = await connection.ExecuteScalarAsync<int>(
            "INSERT INTO dbo.Tenants (Name, Slug) OUTPUT INSERTED.Id VALUES (@Name, @Slug);",
            new { Name = $"M02 Tenant {suffix}", Slug = $"m02-tenant-{suffix}" });
        var userId = await connection.ExecuteScalarAsync<int>(
            "INSERT INTO dbo.Users (Email, DisplayName) OUTPUT INSERTED.Id VALUES (@Email, @DisplayName);",
            new { Email = $"m02-{suffix}@irondev.local", DisplayName = $"M02 User {suffix}" });
        var projectId = await InsertProjectAsync(connection, tenantId, $"M02 Project {suffix}");
        return new(tenantId, projectId, userId);
    }

    private static async Task<int> InsertProjectAsync(SqlConnection connection, int tenantId, string name)
    {
        return await connection.ExecuteScalarAsync<int>(
            "INSERT INTO dbo.Projects (TenantId, Name, Description) OUTPUT INSERTED.Id VALUES (@TenantId, @Name, N'M02 project channel schema test');",
            new { TenantId = tenantId, Name = name });
    }

    private static async Task<long> InsertChannelAsync(SqlConnection connection, SeedIds seed, string slug)
    {
        return await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO dbo.ProjectChannels (TenantId, ProjectId, Name, Slug, Description, ChannelKind, Visibility, CreatedByUserId)
            OUTPUT INSERTED.Id
            VALUES (@TenantId, @ProjectId, @Name, @Slug, N'M02 schema test channel', N'General', N'Project', @UserId);
            """,
            new { seed.TenantId, seed.ProjectId, Name = $"#{slug}", Slug = slug, seed.UserId });
    }

    private static async Task<long> InsertUserMessageAsync(SqlConnection connection, SeedIds seed, long channelId, string message)
    {
        return await connection.ExecuteScalarAsync<long>(
            """
            INSERT INTO dbo.ProjectChannelMessages (TenantId, ProjectId, ChannelId, AuthorUserId, Role, Message)
            OUTPUT INSERTED.Id
            VALUES (@TenantId, @ProjectId, @ChannelId, @UserId, N'User', @Message);
            """,
            new { seed.TenantId, seed.ProjectId, ChannelId = channelId, seed.UserId, Message = message });
    }

    private static async Task AssertSqlFailsAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (SqlException)
        {
            return;
        }

        Assert.Fail("Expected SQL Server to reject the invalid project channel row.");
    }

    private static void AssertContainsAll(IEnumerable<string> actual, params string[] expected)
    {
        var values = actual.ToArray();
        foreach (var item in expected)
            CollectionAssert.Contains(values, item);
    }

    private static void AssertContainsAll(string text, params string[] expected)
    {
        foreach (var item in expected)
            StringAssert.Contains(text, item);
    }

    private static void AssertDoesNotContainAny(string text, params string[] forbidden)
    {
        foreach (var item in forbidden)
            Assert.IsFalse(text.Contains(item, StringComparison.Ordinal), $"Unexpected forbidden marker: {item}");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        Assert.Fail("Could not locate repository root containing IronDev.slnx.");
        return string.Empty;
    }

    private sealed record SeedIds(int TenantId, int ProjectId, int UserId);
}
