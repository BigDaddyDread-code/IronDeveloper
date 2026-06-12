using System.Data;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;
using IronDev.Infrastructure.Governance;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("GovernanceEventStore")]
public sealed class GovernanceEventStoreTests : IntegrationTestBase
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CorrelationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid CausationId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private const string ValidPayload = "{\"schema\":\"governance.event.created.v1\",\"message\":\"Governance event store smoke event\",\"source\":\"integration-test\"}";

    private SqlGovernanceEventStore _store = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropGovernanceSchemaAsync();
        await ApplyGovernanceEventMigrationAsync();

        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        _store = new SqlGovernanceEventStore(connectionFactory);
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropGovernanceSchemaAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public void GovernanceEventContracts_ExposeAppendOnlyStoreShape()
    {
        Assert.IsNotNull(typeof(GovernanceEvent));
        Assert.IsNotNull(typeof(GovernanceEventAppendRequest));
        Assert.IsNotNull(typeof(GovernanceEventValidationIssue));
        Assert.IsNotNull(typeof(GovernanceEventValidator));
        Assert.IsNotNull(typeof(IGovernanceEventStore));

        var methods = typeof(IGovernanceEventStore).GetMethods().Select(method => method.Name).ToArray();
        CollectionAssert.AreEquivalent(
            new[] { "AppendAsync", "GetAsync", "ListForProjectAsync", "ListForCorrelationAsync" },
            methods);
        AssertNoForbiddenNames(methods, "UpdateAsync", "DeleteAsync", "UpsertAsync", "SaveAsync");

        var properties = typeof(GovernanceEvent).GetProperties().Select(property => property.Name).ToArray();
        AssertNoForbiddenNames(properties, "ApprovalGranted", "ExecutionPermission", "PromotesMemory", "StartsWorkflow", "ReleaseApproved");
    }

    [TestMethod]
    public async Task GovernanceEventMigration_CreatesSchemaTableConstraintsAndIndexes()
    {
        await using var connection = new SqlConnection(ConnectionString);

        var tableExists = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE object_id = OBJECT_ID(N'governance.GovernanceEvent')");
        Assert.AreEqual(1, tableExists);

        var requiredColumns = (await connection.QueryAsync<string>(
            """
            SELECT c.name
            FROM sys.columns c
            WHERE c.object_id = OBJECT_ID(N'governance.GovernanceEvent')
              AND c.is_nullable = 0
            """)).ToArray();
        CollectionAssert.IsSubsetOf(
            new[] { "EventId", "ProjectId", "EventType", "ActorType", "ActorId", "PayloadVersion", "PayloadJson", "CreatedUtc" },
            requiredColumns);

        var indexes = (await connection.QueryAsync<string>(
            """
            SELECT name
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'governance.GovernanceEvent')
            """)).ToArray();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "IX_GovernanceEvent_Project_CreatedUtc",
                "IX_GovernanceEvent_Project_EventType_CreatedUtc",
                "IX_GovernanceEvent_CorrelationId_CreatedUtc",
                "IX_GovernanceEvent_CausationId",
                "IX_GovernanceEvent_Subject"
            },
            indexes);
    }

    [TestMethod]
    public async Task GovernanceEventStore_AppendsReadsAndListsEvents()
    {
        var first = await _store.AppendAsync(ValidRequest() with { EventType = " governance.event.created ", ActorId = " test-actor " });
        var second = await _store.AppendAsync(ValidRequest() with { EventType = "tool.request.created" });

        Assert.AreNotEqual(Guid.Empty, first.EventId);
        Assert.AreEqual(ProjectId, first.ProjectId);
        Assert.AreEqual("governance.event.created", first.EventType);
        Assert.AreEqual("test", first.ActorType);
        Assert.AreEqual("test-actor", first.ActorId);
        Assert.AreEqual(CorrelationId, first.CorrelationId);
        Assert.AreEqual(CausationId, first.CausationId);
        Assert.AreEqual("tool_request", first.SubjectType);
        Assert.AreEqual("tool-request-1", first.SubjectId);
        Assert.AreEqual(1, first.PayloadVersion);
        Assert.AreEqual(ValidPayload, first.PayloadJson);
        Assert.AreNotEqual(default, first.CreatedUtc);

        var read = await _store.GetAsync(first.EventId);
        Assert.IsNotNull(read);
        Assert.AreEqual(first.EventId, read.EventId);
        Assert.AreEqual(first.PayloadJson, read.PayloadJson);

        var byProject = await _store.ListForProjectAsync(ProjectId, 50);
        Assert.AreEqual(2, byProject.Count);
        Assert.IsTrue(byProject.Any(item => item.EventId == first.EventId));
        Assert.IsTrue(byProject.Any(item => item.EventId == second.EventId));

        var byCorrelation = await _store.ListForCorrelationAsync(CorrelationId, 50);
        Assert.AreEqual(2, byCorrelation.Count);
        Assert.IsTrue(byCorrelation.All(item => item.CorrelationId == CorrelationId));
    }

    [TestMethod]
    public async Task GovernanceEventStore_IsProjectScopedAndHandlesEmptyReadKeys()
    {
        await _store.AppendAsync(ValidRequest());
        await _store.AppendAsync(ValidRequest() with { ProjectId = OtherProjectId });

        var projectEvents = await _store.ListForProjectAsync(ProjectId, 1000);
        var noneForEmpty = await _store.ListForProjectAsync(Guid.Empty, 100);
        var noneForEmptyCorrelation = await _store.ListForCorrelationAsync(Guid.Empty, 100);
        var noneForEmptyEvent = await _store.GetAsync(Guid.Empty);

        Assert.AreEqual(1, projectEvents.Count);
        Assert.AreEqual(ProjectId, projectEvents.Single().ProjectId);
        Assert.AreEqual(0, noneForEmpty.Count);
        Assert.AreEqual(0, noneForEmptyCorrelation.Count);
        Assert.IsNull(noneForEmptyEvent);
    }

    [TestMethod]
    public async Task GovernanceEventStore_RejectsInvalidAppendRequests()
    {
        var cases = new Dictionary<string, GovernanceEventAppendRequest>
        {
            [GovernanceEventValidator.ProjectIdRequired] = ValidRequest() with { ProjectId = Guid.Empty },
            [GovernanceEventValidator.EventTypeRequired] = ValidRequest() with { EventType = " " },
            [GovernanceEventValidator.ActorTypeRequired] = ValidRequest() with { ActorType = " " },
            [GovernanceEventValidator.ActorIdRequired] = ValidRequest() with { ActorId = " " },
            [GovernanceEventValidator.PayloadVersionInvalid] = ValidRequest() with { PayloadVersion = 0 },
            [GovernanceEventValidator.PayloadJsonRequired] = ValidRequest() with { PayloadJson = " " },
            [GovernanceEventValidator.PayloadJsonInvalid] = ValidRequest() with { PayloadJson = "{not-json" },
            [GovernanceEventValidator.PayloadTextUnsafe] = ValidRequest() with { PayloadJson = "{\"rawPrompt\":\"do not store\"}" }
        };

        foreach (var pair in cases)
            await ExpectArgumentExceptionAsync(pair.Key, () => _store.AppendAsync(pair.Value));
    }

    [TestMethod]
    public async Task GovernanceEventSqlBoundary_BlocksInvalidJsonPayloadVersionUpdateAndDelete()
    {
        await ExpectSqlFailsAsync(() => DirectInsertAsync(ValidRequest() with { PayloadJson = "{not-json" }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(ValidRequest() with { PayloadVersion = -1 }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(ValidRequest() with { EventType = " " }));

        var appended = await _store.AppendAsync(ValidRequest());
        await ExpectSqlFailsAsync(async () =>
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.ExecuteAsync(
                "UPDATE governance.GovernanceEvent SET EventType = N'tampered' WHERE EventId = @EventId",
                new { appended.EventId });
        });
        await ExpectSqlFailsAsync(async () =>
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.ExecuteAsync(
                "DELETE FROM governance.GovernanceEvent WHERE EventId = @EventId",
                new { appended.EventId });
        });
    }

    [TestMethod]
    public async Task GovernanceEventCreation_DoesNotCreateAuthorityAdjacentRecords()
    {
        var beforeTables = await ExistingTablesAsync();
        var appended = await _store.AppendAsync(ValidRequest());
        var afterTables = await ExistingTablesAsync();

        Assert.AreNotEqual(Guid.Empty, appended.EventId);
        CollectionAssert.AreEquivalent(beforeTables, afterTables);
        Assert.IsFalse(afterTables.Contains("governance.ToolRequest", StringComparer.OrdinalIgnoreCase));
        Assert.IsFalse(afterTables.Contains("governance.ToolGateDecision", StringComparer.OrdinalIgnoreCase));
        Assert.IsFalse(afterTables.Contains("governance.ApprovalDecision", StringComparer.OrdinalIgnoreCase));
        Assert.IsFalse(afterTables.Contains("governance.DogfoodReceipt", StringComparer.OrdinalIgnoreCase));
        Assert.IsFalse(afterTables.Contains("governance.WorkflowStep", StringComparer.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void GovernanceEventStore_StaticBoundary_DoesNotAddWorkflowA2aMemoryPromotionApiCliOrRuntimeWiring()
    {
        var root = FindRepositoryRoot();
        var coreText = File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "GovernanceEventModels.cs"));
        var storeText = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlGovernanceEventStore.cs"));
        var migrationText = File.ReadAllText(Path.Combine(root, "Database", "migrate_governance_event.sql"));
        var apiProgramText = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Program.cs"));
        var cliText = File.Exists(Path.Combine(root, "tools", "IronDev.Cli", "IronDevCli.cs"))
            ? File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "IronDevCli.cs"))
            : string.Empty;

        StringAssert.Contains(storeText, "CommandType.StoredProcedure");
        StringAssert.Contains(migrationText, "CREATE TRIGGER governance.TR_GovernanceEvent_BlockUpdateDelete");
        StringAssert.Contains(migrationText, "DENY INSERT, UPDATE, DELETE ON OBJECT::governance.GovernanceEvent");

        AssertNoForbiddenTokens(storeText, "UPDATE governance.GovernanceEvent", "DELETE FROM governance.GovernanceEvent", "MERGE governance.GovernanceEvent", "IWorkflow", "LangGraph", "IAgentHandoff", "PromoteCollectiveMemory", "SourceApply", "ControllerBase", "WebApplication", "IHostedService", "BackgroundService", "ProcessStartInfo", "File.Copy", "File.Delete");
        AssertNoForbiddenTokens(coreText, "SqlConnection", "Dapper", "IWorkflow", "LangGraph", "IAgentHandoff", "PromoteCollectiveMemory", "SourceApply", "ControllerBase", "HttpClient", "ProcessStartInfo", "File.Copy", "File.Delete");
        AssertNoForbiddenTokens(apiProgramText, "SqlGovernanceEventStore", "IGovernanceEventStore");
        AssertNoForbiddenTokens(cliText, "SqlGovernanceEventStore", "IGovernanceEventStore");
    }

    private static GovernanceEventAppendRequest ValidRequest() =>
        new()
        {
            ProjectId = ProjectId,
            EventType = "governance.event.created",
            ActorType = "test",
            ActorId = "test-actor",
            CorrelationId = CorrelationId,
            CausationId = CausationId,
            SubjectType = "tool_request",
            SubjectId = "tool-request-1",
            PayloadVersion = 1,
            PayloadJson = ValidPayload
        };

    private async Task DirectInsertAsync(GovernanceEventAppendRequest request)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT INTO governance.GovernanceEvent
            (
                EventId, ProjectId, EventType, ActorType, ActorId, CorrelationId, CausationId,
                SubjectType, SubjectId, PayloadVersion, PayloadJson
            )
            VALUES
            (
                @EventId, @ProjectId, @EventType, @ActorType, @ActorId, @CorrelationId, @CausationId,
                @SubjectType, @SubjectId, @PayloadVersion, @PayloadJson
            );
            """,
            new
            {
                EventId = Guid.NewGuid(),
                request.ProjectId,
                request.EventType,
                request.ActorType,
                request.ActorId,
                request.CorrelationId,
                request.CausationId,
                request.SubjectType,
                request.SubjectId,
                request.PayloadVersion,
                request.PayloadJson
            });
    }

    private async Task<string[]> ExistingTablesAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var rows = await connection.QueryAsync<string>(
            """
            SELECT CONCAT(SCHEMA_NAME(schema_id), N'.', name)
            FROM sys.tables
            WHERE is_ms_shipped = 0
            ORDER BY SCHEMA_NAME(schema_id), name
            """);

        return rows.ToArray();
    }

    private static async Task ExpectArgumentExceptionAsync(string expectedCode, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ArgumentException ex) when (ex.Message.Contains(expectedCode, StringComparison.Ordinal))
        {
            return;
        }

        Assert.Fail($"Expected ArgumentException containing {expectedCode}.");
    }

    private static async Task ExpectSqlFailsAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (SqlException)
        {
            return;
        }

        Assert.Fail("Expected SQL operation to fail.");
    }

    private async Task ApplyGovernanceEventMigrationAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var migration = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_governance_event.sql"));
        foreach (var batch in SplitSqlBatches(migration))
            await connection.ExecuteAsync(batch);
    }

    private async Task DropGovernanceSchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(
            """
            IF OBJECT_ID(N'governance.TR_GovernanceEvent_BlockUpdateDelete', N'TR') IS NOT NULL
                DROP TRIGGER governance.TR_GovernanceEvent_BlockUpdateDelete;
            IF OBJECT_ID(N'governance.AppendGovernanceEvent', N'P') IS NOT NULL
                DROP PROCEDURE governance.AppendGovernanceEvent;
            IF OBJECT_ID(N'governance.GetGovernanceEvent', N'P') IS NOT NULL
                DROP PROCEDURE governance.GetGovernanceEvent;
            IF OBJECT_ID(N'governance.ListGovernanceEventsForProject', N'P') IS NOT NULL
                DROP PROCEDURE governance.ListGovernanceEventsForProject;
            IF OBJECT_ID(N'governance.ListGovernanceEventsForCorrelation', N'P') IS NOT NULL
                DROP PROCEDURE governance.ListGovernanceEventsForCorrelation;
            IF OBJECT_ID(N'governance.GovernanceEvent', N'U') IS NOT NULL
                DROP TABLE governance.GovernanceEvent;
            IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IronDevGovernanceEventRuntimeRole' AND type = N'R')
                DROP ROLE IronDevGovernanceEventRuntimeRole;
            IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance')
                EXEC(N'DROP SCHEMA governance');
            """);
    }

    private static IReadOnlyList<string> SplitSqlBatches(string sql) =>
        sql.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\nGO\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToArray();

    private static void AssertNoForbiddenNames(IReadOnlyCollection<string> values, params string[] forbidden)
    {
        foreach (var token in forbidden)
            Assert.IsFalse(values.Contains(token), $"Unexpected member found: {token}");
    }

    private static void AssertNoForbiddenTokens(string text, params string[] tokens)
    {
        foreach (var token in tokens)
            Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Unexpected token found: {token}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
