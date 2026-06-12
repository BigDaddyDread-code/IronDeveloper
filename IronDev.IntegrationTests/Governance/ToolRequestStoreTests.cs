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
[TestCategory("ToolRequestStore")]
public sealed class ToolRequestStoreTests : IntegrationTestBase
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherProjectId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid CorrelationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OtherCorrelationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccd");
    private static readonly Guid CausationId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private const string ValidPayload = "{\"schema\":\"workspace.diff.request.v1\",\"workspaceRef\":\"workspace-123\",\"evidenceRefs\":[\"trace-789\"]}";

    private SqlToolRequestStore _store = default!;
    private SqlGovernanceEventStore _governanceEvents = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropGovernanceSchemaAsync();
        await ApplySqlFileAsync("Database", "migrate_governance_event.sql");
        await ApplySqlFileAsync("Database", "migrate_tool_request.sql");

        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        _store = new SqlToolRequestStore(connectionFactory);
        _governanceEvents = new SqlGovernanceEventStore(connectionFactory);
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropGovernanceSchemaAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public void ToolRequestContracts_ExposeDurableRequestStoreWithoutAuthorityMethods()
    {
        Assert.IsNotNull(typeof(ToolRequest));
        Assert.IsNotNull(typeof(ToolRequestCreateRequest));
        Assert.IsNotNull(typeof(ToolRequestReadModel));
        Assert.IsNotNull(typeof(ToolRequestSummary));
        Assert.IsNotNull(typeof(ToolRequestsForProjectQuery));
        Assert.IsNotNull(typeof(ToolRequestsForCorrelationQuery));
        Assert.IsNotNull(typeof(ToolRequestStatus));
        Assert.IsNotNull(typeof(IToolRequestStore));

        var methods = typeof(IToolRequestStore).GetMethods().Select(method => method.Name).ToArray();
        CollectionAssert.AreEquivalent(
            new[] { "CreateAsync", "GetAsync", "ListForProjectAsync", "ListForCorrelationAsync" },
            methods);
        AssertNoForbiddenNames(methods, "ApproveAsync", "GateAsync", "ExecuteAsync", "AuthorizeAsync", "RunAsync", "PromoteAsync", "ApplyAsync", "MarkPassedAsync");

        var statuses = Enum.GetNames<ToolRequestStatus>();
        CollectionAssert.AreEquivalent(new[] { "Recorded", "Cancelled", "Superseded" }, statuses);
        AssertNoForbiddenNames(statuses, "Approved", "Passed", "Executable", "ReadyToRun", "Authorized", "GatePassed", "HumanApproved");

        var summaryProperties = typeof(ToolRequestSummary).GetProperties().Select(property => property.Name).ToArray();
        Assert.IsFalse(summaryProperties.Contains("RequestPayloadJson"), "Summary model must not expose full request payload JSON.");
        AssertNoForbiddenNames(summaryProperties, "ApprovalGranted", "GatePassed", "CanExecute", "ExecutionPermission", "ToolExecuted", "SourceApplied", "MemoryPromoted");
    }

    [TestMethod]
    public async Task ToolRequestMigration_CreatesTableIndexesAndStoredProcedures()
    {
        await using var connection = new SqlConnection(ConnectionString);

        var tableExists = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.tables WHERE object_id = OBJECT_ID(N'governance.ToolRequest')");
        Assert.AreEqual(1, tableExists);

        var indexes = (await connection.QueryAsync<string>(
            """
            SELECT name
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'governance.ToolRequest')
            """)).ToArray();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "IX_ToolRequest_Project_CreatedUtc",
                "IX_ToolRequest_Correlation_CreatedUtc",
                "IX_ToolRequest_Project_Status_CreatedUtc",
                "IX_ToolRequest_GovernanceEventId"
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
                "usp_ToolRequest_Create",
                "usp_ToolRequest_GetById",
                "usp_ToolRequest_ListForProject",
                "usp_ToolRequest_ListForCorrelation"
            },
            procedures);
    }

    [TestMethod]
    public async Task Create_PersistsToolRequestAndCreatesLinkedGovernanceEvent()
    {
        var created = await _store.CreateAsync(ValidRequest() with { ToolName = " workspace.diff ", OperationName = " inspect " });

        Assert.AreNotEqual(Guid.Empty, created.ToolRequestId);
        Assert.AreNotEqual(Guid.Empty, created.GovernanceEventId);
        Assert.AreEqual(ProjectId, created.ProjectId);
        Assert.AreEqual("workspace.diff", created.ToolName);
        Assert.AreEqual("inspect", created.OperationName);
        Assert.AreEqual("agent", created.RequestedByActorType);
        Assert.AreEqual("reporting-agent", created.RequestedByActorId);
        Assert.AreEqual(CorrelationId, created.CorrelationId);
        Assert.AreEqual(CausationId, created.CausationId);
        Assert.AreEqual("Request workspace diff inspection", created.Purpose);
        Assert.AreEqual(1, created.RequestPayloadVersion);
        Assert.AreEqual(ValidPayload, created.RequestPayloadJson);
        Assert.AreEqual(ToolRequestStatus.Recorded, created.Status);
        Assert.AreNotEqual(default, created.CreatedUtc);

        var read = await _store.GetAsync(created.ToolRequestId);
        Assert.IsNotNull(read);
        Assert.AreEqual(created.ToolRequestId, read.ToolRequestId);
        Assert.AreEqual(ValidPayload, read.RequestPayloadJson);

        var governanceEvent = await _governanceEvents.GetAsync(created.GovernanceEventId);
        Assert.IsNotNull(governanceEvent);
        Assert.AreEqual("tool.request.created", governanceEvent.EventType);
        Assert.AreEqual("tool_request", governanceEvent.SubjectType);
        Assert.AreEqual(created.ToolRequestId.ToString(), governanceEvent.SubjectId, ignoreCase: true);
        Assert.AreEqual(created.ProjectId, governanceEvent.ProjectId);
        Assert.AreEqual(created.CorrelationId, governanceEvent.CorrelationId);
        Assert.IsFalse(governanceEvent.PayloadJson.Contains("workspaceRef", StringComparison.Ordinal), "Governance event payload must not duplicate the full request payload.");
    }

    [TestMethod]
    public async Task ListForProject_ReturnsOnlyProjectSummariesDeterministicallyWithoutPayload()
    {
        await _store.CreateAsync(ValidRequest() with { ToolName = "tool.one" });
        await _store.CreateAsync(ValidRequest() with { ToolName = "tool.two" });
        await _store.CreateAsync(ValidRequest() with { ProjectId = OtherProjectId, ToolName = "tool.other" });

        var summaries = await _store.ListForProjectAsync(new ToolRequestsForProjectQuery { ProjectId = ProjectId, Take = 2 });

        Assert.AreEqual(2, summaries.Count);
        Assert.IsTrue(summaries.All(summary => summary.ProjectId == ProjectId));
        AssertOrderedDescending(summaries);
        AssertSummaryDoesNotExposePayload();
    }

    [TestMethod]
    public async Task ListForCorrelation_ReturnsOnlyMatchingRequestsDeterministicallyWithoutPayload()
    {
        await _store.CreateAsync(ValidRequest() with { CorrelationId = CorrelationId, ToolName = "tool.one" });
        await _store.CreateAsync(ValidRequest() with { CorrelationId = CorrelationId, ToolName = "tool.two" });
        await _store.CreateAsync(ValidRequest() with { CorrelationId = OtherCorrelationId, ToolName = "tool.other" });

        var summaries = await _store.ListForCorrelationAsync(new ToolRequestsForCorrelationQuery { CorrelationId = CorrelationId, Take = 10 });

        Assert.AreEqual(2, summaries.Count);
        Assert.IsTrue(summaries.All(summary => summary.CorrelationId == CorrelationId));
        AssertOrderedDescending(summaries);
        AssertSummaryDoesNotExposePayload();
    }

    [TestMethod]
    public async Task Store_RejectsInvalidCreateAndQueryInputs()
    {
        var createCases = new Dictionary<string, Func<Task>>
        {
            [ToolRequestValidator.ProjectIdRequired] = () => _store.CreateAsync(ValidRequest() with { ProjectId = Guid.Empty }),
            [ToolRequestValidator.ToolNameRequired] = () => _store.CreateAsync(ValidRequest() with { ToolName = " " }),
            [ToolRequestValidator.OperationNameRequired] = () => _store.CreateAsync(ValidRequest() with { OperationName = " " }),
            [ToolRequestValidator.ActorTypeRequired] = () => _store.CreateAsync(ValidRequest() with { RequestedByActorType = " " }),
            [ToolRequestValidator.ActorIdRequired] = () => _store.CreateAsync(ValidRequest() with { RequestedByActorId = " " }),
            [ToolRequestValidator.PayloadVersionInvalid] = () => _store.CreateAsync(ValidRequest() with { RequestPayloadVersion = 0 }),
            [ToolRequestValidator.PayloadJsonRequired] = () => _store.CreateAsync(ValidRequest() with { RequestPayloadJson = " " }),
            [ToolRequestValidator.PayloadJsonInvalid] = () => _store.CreateAsync(ValidRequest() with { RequestPayloadJson = "{not-json" }),
            [ToolRequestValidator.PayloadTextUnsafe] = () => _store.CreateAsync(ValidRequest() with { RequestPayloadJson = "{\"summary\":\"raw prompt must not be stored\"}" })
        };

        foreach (var pair in createCases)
            await ExpectArgumentExceptionAsync(pair.Key, pair.Value);

        var queryCases = new Dictionary<string, Func<Task>>
        {
            [ToolRequestValidator.ProjectIdRequired] = () => _store.ListForProjectAsync(new ToolRequestsForProjectQuery { ProjectId = Guid.Empty }),
            [ToolRequestValidator.TakeInvalid] = () => _store.ListForProjectAsync(new ToolRequestsForProjectQuery { ProjectId = ProjectId, Take = 0 }),
            [ToolRequestValidator.TakeInvalid + " max"] = () => _store.ListForProjectAsync(new ToolRequestsForProjectQuery { ProjectId = ProjectId, Take = ToolRequestValidator.MaxTake + 1 }),
            [ToolRequestValidator.CorrelationIdRequired] = () => _store.ListForCorrelationAsync(new ToolRequestsForCorrelationQuery { CorrelationId = Guid.Empty })
        };

        foreach (var pair in queryCases)
            await ExpectArgumentExceptionAsync(pair.Key.Replace(" max", string.Empty, StringComparison.Ordinal), pair.Value);
    }

    [TestMethod]
    public async Task Create_RollsBackBothRowsWhenEitherInsertFails()
    {
        var eventFailureRequestId = Guid.NewGuid();
        var eventFailureEventId = Guid.NewGuid();
        await ExpectSqlFailsAsync(() => DirectCreateProcedureAsync(eventFailureRequestId, eventFailureEventId, ValidPayload, "{not-json"));
        await AssertNoRowsAsync(eventFailureRequestId, eventFailureEventId);

        var requestFailureRequestId = Guid.NewGuid();
        var requestFailureEventId = Guid.NewGuid();
        await ExpectSqlFailsAsync(() => DirectCreateProcedureAsync(requestFailureRequestId, requestFailureEventId, "{not-json", "{\"schema\":\"tool.request.created.v1\"}"));
        await AssertNoRowsAsync(requestFailureRequestId, requestFailureEventId);
    }

    [TestMethod]
    public async Task SqlBoundary_BlocksInvalidPayloadStatusAndBrokenGovernanceLink()
    {
        var created = await _store.CreateAsync(ValidRequest());
        await ExpectSqlFailsAsync(() => DirectInsertAsync(Guid.NewGuid(), created.GovernanceEventId, payloadJson: "{not-json", status: "Recorded"));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(Guid.NewGuid(), created.GovernanceEventId, payloadJson: ValidPayload, status: "Approved"));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(Guid.NewGuid(), Guid.NewGuid(), payloadJson: ValidPayload, status: "Recorded"));
    }

    [TestMethod]
    public async Task Create_DoesNotCreateGateApprovalPolicyDogfoodWorkflowMemoryOrSourceApplyState()
    {
        var beforeTables = await ExistingTablesAsync();
        _ = await _store.CreateAsync(ValidRequest());
        var afterTables = await ExistingTablesAsync();

        Assert.IsTrue(afterTables.Contains("governance.ToolRequest", StringComparer.OrdinalIgnoreCase));
        Assert.IsTrue(afterTables.Contains("governance.GovernanceEvent", StringComparer.OrdinalIgnoreCase));
        AssertNoForbiddenNames(afterTables, "governance.ToolGateDecision", "governance.ApprovalDecision", "governance.PolicyDecision", "governance.DogfoodReceipt", "governance.WorkflowStep", "governance.MemoryPromotion", "governance.SourceApply");
        Assert.IsTrue(afterTables.Length <= beforeTables.Length + 1, "Only governance.ToolRequest may be added on top of the existing governance event spine.");
    }

    [TestMethod]
    public async Task GovernanceEventReadModel_ReturnsToolRequestCreatedEventForSubject()
    {
        var created = await _store.CreateAsync(ValidRequest());

        var events = await _governanceEvents.ListForSubjectAsync(new GovernanceEventsForSubjectQuery
        {
            ProjectId = created.ProjectId,
            SubjectType = "tool_request",
            SubjectId = created.ToolRequestId.ToString(),
            Take = 10
        });

        Assert.AreEqual(1, events.Count);
        Assert.AreEqual(created.GovernanceEventId, events.Single().EventId);
        Assert.AreEqual("tool.request.created", events.Single().EventType);
    }

    [TestMethod]
    public void ToolRequestStore_StaticBoundary_DoesNotAddGateApprovalWorkflowA2aMemoryPromotionApiCliOrRuntimeExecution()
    {
        var root = FindRepositoryRoot();
        var coreText = File.ReadAllText(Path.Combine(root, "IronDev.Core", "Governance", "ToolRequestModels.cs"));
        var storeText = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlToolRequestStore.cs"));
        var migrationText = File.ReadAllText(Path.Combine(root, "Database", "migrate_tool_request.sql"));
        var apiControllerText = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Controllers", "ToolRequestsV1Controller.cs"));
        var apiStoreText = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Controllers", "SqlToolRequestApiStore.cs"));

        StringAssert.Contains(storeText, "CommandType.StoredProcedure");
        StringAssert.Contains(migrationText, "governance.usp_ToolRequest_Create");
        StringAssert.Contains(migrationText, "REFERENCES governance.GovernanceEvent(EventId)");
        StringAssert.Contains(apiStoreText, "SqlToolRequestApiStore");

        AssertNoForbiddenTokens(coreText, "Approved", "Passed", "Executable", "GatePassed", "HumanApproved", "IWorkflow", "LangGraph", "IAgentHandoff", "PromoteCollectiveMemory", "SourceApply", "ControllerBase", "HttpClient", "ProcessStartInfo", "File.Copy", "File.Delete");
        AssertNoForbiddenTokens(storeText, "IAgentToolExecutionGate", "ToolGateDecision", "ApprovalDecision", "PolicyDecision", "DogfoodReceipt", "IWorkflow", "LangGraph", "IAgentHandoff", "PromoteCollectiveMemory", "SourceApply", "ControllerBase", "WebApplication", "IHostedService", "BackgroundService", "ProcessStartInfo", "File.Copy", "File.Delete");
        AssertNoForbiddenTokens(apiControllerText, "InMemoryToolRequestApiStore", "SqlConnection", "INSERT INTO", "UPDATE ", "DELETE FROM", "IAgentToolExecutionGate", "IToolExecutionAuditStore", "IControlledWorktreeApplyService", "PromoteCollectiveMemory");
    }

    private static ToolRequestCreateRequest ValidRequest() =>
        new()
        {
            ProjectId = ProjectId,
            ToolName = "workspace.diff",
            OperationName = "inspect",
            RequestedByActorType = "agent",
            RequestedByActorId = "reporting-agent",
            CorrelationId = CorrelationId,
            CausationId = CausationId,
            Purpose = "Request workspace diff inspection",
            RequestPayloadVersion = 1,
            RequestPayloadJson = ValidPayload
        };

    private async Task DirectCreateProcedureAsync(Guid toolRequestId, Guid eventId, string requestPayloadJson, string eventPayloadJson)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(
            "governance.usp_ToolRequest_Create",
            new
            {
                ToolRequestId = toolRequestId,
                ProjectId,
                GovernanceEventId = eventId,
                ToolName = "workspace.diff",
                OperationName = "inspect",
                RequestedByActorType = "agent",
                RequestedByActorId = "reporting-agent",
                CorrelationId,
                CausationId,
                Purpose = "transaction proof",
                RequestPayloadVersion = 1,
                RequestPayloadJson = requestPayloadJson,
                GovernanceEventPayloadJson = eventPayloadJson
            },
            commandType: CommandType.StoredProcedure);
    }

    private async Task DirectInsertAsync(Guid toolRequestId, Guid governanceEventId, string payloadJson, string status)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT INTO governance.ToolRequest
            (
                ToolRequestId, ProjectId, GovernanceEventId, ToolName, OperationName,
                RequestedByActorType, RequestedByActorId, CorrelationId, CausationId,
                Purpose, RequestPayloadVersion, RequestPayloadJson, Status
            )
            VALUES
            (
                @ToolRequestId, @ProjectId, @GovernanceEventId, N'workspace.diff', N'inspect',
                N'agent', N'reporting-agent', @CorrelationId, @CausationId,
                N'direct insert proof', 1, @PayloadJson, @Status
            );
            """,
            new { ToolRequestId = toolRequestId, ProjectId, GovernanceEventId = governanceEventId, CorrelationId, CausationId, PayloadJson = payloadJson, Status = status });
    }

    private async Task AssertNoRowsAsync(Guid toolRequestId, Guid eventId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        var requestCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM governance.ToolRequest WHERE ToolRequestId = @ToolRequestId", new { ToolRequestId = toolRequestId });
        var eventCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM governance.GovernanceEvent WHERE EventId = @EventId", new { EventId = eventId });
        Assert.AreEqual(0, requestCount);
        Assert.AreEqual(0, eventCount);
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

    private async Task ApplySqlFileAsync(params string[] pathParts)
    {
        await using var connection = new SqlConnection(ConnectionString);
        var migration = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), Path.Combine(pathParts)));
        foreach (var batch in SplitSqlBatches(migration))
            await connection.ExecuteAsync(batch);
    }

    private async Task DropGovernanceSchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(
            """
            IF OBJECT_ID(N'governance.usp_ToolRequest_Create', N'P') IS NOT NULL
                DROP PROCEDURE governance.usp_ToolRequest_Create;
            IF OBJECT_ID(N'governance.usp_ToolRequest_GetById', N'P') IS NOT NULL
                DROP PROCEDURE governance.usp_ToolRequest_GetById;
            IF OBJECT_ID(N'governance.usp_ToolRequest_ListForProject', N'P') IS NOT NULL
                DROP PROCEDURE governance.usp_ToolRequest_ListForProject;
            IF OBJECT_ID(N'governance.usp_ToolRequest_ListForCorrelation', N'P') IS NOT NULL
                DROP PROCEDURE governance.usp_ToolRequest_ListForCorrelation;
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

    private static void AssertOrderedDescending(IReadOnlyList<ToolRequestSummary> summaries)
    {
        var expected = summaries
            .OrderByDescending(summary => summary.CreatedUtc)
            .ThenByDescending(summary => summary.ToolRequestId)
            .Select(summary => summary.ToolRequestId)
            .ToArray();
        var actual = summaries.Select(summary => summary.ToolRequestId).ToArray();
        CollectionAssert.AreEqual(expected, actual);
    }

    private static void AssertSummaryDoesNotExposePayload()
    {
        var properties = typeof(ToolRequestSummary).GetProperties().Select(property => property.Name).ToArray();
        Assert.IsFalse(properties.Contains("RequestPayloadJson"));
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


