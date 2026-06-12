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
public sealed class PolicyDecisionEventStoreTests : IntegrationTestBase
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [TestInitialize]
    public async Task SetUp()
    {
        await DropGovernanceSchemaAsync();
        await ApplySqlFileAsync("Database", "migrate_governance_event.sql");
        await ApplySqlFileAsync("Database", "migrate_tool_request.sql");
        await ApplySqlFileAsync("Database", "migrate_tool_gate_decision.sql");
        await ApplySqlFileAsync("Database", "migrate_approval_decision.sql");
        await ApplySqlFileAsync("Database", "migrate_policy_decision_event.sql");
    }

    [TestCleanup]
    public async Task TearDown() => await DropGovernanceSchemaAsync();

    [TestMethod]
    public void PolicyDecisionContracts_ExposeSafeVocabularyAndRecordOnlyStoreShape()
    {
        var values = Enum.GetNames<PolicyDecisionValue>();
        var methods = typeof(IPolicyDecisionEventStore).GetMethods().Select(method => method.Name).OrderBy(name => name).ToArray();

        CollectionAssert.AreEquivalent(new[] { "Blocked", "NoPolicyBlock", "NotApplicable", "RequiresApproval" }, values);
        CollectionAssert.DoesNotContain(values, "Allowed");
        CollectionAssert.DoesNotContain(values, "Approved");
        CollectionAssert.DoesNotContain(values, "Authorized");
        CollectionAssert.DoesNotContain(values, "Executable");
        CollectionAssert.DoesNotContain(values, "PolicySatisfied");
        CollectionAssert.AreEquivalent(new[] { "GetAsync", "ListForCorrelationAsync", "ListForProjectAsync", "ListForSubjectAsync", "RecordAsync" }, methods);
        Assert.IsFalse(methods.Any(name => name.Contains("Approve", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Authorize", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Execute", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Apply", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Promote", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Release", StringComparison.OrdinalIgnoreCase)
            || name.Contains("EvaluatePolicy", StringComparison.OrdinalIgnoreCase)
            || name.Contains("SatisfyPolicy", StringComparison.OrdinalIgnoreCase)
            || name.Contains("ContinueWorkflow", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Update", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Delete", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Upsert", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Save", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task Record_PersistsPolicyDecisionEventAndGovernanceEventWithoutAuthority()
    {
        var projectId = Guid.NewGuid();
        var chain = await CreateEvidenceChainAsync(projectId, Guid.NewGuid());

        var decision = await PolicyStore().RecordAsync(ValidDecision(projectId, nameof(PolicyDecisionValue.RequiresApproval), chain.ToolRequest.ToolRequestId.ToString("D")) with
        {
            RelatedToolRequestId = chain.ToolRequest.ToolRequestId,
            RelatedToolGateDecisionId = chain.Gate.ToolGateDecisionId,
            RelatedApprovalDecisionId = chain.Approval.ApprovalDecisionId,
            CausationId = chain.Approval.GovernanceEventId,
            CorrelationId = chain.ToolRequest.CorrelationId
        });

        Assert.AreEqual(projectId, decision.ProjectId);
        Assert.AreEqual(PolicyDecisionScopes.ToolExecution, decision.PolicyScope);
        Assert.AreEqual("tool-execution-policy", decision.PolicyName);
        Assert.AreEqual(1, decision.PolicyVersion);
        Assert.AreEqual("tool_request", decision.SubjectType);
        Assert.AreEqual(chain.ToolRequest.ToolRequestId.ToString("D"), decision.SubjectId);
        Assert.AreEqual(nameof(PolicyDecisionValue.RequiresApproval), decision.Decision);
        Assert.AreEqual("HUMAN_APPROVAL_REQUIRED", decision.RequirementCode);
        Assert.AreEqual(chain.ToolRequest.ToolRequestId, decision.RelatedToolRequestId);
        Assert.AreEqual(chain.Gate.ToolGateDecisionId, decision.RelatedToolGateDecisionId);
        Assert.AreEqual(chain.Approval.ApprovalDecisionId, decision.RelatedApprovalDecisionId);

        var eventType = await ExecuteScalarAsync<string>("SELECT EventType FROM governance.GovernanceEvent WHERE EventId = @id", new SqlParameter("@id", decision.GovernanceEventId));
        var payload = await ExecuteScalarAsync<string>("SELECT PayloadJson FROM governance.GovernanceEvent WHERE EventId = @id", new SqlParameter("@id", decision.GovernanceEventId));

        Assert.AreEqual("policy.decision.recorded", eventType);
        StringAssert.Contains(payload, "\"grantsApproval\":false");
        StringAssert.Contains(payload, "\"grantsExecution\":false");
        StringAssert.Contains(payload, "\"mutatesSource\":false");
        StringAssert.Contains(payload, "\"promotesMemory\":false");
        StringAssert.Contains(payload, "\"startsWorkflow\":false");
        StringAssert.Contains(payload, "\"satisfiesPolicy\":false");
        StringAssert.Contains(payload, "\"transfersAuthority\":false");

        Assert.AreEqual(0, await ExecuteScalarAsync<int>("SELECT COUNT(1) FROM governance.ApprovalDecision WHERE ApprovalDecisionId <> @id", new SqlParameter("@id", chain.Approval.ApprovalDecisionId)));
        Assert.AreEqual(1, await ExecuteScalarAsync<int>("SELECT COUNT(1) FROM governance.ToolGateDecision WHERE ToolGateDecisionId = @id", new SqlParameter("@id", chain.Gate.ToolGateDecisionId)));
    }

    [TestMethod]
    public async Task Record_AllowsGenericFutureSubjectWithoutSubjectTable()
    {
        var decision = await PolicyStore().RecordAsync(ValidDecision(Guid.NewGuid(), nameof(PolicyDecisionValue.NoPolicyBlock), "memory-promotion-request-123") with
        {
            PolicyScope = PolicyDecisionScopes.MemoryPromotion,
            SubjectType = "memory_promotion_request"
        });

        Assert.AreEqual("memory_promotion_request", decision.SubjectType);
        Assert.AreEqual("memory-promotion-request-123", decision.SubjectId);
        Assert.IsNull(decision.RelatedToolRequestId);
        Assert.IsNull(decision.RelatedToolGateDecisionId);
        Assert.IsNull(decision.RelatedApprovalDecisionId);
    }

    [TestMethod]
    public async Task Record_RejectsMissingOrCrossProjectRelatedRecords()
    {
        var projectId = Guid.NewGuid();
        var other = await CreateEvidenceChainAsync(Guid.NewGuid(), Guid.NewGuid());

        await AssertThrowsAsync<SqlException>(() => PolicyStore().RecordAsync(ValidDecision(projectId, nameof(PolicyDecisionValue.RequiresApproval), Guid.NewGuid().ToString("D")) with { RelatedToolRequestId = Guid.NewGuid() }));
        await AssertThrowsAsync<SqlException>(() => PolicyStore().RecordAsync(ValidDecision(projectId, nameof(PolicyDecisionValue.RequiresApproval), Guid.NewGuid().ToString("D")) with { RelatedToolGateDecisionId = Guid.NewGuid() }));
        await AssertThrowsAsync<SqlException>(() => PolicyStore().RecordAsync(ValidDecision(projectId, nameof(PolicyDecisionValue.RequiresApproval), Guid.NewGuid().ToString("D")) with { RelatedApprovalDecisionId = Guid.NewGuid() }));
        await AssertThrowsAsync<SqlException>(() => PolicyStore().RecordAsync(ValidDecision(projectId, nameof(PolicyDecisionValue.RequiresApproval), other.ToolRequest.ToolRequestId.ToString("D")) with { RelatedToolRequestId = other.ToolRequest.ToolRequestId }));
        await AssertThrowsAsync<SqlException>(() => PolicyStore().RecordAsync(ValidDecision(projectId, nameof(PolicyDecisionValue.RequiresApproval), other.Gate.ToolGateDecisionId.ToString("D")) with { RelatedToolGateDecisionId = other.Gate.ToolGateDecisionId }));
        await AssertThrowsAsync<SqlException>(() => PolicyStore().RecordAsync(ValidDecision(projectId, nameof(PolicyDecisionValue.RequiresApproval), other.Approval.ApprovalDecisionId.ToString("D")) with { RelatedApprovalDecisionId = other.Approval.ApprovalDecisionId }));
    }

    [TestMethod]
    public async Task QueryPaths_ReturnScopedSummariesWithoutEvidenceJson()
    {
        var store = PolicyStore();
        var projectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var subjectId = Guid.NewGuid().ToString("D");
        var first = await store.RecordAsync(ValidDecision(projectId, nameof(PolicyDecisionValue.Blocked), subjectId) with { CorrelationId = correlationId });
        var second = await store.RecordAsync(ValidDecision(projectId, nameof(PolicyDecisionValue.RequiresApproval), subjectId) with { CorrelationId = correlationId });
        _ = await store.RecordAsync(ValidDecision(otherProjectId, nameof(PolicyDecisionValue.NoPolicyBlock), subjectId) with { CorrelationId = correlationId });

        var bySubject = await store.ListForSubjectAsync(new PolicyDecisionsForSubjectQuery
        {
            ProjectId = projectId,
            PolicyScope = PolicyDecisionScopes.ToolExecution,
            SubjectType = "tool_request",
            SubjectId = subjectId
        });
        var byProject = await store.ListForProjectAsync(new PolicyDecisionsForProjectQuery { ProjectId = projectId });
        var byCorrelation = await store.ListForCorrelationAsync(new PolicyDecisionsForCorrelationQuery { ProjectId = projectId, CorrelationId = correlationId });
        var bounded = await store.ListForSubjectAsync(new PolicyDecisionsForSubjectQuery
        {
            ProjectId = projectId,
            PolicyScope = PolicyDecisionScopes.ToolExecution,
            SubjectType = "tool_request",
            SubjectId = subjectId,
            Take = 1
        });

        Assert.AreEqual(2, bySubject.Count);
        Assert.AreEqual(2, byProject.Count);
        Assert.AreEqual(2, byCorrelation.Count);
        Assert.AreEqual(1, bounded.Count);
        CollectionAssert.AreEquivalent(new[] { first.PolicyDecisionEventId, second.PolicyDecisionEventId }, bySubject.Select(row => row.PolicyDecisionEventId).ToArray());
        Assert.IsFalse(typeof(PolicyDecisionSummary).GetProperties().Any(property => property.Name.Equals("EvidenceJson", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Get_ReturnsPolicyDecisionByIdAndNullForMissing()
    {
        var decision = await PolicyStore().RecordAsync(ValidDecision(Guid.NewGuid(), nameof(PolicyDecisionValue.NoPolicyBlock), Guid.NewGuid().ToString("D")));

        var found = await PolicyStore().GetAsync(decision.PolicyDecisionEventId);
        var missing = await PolicyStore().GetAsync(Guid.NewGuid());

        Assert.IsNotNull(found);
        Assert.AreEqual(decision.PolicyDecisionEventId, found.PolicyDecisionEventId);
        Assert.IsNull(missing);
    }

    [TestMethod]
    public async Task Record_RejectsUnsafeValuesBeforeSqlWrite()
    {
        var valid = ValidDecision(Guid.NewGuid(), nameof(PolicyDecisionValue.NoPolicyBlock), Guid.NewGuid().ToString("D"));

        await AssertThrowsAsync<ArgumentException>(() => PolicyStore().RecordAsync(valid with { ProjectId = Guid.Empty }));
        await AssertThrowsAsync<ArgumentException>(() => PolicyStore().RecordAsync(valid with { PolicyScope = "" }));
        await AssertThrowsAsync<ArgumentException>(() => PolicyStore().RecordAsync(valid with { PolicyName = "" }));
        await AssertThrowsAsync<ArgumentException>(() => PolicyStore().RecordAsync(valid with { PolicyVersion = 0 }));
        await AssertThrowsAsync<ArgumentException>(() => PolicyStore().RecordAsync(valid with { SubjectType = "" }));
        await AssertThrowsAsync<ArgumentException>(() => PolicyStore().RecordAsync(valid with { SubjectId = "" }));
        await AssertThrowsAsync<ArgumentException>(() => PolicyStore().RecordAsync(valid with { Decision = "Allowed" }));
        await AssertThrowsAsync<ArgumentException>(() => PolicyStore().RecordAsync(valid with { RequirementCode = "" }));
        await AssertThrowsAsync<ArgumentException>(() => PolicyStore().RecordAsync(valid with { ReasonCode = "" }));
        await AssertThrowsAsync<ArgumentException>(() => PolicyStore().RecordAsync(valid with { DecidedByActorType = "" }));
        await AssertThrowsAsync<ArgumentException>(() => PolicyStore().RecordAsync(valid with { DecidedByActorId = "" }));
        await AssertThrowsAsync<ArgumentException>(() => PolicyStore().RecordAsync(valid with { EvidenceVersion = 0 }));
        await AssertThrowsAsync<ArgumentException>(() => PolicyStore().RecordAsync(valid with { EvidenceJson = "not-json" }));
        await AssertThrowsAsync<ArgumentException>(() => PolicyStore().RecordAsync(valid with { EvidenceJson = "{\"schema\":\"policy.decision.evidence.v1\",\"chainOfThought\":\"secret\"}" }));
        await AssertThrowsAsync<ArgumentException>(() => PolicyStore().RecordAsync(valid with { EvidenceJson = "{\"schema\":\"policy.decision.evidence.v1\",\"grantsExecution\":true}" }));

        Assert.AreEqual(0, await ExecuteScalarAsync<int>("SELECT COUNT(1) FROM governance.PolicyDecisionEvent"));
    }

    [TestMethod]
    public async Task DirectSql_MalformedPolicyDecisionRowsAreBlocked()
    {
        var projectId = Guid.NewGuid();
        var eventId = await CreateGovernanceEventAsync(projectId);

        await AssertSqlFailsAsync(DirectInsertSql("Allowed", "tool_execution", "tool_request", "subject", 1, 1, SafeEvidenceJson()), DirectInsertParameters(projectId, eventId, SafeEvidenceJson()));
        await AssertSqlFailsAsync(DirectInsertSql("NoPolicyBlock", "", "tool_request", "subject", 1, 1, SafeEvidenceJson()), DirectInsertParameters(projectId, eventId, SafeEvidenceJson()));
        await AssertSqlFailsAsync(DirectInsertSql("NoPolicyBlock", "tool_execution", "", "subject", 1, 1, SafeEvidenceJson()), DirectInsertParameters(projectId, eventId, SafeEvidenceJson()));
        await AssertSqlFailsAsync(DirectInsertSql("NoPolicyBlock", "tool_execution", "tool_request", "", 1, 1, SafeEvidenceJson()), DirectInsertParameters(projectId, eventId, SafeEvidenceJson()));
        await AssertSqlFailsAsync(DirectInsertSql("NoPolicyBlock", "tool_execution", "tool_request", "subject", 0, 1, SafeEvidenceJson()), DirectInsertParameters(projectId, eventId, SafeEvidenceJson()));
        await AssertSqlFailsAsync(DirectInsertSql("NoPolicyBlock", "tool_execution", "tool_request", "subject", 1, 0, SafeEvidenceJson()), DirectInsertParameters(projectId, eventId, SafeEvidenceJson()));
        await AssertSqlFailsAsync(DirectInsertSql("NoPolicyBlock", "tool_execution", "tool_request", "subject", 1, 1, "not-json"), DirectInsertParameters(projectId, eventId, "not-json"));
        await AssertSqlFailsAsync(DirectInsertSql("NoPolicyBlock", "tool_execution", "tool_request", "subject", 1, 1, "{\"schema\":\"policy.decision.evidence.v1\",\"grantsApproval\":true}"), DirectInsertParameters(projectId, eventId, "{\"schema\":\"policy.decision.evidence.v1\",\"grantsApproval\":true}"));
    }

    [TestMethod]
    public async Task DirectSql_UpdateAndDeleteAreBlocked()
    {
        var decision = await PolicyStore().RecordAsync(ValidDecision(Guid.NewGuid(), nameof(PolicyDecisionValue.NoPolicyBlock), Guid.NewGuid().ToString("D")));

        await AssertSqlFailsAsync("UPDATE governance.PolicyDecisionEvent SET ReasonCode = N'CHANGED' WHERE PolicyDecisionEventId = @id", new SqlParameter("@id", decision.PolicyDecisionEventId));
        await AssertSqlFailsAsync("DELETE FROM governance.PolicyDecisionEvent WHERE PolicyDecisionEventId = @id", new SqlParameter("@id", decision.PolicyDecisionEventId));
    }

    [TestMethod]
    public async Task NoPolicyBlockAndRequiresApprovalDoNotCreateAuthoritySideEffects()
    {
        var projectId = Guid.NewGuid();
        _ = await PolicyStore().RecordAsync(ValidDecision(projectId, nameof(PolicyDecisionValue.NoPolicyBlock), Guid.NewGuid().ToString("D")));
        _ = await PolicyStore().RecordAsync(ValidDecision(projectId, nameof(PolicyDecisionValue.RequiresApproval), Guid.NewGuid().ToString("D")));

        Assert.AreEqual(0, await ExecuteScalarAsync<int>("SELECT COUNT(1) FROM governance.ApprovalDecision"));
        Assert.AreEqual(0, await ExecuteScalarAsync<int>("SELECT COUNT(1) FROM governance.ToolGateDecision"));
        Assert.AreEqual(0, await CountRowsIfTableExistsAsync("governance.DogfoodReceipt"));
        Assert.AreEqual(0, await CountRowsIfTableExistsAsync("governance.WorkflowStep"));
        Assert.AreEqual(0, await CountRowsIfTableExistsAsync("governance.A2aHandoff"));
        Assert.AreEqual(0, await CountRowsIfTableExistsAsync("governance.MemoryPromotion"));
        Assert.AreEqual(0, await CountRowsIfTableExistsAsync("governance.SourceApply"));
    }

    [TestMethod]
    public void MigrationAndInventory_RegisterPolicyDecisionEventStore()
    {
        var root = FindRepositoryRoot();
        var manifest = File.ReadAllText(Path.Combine(root, "Database", "migrations.json"));
        var sql = File.ReadAllText(Path.Combine(root, "Database", "migrate_policy_decision_event.sql"));
        var inventory = File.ReadAllText(Path.Combine(root, "Database", "sql-inventory.json"));
        var verifier = File.ReadAllText(Path.Combine(root, "Database", "verify-migrations.ps1"));

        StringAssert.Contains(manifest, "Database/migrate_policy_decision_event.sql");
        StringAssert.Contains(inventory, "database.migrate-policy-decision-event");
        StringAssert.Contains(inventory, "runtime.policy-decision-event-store");
        StringAssert.Contains(verifier, "governance.PolicyDecisionEvent table");
        StringAssert.Contains(sql, "governance.usp_PolicyDecisionEvent_Record");
        StringAssert.Contains(sql, "governance.usp_PolicyDecisionEvent_ListForSubject");
        StringAssert.Contains(sql, "FK_PolicyDecisionEvent_GovernanceEvent");
        StringAssert.Contains(sql, "FK_PolicyDecisionEvent_ToolRequest");
        StringAssert.Contains(sql, "FK_PolicyDecisionEvent_ToolGateDecision");
        StringAssert.Contains(sql, "FK_PolicyDecisionEvent_ApprovalDecision");
        StringAssert.Contains(sql, "TR_PolicyDecisionEvent_BlockUpdateDelete");
        StringAssert.Contains(sql, "grantsApproval");
        StringAssert.Contains(sql, "transfersAuthority");
    }

    [TestMethod]
    public void RuntimeWiring_DoesNotExposePolicyDecisionThroughApiCliOrExecution()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Program.cs"));
        var apiControllers = Directory.GetFiles(Path.Combine(root, "IronDev.Api", "Controllers"), "*.cs").Select(File.ReadAllText).ToArray();
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "IronDevCli.cs"));
        var store = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlPolicyDecisionEventStore.cs"));
        var agents = Directory.GetFiles(Path.Combine(root, "IronDev.Core", "Agents"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText).ToArray();

        Assert.IsFalse(program.Contains("IPolicyDecisionEventStore", StringComparison.Ordinal));
        Assert.IsFalse(apiControllers.Any(text => text.Contains("IPolicyDecisionEventStore", StringComparison.Ordinal) || text.Contains("SqlPolicyDecisionEventStore", StringComparison.Ordinal)));
        Assert.IsFalse(cli.Contains("IPolicyDecisionEventStore", StringComparison.Ordinal));
        Assert.IsFalse(cli.Contains("SqlPolicyDecisionEventStore", StringComparison.Ordinal));
        Assert.IsFalse(agents.Any(text => text.Contains("IPolicyDecisionEventStore", StringComparison.Ordinal) || text.Contains("SqlPolicyDecisionEventStore", StringComparison.Ordinal)));
        StringAssert.Contains(store, "CommandType.StoredProcedure");
        AssertNoForbiddenTokens(store, "INSERT INTO governance.PolicyDecisionEvent", "UPDATE governance.PolicyDecisionEvent", "DELETE FROM governance.PolicyDecisionEvent", "CREATE TABLE", "ALTER TABLE", "ControllerBase", "WebApplication", "IHostedService", "BackgroundService", "ProcessStartInfo", "File.Copy", "File.Delete");
    }

    private IPolicyDecisionEventStore PolicyStore() =>
        new SqlPolicyDecisionEventStore(ServiceProvider.GetRequiredService<IDbConnectionFactory>());

    private IApprovalDecisionStore ApprovalStore() =>
        new SqlApprovalDecisionStore(ServiceProvider.GetRequiredService<IDbConnectionFactory>());

    private IToolGateDecisionStore GateStore() =>
        new SqlToolGateDecisionStore(ServiceProvider.GetRequiredService<IDbConnectionFactory>());

    private IToolRequestStore RequestStore() =>
        new SqlToolRequestStore(ServiceProvider.GetRequiredService<IDbConnectionFactory>());

    private async Task<(ToolRequestReadModel ToolRequest, ToolGateDecisionReadModel Gate, ApprovalDecisionReadModel Approval)> CreateEvidenceChainAsync(Guid projectId, Guid correlationId)
    {
        var toolRequest = await RequestStore().CreateAsync(new ToolRequestCreateRequest
        {
            ProjectId = projectId,
            ToolName = "workspace.apply-copy",
            OperationName = "request",
            RequestedByActorType = "agent",
            RequestedByActorId = "tester-agent",
            CorrelationId = correlationId,
            Purpose = "Create parent request for durable policy decision test.",
            RequestPayloadVersion = 1,
            RequestPayloadJson = "{\"schemaVersion\":1,\"purpose\":\"test\"}"
        });

        var gate = await GateStore().RecordAsync(new ToolGateDecisionRecordRequest(
            TenantId,
            projectId,
            toolRequest.ToolRequestId,
            nameof(ToolGateDecisionValue.RequiresApproval),
            "tool-request-gate",
            1,
            "system",
            "policy-decision-tests",
            "TEST_GATE",
            JsonSerializer.Serialize(new { schemaVersion = 1, evidence = "test" })));

        var approval = await ApprovalStore().RecordAsync(new ApprovalDecisionRecordRequest
        {
            ProjectId = projectId,
            ApprovalScope = ApprovalDecisionScopes.ToolExecution,
            SubjectType = "tool_request",
            SubjectId = toolRequest.ToolRequestId.ToString("D"),
            Decision = nameof(ApprovalDecisionValue.Approved),
            ReasonCode = "HUMAN_REVIEWED",
            Reason = "Reviewed explicit evidence only.",
            DecidedByActorType = "human",
            DecidedByActorId = "human-reviewer",
            CorrelationId = correlationId,
            CausationId = gate.GovernanceEventId,
            EvidenceVersion = 1,
            EvidenceJson = JsonSerializer.Serialize(new { schema = "approval.decision.evidence.v1", reviewedBy = "human", grantsExecution = false, mutatesSource = false, promotesMemory = false, startsWorkflow = false })
        });

        return (toolRequest, gate, approval);
    }

    private async Task<Guid> CreateGovernanceEventAsync(Guid projectId)
    {
        var eventId = Guid.NewGuid();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            @"INSERT INTO governance.GovernanceEvent
              (EventId, ProjectId, EventType, ActorType, ActorId, CorrelationId, CausationId, SubjectType, SubjectId, PayloadVersion, PayloadJson)
              VALUES (@eventId, @projectId, N'test.event', N'system', N'policy-decision-tests', NEWID(), NULL, N'test', N'test', 1, N'{""schemaVersion"":1}')",
            connection);
        command.Parameters.AddRange([
            new SqlParameter("@eventId", eventId),
            new SqlParameter("@projectId", projectId)
        ]);
        await command.ExecuteNonQueryAsync();
        return eventId;
    }

    private static PolicyDecisionRecordRequest ValidDecision(Guid projectId, string decision, string subjectId) =>
        new()
        {
            ProjectId = projectId,
            PolicyScope = PolicyDecisionScopes.ToolExecution,
            PolicyName = "tool-execution-policy",
            PolicyVersion = 1,
            SubjectType = "tool_request",
            SubjectId = subjectId,
            Decision = decision,
            RequirementCode = string.Equals(decision, nameof(PolicyDecisionValue.RequiresApproval), StringComparison.Ordinal) ? "HUMAN_APPROVAL_REQUIRED" : "NO_POLICY_BLOCK",
            ReasonCode = "POLICY_CHECK_RECORDED",
            Reason = "Recorded policy check evidence only.",
            DecidedByActorType = "system",
            DecidedByActorId = "policy-decision-tests",
            EvidenceVersion = 1,
            EvidenceJson = SafeEvidenceJson(decision)
        };

    private static string SafeEvidenceJson(string decision = nameof(PolicyDecisionValue.NoPolicyBlock)) =>
        JsonSerializer.Serialize(new
        {
            schema = "policy.decision.evidence.v1",
            inputRefs = new[] { "tool_request:test" },
            result = new { decision, requirementCode = "NO_POLICY_BLOCK" },
            grantsApproval = false,
            grantsExecution = false,
            mutatesSource = false,
            promotesMemory = false,
            startsWorkflow = false,
            satisfiesPolicy = false,
            transfersAuthority = false
        });

    private static string DirectInsertSql(string decision, string policyScope, string subjectType, string subjectId, int policyVersion, int evidenceVersion, string evidenceJson) =>
        $@"INSERT INTO governance.PolicyDecisionEvent
          (PolicyDecisionEventId, ProjectId, GovernanceEventId, PolicyScope, PolicyName, PolicyVersion, SubjectType, SubjectId, Decision, RequirementCode, ReasonCode, Reason, DecidedByActorType, DecidedByActorId, CorrelationId, CausationId, EvidenceVersion, EvidenceJson)
          VALUES (@id, @projectId, @eventId, N'{policyScope.Replace("'", "''")}', N'test-policy', {policyVersion}, N'{subjectType.Replace("'", "''")}', N'{subjectId.Replace("'", "''")}', N'{decision.Replace("'", "''")}', N'TEST_REQUIREMENT', N'DIRECT_SQL', N'Direct SQL test.', N'system', N'direct-sql', NEWID(), @eventId, {evidenceVersion}, @evidenceJson)";

    private static SqlParameter[] DirectInsertParameters(Guid projectId, Guid eventId, string evidenceJson) =>
    [
        new SqlParameter("@id", Guid.NewGuid()),
        new SqlParameter("@projectId", projectId),
        new SqlParameter("@eventId", eventId),
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
            @"IF OBJECT_ID(N'governance.usp_PolicyDecisionEvent_Record', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PolicyDecisionEvent_Record;
              IF OBJECT_ID(N'governance.usp_PolicyDecisionEvent_GetById', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PolicyDecisionEvent_GetById;
              IF OBJECT_ID(N'governance.usp_PolicyDecisionEvent_ListForSubject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PolicyDecisionEvent_ListForSubject;
              IF OBJECT_ID(N'governance.usp_PolicyDecisionEvent_ListForProject', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PolicyDecisionEvent_ListForProject;
              IF OBJECT_ID(N'governance.usp_PolicyDecisionEvent_ListForCorrelation', N'P') IS NOT NULL DROP PROCEDURE governance.usp_PolicyDecisionEvent_ListForCorrelation;
              IF OBJECT_ID(N'governance.TR_PolicyDecisionEvent_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_PolicyDecisionEvent_ValidateInsert;
              IF OBJECT_ID(N'governance.TR_PolicyDecisionEvent_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_PolicyDecisionEvent_BlockUpdateDelete;
              IF OBJECT_ID(N'governance.PolicyDecisionEvent', N'U') IS NOT NULL DROP TABLE governance.PolicyDecisionEvent;
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

    private async Task<int> CountRowsIfTableExistsAsync(string tableName)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            $"IF OBJECT_ID(N'{tableName}', N'U') IS NULL SELECT 0 ELSE SELECT COUNT(1) FROM {tableName}",
            connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
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
