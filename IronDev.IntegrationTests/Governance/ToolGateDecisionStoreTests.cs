using System.Text.Json;
using System.Text.RegularExpressions;
using IronDev.Core.Governance;
using IronDev.Infrastructure.Governance;
using IronDev.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class ToolGateDecisionStoreTests : IntegrationTestBase
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [TestInitialize]
    public async Task SetUp()
    {
        await DropGovernanceSchemaAsync();
        await ApplySqlFileAsync("Database", "migrate_governance_event.sql");
        await ApplySqlFileAsync("Database", "migrate_tool_request.sql");
        await ApplySqlFileAsync("Database", "migrate_tool_gate_decision.sql");
    }

    [TestCleanup]
    public async Task TearDown() => await DropGovernanceSchemaAsync();

    [TestMethod]
    public void ToolGateDecisionContracts_ExposeSafeDecisionVocabularyOnly()
    {
        var names = Enum.GetNames<ToolGateDecisionValue>();

        CollectionAssert.AreEquivalent(new[] { "Passed", "Blocked", "RequiresApproval" }, names);
        CollectionAssert.DoesNotContain(names, "Approved");
        CollectionAssert.DoesNotContain(names, "Authorized");
        CollectionAssert.DoesNotContain(names, "Executable");
        CollectionAssert.DoesNotContain(names, "ReadyToRun");
        CollectionAssert.DoesNotContain(names, "HumanApproved");
        CollectionAssert.DoesNotContain(names, "ExecutionGranted");
        CollectionAssert.DoesNotContain(names, "PermissionGranted");
    }

    [TestMethod]
    public void ToolGateDecisionInterface_IsAppendOnlyStoreSurface()
    {
        var methods = typeof(IToolGateDecisionStore).GetMethods().Select(method => method.Name).OrderBy(name => name).ToArray();

        CollectionAssert.AreEquivalent(
            new[] { "GetAsync", "ListForCorrelationAsync", "ListForProjectAsync", "ListForToolRequestAsync", "RecordAsync" },
            methods);
        Assert.IsFalse(methods.Any(name => name.Contains("Approve", StringComparison.OrdinalIgnoreCase) || name.Contains("Execute", StringComparison.OrdinalIgnoreCase) || name.Contains("Update", StringComparison.OrdinalIgnoreCase) || name.Contains("Delete", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task Record_PassedDecision_PersistsDecisionAndGovernanceEvent()
    {
        var projectId = Guid.NewGuid();
        var toolRequest = await CreateToolRequestAsync(projectId, Guid.NewGuid());
        var store = GateStore();

        var decision = await store.RecordAsync(ValidDecision(projectId, toolRequest.ToolRequestId, nameof(ToolGateDecisionValue.Passed)));

        Assert.AreEqual(projectId, decision.ProjectId);
        Assert.AreEqual(toolRequest.ToolRequestId, decision.ToolRequestId);
        Assert.AreEqual(nameof(ToolGateDecisionValue.Passed), decision.Decision);
        Assert.AreEqual("tool-request-gate", decision.GateName);
        Assert.AreEqual("system", decision.ActorType);
        Assert.AreNotEqual(Guid.Empty, decision.GovernanceEventId);
        Assert.AreEqual(toolRequest.GovernanceEventId, decision.CausationId);
        var eventType = await ExecuteScalarAsync<string>("SELECT EventType FROM governance.GovernanceEvent WHERE EventId = @id", new SqlParameter("@id", decision.GovernanceEventId));
        Assert.AreEqual("tool.gate.decision.recorded", eventType);
    }

    [TestMethod]
    public async Task Record_BlockedDecision_PersistsWithoutGrantingAuthority()
    {
        var projectId = Guid.NewGuid();
        var toolRequest = await CreateToolRequestAsync(projectId, Guid.NewGuid());
        var decision = await GateStore().RecordAsync(ValidDecision(projectId, toolRequest.ToolRequestId, nameof(ToolGateDecisionValue.Blocked)));

        var flags = await ExecuteScalarAsync<int>("SELECT CONVERT(INT, GrantsApproval) + CONVERT(INT, GrantsExecution) + CONVERT(INT, MutatesSource) + CONVERT(INT, PromotesMemory) FROM governance.ToolGateDecision WHERE ToolGateDecisionId = @id", new SqlParameter("@id", decision.ToolGateDecisionId));
        Assert.AreEqual(0, flags);
    }

    [TestMethod]
    public async Task Record_RequiresApprovalDecision_IsNotApproval()
    {
        var projectId = Guid.NewGuid();
        var toolRequest = await CreateToolRequestAsync(projectId, Guid.NewGuid());
        var decision = await GateStore().RecordAsync(ValidDecision(projectId, toolRequest.ToolRequestId, nameof(ToolGateDecisionValue.RequiresApproval)));

        Assert.AreEqual(nameof(ToolGateDecisionValue.RequiresApproval), decision.Decision);
        var grantsApproval = await ExecuteScalarAsync<bool>("SELECT GrantsApproval FROM governance.ToolGateDecision WHERE ToolGateDecisionId = @id", new SqlParameter("@id", decision.ToolGateDecisionId));
        Assert.IsFalse(grantsApproval);
    }

    [TestMethod]
    public async Task Record_InheritsCorrelationAndCausationFromToolRequestWhenMissing()
    {
        var projectId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var toolRequest = await CreateToolRequestAsync(projectId, correlationId);

        var decision = await GateStore().RecordAsync(ValidDecision(projectId, toolRequest.ToolRequestId, nameof(ToolGateDecisionValue.Blocked)));

        Assert.AreEqual(correlationId, decision.CorrelationId);
        Assert.AreEqual(toolRequest.GovernanceEventId, decision.CausationId);
    }

    [TestMethod]
    public async Task Record_ExplicitCorrelationAndCausationOverrideParentDefaults()
    {
        var projectId = Guid.NewGuid();
        var toolRequest = await CreateToolRequestAsync(projectId, Guid.NewGuid());
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var request = ValidDecision(projectId, toolRequest.ToolRequestId, nameof(ToolGateDecisionValue.Blocked)) with
        {
            CorrelationId = correlationId,
            CausationId = causationId
        };

        var decision = await GateStore().RecordAsync(request);

        Assert.AreEqual(correlationId, decision.CorrelationId);
        Assert.AreEqual(causationId, decision.CausationId);
    }

    [TestMethod]
    public async Task Record_RejectsMissingToolRequest()
    {
        var request = ValidDecision(Guid.NewGuid(), Guid.NewGuid(), nameof(ToolGateDecisionValue.Blocked));

        await AssertThrowsAsync<SqlException>(() => GateStore().RecordAsync(request));
    }

    [TestMethod]
    public async Task Record_RejectsCrossProjectToolRequest()
    {
        var toolRequest = await CreateToolRequestAsync(Guid.NewGuid(), Guid.NewGuid());
        var request = ValidDecision(Guid.NewGuid(), toolRequest.ToolRequestId, nameof(ToolGateDecisionValue.Blocked));

        await AssertThrowsAsync<SqlException>(() => GateStore().RecordAsync(request));
    }

    [TestMethod]
    public async Task Record_RejectsUnsafeEvidenceJsonBeforeSqlWrite()
    {
        var projectId = Guid.NewGuid();
        var toolRequest = await CreateToolRequestAsync(projectId, Guid.NewGuid());
        var request = ValidDecision(projectId, toolRequest.ToolRequestId, nameof(ToolGateDecisionValue.Blocked)) with
        {
            EvidenceJson = "{\"schemaVersion\":1,\"note\":\"chain-of-thought approval granted\"}"
        };

        await AssertThrowsAsync<ArgumentException>(() => GateStore().RecordAsync(request));
        var count = await ExecuteScalarAsync<int>("SELECT COUNT(1) FROM governance.ToolGateDecision");
        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public async Task ListForToolRequestProjectAndCorrelation_ReturnExpectedScopedRows()
    {
        var projectId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var toolRequest = await CreateToolRequestAsync(projectId, correlationId);
        var store = GateStore();
        var first = await store.RecordAsync(ValidDecision(projectId, toolRequest.ToolRequestId, nameof(ToolGateDecisionValue.Blocked)));
        var second = await store.RecordAsync(ValidDecision(projectId, toolRequest.ToolRequestId, nameof(ToolGateDecisionValue.RequiresApproval)));

        var byRequest = await store.ListForToolRequestAsync(new ToolGateDecisionToolRequestQuery(TenantId, projectId, toolRequest.ToolRequestId));
        var byProject = await store.ListForProjectAsync(new ToolGateDecisionProjectQuery(TenantId, projectId));
        var byCorrelation = await store.ListForCorrelationAsync(new ToolGateDecisionCorrelationQuery(TenantId, projectId, correlationId));

        Assert.AreEqual(2, byRequest.Count);
        Assert.AreEqual(2, byProject.Count);
        Assert.AreEqual(2, byCorrelation.Count);
        CollectionAssert.AreEquivalent(new[] { first.ToolGateDecisionId, second.ToolGateDecisionId }, byRequest.Select(row => row.ToolGateDecisionId).ToArray());
    }

    [TestMethod]
    public async Task Get_WrongProjectReturnsNull()
    {
        var projectId = Guid.NewGuid();
        var toolRequest = await CreateToolRequestAsync(projectId, Guid.NewGuid());
        var decision = await GateStore().RecordAsync(ValidDecision(projectId, toolRequest.ToolRequestId, nameof(ToolGateDecisionValue.Blocked)));

        var wrongProject = await GateStore().GetAsync(TenantId, Guid.NewGuid(), decision.ToolGateDecisionId);

        Assert.IsNull(wrongProject);
    }

    [TestMethod]
    public async Task DirectSql_UpdateAndDeleteAreBlocked()
    {
        var projectId = Guid.NewGuid();
        var toolRequest = await CreateToolRequestAsync(projectId, Guid.NewGuid());
        var decision = await GateStore().RecordAsync(ValidDecision(projectId, toolRequest.ToolRequestId, nameof(ToolGateDecisionValue.Blocked)));

        await AssertSqlFailsAsync("UPDATE governance.ToolGateDecision SET ReasonCode = N'CHANGED' WHERE ToolGateDecisionId = @id", new SqlParameter("@id", decision.ToolGateDecisionId));
        await AssertSqlFailsAsync("DELETE FROM governance.ToolGateDecision WHERE ToolGateDecisionId = @id", new SqlParameter("@id", decision.ToolGateDecisionId));
    }

    [TestMethod]
    public async Task DirectSql_AuthorityFlagsAreRejectedByCheckConstraints()
    {
        var projectId = Guid.NewGuid();
        var toolRequest = await CreateToolRequestAsync(projectId, Guid.NewGuid());

        await AssertSqlFailsAsync(
            @"INSERT INTO governance.ToolGateDecision
              (ToolGateDecisionId, ProjectId, ToolRequestId, GovernanceEventId, CorrelationId, CausationId, Decision, GateName, GateVersion, ActorType, ActorId, ReasonCode, EvidenceVersion, EvidenceJson, GrantsApproval, GrantsExecution, MutatesSource, PromotesMemory)
              VALUES (@id, @projectId, @toolRequestId, @eventId, @correlationId, @causationId, N'Blocked', N'tool-request-gate', 1, N'system', N'test', N'DIRECT_SQL', 1, N'{""schemaVersion"":1}', 1, 0, 0, 0)",
            new SqlParameter("@id", Guid.NewGuid()),
            new SqlParameter("@projectId", projectId),
            new SqlParameter("@toolRequestId", toolRequest.ToolRequestId),
            new SqlParameter("@eventId", toolRequest.GovernanceEventId),
            new SqlParameter("@correlationId", Guid.NewGuid()),
            new SqlParameter("@causationId", toolRequest.GovernanceEventId));
    }

    [TestMethod]
    public void MigrationAndInventory_RegisterToolGateDecisionStore()
    {
        var root = FindRepositoryRoot();
        var manifest = File.ReadAllText(Path.Combine(root, "Database", "migrations.json"));
        var sql = File.ReadAllText(Path.Combine(root, "Database", "migrate_tool_gate_decision.sql"));
        var inventory = File.ReadAllText(Path.Combine(root, "Database", "sql-inventory.json"));
        var verifier = File.ReadAllText(Path.Combine(root, "Database", "verify-migrations.ps1"));

        StringAssert.Contains(manifest, "Database/migrate_tool_gate_decision.sql");
        StringAssert.Contains(inventory, "database.migrate-tool-gate-decision");
        StringAssert.Contains(verifier, "governance.ToolGateDecision table");
        StringAssert.Contains(sql, "governance.usp_ToolGateDecision_Record");
        StringAssert.Contains(sql, "CK_ToolGateDecision_NoApprovalGrant");
        StringAssert.Contains(sql, "CK_ToolGateDecision_NoExecutionGrant");
        StringAssert.Contains(sql, "CK_ToolGateDecision_NoSourceMutation");
        StringAssert.Contains(sql, "CK_ToolGateDecision_NoMemoryPromotion");
    }

    [TestMethod]
    public void ApiWiring_UsesDurableSqlGateStoreAndNotInMemoryCache()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Program.cs"));
        var controller = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Controllers", "ToolGatesV1Controller.cs"));
        var apiStore = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Controllers", "SqlToolGateApiStore.cs"));

        StringAssert.Contains(program, "AddScoped<IToolGateDecisionStore, SqlToolGateDecisionStore>");
        StringAssert.Contains(program, "AddScoped<IToolGateApiStore, SqlToolGateApiStore>");
        Assert.IsFalse(program.Contains("InMemoryToolGateApiStore", StringComparison.Ordinal));
        Assert.IsFalse(controller.Contains("InMemoryToolGateApiStore", StringComparison.Ordinal));
        Assert.IsFalse(controller.Contains("SqlConnection", StringComparison.Ordinal));
        StringAssert.Contains(apiStore, "IToolGateDecisionStore");
        StringAssert.Contains(apiStore, "ToolGateDecisionValue.Passed");
    }

    private IToolGateDecisionStore GateStore() =>
        new SqlToolGateDecisionStore(ServiceProvider.GetRequiredService<IDbConnectionFactory>());

    private IToolRequestStore RequestStore() =>
        new SqlToolRequestStore(ServiceProvider.GetRequiredService<IDbConnectionFactory>());

    private async Task<ToolRequestReadModel> CreateToolRequestAsync(Guid projectId, Guid correlationId) =>
        await RequestStore().CreateAsync(new ToolRequestCreateRequest
        {
            ProjectId = projectId,
            ToolName = "workspace.diff",
            OperationName = "inspect",
            RequestedByActorType = "agent",
            RequestedByActorId = "tester-agent",
            CorrelationId = correlationId,
            Purpose = "Create parent request for durable gate decision test.",
            RequestPayloadVersion = 1,
            RequestPayloadJson = "{\"schemaVersion\":1,\"purpose\":\"test\"}"
        });

    private static ToolGateDecisionRecordRequest ValidDecision(Guid projectId, Guid toolRequestId, string decision) =>
        new(
            TenantId,
            projectId,
            toolRequestId,
            decision,
            "tool-request-gate",
            1,
            "system",
            "tool-gate-store-tests",
            "TEST_REASON",
            JsonSerializer.Serialize(new { schemaVersion = 1, evidence = "test" }));

    private async Task ApplySqlFileAsync(params string[] pathParts)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), Path.Combine(pathParts)));
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            await using var command = new SqlCommand(batch, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task DropGovernanceSchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            @"              IF OBJECT_ID(N'governance.usp_ThoughtLedgerGovernanceEventReference_Record', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ThoughtLedgerGovernanceEventReference_Record;
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
              IF OBJECT_ID(N'governance.AcceptedApproval', N'U') IS NOT NULL DROP TABLE governance.AcceptedApproval;              IF OBJECT_ID(N'governance.usp_ApprovalDecision_Record', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ApprovalDecision_Record;
              IF OBJECT_ID(N'governance.usp_ApprovalDecision_GetById', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ApprovalDecision_GetById;
              IF OBJECT_ID(N'governance.usp_ApprovalDecision_ListForSubject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_ApprovalDecision_ListForSubject;
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
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
