using System.Text.Json;
using System.Text.RegularExpressions;
using IronDev.Core.Governance;
using IronDev.Data;
using IronDev.Infrastructure.Governance;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class ApprovalDecisionStoreTests : IntegrationTestBase
{
    [TestInitialize]
    public async Task SetUp()
    {
        await DropGovernanceSchemaAsync();
        await ApplySqlFileAsync("Database", "migrate_governance_event.sql");
        await ApplySqlFileAsync("Database", "migrate_tool_request.sql");
        await ApplySqlFileAsync("Database", "migrate_tool_gate_decision.sql");
        await ApplySqlFileAsync("Database", "migrate_approval_decision.sql");
    }

    [TestCleanup]
    public async Task TearDown() => await DropGovernanceSchemaAsync();

    [TestMethod]
    public void ApprovalDecisionContracts_ExposeApprovalVocabularyButNoExecutionStatuses()
    {
        var names = Enum.GetNames<ApprovalDecisionValue>();

        CollectionAssert.AreEquivalent(new[] { "Approved", "Rejected", "Revoked", "Expired" }, names);
        CollectionAssert.DoesNotContain(names, "Executed");
        CollectionAssert.DoesNotContain(names, "AuthorizedToRun");
        CollectionAssert.DoesNotContain(names, "ReadyToExecute");
        CollectionAssert.DoesNotContain(names, "Applied");
        CollectionAssert.DoesNotContain(names, "Promoted");
        CollectionAssert.DoesNotContain(names, "Released");
    }

    [TestMethod]
    public void ApprovalDecisionInterface_IsAppendOnlyRecordStoreSurface()
    {
        var methods = typeof(IApprovalDecisionStore).GetMethods().Select(method => method.Name).OrderBy(name => name).ToArray();

        CollectionAssert.AreEquivalent(
            new[] { "GetAsync", "ListForCorrelationAsync", "ListForProjectAsync", "ListForSubjectAsync", "RecordAsync" },
            methods);
        Assert.IsFalse(methods.Any(name => name.Contains("Execute", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Apply", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Promote", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Gate", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Authorize", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Update", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Delete", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Upsert", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Save", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task Record_ApprovedDecision_PersistsDecisionAndGovernanceEventWithoutExecutionAuthority()
    {
        var projectId = Guid.NewGuid();
        var subjectId = Guid.NewGuid().ToString("D");
        var decision = await ApprovalStore().RecordAsync(ValidDecision(projectId, ApprovalDecisionScopes.ToolExecution, "tool_request", subjectId, nameof(ApprovalDecisionValue.Approved)));

        Assert.AreEqual(projectId, decision.ProjectId);
        Assert.AreEqual(ApprovalDecisionScopes.ToolExecution, decision.ApprovalScope);
        Assert.AreEqual("tool_request", decision.SubjectType);
        Assert.AreEqual(subjectId, decision.SubjectId);
        Assert.AreEqual(nameof(ApprovalDecisionValue.Approved), decision.Decision);
        Assert.AreEqual("human", decision.DecidedByActorType);
        Assert.AreEqual(1, decision.EvidenceVersion);
        StringAssert.Contains(decision.EvidenceJson, "approval.decision.evidence.v1");
        Assert.AreNotEqual(Guid.Empty, decision.GovernanceEventId);

        var eventType = await ExecuteScalarAsync<string>("SELECT EventType FROM governance.GovernanceEvent WHERE EventId = @id", new SqlParameter("@id", decision.GovernanceEventId));
        Assert.AreEqual("approval.decision.recorded", eventType);

        var eventSubject = await ExecuteScalarAsync<string>("SELECT SubjectType FROM governance.GovernanceEvent WHERE EventId = @id", new SqlParameter("@id", decision.GovernanceEventId));
        Assert.AreEqual("approval_decision", eventSubject);

        var payload = await ExecuteScalarAsync<string>("SELECT PayloadJson FROM governance.GovernanceEvent WHERE EventId = @id", new SqlParameter("@id", decision.GovernanceEventId));
        StringAssert.Contains(payload, "\"grantsExecution\":false");
        StringAssert.Contains(payload, "\"mutatesSource\":false");
        StringAssert.Contains(payload, "\"promotesMemory\":false");
    }

    [TestMethod]
    public async Task Record_RejectedDecision_PersistsDecisionWithoutExecutionAuthority()
    {
        var projectId = Guid.NewGuid();
        var decision = await ApprovalStore().RecordAsync(ValidDecision(projectId, ApprovalDecisionScopes.ToolExecution, "tool_request", Guid.NewGuid().ToString("D"), nameof(ApprovalDecisionValue.Rejected)));

        Assert.AreEqual(nameof(ApprovalDecisionValue.Rejected), decision.Decision);
        Assert.AreEqual("human", decision.DecidedByActorType);
        Assert.IsNull(decision.SupersedesApprovalDecisionId);
    }

    [TestMethod]
    public async Task Record_RevokedAndExpiredRequireExistingSupersededApproval()
    {
        var projectId = Guid.NewGuid();
        var approved = await ApprovalStore().RecordAsync(ValidDecision(projectId, ApprovalDecisionScopes.ToolExecution, "tool_request", Guid.NewGuid().ToString("D"), nameof(ApprovalDecisionValue.Approved)));

        var revoked = await ApprovalStore().RecordAsync(ValidDecision(projectId, ApprovalDecisionScopes.ToolExecution, approved.SubjectType, approved.SubjectId, nameof(ApprovalDecisionValue.Revoked)) with
        {
            SupersedesApprovalDecisionId = approved.ApprovalDecisionId,
            ReasonCode = "HUMAN_REVOKED"
        });

        Assert.AreEqual(nameof(ApprovalDecisionValue.Revoked), revoked.Decision);
        Assert.AreEqual(approved.ApprovalDecisionId, revoked.SupersedesApprovalDecisionId);

        await AssertThrowsAsync<ArgumentException>(() => ApprovalStore().RecordAsync(ValidDecision(projectId, ApprovalDecisionScopes.ToolExecution, "tool_request", Guid.NewGuid().ToString("D"), nameof(ApprovalDecisionValue.Expired))));
        await AssertThrowsAsync<SqlException>(() => ApprovalStore().RecordAsync(ValidDecision(projectId, ApprovalDecisionScopes.ToolExecution, "tool_request", Guid.NewGuid().ToString("D"), nameof(ApprovalDecisionValue.Expired)) with
        {
            SupersedesApprovalDecisionId = Guid.NewGuid()
        }));
    }

    [TestMethod]
    public async Task Record_RejectsSensitiveApprovalFromNonHumanActor()
    {
        var request = ValidDecision(Guid.NewGuid(), ApprovalDecisionScopes.SourceApply, "source_apply_package", Guid.NewGuid().ToString("D"), nameof(ApprovalDecisionValue.Approved)) with
        {
            DecidedByActorType = "system_test_fixture"
        };

        await AssertThrowsAsync<ArgumentException>(() => ApprovalStore().RecordAsync(request));
        var count = await ExecuteScalarAsync<int>("SELECT COUNT(1) FROM governance.ApprovalDecision");
        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public async Task Record_RejectsInvalidEvidenceAndPrivateReasoningMarkersBeforeSqlWrite()
    {
        var invalidJson = ValidDecision(Guid.NewGuid(), ApprovalDecisionScopes.ToolExecution, "tool_request", Guid.NewGuid().ToString("D"), nameof(ApprovalDecisionValue.Approved)) with
        {
            EvidenceJson = "not-json"
        };
        var privateReasoning = invalidJson with
        {
            EvidenceJson = "{\"schema\":\"approval.decision.evidence.v1\",\"chainOfThought\":\"secret\"}"
        };

        await AssertThrowsAsync<ArgumentException>(() => ApprovalStore().RecordAsync(invalidJson));
        await AssertThrowsAsync<ArgumentException>(() => ApprovalStore().RecordAsync(privateReasoning));
        var count = await ExecuteScalarAsync<int>("SELECT COUNT(1) FROM governance.ApprovalDecision");
        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public async Task QueryPaths_ReturnScopedSummariesWithoutEvidenceJson()
    {
        var store = ApprovalStore();
        var projectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var subjectId = Guid.NewGuid().ToString("D");
        var first = await store.RecordAsync(ValidDecision(projectId, ApprovalDecisionScopes.ToolExecution, "tool_request", subjectId, nameof(ApprovalDecisionValue.Rejected)) with { CorrelationId = correlationId });
        var second = await store.RecordAsync(ValidDecision(projectId, ApprovalDecisionScopes.ToolExecution, "tool_request", subjectId, nameof(ApprovalDecisionValue.Approved)) with { CorrelationId = correlationId });
        _ = await store.RecordAsync(ValidDecision(otherProjectId, ApprovalDecisionScopes.ToolExecution, "tool_request", subjectId, nameof(ApprovalDecisionValue.Approved)) with { CorrelationId = correlationId });

        var bySubject = await store.ListForSubjectAsync(new ApprovalDecisionsForSubjectQuery
        {
            ProjectId = projectId,
            ApprovalScope = ApprovalDecisionScopes.ToolExecution,
            SubjectType = "tool_request",
            SubjectId = subjectId
        });
        var byProject = await store.ListForProjectAsync(new ApprovalDecisionsForProjectQuery { ProjectId = projectId });
        var byCorrelation = await store.ListForCorrelationAsync(new ApprovalDecisionsForCorrelationQuery { ProjectId = projectId, CorrelationId = correlationId });
        var bounded = await store.ListForSubjectAsync(new ApprovalDecisionsForSubjectQuery
        {
            ProjectId = projectId,
            ApprovalScope = ApprovalDecisionScopes.ToolExecution,
            SubjectType = "tool_request",
            SubjectId = subjectId,
            Take = 1
        });

        Assert.AreEqual(2, bySubject.Count);
        Assert.AreEqual(2, byProject.Count);
        Assert.AreEqual(2, byCorrelation.Count);
        Assert.AreEqual(1, bounded.Count);
        CollectionAssert.AreEquivalent(new[] { first.ApprovalDecisionId, second.ApprovalDecisionId }, bySubject.Select(row => row.ApprovalDecisionId).ToArray());
        Assert.IsFalse(typeof(ApprovalDecisionSummary).GetProperties().Any(property => property.Name.Equals("EvidenceJson", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Get_ReturnsApprovalDecisionByIdAndNullForMissing()
    {
        var decision = await ApprovalStore().RecordAsync(ValidDecision(Guid.NewGuid(), ApprovalDecisionScopes.ToolExecution, "tool_request", Guid.NewGuid().ToString("D"), nameof(ApprovalDecisionValue.Approved)));

        var found = await ApprovalStore().GetAsync(decision.ApprovalDecisionId);
        var missing = await ApprovalStore().GetAsync(Guid.NewGuid());

        Assert.IsNotNull(found);
        Assert.AreEqual(decision.ApprovalDecisionId, found.ApprovalDecisionId);
        Assert.IsNull(missing);
    }

    [TestMethod]
    public async Task DirectSql_UpdateDeleteSensitiveNonHumanAndUnsafeEvidenceAreBlocked()
    {
        var decision = await ApprovalStore().RecordAsync(ValidDecision(Guid.NewGuid(), ApprovalDecisionScopes.ToolExecution, "tool_request", Guid.NewGuid().ToString("D"), nameof(ApprovalDecisionValue.Approved)));

        await AssertSqlFailsAsync("UPDATE governance.ApprovalDecision SET ReasonCode = N'CHANGED' WHERE ApprovalDecisionId = @id", new SqlParameter("@id", decision.ApprovalDecisionId));
        await AssertSqlFailsAsync("DELETE FROM governance.ApprovalDecision WHERE ApprovalDecisionId = @id", new SqlParameter("@id", decision.ApprovalDecisionId));
        await AssertSqlFailsAsync(DirectInsertSql(), DirectInsertParameters(decision.ProjectId, decision.GovernanceEventId, ApprovalDecisionScopes.SourceApply, "system_test_fixture", SafeEvidenceJson()));
        await AssertSqlFailsAsync(DirectInsertSql(), DirectInsertParameters(decision.ProjectId, decision.GovernanceEventId, ApprovalDecisionScopes.ToolExecution, "human", "{\"schema\":\"approval.decision.evidence.v1\",\"rawPrompt\":\"secret\"}"));
    }

    [TestMethod]
    public async Task GatePassAndRequiresApprovalDoNotCreateApprovalDecision()
    {
        var projectId = Guid.NewGuid();
        var toolRequest = await CreateToolRequestAsync(projectId, Guid.NewGuid());
        _ = await CreateGateDecisionAsync(projectId, toolRequest.ToolRequestId, nameof(ToolGateDecisionValue.Passed));
        _ = await CreateGateDecisionAsync(projectId, toolRequest.ToolRequestId, nameof(ToolGateDecisionValue.RequiresApproval));

        var count = await ExecuteScalarAsync<int>("SELECT COUNT(1) FROM governance.ApprovalDecision");

        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public void MigrationAndInventory_RegisterApprovalDecisionStore()
    {
        var root = FindRepositoryRoot();
        var manifest = File.ReadAllText(Path.Combine(root, "Database", "migrations.json"));
        var sql = File.ReadAllText(Path.Combine(root, "Database", "migrate_approval_decision.sql"));
        var inventory = File.ReadAllText(Path.Combine(root, "Database", "sql-inventory.json"));
        var verifier = File.ReadAllText(Path.Combine(root, "Database", "verify-migrations.ps1"));

        StringAssert.Contains(manifest, "Database/migrate_approval_decision.sql");
        StringAssert.Contains(inventory, "database.migrate-approval-decision");
        StringAssert.Contains(inventory, "runtime.approval-decision-store");
        StringAssert.Contains(verifier, "governance.ApprovalDecision table");
        StringAssert.Contains(sql, "governance.usp_ApprovalDecision_Record");
        StringAssert.Contains(sql, "governance.usp_ApprovalDecision_ListForSubject");
        StringAssert.Contains(sql, "FK_ApprovalDecision_GovernanceEvent");
        StringAssert.Contains(sql, "FK_ApprovalDecision_Supersedes");
        StringAssert.Contains(sql, "TR_ApprovalDecision_BlockUpdateDelete");
        StringAssert.Contains(sql, "Sensitive approval scopes require a human actor");
    }

    [TestMethod]
    public void RuntimeWiring_DoesNotExposeApprovalDecisionThroughApiCliOrController()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Program.cs"));
        var apiControllers = Directory.GetFiles(Path.Combine(root, "IronDev.Api", "Controllers"), "*.cs").Select(File.ReadAllText).ToArray();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "IronDevCli.cs"));
        var store = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlApprovalDecisionStore.cs"));

        Assert.IsFalse(program.Contains("IApprovalDecisionStore", StringComparison.Ordinal));
        Assert.IsFalse(apiControllers.Any(text => text.Contains("IApprovalDecisionStore", StringComparison.Ordinal) || text.Contains("SqlApprovalDecisionStore", StringComparison.Ordinal)));
        Assert.IsFalse(cli.Contains("IApprovalDecisionStore", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("SqlApprovalDecisionStore", StringComparison.Ordinal));
        StringAssert.Contains(store, "CommandType.StoredProcedure");
        AssertNoForbiddenTokens(store, "INSERT INTO governance.ApprovalDecision", "UPDATE governance.ApprovalDecision", "DELETE FROM governance.ApprovalDecision", "CREATE TABLE", "ALTER TABLE", "ControllerBase", "WebApplication", "IHostedService", "BackgroundService", "ProcessStartInfo", "File.Copy", "File.Delete");
    }

    private IApprovalDecisionStore ApprovalStore() =>
        new SqlApprovalDecisionStore(ServiceProvider.GetRequiredService<IDbConnectionFactory>());

    private IToolGateDecisionStore GateStore() =>
        new SqlToolGateDecisionStore(ServiceProvider.GetRequiredService<IDbConnectionFactory>());

    private IToolRequestStore RequestStore() =>
        new SqlToolRequestStore(ServiceProvider.GetRequiredService<IDbConnectionFactory>());

    private async Task<ToolRequestReadModel> CreateToolRequestAsync(Guid projectId, Guid correlationId) =>
        await RequestStore().CreateAsync(new ToolRequestCreateRequest
        {
            ProjectId = projectId,
            ToolName = "workspace.apply-copy",
            OperationName = "request",
            RequestedByActorType = "agent",
            RequestedByActorId = "tester-agent",
            CorrelationId = correlationId,
            Purpose = "Create parent request for durable approval decision test.",
            RequestPayloadVersion = 1,
            RequestPayloadJson = "{\"schemaVersion\":1,\"purpose\":\"test\"}"
        });

    private async Task<ToolGateDecisionReadModel> CreateGateDecisionAsync(Guid projectId, Guid toolRequestId, string decision) =>
        await GateStore().RecordAsync(new ToolGateDecisionRecordRequest(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            projectId,
            toolRequestId,
            decision,
            "tool-request-gate",
            1,
            "system",
            "approval-decision-tests",
            "TEST_GATE",
            JsonSerializer.Serialize(new { schemaVersion = 1, evidence = "test" })));

    private static ApprovalDecisionRecordRequest ValidDecision(Guid projectId, string scope, string subjectType, string subjectId, string decision) =>
        new()
        {
            ProjectId = projectId,
            ApprovalScope = scope,
            SubjectType = subjectType,
            SubjectId = subjectId,
            Decision = decision,
            ReasonCode = "HUMAN_REVIEWED",
            Reason = "Reviewed explicit evidence only.",
            DecidedByActorType = "human",
            DecidedByActorId = "human-reviewer",
            EvidenceVersion = 1,
            EvidenceJson = SafeEvidenceJson()
        };

    private static string SafeEvidenceJson() =>
        JsonSerializer.Serialize(new
        {
            schema = "approval.decision.evidence.v1",
            reviewedBy = "human",
            evidenceRefs = new[] { "tool_request:test", "tool_gate_decision:test" },
            grantsExecution = false,
            mutatesSource = false,
            promotesMemory = false,
            startsWorkflow = false
        });

    private static string DirectInsertSql() =>
        @"INSERT INTO governance.ApprovalDecision
          (ApprovalDecisionId, ProjectId, GovernanceEventId, ApprovalScope, SubjectType, SubjectId, Decision, ReasonCode, Reason, DecidedByActorType, DecidedByActorId, SupersedesApprovalDecisionId, CorrelationId, CausationId, EvidenceVersion, EvidenceJson)
          VALUES (@id, @projectId, @eventId, @scope, N'tool_request', CONVERT(NVARCHAR(36), NEWID()), N'Approved', N'DIRECT_SQL', N'Direct SQL test.', @actorType, N'direct-sql', NULL, NEWID(), @eventId, 1, @evidenceJson)";

    private static SqlParameter[] DirectInsertParameters(Guid projectId, Guid eventId, string scope, string actorType, string evidenceJson) =>
    [
        new SqlParameter("@id", Guid.NewGuid()),
        new SqlParameter("@projectId", projectId),
        new SqlParameter("@eventId", eventId),
        new SqlParameter("@scope", scope),
        new SqlParameter("@actorType", actorType),
        new SqlParameter("@evidenceJson", evidenceJson)
    ];

    private async Task ApplySqlFileAsync(params string[] pathParts)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), Path.Combine(pathParts)));
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(batch))
                continue;

            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task DropGovernanceSchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            @"IF OBJECT_ID(N'governance.usp_ApprovalDecision_Record', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ApprovalDecision_Record;
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
              IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance') DROP SCHEMA governance;",
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<T> ExecuteScalarAsync<T>(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        var value = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(value!, typeof(T));
    }

    private async Task AssertSqlFailsAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await AssertThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        Assert.Fail($"Expected exception of type {typeof(TException).Name}.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }

    private static void AssertNoForbiddenTokens(string text, params string[] tokens)
    {
        foreach (var token in tokens)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Unexpected token: {token}");
    }
}
