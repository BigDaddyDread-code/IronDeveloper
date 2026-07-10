using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Data.SqlClient;
using Dapper;
using IronDev.Core;
using IronDev.Core.Agents;
using IronDev.Core.Auth;
using IronDev.Core.KnowledgeCompiler;
using IronDev.Data;
using IronDev.Services;
using IronDev.AI;
using IronDev.Core.Interfaces;
using IronDev.Infrastructure.Builder;
using IronDev.Infrastructure.Services;
using IronDev.Infrastructure.Services.SemanticMemory;

namespace IronDev.IntegrationTests;

/// <summary>
/// A mutable tenant context for tests — lets each test switch tenants to verify isolation.
/// </summary>
public sealed class TestTenantContext : ICurrentTenantContext
{
    public int TenantId { get; set; }

    public TestTenantContext(int tenantId = 1) => TenantId = tenantId;
}

[TestClass]
public abstract class IntegrationTestBase
{
    protected IServiceProvider ServiceProvider { get; private set; } = default!;
    protected string ConnectionString { get; private set; } = string.Empty;
    private SqlConnection? _databaseLockConnection;

    /// <summary>
    /// Mutable tenant context — tests can switch tenants mid-run to verify isolation.
    /// </summary>
    protected TestTenantContext TenantContext { get; private set; } = default!;

    [TestInitialize]
    public virtual async Task TestInitialize()
    {
        var configurationBuilder = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Test.json");

        var connectionStringOverride = Environment.GetEnvironmentVariable("ConnectionStrings__IronDeveloperDb");
        if (!string.IsNullOrWhiteSpace(connectionStringOverride))
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:IronDeveloperDb"] = connectionStringOverride
            });
        }

        // AG-6: agents resolve their LLM per profile; in tests every agent uses the
        // fake provider (no network, no key), so the resolver returns FakeLlmService.
        configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Provider"] = "fake",
            ["AgentProfiles:Root"] = Path.Combine(Path.GetTempPath(), $"irondev-test-agent-profiles-{Guid.NewGuid():N}")
        });

        var configuration = configurationBuilder.Build();

        ConnectionString = configuration.GetConnectionString("IronDeveloperDb")
            ?? throw new InvalidOperationException("Missing test connection string.");

        TenantContext = new TestTenantContext(tenantId: 1);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();

        // Register the switchable context as a singleton so the same instance is used
        // throughout the test scope — tests mutate TenantContext.TenantId to switch tenants.
        services.AddSingleton<ICurrentTenantContext>(TenantContext);

        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IChatTurnPersistenceService, ChatTurnPersistenceService>();
        services.AddScoped<IChatHistoryService, ChatHistoryService>();
        services.AddScoped<IChatBaDraftService, ChatBaDraftService>();
        services.AddScoped<IChatFeedbackService, ChatFeedbackService>();
        services.AddScoped<IProjectMemoryService, ProjectMemoryService>();
        services.AddScoped<IProjectMemoryMapService, ProjectMemoryMapService>();
        services.AddScoped<IArtifactSourceReferenceService, ArtifactSourceReferenceService>();
        services.AddScoped<IProjectProfileDetectionService, ProjectProfileDetectionService>();
        services.AddScoped<ITicketService, TicketService>();
        services.AddScoped<ICodeIndexService, SqlCodeIndexService>();
        services.AddScoped<IPromptContextBuilder, PromptContextBuilder>();
        services.AddScoped<IBuilderContextService, BuilderContextService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ILlmTraceService, LlmTraceService>();
        services.AddScoped<ICodePatchService, CodePatchService>();
        services.AddScoped<IProjectProfileService, ProjectProfileService>();
        services.AddScoped<IBuilderProposalService, BuilderProposalService>();
        services.AddScoped<IBuildErrorClassifierService, BuildErrorClassifierService>();
        services.AddScoped<IProjectContextExportService, ProjectContextExportService>();
        services.AddScoped<ISemanticArtefactRepository, SemanticArtefactRepository>();
        services.AddScoped<ISemanticChunkRepository, SemanticChunkRepository>();
        services.AddScoped<IBuilderReadinessService, BuilderReadinessService>();
        services.AddScoped<ISkeletonAgentProfileService, SkeletonAgentProfileService>();
        services.AddScoped<IAgentLlmResolver, AgentLlmResolver>();
        services.AddScoped<ICodeChangeProposalService, CodeChangeProposalService>();
        services.AddScoped<IChatClarificationClassifier, LlmChatClarificationClassifier>();
        services.AddScoped<IDotNetBuildService, DotNetRunnerService>();
        services.AddScoped<IDotNetTestService, DotNetRunnerService>();
        services.AddScoped<ILLMService, FakeLlmService>();

        ServiceProvider = services.BuildServiceProvider();

        await AcquireDatabaseLockAsync();
        await ResetDatabaseAsync();
    }

    [TestCleanup]
    public virtual async Task TestCleanup() => await ReleaseDatabaseLockAsync();

    protected async Task ResetDatabaseAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        // Delete in correct FK order: children before parents.
        var sql = """
            IF OBJECT_ID('dbo.ProjectContextDocuments', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProjectContextDocuments
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId INT NOT NULL CONSTRAINT DF_ProjectContextDocuments_Tenant DEFAULT 1,
                    ProjectId INT NOT NULL,
                    DocumentType NVARCHAR(100) NOT NULL,
                    AuthorityLevel NVARCHAR(50) NOT NULL,
                    Status NVARCHAR(50) NOT NULL CONSTRAINT DF_ProjectContextDocuments_Status DEFAULT 'Active',
                    Title NVARCHAR(200) NOT NULL,
                    Content NVARCHAR(MAX) NOT NULL,
                    Summary NVARCHAR(MAX) NULL,
                    Tags NVARCHAR(MAX) NULL,
                    AppliesToCapability NVARCHAR(200) NULL,
                    AppliesToArea NVARCHAR(200) NULL,
                    Source NVARCHAR(200) NULL,
                    SupersedesDocumentId BIGINT NULL,
                    SourceChatMessageId BIGINT NULL,
                    CreatedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectContextDocuments_CreatedDate DEFAULT SYSUTCDATETIME(),
                    UpdatedDate DATETIME2 NULL
                );
            END

            IF OBJECT_ID('dbo.ProjectObservableStates', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProjectObservableStates
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId INT NOT NULL CONSTRAINT DF_ProjectObservableStates_Tenant DEFAULT 1,
                    ProjectId INT NOT NULL,
                    ActiveCapability NVARCHAR(200) NULL,
                    ActiveMilestone NVARCHAR(200) NULL,
                    CurrentFocus NVARCHAR(500) NULL,
                    BuildReadiness NVARCHAR(100) NULL,
                    IndexStatus NVARCHAR(100) NULL,
                    BuilderMode NVARCHAR(100) NULL,
                    OpenBlockers NVARCHAR(MAX) NULL,
                    LastRecommendation NVARCHAR(MAX) NULL,
                    CurrentTargetPath NVARCHAR(1000) NULL,
                    KnownCurrentGaps NVARCHAR(MAX) NULL,
                    SnapshotJson NVARCHAR(MAX) NULL,
                    UpdatedDate DATETIME2 NOT NULL CONSTRAINT DF_ProjectObservableStates_UpdatedDate DEFAULT SYSUTCDATETIME()
                );
            END

            IF COL_LENGTH('dbo.ProjectTickets', 'SourceChatSessionId') IS NULL
            BEGIN
                ALTER TABLE dbo.ProjectTickets ADD SourceChatSessionId BIGINT NULL;
            END

            IF COL_LENGTH('dbo.ProjectTickets', 'SourceChatMessageId') IS NULL
            BEGIN
                ALTER TABLE dbo.ProjectTickets ADD SourceChatMessageId BIGINT NULL;
            END

            IF OBJECT_ID('dbo.ArtifactSourceReferences', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ArtifactSourceReferences
                (
                    ArtifactSourceReferenceId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId INT NOT NULL,
                    ProjectId INT NOT NULL,
                    ArtifactType NVARCHAR(100) NOT NULL,
                    ArtifactId BIGINT NOT NULL,
                    SourceType NVARCHAR(100) NOT NULL,
                    SourceId BIGINT NULL,
                    SourcePath NVARCHAR(1000) NULL,
                    SourceSymbol NVARCHAR(500) NULL,
                    SourceSection NVARCHAR(500) NULL,
                    SourceAnchor NVARCHAR(500) NULL,
                    ReferenceType NVARCHAR(100) NOT NULL,
                    Summary NVARCHAR(MAX) NULL,
                    RelevanceScore DECIMAL(9,4) NULL,
                    IsRequired BIT NOT NULL CONSTRAINT DF_ArtifactSourceReferences_IsRequired DEFAULT 0,
                    CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_ArtifactSourceReferences_CreatedUtc DEFAULT SYSUTCDATETIME(),
                    CreatedBy NVARCHAR(200) NULL
                );
            END

            IF OBJECT_ID('dbo.ChatTurnGovernance', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ChatTurnGovernance
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId INT NOT NULL,
                    ProjectId INT NOT NULL,
                    ChatSessionId BIGINT NOT NULL,
                    ChatMessageId BIGINT NOT NULL,
                    Mode NVARCHAR(50) NOT NULL,
                    ModeConfidence FLOAT NOT NULL,
                    ModeReason NVARCHAR(MAX) NOT NULL,
                    GateJson NVARCHAR(MAX) NOT NULL,
                    RouteSource NVARCHAR(200) NOT NULL CONSTRAINT DF_ChatTurnGovernance_RouteSource DEFAULT N'unknown',
                    RouteChallengeJson NVARCHAR(MAX) NULL,
                    BaDraftJson NVARCHAR(MAX) NULL,
                    CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_ChatTurnGovernance_CreatedUtc DEFAULT SYSUTCDATETIME()
                );
                CREATE UNIQUE INDEX UX_ChatTurnGovernance_MessageTenant ON dbo.ChatTurnGovernance(ChatMessageId, TenantId);
            END

            IF COL_LENGTH('dbo.ChatTurnGovernance', 'RouteSource') IS NULL
            BEGIN
                ALTER TABLE dbo.ChatTurnGovernance
                    ADD RouteSource NVARCHAR(200) NOT NULL
                        CONSTRAINT DF_ChatTurnGovernance_RouteSource DEFAULT N'unknown' WITH VALUES;
            END

            IF COL_LENGTH('dbo.ChatTurnGovernance', 'RouteChallengeJson') IS NULL
            BEGIN
                ALTER TABLE dbo.ChatTurnGovernance
                    ADD RouteChallengeJson NVARCHAR(MAX) NULL;
            END

            IF COL_LENGTH('dbo.ChatTurnGovernance', 'BaDraftJson') IS NULL
            BEGIN
                ALTER TABLE dbo.ChatTurnGovernance
                    ADD BaDraftJson NVARCHAR(MAX) NULL;
            END

            IF OBJECT_ID('dbo.ChatTurnClarifications', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ChatTurnClarifications
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId INT NOT NULL,
                    ProjectId INT NOT NULL,
                    ChatSessionId BIGINT NOT NULL,
                    ChatMessageId BIGINT NOT NULL,
                    Required BIT NOT NULL,
                    Kind NVARCHAR(100) NOT NULL,
                    Reason NVARCHAR(MAX) NULL,
                    QuestionsJson NVARCHAR(MAX) NOT NULL,
                    CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_ChatTurnClarifications_CreatedUtc DEFAULT SYSUTCDATETIME()
                );
                CREATE UNIQUE INDEX UX_ChatTurnClarifications_MessageTenant ON dbo.ChatTurnClarifications(ChatMessageId, TenantId);
            END

            IF OBJECT_ID('dbo.ChatTurnTraces', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ChatTurnTraces
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId INT NOT NULL,
                    ProjectId INT NOT NULL,
                    ChatSessionId BIGINT NOT NULL,
                    ChatMessageId BIGINT NOT NULL,
                    RouteTraceId NVARCHAR(200) NULL,
                    DogfoodTraceId NVARCHAR(200) NULL,
                    ContextSummary NVARCHAR(MAX) NULL,
                    LinkedFilePaths NVARCHAR(MAX) NULL,
                    LinkedSymbols NVARCHAR(MAX) NULL,
                    CreatedUtc DATETIME2 NOT NULL CONSTRAINT DF_ChatTurnTraces_CreatedUtc DEFAULT SYSUTCDATETIME()
                );
                CREATE UNIQUE INDEX UX_ChatTurnTraces_MessageTenant ON dbo.ChatTurnTraces(ChatMessageId, TenantId);
            END

            IF OBJECT_ID('dbo.ChatMessageFeedback', 'U') IS NOT NULL DELETE FROM dbo.ChatMessageFeedback;
            IF OBJECT_ID('dbo.ChatTurnTraces', 'U') IS NOT NULL DELETE FROM dbo.ChatTurnTraces;
            IF OBJECT_ID('dbo.ChatTurnClarifications', 'U') IS NOT NULL DELETE FROM dbo.ChatTurnClarifications;
            IF OBJECT_ID('dbo.ChatTurnGovernance', 'U') IS NOT NULL DELETE FROM dbo.ChatTurnGovernance;
            IF OBJECT_ID('dbo.ProjectContextDocuments', 'U') IS NOT NULL DELETE FROM dbo.ProjectContextDocuments;
            IF OBJECT_ID('dbo.ProjectObservableStates', 'U') IS NOT NULL DELETE FROM dbo.ProjectObservableStates;
            DELETE FROM dbo.ChatMessages;
            IF OBJECT_ID('dbo.ProjectChannelPins', 'U') IS NOT NULL DELETE FROM dbo.ProjectChannelPins;
            IF OBJECT_ID('dbo.ProjectChannelMessageReads', 'U') IS NOT NULL DELETE FROM dbo.ProjectChannelMessageReads;
            IF OBJECT_ID('dbo.ProjectChannelAssistantTurns', 'U') IS NOT NULL DELETE FROM dbo.ProjectChannelAssistantTurns;
            IF OBJECT_ID('dbo.ProjectChannelMessageContextLinks', 'U') IS NOT NULL DELETE FROM dbo.ProjectChannelMessageContextLinks;
            IF OBJECT_ID('dbo.ProjectChannelMessages', 'U') IS NOT NULL DELETE FROM dbo.ProjectChannelMessages;
            IF OBJECT_ID('dbo.ProjectChannelMembers', 'U') IS NOT NULL DELETE FROM dbo.ProjectChannelMembers;
            IF OBJECT_ID('dbo.ProjectChannels', 'U') IS NOT NULL DELETE FROM dbo.ProjectChannels;
            IF OBJECT_ID('dbo.RunEvents', 'U') IS NOT NULL DELETE FROM dbo.RunEvents;
            IF OBJECT_ID('dbo.Runs', 'U') IS NOT NULL DELETE FROM dbo.Runs;
            IF OBJECT_ID('dbo.ArtifactSourceReferences', 'U') IS NOT NULL DELETE FROM dbo.ArtifactSourceReferences;
            IF OBJECT_ID('dbo.SemanticSearchTraces', 'U') IS NOT NULL DELETE FROM dbo.SemanticSearchTraces;
            IF OBJECT_ID('dbo.EmbeddingJobs', 'U') IS NOT NULL DELETE FROM dbo.EmbeddingJobs;
            IF OBJECT_ID('dbo.SemanticChunks', 'U') IS NOT NULL DELETE FROM dbo.SemanticChunks;
            IF OBJECT_ID('dbo.SemanticArtefacts', 'U') IS NOT NULL DELETE FROM dbo.SemanticArtefacts;
            IF OBJECT_ID('dbo.CodeIndexEntries', 'U') IS NOT NULL DELETE FROM dbo.CodeIndexEntries;
            IF OBJECT_ID('dbo.ProjectProfiles', 'U') IS NOT NULL DELETE FROM dbo.ProjectProfiles;
            IF OBJECT_ID('dbo.ProjectCommands', 'U') IS NOT NULL DELETE FROM dbo.ProjectCommands;
            IF OBJECT_ID('dbo.ProjectProfileOptions', 'U') IS NOT NULL DELETE FROM dbo.ProjectProfileOptions;
            IF OBJECT_ID('dbo.ProjectImplementationPlans', 'U') IS NOT NULL DELETE FROM dbo.ProjectImplementationPlans;
            IF OBJECT_ID('dbo.ProjectChatSessions', 'U') IS NOT NULL DELETE FROM dbo.ProjectChatSessions;
            IF OBJECT_ID('dbo.ProjectRules', 'U') IS NOT NULL DELETE FROM dbo.ProjectRules;
            IF OBJECT_ID('dbo.ProjectDocumentLinks', 'U') IS NOT NULL DELETE FROM dbo.ProjectDocumentLinks;
            IF OBJECT_ID('dbo.ProjectDocumentVersions', 'U') IS NOT NULL DELETE FROM dbo.ProjectDocumentVersions;
            IF OBJECT_ID('dbo.ProjectDocuments', 'U') IS NOT NULL DELETE FROM dbo.ProjectDocuments;
            DELETE FROM dbo.ProjectDecisions;
            DELETE FROM dbo.ProjectFiles;
            DELETE FROM dbo.ProjectTickets;
            DELETE FROM dbo.ProjectSummaries;
            DELETE FROM dbo.Projects;
            DELETE FROM dbo.TenantUsers;
            DELETE FROM dbo.Users;
            DELETE FROM dbo.Tenants;
            """;

        await connection.ExecuteAsync(sql);
    }

    private async Task AcquireDatabaseLockAsync()
    {
        if (_databaseLockConnection is not null) return;

        _databaseLockConnection = new SqlConnection(ConnectionString);
        await _databaseLockConnection.OpenAsync();

        var result = await _databaseLockConnection.ExecuteScalarAsync<int>("""
            DECLARE @Result INT;
            EXEC @Result = sp_getapplock
                @Resource = 'IronDeveloper_Test_Database',
                @LockMode = 'Exclusive',
                @LockOwner = 'Session',
                @LockTimeout = 60000;
            SELECT @Result;
            """);

        if (result < 0)
        {
            await _databaseLockConnection.DisposeAsync();
            _databaseLockConnection = null;
            throw new TimeoutException("Timed out waiting for the IronDeveloper test database lock.");
        }
    }

    private async Task ReleaseDatabaseLockAsync()
    {
        if (_databaseLockConnection is null) return;

        try
        {
            await _databaseLockConnection.ExecuteAsync("""
                EXEC sp_releaseapplock
                    @Resource = 'IronDeveloper_Test_Database',
                    @LockOwner = 'Session';
                """);
        }
        finally
        {
            await _databaseLockConnection.DisposeAsync();
            _databaseLockConnection = null;
        }
    }

    protected async Task<int> SeedProjectAsync(int tenantId = 1, string name = "IronDev", string? localPath = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        
        await connection.ExecuteAsync($"""
            IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = {tenantId})
            BEGIN
                SET IDENTITY_INSERT dbo.Tenants ON;
                INSERT INTO dbo.Tenants (Id, Name, Slug) VALUES ({tenantId}, 'Test Tenant {tenantId}', 'test-{tenantId}');
                SET IDENTITY_INSERT dbo.Tenants OFF;
            END
        """);

        const string insertSql = """
            INSERT INTO dbo.Projects (TenantId, Name, Description, LocalPath)
            OUTPUT inserted.Id
            VALUES (@TenantId, @Name, @Description, @LocalPath);
            """;

        return await connection.QuerySingleAsync<int>(insertSql, new
        {
            TenantId = tenantId,
            Name = name,
            Description = "Integration test project",
            LocalPath = localPath
        });
    }

    protected async Task SeedProjectProfileAsync(int projectId, string testFramework = "xUnit", bool allowBuilderApply = true, int tenantId = 1)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        const string sql = """
            INSERT INTO dbo.ProjectProfiles (TenantId, ProjectId, TestFramework, AllowBuilderApply, CreatedUtc)
            VALUES (@TenantId, @ProjectId, @TestFramework, @AllowBuilderApply, SYSUTCDATETIME());
        """;

        await connection.ExecuteAsync(sql, new { TenantId = tenantId, ProjectId = projectId, TestFramework = testFramework, AllowBuilderApply = allowBuilderApply });
    }

    protected async Task SeedProjectCommandAsync(int projectId, string type, string command, int tenantId = 1)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        const string sql = """
            INSERT INTO dbo.ProjectCommands (TenantId, ProjectId, CommandType, CommandText, IsDefault, IsEnabled, CreatedUtc)
            VALUES (@TenantId, @ProjectId, @CommandType, @CommandText, 1, 1, SYSUTCDATETIME());
        """;

        await connection.ExecuteAsync(sql, new { TenantId = tenantId, ProjectId = projectId, CommandType = type, CommandText = command });
    }
}
