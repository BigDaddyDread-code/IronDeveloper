using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using IronDev.Core;
using IronDev.Core.Agents.Concrete;
using IronDev.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

/// <summary>
/// Spins up the real IronDev.Api in-process against the test database.
/// Each test class gets a fresh database reset; the factory is shared across the class.
/// </summary>
[TestClass]
public abstract class ApiTestBase
{
    private const string TestJwtKey = "irondev-test-only-jwt-key-not-from-committed-config-32chars";
    private const string DefaultTestConnectionString = "Server=(localdb)\\MSSQLLocalDB;Database=IronDeveloper_Test;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";
    protected static WebApplicationFactory<Program> Factory { get; private set; } = default!;
    protected static HttpClient Client { get; private set; } = default!;
    protected static string ConnectionString { get; private set; } = string.Empty;
    private SqlConnection? _databaseLockConnection;

    /// <summary>Known test credentials ??? seeded in SetupAsync.</summary>
    protected const string AdminEmail = "admin@irondev.local";
    protected const string AdminPassword = "password123";

    /// <summary>
    /// Tenant 1: admin IS a member.
    /// Tenant 2: admin is NOT a member (used for rejection tests).
    /// </summary>
    protected const int AssignedTenantId = 1;
    protected const int UnassignedTenantId = 2;

    [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task ClassInitialize(TestContext _)
    {
        try
        {
            Environment.SetEnvironmentVariable("IRONDEV_JWT_KEY", TestJwtKey);

            Factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Test");
                    // Force non-secret configuration values into the builder so Program.cs picks them up.
                    builder.UseSetting("Jwt:Issuer", "irondev-api");
                    builder.UseSetting("Jwt:Audience", "irondev-client");
                    builder.UseSetting("ConnectionStrings:IronDeveloperDb", TestConnectionString());
                    builder.UseSetting("LocalTest:WorkspaceRoot", Path.Combine(Path.GetTempPath(), "IronDevTestWorkspaces"));
                    builder.UseSetting("LocalTest:LogsRoot", Path.Combine(Path.GetTempPath(), "IronDevTestLogs"));
                    builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:1420");
                    builder.UseSetting("Cors:AllowedOrigins:1", "http://127.0.0.1:1420");
                    // The suite performs many rapid logins from one in-process client; production-shaped
                    // rate limits would fail unrelated tests. Rate limiting itself is covered by
                    // SensitiveApiRateLimitTests with its own dedicated factory and tight limits.
                    builder.UseSetting("RateLimiting:AuthLogin:PermitLimit", "1000");
                    builder.UseSetting("RateLimiting:SensitiveApi:PermitLimit", "1000");

                    builder.ConfigureAppConfiguration((context, cfg) =>
                    {
                        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.Test.json");
                        if (File.Exists(path))
                        {
                            cfg.AddJsonFile(path, optional: false);
                        }

                        cfg.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["Ai:Provider"] = "fake",
                            ["AgentProfiles:Root"] = Path.Combine(Path.GetTempPath(), $"irondev-api-test-agent-profiles-{Guid.NewGuid():N}"),
                            ["ConnectionStrings:IronDeveloperDb"] = TestConnectionString(),
                            ["Jwt:Key"] = TestJwtKey
                        });
                    });

                    builder.ConfigureServices(services =>
                    {
                        services.RemoveAll<ILLMService>();
                        services.AddScoped<ILLMService, FakeLlmService>();
                        services.RemoveAll<IStoredManualIndependentCriticAgentService>();
                        services.AddScoped<IStoredManualIndependentCriticAgentService, StartupOnlyStoredCriticService>();
                    });
                });

            Client = Factory.CreateClient();

            // HERO-2 root-safety fix: NEVER resolve the destructive provisioning/reset
            // connection from the composed app configuration. App-level sources were
            // observed outranking the test overrides here, which pointed this connection
            // at the REAL IronDeveloper database — running DropGovernanceSql and domain
            // resets against real data. The destructive connection is pinned to the
            // explicit test connection string and hard-guarded to a test-shaped catalog.
            ConnectionString = TestConnectionString();
            var catalog = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;
            if (!IsTestShapedCatalog(catalog))
            {
                throw new InvalidOperationException(
                    $"Refusing to provision/reset database '{catalog}': API test provisioning is destructive " +
                    "and may only target an explicitly test-shaped catalog (name ending in '_Test' or starting with 'IronDev_CI_').");
            }

            await SetupDatabaseAsync();
        }
        catch (Exception ex)
        {
            // Log to console so it's visible in test output
            Console.WriteLine($"FATAL: ClassInitialize failed: {ex}");
            throw;
        }
    }

    [ClassCleanup(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task ClassCleanup()
    {
        Client?.Dispose();
        Environment.SetEnvironmentVariable("IRONDEV_JWT_KEY", null);
        if (Factory != null)
        {
            await Factory.DisposeAsync();
        }
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        await AcquireDatabaseLockAsync();
        await ResetDomainDataAsync();
    }

    [TestCleanup]
    public async Task TestCleanup() => await ReleaseDatabaseLockAsync();

    // ?????? Database helpers ??????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????

    /// <summary>
    /// Ensures the required schema extensions exist.
    /// Called once per test class.
    /// </summary>
    private static async Task SetupDatabaseAsync()
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(DropGovernanceSql);

        await conn.ExecuteAsync("""
            -- Extend Projects (Grounding)
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Projects') AND name = 'LastIndexedUtc')
                ALTER TABLE dbo.Projects ADD LastIndexedUtc DATETIME2 NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Projects') AND name = N'IndexingStatus')
                ALTER TABLE dbo.Projects ADD IndexingStatus NVARCHAR(50) NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Projects') AND name = N'IndexedFileCount')
                ALTER TABLE dbo.Projects ADD IndexedFileCount INT NULL;

            -- Extend ProjectFiles (Grounding)
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectFiles') AND name = 'LastIndexedUtc')
                ALTER TABLE dbo.ProjectFiles ADD LastIndexedUtc DATETIME2 NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectFiles') AND name = 'IndexingStatus')
                ALTER TABLE dbo.ProjectFiles ADD IndexingStatus NVARCHAR(50) NULL;

            -- Extend ProjectTickets (Structured Tickets)
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'Title')
                ALTER TABLE dbo.ProjectTickets ADD Title NVARCHAR(200) NOT NULL DEFAULT '';
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'TicketType')
                ALTER TABLE dbo.ProjectTickets ADD TicketType NVARCHAR(50) NOT NULL DEFAULT 'Task';
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'Priority')
                ALTER TABLE dbo.ProjectTickets ADD Priority NVARCHAR(50) NOT NULL DEFAULT 'Medium';
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'Summary')
                ALTER TABLE dbo.ProjectTickets ADD Summary NVARCHAR(MAX) NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'Background')
                ALTER TABLE dbo.ProjectTickets ADD Background NVARCHAR(MAX) NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = N'Problem')
                ALTER TABLE dbo.ProjectTickets ADD Problem NVARCHAR(MAX) NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'AcceptanceCriteria')
                ALTER TABLE dbo.ProjectTickets ADD AcceptanceCriteria NVARCHAR(MAX) NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'TechnicalNotes')
                ALTER TABLE dbo.ProjectTickets ADD TechnicalNotes NVARCHAR(MAX) NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'Status')
                ALTER TABLE dbo.ProjectTickets ADD Status NVARCHAR(50) NOT NULL DEFAULT 'Draft';
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'LinkedFilePaths')
                ALTER TABLE dbo.ProjectTickets ADD LinkedFilePaths NVARCHAR(MAX) NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'LinkedCodeIndexEntryIds')
                ALTER TABLE dbo.ProjectTickets ADD LinkedCodeIndexEntryIds NVARCHAR(MAX) NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ProjectTickets') AND name = 'LinkedSymbols')
                ALTER TABLE dbo.ProjectTickets ADD LinkedSymbols NVARCHAR(MAX) NULL;

            -- Extend ProjectDocuments (backend-owned upload metadata)
            IF OBJECT_ID('dbo.ProjectDocuments', 'U') IS NOT NULL AND COL_LENGTH('dbo.ProjectDocuments', 'Origin') IS NULL
                ALTER TABLE dbo.ProjectDocuments ADD Origin NVARCHAR(50) NOT NULL DEFAULT 'CreatedInIronDev' WITH VALUES;

            IF OBJECT_ID('dbo.ProjectDocuments', 'U') IS NOT NULL AND COL_LENGTH('dbo.ProjectDocuments', 'ProcessingStatus') IS NULL
                ALTER TABLE dbo.ProjectDocuments ADD ProcessingStatus NVARCHAR(50) NOT NULL DEFAULT 'Draft' WITH VALUES;

            IF OBJECT_ID('dbo.ProjectDocuments', 'U') IS NOT NULL AND COL_LENGTH('dbo.ProjectDocuments', 'Description') IS NULL
                ALTER TABLE dbo.ProjectDocuments ADD Description NVARCHAR(1000) NULL;

            IF OBJECT_ID('dbo.ProjectDocuments', 'U') IS NOT NULL AND COL_LENGTH('dbo.ProjectDocuments', 'Visibility') IS NULL
                ALTER TABLE dbo.ProjectDocuments ADD Visibility NVARCHAR(50) NOT NULL DEFAULT 'Project' WITH VALUES;

            IF OBJECT_ID('dbo.ProjectDocuments', 'U') IS NOT NULL AND COL_LENGTH('dbo.ProjectDocuments', 'OriginalFileName') IS NULL
                ALTER TABLE dbo.ProjectDocuments ADD OriginalFileName NVARCHAR(260) NULL;

            IF OBJECT_ID('dbo.ProjectDocuments', 'U') IS NOT NULL AND COL_LENGTH('dbo.ProjectDocuments', 'MediaType') IS NULL
                ALTER TABLE dbo.ProjectDocuments ADD MediaType NVARCHAR(100) NULL;

            IF OBJECT_ID('dbo.ProjectDocuments', 'U') IS NOT NULL AND COL_LENGTH('dbo.ProjectDocuments', 'ByteSize') IS NULL
                ALTER TABLE dbo.ProjectDocuments ADD ByteSize BIGINT NULL;

            IF OBJECT_ID('dbo.ProjectDocuments', 'U') IS NOT NULL AND COL_LENGTH('dbo.ProjectDocuments', 'ProcessingFailureReason') IS NULL
                ALTER TABLE dbo.ProjectDocuments ADD ProcessingFailureReason NVARCHAR(1000) NULL;

            IF OBJECT_ID('dbo.ProjectDocuments', 'U') IS NOT NULL AND COL_LENGTH('dbo.ProjectDocuments', 'ProcessingStartedAtUtc') IS NULL
                ALTER TABLE dbo.ProjectDocuments ADD ProcessingStartedAtUtc DATETIME2(7) NULL;

            IF OBJECT_ID('dbo.ProjectDocuments', 'U') IS NOT NULL AND COL_LENGTH('dbo.ProjectDocuments', 'ProcessingCompletedAtUtc') IS NULL
                ALTER TABLE dbo.ProjectDocuments ADD ProcessingCompletedAtUtc DATETIME2(7) NULL;

            -- Ensure ProjectChatSessions exists
            IF OBJECT_ID('dbo.ProjectChatSessions', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ProjectChatSessions
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId INT NOT NULL,
                    ProjectId INT NOT NULL,
                    Title NVARCHAR(200) NOT NULL DEFAULT 'New Chat',
                    Summary NVARCHAR(MAX) NULL,
                    CreatedDate DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    UpdatedDate DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    PrimaryTicketId BIGINT NULL,
                    PrimaryDecisionId BIGINT NULL,
                    PrimaryPlanId BIGINT NULL,
                    OriginTicketId BIGINT NULL,
                    OriginDecisionId BIGINT NULL,
                    OriginPlanId BIGINT NULL,
                    CONSTRAINT FK_ProjectChatSessions_Tenants_API FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT FK_ProjectChatSessions_Projects_API FOREIGN KEY (ProjectId) REFERENCES dbo.Projects(Id)
                );
            END

            -- Extend ChatMessages (Sessions & Grounding)
            IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ChatMessages') AND name = 'Tags')
                ALTER TABLE dbo.ChatMessages ALTER COLUMN Tags NVARCHAR(MAX) NULL;

            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ChatMessages') AND name = 'ChatSessionId')
                ALTER TABLE dbo.ChatMessages ADD ChatSessionId BIGINT NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ChatMessages') AND name = 'ContextSummary')
                ALTER TABLE dbo.ChatMessages ADD ContextSummary NVARCHAR(MAX) NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ChatMessages') AND name = 'LinkedFilePaths')
                ALTER TABLE dbo.ChatMessages ADD LinkedFilePaths NVARCHAR(MAX) NULL;
            
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ChatMessages') AND name = 'LinkedSymbols')
                ALTER TABLE dbo.ChatMessages ADD LinkedSymbols NVARCHAR(MAX) NULL;
            """);

        await ApplySqlFileAsync(conn, "Database", "migrate_governance_event.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_tool_request.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_tool_gate_decision.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_approval_decision.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_accepted_approval.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_policy_satisfaction.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_patch_artifact.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_rollback_support_receipt.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_source_apply_dry_run_receipt.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_policy_decision_event.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_dogfood_receipt.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_thoughtledger_governance_event_reference.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_workflow_run.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_workflow_step_store.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_workflow_checkpoint_store.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_workflow_transition_record.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_release_readiness_decision_record.sql");

        // HERO-2: the real (non-test-double) loop needs the tables the fakes never
        // touched — project profiles, code indexing, and the agent-run audit store
        // the real critic persists durable reviews into.
        await ApplySqlFileAsync(conn, "Database", "migrate_project_profiles.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_code_indexing.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_projects_indexing_fields.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_agent_run_audit_envelope.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_chat_document_sources.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_project_channels.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_project_collaboration.sql");
        await ApplySqlFileAsync(conn, "Database", "migrate_versioned_collaboration_writes.sql");
    }

    private const string DropGovernanceSql = """
        IF OBJECT_ID(N'governance.usp_ReleaseReadinessDecisionRecord_Save', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ReleaseReadinessDecisionRecord_Save;
        IF OBJECT_ID(N'governance.usp_ReleaseReadinessDecisionRecord_Get', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ReleaseReadinessDecisionRecord_Get;
        IF OBJECT_ID(N'governance.usp_ReleaseReadinessDecisionRecord_GetByHash', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ReleaseReadinessDecisionRecord_GetByHash;
        IF OBJECT_ID(N'governance.usp_ReleaseReadinessDecisionRecord_ListByReleaseReadinessReport', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ReleaseReadinessDecisionRecord_ListByReleaseReadinessReport;
        IF OBJECT_ID(N'governance.usp_ReleaseReadinessDecisionRecord_ListByWorkflowRun', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ReleaseReadinessDecisionRecord_ListByWorkflowRun;
        IF OBJECT_ID(N'governance.usp_ReleaseReadinessDecisionRecord_ListBySubject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ReleaseReadinessDecisionRecord_ListBySubject;
        IF OBJECT_ID(N'governance.TR_ReleaseReadinessDecisionRecord_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_ReleaseReadinessDecisionRecord_ValidateInsert;
        IF OBJECT_ID(N'governance.TR_ReleaseReadinessDecisionRecord_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_ReleaseReadinessDecisionRecord_BlockUpdateDelete;
        IF OBJECT_ID(N'governance.ReleaseReadinessDecisionRecord', N'U') IS NOT NULL DROP TABLE governance.ReleaseReadinessDecisionRecord;
        IF OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_Save', N'P') IS NOT NULL DROP PROCEDURE governance.usp_WorkflowTransitionRecord_Save;
        IF OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_Get', N'P') IS NOT NULL DROP PROCEDURE governance.usp_WorkflowTransitionRecord_Get;
        IF OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_GetByRecordHash', N'P') IS NOT NULL DROP PROCEDURE governance.usp_WorkflowTransitionRecord_GetByRecordHash;
        IF OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_ListByWorkflowRun', N'P') IS NOT NULL DROP PROCEDURE governance.usp_WorkflowTransitionRecord_ListByWorkflowRun;
        IF OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_ListByWorkflowStep', N'P') IS NOT NULL DROP PROCEDURE governance.usp_WorkflowTransitionRecord_ListByWorkflowStep;
        IF OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_ListByContinuationGateEvaluation', N'P') IS NOT NULL DROP PROCEDURE governance.usp_WorkflowTransitionRecord_ListByContinuationGateEvaluation;
        IF OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_ListBySourceApplyReceipt', N'P') IS NOT NULL DROP PROCEDURE governance.usp_WorkflowTransitionRecord_ListBySourceApplyReceipt;
        IF OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_ListByRollbackExecutionReceipt', N'P') IS NOT NULL DROP PROCEDURE governance.usp_WorkflowTransitionRecord_ListByRollbackExecutionReceipt;
        IF OBJECT_ID(N'governance.TR_WorkflowTransitionRecord_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_WorkflowTransitionRecord_ValidateInsert;
        IF OBJECT_ID(N'governance.TR_WorkflowTransitionRecord_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_WorkflowTransitionRecord_BlockUpdateDelete;
        IF OBJECT_ID(N'governance.WorkflowTransitionRecord', N'U') IS NOT NULL DROP TABLE governance.WorkflowTransitionRecord;
        IF OBJECT_ID(N'governance.usp_RollbackSupportReceipt_Save', N'P') IS NOT NULL DROP PROCEDURE governance.usp_RollbackSupportReceipt_Save;
        IF OBJECT_ID(N'governance.usp_RollbackSupportReceipt_Get', N'P') IS NOT NULL DROP PROCEDURE governance.usp_RollbackSupportReceipt_Get;
        IF OBJECT_ID(N'governance.usp_RollbackSupportReceipt_GetByReceiptHash', N'P') IS NOT NULL DROP PROCEDURE governance.usp_RollbackSupportReceipt_GetByReceiptHash;
        IF OBJECT_ID(N'governance.usp_RollbackSupportReceipt_ListByPatchArtifact', N'P') IS NOT NULL DROP PROCEDURE governance.usp_RollbackSupportReceipt_ListByPatchArtifact;
        IF OBJECT_ID(N'governance.usp_RollbackSupportReceipt_ListByPatchHash', N'P') IS NOT NULL DROP PROCEDURE governance.usp_RollbackSupportReceipt_ListByPatchHash;
        IF OBJECT_ID(N'governance.usp_RollbackSupportReceipt_ListByRollbackPlan', N'P') IS NOT NULL DROP PROCEDURE governance.usp_RollbackSupportReceipt_ListByRollbackPlan;
        IF OBJECT_ID(N'governance.usp_RollbackSupportReceipt_ListBySourceBaselineHash', N'P') IS NOT NULL DROP PROCEDURE governance.usp_RollbackSupportReceipt_ListBySourceBaselineHash;
        IF OBJECT_ID(N'governance.TR_RollbackSupportReceipt_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_RollbackSupportReceipt_ValidateInsert;
        IF OBJECT_ID(N'governance.TR_RollbackSupportReceipt_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_RollbackSupportReceipt_BlockUpdateDelete;
        IF OBJECT_ID(N'governance.RollbackSupportReceipt', N'U') IS NOT NULL DROP TABLE governance.RollbackSupportReceipt;
        IF OBJECT_ID(N'governance.usp_SourceApplyDryRunReceipt_Save', N'P') IS NOT NULL DROP PROCEDURE governance.usp_SourceApplyDryRunReceipt_Save;
        IF OBJECT_ID(N'governance.usp_SourceApplyDryRunReceipt_Get', N'P') IS NOT NULL DROP PROCEDURE governance.usp_SourceApplyDryRunReceipt_Get;
        IF OBJECT_ID(N'governance.usp_SourceApplyDryRunReceipt_GetByReceiptHash', N'P') IS NOT NULL DROP PROCEDURE governance.usp_SourceApplyDryRunReceipt_GetByReceiptHash;
        IF OBJECT_ID(N'governance.usp_SourceApplyDryRunReceipt_ListBySourceApplyRequest', N'P') IS NOT NULL DROP PROCEDURE governance.usp_SourceApplyDryRunReceipt_ListBySourceApplyRequest;
        IF OBJECT_ID(N'governance.usp_SourceApplyDryRunReceipt_ListBySourceApplyGateEvaluation', N'P') IS NOT NULL DROP PROCEDURE governance.usp_SourceApplyDryRunReceipt_ListBySourceApplyGateEvaluation;
        IF OBJECT_ID(N'governance.usp_SourceApplyDryRunReceipt_ListByPatchArtifact', N'P') IS NOT NULL DROP PROCEDURE governance.usp_SourceApplyDryRunReceipt_ListByPatchArtifact;
        IF OBJECT_ID(N'governance.usp_SourceApplyDryRunReceipt_ListByRollbackSupportReceipt', N'P') IS NOT NULL DROP PROCEDURE governance.usp_SourceApplyDryRunReceipt_ListByRollbackSupportReceipt;
        IF OBJECT_ID(N'governance.TR_SourceApplyDryRunReceipt_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_SourceApplyDryRunReceipt_ValidateInsert;
        IF OBJECT_ID(N'governance.TR_SourceApplyDryRunReceipt_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_SourceApplyDryRunReceipt_BlockUpdateDelete;
        IF OBJECT_ID(N'governance.SourceApplyDryRunReceipt', N'U') IS NOT NULL DROP TABLE governance.SourceApplyDryRunReceipt;
        IF OBJECT_ID(N'governance.usp_PatchArtifact_Save', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PatchArtifact_Save;
        IF OBJECT_ID(N'governance.usp_PatchArtifact_Get', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PatchArtifact_Get;
        IF OBJECT_ID(N'governance.usp_PatchArtifact_ListByDryRunReceiptHash', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PatchArtifact_ListByDryRunReceiptHash;
        IF OBJECT_ID(N'governance.usp_PatchArtifact_ListByDryRunAuditHash', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PatchArtifact_ListByDryRunAuditHash;
        IF OBJECT_ID(N'governance.usp_PatchArtifact_ListByControlledDryRunRequest', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PatchArtifact_ListByControlledDryRunRequest;
        IF OBJECT_ID(N'governance.usp_PatchArtifact_ListBySubject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PatchArtifact_ListBySubject;
        IF OBJECT_ID(N'governance.usp_PatchArtifact_ListByPatchHash', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PatchArtifact_ListByPatchHash;
        IF OBJECT_ID(N'governance.usp_PatchArtifact_ListBySourceBaselineHash', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PatchArtifact_ListBySourceBaselineHash;
        IF OBJECT_ID(N'governance.TR_PatchArtifact_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_PatchArtifact_ValidateInsert;
        IF OBJECT_ID(N'governance.TR_PatchArtifact_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_PatchArtifact_BlockUpdateDelete;
        IF OBJECT_ID(N'governance.PatchArtifact', N'U') IS NOT NULL DROP TABLE governance.PatchArtifact;
        IF OBJECT_ID(N'governance.usp_ThoughtLedgerGovernanceEventReference_Record', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ThoughtLedgerGovernanceEventReference_Record;
        IF OBJECT_ID(N'governance.usp_ThoughtLedgerGovernanceEventReference_GetById', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ThoughtLedgerGovernanceEventReference_GetById;
        IF OBJECT_ID(N'governance.usp_ThoughtLedgerGovernanceEventReference_ListForThoughtLedgerEntry', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ThoughtLedgerGovernanceEventReference_ListForThoughtLedgerEntry;
        IF OBJECT_ID(N'governance.usp_ThoughtLedgerGovernanceEventReference_ListForGovernanceEvent', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ThoughtLedgerGovernanceEventReference_ListForGovernanceEvent;
        IF OBJECT_ID(N'governance.usp_ThoughtLedgerGovernanceEventReference_ListForCorrelation', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ThoughtLedgerGovernanceEventReference_ListForCorrelation;
        IF OBJECT_ID(N'governance.TR_ThoughtLedgerGovernanceEventReference_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_ThoughtLedgerGovernanceEventReference_ValidateInsert;
        IF OBJECT_ID(N'governance.TR_ThoughtLedgerGovernanceEventReference_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_ThoughtLedgerGovernanceEventReference_BlockUpdateDelete;
        IF OBJECT_ID(N'governance.ThoughtLedgerGovernanceEventReference', N'U') IS NOT NULL DROP TABLE governance.ThoughtLedgerGovernanceEventReference;
        IF OBJECT_ID(N'governance.usp_DogfoodReceipt_Record', N'P') IS NOT NULL DROP PROCEDURE governance.usp_DogfoodReceipt_Record;
        IF OBJECT_ID(N'governance.usp_DogfoodReceipt_GetById', N'P') IS NOT NULL DROP PROCEDURE governance.usp_DogfoodReceipt_GetById;
        IF OBJECT_ID(N'governance.usp_DogfoodReceipt_ListForSubject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_DogfoodReceipt_ListForSubject;
        IF OBJECT_ID(N'governance.usp_DogfoodReceipt_ListForProject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_DogfoodReceipt_ListForProject;
        IF OBJECT_ID(N'governance.usp_DogfoodReceipt_ListForCorrelation', N'P') IS NOT NULL DROP PROCEDURE governance.usp_DogfoodReceipt_ListForCorrelation;
        IF OBJECT_ID(N'governance.TR_DogfoodReceipt_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_DogfoodReceipt_ValidateInsert;
        IF OBJECT_ID(N'governance.TR_DogfoodReceipt_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_DogfoodReceipt_BlockUpdateDelete;
        IF OBJECT_ID(N'governance.DogfoodReceipt', N'U') IS NOT NULL DROP TABLE governance.DogfoodReceipt;
        IF OBJECT_ID(N'governance.usp_PolicyDecisionEvent_Record', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PolicyDecisionEvent_Record;
        IF OBJECT_ID(N'governance.usp_PolicyDecisionEvent_GetById', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PolicyDecisionEvent_GetById;
        IF OBJECT_ID(N'governance.usp_PolicyDecisionEvent_ListForSubject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PolicyDecisionEvent_ListForSubject;
        IF OBJECT_ID(N'governance.usp_PolicyDecisionEvent_ListForProject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PolicyDecisionEvent_ListForProject;
        IF OBJECT_ID(N'governance.usp_PolicyDecisionEvent_ListForCorrelation', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PolicyDecisionEvent_ListForCorrelation;
        IF OBJECT_ID(N'governance.TR_PolicyDecisionEvent_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_PolicyDecisionEvent_ValidateInsert;
        IF OBJECT_ID(N'governance.TR_PolicyDecisionEvent_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_PolicyDecisionEvent_BlockUpdateDelete;
        IF OBJECT_ID(N'governance.PolicyDecisionEvent', N'U') IS NOT NULL DROP TABLE governance.PolicyDecisionEvent;
        IF OBJECT_ID(N'governance.usp_PolicySatisfaction_Save', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PolicySatisfaction_Save;
        IF OBJECT_ID(N'governance.usp_PolicySatisfaction_Get', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PolicySatisfaction_Get;
        IF OBJECT_ID(N'governance.usp_PolicySatisfaction_ListBySubject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PolicySatisfaction_ListBySubject;
        IF OBJECT_ID(N'governance.usp_PolicySatisfaction_ListByAcceptedApproval', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PolicySatisfaction_ListByAcceptedApproval;
        IF OBJECT_ID(N'governance.usp_PolicySatisfaction_ListByProjectAndCorrelation', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PolicySatisfaction_ListByProjectAndCorrelation;
        IF OBJECT_ID(N'governance.TR_PolicySatisfaction_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_PolicySatisfaction_ValidateInsert;
        IF OBJECT_ID(N'governance.TR_PolicySatisfaction_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_PolicySatisfaction_BlockUpdateDelete;
        IF OBJECT_ID(N'governance.PolicySatisfaction', N'U') IS NOT NULL DROP TABLE governance.PolicySatisfaction;
        IF OBJECT_ID(N'governance.usp_AcceptedApproval_Save', N'P') IS NOT NULL DROP PROCEDURE governance.usp_AcceptedApproval_Save;
        IF OBJECT_ID(N'governance.usp_AcceptedApproval_Get', N'P') IS NOT NULL DROP PROCEDURE governance.usp_AcceptedApproval_Get;
        IF OBJECT_ID(N'governance.usp_AcceptedApproval_ListByTarget', N'P') IS NOT NULL DROP PROCEDURE governance.usp_AcceptedApproval_ListByTarget;
        IF OBJECT_ID(N'governance.usp_AcceptedApproval_ListByCorrelation', N'P') IS NOT NULL DROP PROCEDURE governance.usp_AcceptedApproval_ListByCorrelation;
        IF OBJECT_ID(N'governance.usp_AcceptedApproval_ListByProjectAndCorrelation', N'P') IS NOT NULL DROP PROCEDURE governance.usp_AcceptedApproval_ListByProjectAndCorrelation;
        IF OBJECT_ID(N'governance.TR_AcceptedApproval_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_AcceptedApproval_ValidateInsert;
        IF OBJECT_ID(N'governance.TR_AcceptedApproval_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_AcceptedApproval_BlockUpdateDelete;
        IF OBJECT_ID(N'governance.AcceptedApproval', N'U') IS NOT NULL DROP TABLE governance.AcceptedApproval;
        IF OBJECT_ID(N'governance.usp_ApprovalDecision_Record', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ApprovalDecision_Record;
        IF OBJECT_ID(N'governance.usp_ApprovalDecision_GetById', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ApprovalDecision_GetById;
        IF OBJECT_ID(N'governance.usp_ApprovalDecision_ListForSubject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ApprovalDecision_ListForSubject;
        IF OBJECT_ID(N'governance.usp_ApprovalDecision_ListForProject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ApprovalDecision_ListForProject;
        IF OBJECT_ID(N'governance.usp_ApprovalDecision_ListForCorrelation', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ApprovalDecision_ListForCorrelation;
        IF OBJECT_ID(N'governance.TR_ApprovalDecision_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_ApprovalDecision_ValidateInsert;
        IF OBJECT_ID(N'governance.TR_ApprovalDecision_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_ApprovalDecision_BlockUpdateDelete;
        IF OBJECT_ID(N'governance.ApprovalDecision', N'U') IS NOT NULL DROP TABLE governance.ApprovalDecision;
        IF OBJECT_ID(N'governance.usp_ToolGateDecision_Record', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ToolGateDecision_Record;
        IF OBJECT_ID(N'governance.usp_ToolGateDecision_GetById', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ToolGateDecision_GetById;
        IF OBJECT_ID(N'governance.usp_ToolGateDecision_ListForToolRequest', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ToolGateDecision_ListForToolRequest;
        IF OBJECT_ID(N'governance.usp_ToolGateDecision_ListForProject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ToolGateDecision_ListForProject;
        IF OBJECT_ID(N'governance.usp_ToolGateDecision_ListForCorrelation', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ToolGateDecision_ListForCorrelation;
        IF OBJECT_ID(N'governance.TR_ToolGateDecision_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_ToolGateDecision_BlockUpdateDelete;
        IF OBJECT_ID(N'governance.ToolGateDecision', N'U') IS NOT NULL DROP TABLE governance.ToolGateDecision;
        IF OBJECT_ID(N'governance.usp_ToolRequest_Create', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ToolRequest_Create;
        IF OBJECT_ID(N'governance.usp_ToolRequest_GetById', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ToolRequest_GetById;
        IF OBJECT_ID(N'governance.usp_ToolRequest_ListForProject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ToolRequest_ListForProject;
        IF OBJECT_ID(N'governance.usp_ToolRequest_ListForCorrelation', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ToolRequest_ListForCorrelation;
        IF OBJECT_ID(N'governance.ToolRequest', N'U') IS NOT NULL DROP TABLE governance.ToolRequest;
        IF OBJECT_ID(N'governance.AppendGovernanceEvent', N'P') IS NOT NULL DROP PROCEDURE governance.AppendGovernanceEvent;
        IF OBJECT_ID(N'governance.GetGovernanceEvent', N'P') IS NOT NULL DROP PROCEDURE governance.GetGovernanceEvent;
        IF OBJECT_ID(N'governance.ListGovernanceEventsForProject', N'P') IS NOT NULL DROP PROCEDURE governance.ListGovernanceEventsForProject;
        IF OBJECT_ID(N'governance.ListGovernanceEventsForCorrelation', N'P') IS NOT NULL DROP PROCEDURE governance.ListGovernanceEventsForCorrelation;
        IF OBJECT_ID(N'governance.ListGovernanceEventsForSubject', N'P') IS NOT NULL DROP PROCEDURE governance.ListGovernanceEventsForSubject;
        IF OBJECT_ID(N'governance.ListGovernanceEventsCausedBy', N'P') IS NOT NULL DROP PROCEDURE governance.ListGovernanceEventsCausedBy;
        IF OBJECT_ID(N'governance.TR_GovernanceEvent_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_GovernanceEvent_BlockUpdateDelete;
        IF OBJECT_ID(N'governance.GovernanceEvent', N'U') IS NOT NULL DROP TABLE governance.GovernanceEvent;

        -- The explicit list above drifts as new governance objects appear. Sweep whatever remains
        -- in the schema dynamically so the drop never fails on an object the list has not learned about.
        IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance')
        BEGIN
            DECLARE @sweep NVARCHAR(MAX) = N'';
            SELECT @sweep = @sweep + N'DROP TRIGGER governance.' + QUOTENAME(t.name) + N';'
            FROM sys.triggers t
            INNER JOIN sys.objects o ON o.object_id = t.parent_id
            WHERE o.schema_id = SCHEMA_ID(N'governance');
            SELECT @sweep = @sweep + N'DROP PROCEDURE governance.' + QUOTENAME(p.name) + N';'
            FROM sys.procedures p
            WHERE p.schema_id = SCHEMA_ID(N'governance');
            SELECT @sweep = @sweep + N'DROP VIEW governance.' + QUOTENAME(v.name) + N';'
            FROM sys.views v
            WHERE v.schema_id = SCHEMA_ID(N'governance');
            SELECT @sweep = @sweep + N'DROP TABLE governance.' + QUOTENAME(tb.name) + N';'
            FROM sys.tables tb
            WHERE tb.schema_id = SCHEMA_ID(N'governance');
            SELECT @sweep = @sweep + N'DROP FUNCTION governance.' + QUOTENAME(f.name) + N';'
            FROM sys.objects f
            WHERE f.schema_id = SCHEMA_ID(N'governance') AND f.type IN ('FN', 'IF', 'TF');
            IF LEN(@sweep) > 0 EXEC sp_executesql @sweep;
        END

        IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance') DROP SCHEMA governance;
        """;

    private static async Task ApplySqlFileAsync(SqlConnection connection, params string[] pathParts)
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), Path.Combine(pathParts)));
        AssertNoCatalogHijack(sql, pathParts[^1]);
        foreach (var batch in SplitSqlBatches(sql))
            await connection.ExecuteAsync(batch);
    }

    /// <summary>
    /// A migration must never choose its own catalog: the CONNECTION decides the
    /// database. A `USE` statement silently rides the provisioning connection onto
    /// another database — this is exactly how test provisioning once escaped to the
    /// real IronDeveloper catalog and how CI broke on a nonexistent one.
    /// </summary>
    internal static void AssertNoCatalogHijack(string sql, string fileName)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(sql, @"(?im)^\s*USE\s"))
        {
            throw new InvalidOperationException(
                $"Refusing to apply migration '{fileName}': it contains a USE statement. " +
                "Migrations applied by the test host must be catalog-agnostic — the connection chooses the database, never the script.");
        }
    }

    private static IReadOnlyList<string> SplitSqlBatches(string sql) =>
        System.Text.RegularExpressions.Regex.Split(
                sql.Replace("\r\n", "\n", StringComparison.Ordinal),
                @"(?im)^\s*GO\s*$")
            .Select(batch => batch.Trim())
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToArray();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    /// <summary>Clears domain data between tests; tenant/user seed is preserved and synchronized.</summary>
    private static async Task ResetDomainDataAsync()
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        
        // 1. Wipe domain data
        await conn.ExecuteAsync("""
            IF OBJECT_ID('governance.TR_PatchArtifact_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER governance.TR_PatchArtifact_BlockUpdateDelete ON governance.PatchArtifact;
            IF OBJECT_ID('governance.TR_RollbackSupportReceipt_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER governance.TR_RollbackSupportReceipt_BlockUpdateDelete ON governance.RollbackSupportReceipt;
            IF OBJECT_ID('governance.TR_SourceApplyDryRunReceipt_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER governance.TR_SourceApplyDryRunReceipt_BlockUpdateDelete ON governance.SourceApplyDryRunReceipt;
            IF OBJECT_ID('governance.TR_WorkflowTransitionRecord_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER governance.TR_WorkflowTransitionRecord_BlockUpdateDelete ON governance.WorkflowTransitionRecord;
            IF OBJECT_ID('governance.TR_ReleaseReadinessDecisionRecord_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER governance.TR_ReleaseReadinessDecisionRecord_BlockUpdateDelete ON governance.ReleaseReadinessDecisionRecord;
            IF OBJECT_ID('governance.TR_ThoughtLedgerGovernanceEventReference_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER governance.TR_ThoughtLedgerGovernanceEventReference_BlockUpdateDelete ON governance.ThoughtLedgerGovernanceEventReference;
            IF OBJECT_ID('governance.TR_DogfoodReceipt_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER governance.TR_DogfoodReceipt_BlockUpdateDelete ON governance.DogfoodReceipt;
            IF OBJECT_ID('governance.TR_PolicyDecisionEvent_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER governance.TR_PolicyDecisionEvent_BlockUpdateDelete ON governance.PolicyDecisionEvent;
            IF OBJECT_ID('governance.TR_PolicySatisfaction_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER governance.TR_PolicySatisfaction_BlockUpdateDelete ON governance.PolicySatisfaction;
            IF OBJECT_ID('governance.TR_AcceptedApproval_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER governance.TR_AcceptedApproval_BlockUpdateDelete ON governance.AcceptedApproval;
            IF OBJECT_ID('governance.TR_ApprovalDecision_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER governance.TR_ApprovalDecision_BlockUpdateDelete ON governance.ApprovalDecision;
            IF OBJECT_ID('governance.TR_ToolGateDecision_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER governance.TR_ToolGateDecision_BlockUpdateDelete ON governance.ToolGateDecision;
            IF OBJECT_ID('workflow.TR_WorkflowCheckpointGroundingReference_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER workflow.TR_WorkflowCheckpointGroundingReference_BlockUpdateDelete ON workflow.WorkflowCheckpointGroundingReference;
            IF OBJECT_ID('workflow.TR_WorkflowCheckpointEvidenceReference_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER workflow.TR_WorkflowCheckpointEvidenceReference_BlockUpdateDelete ON workflow.WorkflowCheckpointEvidenceReference;
            IF OBJECT_ID('workflow.TR_WorkflowCheckpoint_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER workflow.TR_WorkflowCheckpoint_BlockUpdateDelete ON workflow.WorkflowCheckpoint;
            IF OBJECT_ID('workflow.TR_WorkflowRunGroundingReference_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER workflow.TR_WorkflowRunGroundingReference_BlockUpdateDelete ON workflow.WorkflowRunGroundingReference;
            IF OBJECT_ID('workflow.TR_WorkflowRunEvidenceReference_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER workflow.TR_WorkflowRunEvidenceReference_BlockUpdateDelete ON workflow.WorkflowRunEvidenceReference;
            IF OBJECT_ID('workflow.TR_WorkflowRunStep_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER workflow.TR_WorkflowRunStep_BlockUpdateDelete ON workflow.WorkflowRunStep;
            IF OBJECT_ID('workflow.TR_WorkflowRun_BlockUpdateDelete', 'TR') IS NOT NULL DISABLE TRIGGER workflow.TR_WorkflowRun_BlockUpdateDelete ON workflow.WorkflowRun;
            IF OBJECT_ID('workflow.WorkflowCheckpointGroundingReference', 'U') IS NOT NULL DELETE FROM workflow.WorkflowCheckpointGroundingReference;
            IF OBJECT_ID('workflow.WorkflowCheckpointEvidenceReference', 'U') IS NOT NULL DELETE FROM workflow.WorkflowCheckpointEvidenceReference;
            IF OBJECT_ID('workflow.WorkflowCheckpoint', 'U') IS NOT NULL DELETE FROM workflow.WorkflowCheckpoint;
            IF OBJECT_ID('workflow.WorkflowRunGroundingReference', 'U') IS NOT NULL DELETE FROM workflow.WorkflowRunGroundingReference;
            IF OBJECT_ID('workflow.WorkflowRunEvidenceReference', 'U') IS NOT NULL DELETE FROM workflow.WorkflowRunEvidenceReference;
            IF OBJECT_ID('workflow.WorkflowRunStep', 'U') IS NOT NULL DELETE FROM workflow.WorkflowRunStep;
            IF OBJECT_ID('workflow.WorkflowRun', 'U') IS NOT NULL DELETE FROM workflow.WorkflowRun;
            IF OBJECT_ID('workflow.TR_WorkflowRun_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER workflow.TR_WorkflowRun_BlockUpdateDelete ON workflow.WorkflowRun;
            IF OBJECT_ID('workflow.TR_WorkflowRunStep_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER workflow.TR_WorkflowRunStep_BlockUpdateDelete ON workflow.WorkflowRunStep;
            IF OBJECT_ID('workflow.TR_WorkflowRunEvidenceReference_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER workflow.TR_WorkflowRunEvidenceReference_BlockUpdateDelete ON workflow.WorkflowRunEvidenceReference;
            IF OBJECT_ID('workflow.TR_WorkflowRunGroundingReference_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER workflow.TR_WorkflowRunGroundingReference_BlockUpdateDelete ON workflow.WorkflowRunGroundingReference;
            IF OBJECT_ID('workflow.TR_WorkflowCheckpoint_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER workflow.TR_WorkflowCheckpoint_BlockUpdateDelete ON workflow.WorkflowCheckpoint;
            IF OBJECT_ID('workflow.TR_WorkflowCheckpointEvidenceReference_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER workflow.TR_WorkflowCheckpointEvidenceReference_BlockUpdateDelete ON workflow.WorkflowCheckpointEvidenceReference;
            IF OBJECT_ID('workflow.TR_WorkflowCheckpointGroundingReference_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER workflow.TR_WorkflowCheckpointGroundingReference_BlockUpdateDelete ON workflow.WorkflowCheckpointGroundingReference;
            IF OBJECT_ID('governance.WorkflowTransitionRecord', 'U') IS NOT NULL DELETE FROM governance.WorkflowTransitionRecord;
            IF OBJECT_ID('governance.ReleaseReadinessDecisionRecord', 'U') IS NOT NULL DELETE FROM governance.ReleaseReadinessDecisionRecord;
            IF OBJECT_ID('governance.SourceApplyDryRunReceipt', 'U') IS NOT NULL DELETE FROM governance.SourceApplyDryRunReceipt;
            IF OBJECT_ID('governance.RollbackSupportReceipt', 'U') IS NOT NULL DELETE FROM governance.RollbackSupportReceipt;
            IF OBJECT_ID('governance.PatchArtifact', 'U') IS NOT NULL DELETE FROM governance.PatchArtifact;
            IF OBJECT_ID('governance.ThoughtLedgerGovernanceEventReference', 'U') IS NOT NULL DELETE FROM governance.ThoughtLedgerGovernanceEventReference;
            IF OBJECT_ID('governance.DogfoodReceipt', 'U') IS NOT NULL DELETE FROM governance.DogfoodReceipt;
            IF OBJECT_ID('governance.PolicyDecisionEvent', 'U') IS NOT NULL DELETE FROM governance.PolicyDecisionEvent;
            IF OBJECT_ID('governance.PolicySatisfaction', 'U') IS NOT NULL DELETE FROM governance.PolicySatisfaction;
            IF OBJECT_ID('governance.AcceptedApproval', 'U') IS NOT NULL DELETE FROM governance.AcceptedApproval;
            IF OBJECT_ID('governance.ApprovalDecision', 'U') IS NOT NULL DELETE FROM governance.ApprovalDecision;
            IF OBJECT_ID('governance.ToolGateDecision', 'U') IS NOT NULL DELETE FROM governance.ToolGateDecision;
            IF OBJECT_ID('governance.TR_PatchArtifact_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER governance.TR_PatchArtifact_BlockUpdateDelete ON governance.PatchArtifact;
            IF OBJECT_ID('governance.TR_RollbackSupportReceipt_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER governance.TR_RollbackSupportReceipt_BlockUpdateDelete ON governance.RollbackSupportReceipt;
            IF OBJECT_ID('governance.TR_SourceApplyDryRunReceipt_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER governance.TR_SourceApplyDryRunReceipt_BlockUpdateDelete ON governance.SourceApplyDryRunReceipt;
            IF OBJECT_ID('governance.TR_WorkflowTransitionRecord_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER governance.TR_WorkflowTransitionRecord_BlockUpdateDelete ON governance.WorkflowTransitionRecord;
            IF OBJECT_ID('governance.TR_ReleaseReadinessDecisionRecord_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER governance.TR_ReleaseReadinessDecisionRecord_BlockUpdateDelete ON governance.ReleaseReadinessDecisionRecord;
            IF OBJECT_ID('governance.TR_ToolGateDecision_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER governance.TR_ToolGateDecision_BlockUpdateDelete ON governance.ToolGateDecision;
            IF OBJECT_ID('governance.TR_ApprovalDecision_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER governance.TR_ApprovalDecision_BlockUpdateDelete ON governance.ApprovalDecision;
            IF OBJECT_ID('governance.TR_PolicyDecisionEvent_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER governance.TR_PolicyDecisionEvent_BlockUpdateDelete ON governance.PolicyDecisionEvent;
            IF OBJECT_ID('governance.TR_PolicySatisfaction_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER governance.TR_PolicySatisfaction_BlockUpdateDelete ON governance.PolicySatisfaction;
            IF OBJECT_ID('governance.TR_AcceptedApproval_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER governance.TR_AcceptedApproval_BlockUpdateDelete ON governance.AcceptedApproval;
            IF OBJECT_ID('governance.TR_DogfoodReceipt_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER governance.TR_DogfoodReceipt_BlockUpdateDelete ON governance.DogfoodReceipt;
            IF OBJECT_ID('governance.TR_ThoughtLedgerGovernanceEventReference_BlockUpdateDelete', 'TR') IS NOT NULL ENABLE TRIGGER governance.TR_ThoughtLedgerGovernanceEventReference_BlockUpdateDelete ON governance.ThoughtLedgerGovernanceEventReference;
            IF OBJECT_ID('governance.ToolRequest', 'U') IS NOT NULL DELETE FROM governance.ToolRequest;
            IF OBJECT_ID('dbo.ChatMessageFeedback', 'U') IS NOT NULL DELETE FROM dbo.ChatMessageFeedback;
            IF OBJECT_ID('dbo.ChatTurnClarifications', 'U') IS NOT NULL DELETE FROM dbo.ChatTurnClarifications;
            IF OBJECT_ID('dbo.ChatTurnGovernance', 'U') IS NOT NULL DELETE FROM dbo.ChatTurnGovernance;
            IF OBJECT_ID('dbo.ChatTurnTraces', 'U') IS NOT NULL DELETE FROM dbo.ChatTurnTraces;
            IF OBJECT_ID('dbo.ProjectChannelPins', 'U') IS NOT NULL DELETE FROM dbo.ProjectChannelPins;
            IF OBJECT_ID('dbo.ProjectChannelMessageReads', 'U') IS NOT NULL DELETE FROM dbo.ProjectChannelMessageReads;
            IF OBJECT_ID('dbo.ProjectChannelAssistantTurns', 'U') IS NOT NULL DELETE FROM dbo.ProjectChannelAssistantTurns;
            IF OBJECT_ID('dbo.ProjectChannelMessageContextLinks', 'U') IS NOT NULL DELETE FROM dbo.ProjectChannelMessageContextLinks;
            IF OBJECT_ID('dbo.ProjectNotifications', 'U') IS NOT NULL DELETE FROM dbo.ProjectNotifications;
            IF OBJECT_ID('dbo.ProjectChannelMessageMentions', 'U') IS NOT NULL DELETE FROM dbo.ProjectChannelMessageMentions;
            IF OBJECT_ID('dbo.ProjectChannelMessages', 'U') IS NOT NULL DELETE FROM dbo.ProjectChannelMessages;
            IF OBJECT_ID('dbo.ProjectChannelMembers', 'U') IS NOT NULL DELETE FROM dbo.ProjectChannelMembers;
            IF OBJECT_ID('dbo.ProjectChannels', 'U') IS NOT NULL DELETE FROM dbo.ProjectChannels;
            IF OBJECT_ID('dbo.ProjectWorkItemActivity', 'U') IS NOT NULL DELETE FROM dbo.ProjectWorkItemActivity;
            IF OBJECT_ID('dbo.ProjectWorkItemFollowers', 'U') IS NOT NULL DELETE FROM dbo.ProjectWorkItemFollowers;
            IF OBJECT_ID('dbo.ProjectWorkItemCollaboration', 'U') IS NOT NULL DELETE FROM dbo.ProjectWorkItemCollaboration;
            IF OBJECT_ID('dbo.ProjectContextDocuments', 'U') IS NOT NULL DELETE FROM dbo.ProjectContextDocuments;
            IF OBJECT_ID('dbo.ProjectDocumentLinks', 'U') IS NOT NULL DELETE FROM dbo.ProjectDocumentLinks;
            IF OBJECT_ID('dbo.ProjectDocumentVersions', 'U') IS NOT NULL DELETE FROM dbo.ProjectDocumentVersions;
            IF OBJECT_ID('dbo.ProjectDocuments', 'U') IS NOT NULL DELETE FROM dbo.ProjectDocuments;
            IF OBJECT_ID('dbo.ArtifactSourceReferences', 'U') IS NOT NULL DELETE FROM dbo.ArtifactSourceReferences;
            IF OBJECT_ID('dbo.RunEvents', 'U') IS NOT NULL DELETE FROM dbo.RunEvents;
            IF OBJECT_ID('dbo.Runs', 'U') IS NOT NULL DELETE FROM dbo.Runs;
            IF OBJECT_ID('dbo.SemanticSearchTraces', 'U') IS NOT NULL DELETE FROM dbo.SemanticSearchTraces;
            IF OBJECT_ID('dbo.EmbeddingJobs', 'U') IS NOT NULL DELETE FROM dbo.EmbeddingJobs;
            IF OBJECT_ID('dbo.SemanticChunks', 'U') IS NOT NULL DELETE FROM dbo.SemanticChunks;
            IF OBJECT_ID('dbo.SemanticArtefacts', 'U') IS NOT NULL DELETE FROM dbo.SemanticArtefacts;
            IF OBJECT_ID('dbo.CodeIndexEntries', 'U') IS NOT NULL DELETE FROM dbo.CodeIndexEntries;
            IF OBJECT_ID('dbo.ProjectProfiles', 'U') IS NOT NULL DELETE FROM dbo.ProjectProfiles;
            IF OBJECT_ID('dbo.ProjectCommands', 'U') IS NOT NULL DELETE FROM dbo.ProjectCommands;
            IF OBJECT_ID('dbo.ProjectProfileOptions', 'U') IS NOT NULL DELETE FROM dbo.ProjectProfileOptions;
            IF OBJECT_ID('dbo.ProjectRules', 'U') IS NOT NULL DELETE FROM dbo.ProjectRules;
            IF OBJECT_ID('dbo.ProjectImplementationPlans', 'U') IS NOT NULL DELETE FROM dbo.ProjectImplementationPlans;
            DELETE FROM dbo.ProjectDecisions;
            DELETE FROM dbo.ProjectSummaries;
            DELETE FROM dbo.ProjectTickets;
            DELETE FROM dbo.ProjectFiles;
            IF OBJECT_ID('dbo.ProjectObservableStates', 'U') IS NOT NULL DELETE FROM dbo.ProjectObservableStates;
            DELETE FROM dbo.ChatMessages;
            IF OBJECT_ID('dbo.ProjectChatSessions', 'U') IS NOT NULL DELETE FROM dbo.ProjectChatSessions;
            IF OBJECT_ID('dbo.ProjectMembers', 'U') IS NOT NULL DELETE FROM dbo.ProjectMembers;
            DELETE FROM dbo.Projects;
            DELETE FROM dbo.TenantUsers;
            DELETE FROM dbo.Users WHERE Id <> 1;
            """);

        // 2. Synchronize test tenants and users (resetting any changes from other test suites)
        var hash = BCrypt.Net.BCrypt.HashPassword(AdminPassword, workFactor: 4);
        
        await conn.ExecuteAsync("""
            -- Ensure Tenants and Users exist with expected names and status
            IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Tenants') AND name = 'IsActive')
                ALTER TABLE dbo.Tenants ADD IsActive BIT NOT NULL DEFAULT 1;

            IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = 1)
            BEGIN
                SET IDENTITY_INSERT dbo.Tenants ON;
                INSERT INTO dbo.Tenants (Id, Name, Slug, IsActive) VALUES (1, 'Default Tenant', 'default', 1);
                SET IDENTITY_INSERT dbo.Tenants OFF;
            END
            ELSE
            BEGIN
                UPDATE dbo.Tenants SET Name = 'Default Tenant', Slug = 'default', IsActive = 1 WHERE Id = 1;
            END

            IF NOT EXISTS (SELECT 1 FROM dbo.Tenants WHERE Id = 2)
            BEGIN
                SET IDENTITY_INSERT dbo.Tenants ON;
                INSERT INTO dbo.Tenants (Id, Name, Slug, IsActive) VALUES (2, 'Other Tenant', 'other', 1);
                SET IDENTITY_INSERT dbo.Tenants OFF;
            END
            ELSE
            BEGIN
                UPDATE dbo.Tenants SET Name = 'Other Tenant', Slug = 'other', IsActive = 1 WHERE Id = 2;
            END

            IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Id = 1)
            BEGIN
                SET IDENTITY_INSERT dbo.Users ON;
                INSERT INTO dbo.Users (Id, Email, DisplayName, PasswordHash, IsActive)
                VALUES (1, @Email, 'Admin User', @Hash, 1);
                SET IDENTITY_INSERT dbo.Users OFF;
            END
            ELSE
            BEGIN
                UPDATE dbo.Users SET Email = @Email, DisplayName = 'Admin User', PasswordHash = @Hash, IsActive = 1 WHERE Id = 1;
            END

            -- Isolated membership: Admin is ONLY a member of Tenant 1.
            DELETE FROM dbo.TenantUsers WHERE UserId = 1;
            INSERT INTO dbo.TenantUsers (TenantId, UserId, Role) VALUES (1, 1, 'Owner');
            """, new { Email = AdminEmail, Hash = hash });
    }

    // ?????? HTTP helpers ??????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????????

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

    protected static async Task<string> LoginAsync(string email = AdminEmail, string password = AdminPassword)
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    protected static async Task<string> SelectTenantAsync(string baseToken, int tenantId = AssignedTenantId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tenants/select");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", baseToken);
        request.Content = JsonContent.Create(new { tenantId });

        var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    protected static HttpClient GetAuthedClient(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected static string TestConnectionString()
    {
        var overrideValue = Environment.GetEnvironmentVariable("ConnectionStrings__IronDeveloperDb");
        return string.IsNullOrWhiteSpace(overrideValue)
            ? DefaultTestConnectionString
            : overrideValue;
    }

    /// <summary>
    /// Destructive API test provisioning/resets may only target an explicitly
    /// test-shaped catalog: local test databases ('*_Test') or ephemeral CI
    /// databases ('IronDev_CI_*'). Real and local developer catalogs
    /// (IronDeveloper, IronDeveloper_Local) and empty names are refused.
    /// Strict and compatible: a safety guard that breaks CI gets bypassed.
    /// </summary>
    internal static bool IsTestShapedCatalog(string? catalog) =>
        !string.IsNullOrWhiteSpace(catalog) &&
        (catalog.EndsWith("_Test", StringComparison.OrdinalIgnoreCase) ||
         catalog.StartsWith("IronDev_CI_", StringComparison.OrdinalIgnoreCase));

    private sealed class StartupOnlyStoredCriticService : IStoredManualIndependentCriticAgentService
    {
        public StoredManualAgentExecutionResult<CriticReviewResult> ExecuteAndStore(
            ManualCriticReviewRequest request,
            ManualAgentExecutionSpecialisationSelection specialisation,
            DateTimeOffset executedAtUtc) =>
            new()
            {
                Status = StoredManualAgentExecutionStatus.Rejected,
                AgentRunId = "api-test-startup-only-stub",
                AgentId = "independent-critic",
                SpecialisationId = specialisation.SpecialisationId,
                Issues =
                [
                    new StoredManualAgentExecutionIssue
                    {
                        Code = "API_TEST_STARTUP_ONLY",
                        Severity = "Info",
                        Message = "API integration tests replace stored critic execution unless a test registers a dedicated implementation."
                    }
                ]
            };
    }
}
