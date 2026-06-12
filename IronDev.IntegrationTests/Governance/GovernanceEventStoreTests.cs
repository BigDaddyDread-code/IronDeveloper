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
    private static readonly Guid OtherCorrelationId = Guid.Parse("33333333-3333-3333-3333-333333333334");
    private static readonly Guid CausationId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OtherCausationId = Guid.Parse("44444444-4444-4444-4444-444444444445");
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
    public void GovernanceEventContracts_ExposeAppendOnlyReadModelStoreShape()
    {
        Assert.IsNotNull(typeof(GovernanceEvent));
        Assert.IsNotNull(typeof(GovernanceEventReadModel));
        Assert.IsNotNull(typeof(GovernanceEventSummary));
        Assert.IsNotNull(typeof(GovernanceEventsForProjectQuery));
        Assert.IsNotNull(typeof(GovernanceEventsForCorrelationQuery));
        Assert.IsNotNull(typeof(GovernanceEventsForSubjectQuery));
        Assert.IsNotNull(typeof(GovernanceEventsCausedByQuery));
        Assert.IsNotNull(typeof(GovernanceEventAppendRequest));
        Assert.IsNotNull(typeof(GovernanceEventValidator));
        Assert.IsNotNull(typeof(IGovernanceEventStore));

        var methods = typeof(IGovernanceEventStore).GetMethods().Select(method => method.Name).ToArray();
        CollectionAssert.AreEquivalent(
            new[] { "AppendAsync", "GetAsync", "ListForProjectAsync", "ListForCorrelationAsync", "ListForSubjectAsync", "ListCausedByAsync" },
            methods);
        AssertNoForbiddenNames(methods, "UpdateAsync", "DeleteAsync", "UpsertAsync", "SaveAsync", "ApproveAsync", "ExecuteAsync", "PromoteAsync", "MarkReleaseReadyAsync");

        var summaryProperties = typeof(GovernanceEventSummary).GetProperties().Select(property => property.Name).ToArray();
        Assert.IsFalse(summaryProperties.Contains("PayloadJson"), "Summary model must not expose payload JSON.");
        AssertNoForbiddenNames(summaryProperties, "ApprovalGranted", "ExecutionPermission", "PromotesMemory", "StartsWorkflow", "ReleaseApproved");

        var readProperties = typeof(GovernanceEventReadModel).GetProperties().Select(property => property.Name).ToArray();
        CollectionAssert.Contains(readProperties, "PayloadJson");
        AssertNoForbiddenNames(readProperties, "ApprovalGranted", "ExecutionPermission", "PromotesMemory", "StartsWorkflow", "ReleaseApproved");
    }

    [TestMethod]
    public async Task GovernanceEventMigration_CreatesSchemaTableConstraintsIndexesAndReadProcedures()
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

        var procedures = (await connection.QueryAsync<string>(
            """
            SELECT name
            FROM sys.procedures
            WHERE schema_id = SCHEMA_ID(N'governance')
            """)).ToArray();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "AppendGovernanceEvent",
                "GetGovernanceEvent",
                "ListGovernanceEventsForProject",
                "ListGovernanceEventsForCorrelation",
                "ListGovernanceEventsForSubject",
                "ListGovernanceEventsCausedBy"
            },
            procedures);
    }

    [TestMethod]
    public async Task GovernanceEventStore_AppendsAndGetReturnsFullPayloadReadModel()
    {
        var appended = await _store.AppendAsync(ValidRequest() with { EventType = " governance.event.created ", ActorId = " test-actor " });

        Assert.AreNotEqual(Guid.Empty, appended.EventId);
        Assert.AreEqual(ProjectId, appended.ProjectId);
        Assert.AreEqual("governance.event.created", appended.EventType);
        Assert.AreEqual("test", appended.ActorType);
        Assert.AreEqual("test-actor", appended.ActorId);
        Assert.AreEqual(CorrelationId, appended.CorrelationId);
        Assert.AreEqual(CausationId, appended.CausationId);
        Assert.AreEqual("tool_request", appended.SubjectType);
        Assert.AreEqual("tool-request-1", appended.SubjectId);
        Assert.AreEqual(1, appended.PayloadVersion);
        Assert.AreEqual(ValidPayload, appended.PayloadJson);
        Assert.AreNotEqual(default, appended.CreatedUtc);

        var read = await _store.GetAsync(appended.EventId);
        Assert.IsNotNull(read);
        Assert.AreEqual(appended.EventId, read.EventId);
        Assert.AreEqual(appended.PayloadJson, read.PayloadJson);
        Assert.AreEqual(appended.PayloadVersion, read.PayloadVersion);
        Assert.AreEqual(appended.CorrelationId, read.CorrelationId);
        Assert.AreEqual(appended.CausationId, read.CausationId);
        Assert.IsNull(await _store.GetAsync(Guid.NewGuid()));
        Assert.IsNull(await _store.GetAsync(Guid.Empty));
    }

    [TestMethod]
    public async Task ListForProject_ReturnsMatchingProjectSummariesOnlyDeterministicallyAndWithoutPayload()
    {
        await _store.AppendAsync(ValidRequest() with { EventType = "event.one" });
        await _store.AppendAsync(ValidRequest() with { EventType = "event.two", SubjectId = "tool-request-2" });
        await _store.AppendAsync(ValidRequest() with { ProjectId = OtherProjectId, EventType = "event.other" });

        var summaries = await _store.ListForProjectAsync(new GovernanceEventsForProjectQuery { ProjectId = ProjectId, Take = 2 });

        Assert.AreEqual(2, summaries.Count);
        Assert.IsTrue(summaries.All(summary => summary.ProjectId == ProjectId));
        AssertOrderedDescending(summaries);
        AssertSummaryDoesNotExposePayload();
    }

    [TestMethod]
    public async Task ListForCorrelation_ReturnsMatchingCorrelationAcrossSubjectsDeterministicallyAndWithoutPayload()
    {
        await _store.AppendAsync(ValidRequest() with { SubjectType = "tool_request", SubjectId = "tool-request-1" });
        await _store.AppendAsync(ValidRequest() with { SubjectType = "gate_decision", SubjectId = "gate-decision-1" });
        await _store.AppendAsync(ValidRequest() with { CorrelationId = OtherCorrelationId, SubjectId = "other" });

        var summaries = await _store.ListForCorrelationAsync(new GovernanceEventsForCorrelationQuery { CorrelationId = CorrelationId, Take = 10 });

        Assert.AreEqual(2, summaries.Count);
        Assert.IsTrue(summaries.All(summary => summary.CorrelationId == CorrelationId));
        Assert.IsTrue(summaries.Select(summary => summary.SubjectType).Contains("tool_request"));
        Assert.IsTrue(summaries.Select(summary => summary.SubjectType).Contains("gate_decision"));
        AssertOrderedDescending(summaries);
        AssertSummaryDoesNotExposePayload();
    }

    [TestMethod]
    public async Task ListForSubject_IsProjectScopedAndTrimsSubjectFilters()
    {
        await _store.AppendAsync(ValidRequest() with { SubjectType = "tool_request", SubjectId = "shared-subject" });
        await _store.AppendAsync(ValidRequest() with { SubjectType = "tool_request", SubjectId = "other-subject" });
        await _store.AppendAsync(ValidRequest() with { ProjectId = OtherProjectId, SubjectType = "tool_request", SubjectId = "shared-subject" });

        var summaries = await _store.ListForSubjectAsync(new GovernanceEventsForSubjectQuery
        {
            ProjectId = ProjectId,
            SubjectType = " tool_request ",
            SubjectId = " shared-subject ",
            Take = 10
        });

        Assert.AreEqual(1, summaries.Count);
        Assert.AreEqual(ProjectId, summaries.Single().ProjectId);
        Assert.AreEqual("tool_request", summaries.Single().SubjectType);
        Assert.AreEqual("shared-subject", summaries.Single().SubjectId);
        AssertSummaryDoesNotExposePayload();
    }

    [TestMethod]
    public async Task ListCausedBy_ReturnsMatchingCausationSummariesOnly()
    {
        await _store.AppendAsync(ValidRequest() with { CausationId = CausationId, EventType = "event.caused.one" });
        await _store.AppendAsync(ValidRequest() with { CausationId = CausationId, EventType = "event.caused.two" });
        await _store.AppendAsync(ValidRequest() with { CausationId = OtherCausationId, EventType = "event.other" });

        var summaries = await _store.ListCausedByAsync(new GovernanceEventsCausedByQuery { CausationId = CausationId, Take = 10 });

        Assert.AreEqual(2, summaries.Count);
        Assert.IsTrue(summaries.All(summary => summary.CausationId == CausationId));
        AssertOrderedDescending(summaries);
        AssertSummaryDoesNotExposePayload();
    }

    [TestMethod]
    public async Task GovernanceEventStore_RejectsInvalidAppendAndReadQueries()
    {
        var appendCases = new Dictionary<string, Func<Task>>
        {
            [GovernanceEventValidator.ProjectIdRequired] = () => _store.AppendAsync(ValidRequest() with { ProjectId = Guid.Empty }),
            [GovernanceEventValidator.EventTypeRequired] = () => _store.AppendAsync(ValidRequest() with { EventType = " " }),
            [GovernanceEventValidator.ActorTypeRequired] = () => _store.AppendAsync(ValidRequest() with { ActorType = " " }),
            [GovernanceEventValidator.ActorIdRequired] = () => _store.AppendAsync(ValidRequest() with { ActorId = " " }),
            [GovernanceEventValidator.PayloadVersionInvalid] = () => _store.AppendAsync(ValidRequest() with { PayloadVersion = 0 }),
            [GovernanceEventValidator.PayloadJsonRequired] = () => _store.AppendAsync(ValidRequest() with { PayloadJson = " " }),
            [GovernanceEventValidator.PayloadJsonInvalid] = () => _store.AppendAsync(ValidRequest() with { PayloadJson = "{not-json" }),
            [GovernanceEventValidator.PayloadTextUnsafe] = () => _store.AppendAsync(ValidRequest() with { PayloadJson = "{\"rawPrompt\":\"do not store\"}" })
        };

        foreach (var pair in appendCases)
            await ExpectArgumentExceptionAsync(pair.Key, pair.Value);

        var queryCases = new Dictionary<string, Func<Task>>
        {
            [GovernanceEventValidator.ProjectIdRequired] = () => _store.ListForProjectAsync(new GovernanceEventsForProjectQuery { ProjectId = Guid.Empty }),
            [GovernanceEventValidator.TakeInvalid] = () => _store.ListForProjectAsync(new GovernanceEventsForProjectQuery { ProjectId = ProjectId, Take = 0 }),
            [GovernanceEventValidator.TakeInvalid + " max"] = () => _store.ListForProjectAsync(new GovernanceEventsForProjectQuery { ProjectId = ProjectId, Take = GovernanceEventValidator.MaxTake + 1 }),
            [GovernanceEventValidator.CorrelationIdRequired] = () => _store.ListForCorrelationAsync(new GovernanceEventsForCorrelationQuery { CorrelationId = Guid.Empty }),
            [GovernanceEventValidator.SubjectTypeRequired] = () => _store.ListForSubjectAsync(new GovernanceEventsForSubjectQuery { ProjectId = ProjectId, SubjectType = " ", SubjectId = "subject" }),
            [GovernanceEventValidator.SubjectIdRequired] = () => _store.ListForSubjectAsync(new GovernanceEventsForSubjectQuery { ProjectId = ProjectId, SubjectType = "subject", SubjectId = " " }),
            [GovernanceEventValidator.CausationIdRequired] = () => _store.ListCausedByAsync(new GovernanceEventsCausedByQuery { CausationId = Guid.Empty })
        };

        foreach (var pair in queryCases)
            await ExpectArgumentExceptionAsync(pair.Key.Replace(" max", string.Empty, StringComparison.Ordinal), pair.Value);
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
    public async Task GovernanceEventReadModels_DoNotCreateAuthorityAdjacentRecords()
    {
        var beforeTables = await ExistingTablesAsync();
        var appended = await _store.AppendAsync(ValidRequest());

        _ = await _store.GetAsync(appended.EventId);
        _ = await _store.ListForProjectAsync(new GovernanceEventsForProjectQuery { ProjectId = ProjectId });
        _ = await _store.ListForCorrelationAsync(new GovernanceEventsForCorrelationQuery { CorrelationId = CorrelationId });
        _ = await _store.ListForSubjectAsync(new GovernanceEventsForSubjectQuery { ProjectId = ProjectId, SubjectType = "tool_request", SubjectId = "tool-request-1" });
        _ = await _store.ListCausedByAsync(new GovernanceEventsCausedByQuery { CausationId = CausationId });

        var afterTables = await ExistingTablesAsync();
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
        StringAssert.Contains(migrationText, "ListGovernanceEventsForSubject");
        StringAssert.Contains(migrationText, "ListGovernanceEventsCausedBy");

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
            IF OBJECT_ID(N'governance.ListGovernanceEventsForSubject', N'P') IS NOT NULL
                DROP PROCEDURE governance.ListGovernanceEventsForSubject;
            IF OBJECT_ID(N'governance.ListGovernanceEventsCausedBy', N'P') IS NOT NULL
                DROP PROCEDURE governance.ListGovernanceEventsCausedBy;
            IF OBJECT_ID(N'governance.usp_ToolGateDecision_Record', N'P') IS NOT NULL
                DROP PROCEDURE governance.usp_ToolGateDecision_Record;
            IF OBJECT_ID(N'governance.usp_ToolGateDecision_GetById', N'P') IS NOT NULL
                DROP PROCEDURE governance.usp_ToolGateDecision_GetById;
            IF OBJECT_ID(N'governance.usp_ToolGateDecision_ListForToolRequest', N'P') IS NOT NULL
                DROP PROCEDURE governance.usp_ToolGateDecision_ListForToolRequest;
            IF OBJECT_ID(N'governance.usp_ToolGateDecision_ListForProject', N'P') IS NOT NULL
                DROP PROCEDURE governance.usp_ToolGateDecision_ListForProject;
            IF OBJECT_ID(N'governance.usp_ToolGateDecision_ListForCorrelation', N'P') IS NOT NULL
                DROP PROCEDURE governance.usp_ToolGateDecision_ListForCorrelation;
            IF OBJECT_ID(N'governance.TR_ToolGateDecision_BlockUpdateDelete', N'TR') IS NOT NULL
                DROP TRIGGER governance.TR_ToolGateDecision_BlockUpdateDelete;
            IF OBJECT_ID(N'governance.ToolGateDecision', N'U') IS NOT NULL
                DROP TABLE governance.ToolGateDecision;

            IF OBJECT_ID(N'governance.usp_ToolRequest_Create', N'P') IS NOT NULL
                DROP PROCEDURE governance.usp_ToolRequest_Create;
            IF OBJECT_ID(N'governance.usp_ToolRequest_GetById', N'P') IS NOT NULL
                DROP PROCEDURE governance.usp_ToolRequest_GetById;
            IF OBJECT_ID(N'governance.usp_ToolRequest_ListForProject', N'P') IS NOT NULL
                DROP PROCEDURE governance.usp_ToolRequest_ListForProject;
            IF OBJECT_ID(N'governance.usp_ToolRequest_ListForCorrelation', N'P') IS NOT NULL
                DROP PROCEDURE governance.usp_ToolRequest_ListForCorrelation;
            IF OBJECT_ID(N'governance.ToolRequest', N'U') IS NOT NULL
                DROP TABLE governance.ToolRequest;
            IF OBJECT_ID(N'governance.GovernanceEvent', N'U') IS NOT NULL
                DROP TABLE governance.GovernanceEvent;
            IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IronDevGovernanceEventRuntimeRole' AND type = N'R')
                DROP ROLE IronDevGovernanceEventRuntimeRole;
            IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance')
                EXEC(N'DROP SCHEMA governance');
            """);
    }

    private static IReadOnlyList<string> SplitSqlBatches(string sql) =>
        System.Text.RegularExpressions.Regex.Split(
                sql.Replace("\r\n", "\n", StringComparison.Ordinal),
                @"(?im)^\s*GO\s*$")
            .Select(batch => batch.Trim())
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToArray();

    private static void AssertOrderedDescending(IReadOnlyList<GovernanceEventSummary> summaries)
    {
        var expected = summaries
            .OrderByDescending(summary => summary.CreatedUtc)
            .ThenByDescending(summary => summary.EventId)
            .Select(summary => summary.EventId)
            .ToArray();
        var actual = summaries.Select(summary => summary.EventId).ToArray();
        CollectionAssert.AreEqual(expected, actual);
    }

    private static void AssertSummaryDoesNotExposePayload()
    {
        var properties = typeof(GovernanceEventSummary).GetProperties().Select(property => property.Name).ToArray();
        Assert.IsFalse(properties.Contains("PayloadJson"));
    }

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
