using System.Data;
using Dapper;
using IronDev.Core.Agents;
using IronDev.Data;
using IronDev.Infrastructure.Governance;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("AgentHandoffStore")]
[TestCategory("RealDatabaseAgentHandoffSmoke")]
public sealed class AgentHandoffStoreTests : IntegrationTestBase
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-aaaa-4444-8888-111111111111");
    private static readonly Guid OtherProjectId = Guid.Parse("22222222-aaaa-4444-8888-222222222222");
    private static readonly Guid CorrelationId = Guid.Parse("33333333-aaaa-4444-8888-333333333333");
    private static readonly Guid OtherCorrelationId = Guid.Parse("44444444-aaaa-4444-8888-444444444444");
    private static readonly Guid CausationId = Guid.Parse("55555555-aaaa-4444-8888-555555555555");

    private SqlAgentHandoffStore _store = default!;
    private SqlGovernanceEventStore _governanceEvents = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropA2aAndGovernanceSchemaAsync();
        await ApplyGovernanceMigrationsAsync();

        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        _store = new SqlAgentHandoffStore(connectionFactory);
        _governanceEvents = new SqlGovernanceEventStore(connectionFactory);
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropA2aAndGovernanceSchemaAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public void AgentHandoffStore_ExposesRecordOnlyContractWithoutDeliveryOrAuthorityMethods()
    {
        var methods = typeof(IAgentHandoffStore).GetMethods().Select(method => method.Name).OrderBy(name => name).ToArray();
        CollectionAssert.AreEquivalent(
            new[] { "CreateAsync", "GetAsync", "ListByCorrelationAsync", "ListByProjectAsync", "ListBySubjectAsync" },
            methods);

        AssertNoForbiddenTokens(string.Join("\n", methods), "UpdateStatus", "MarkOffered", "MarkReceived", "Accept", "Dispatch", "Send", "Receive", "Execute", "Approve", "ContinueWorkflow", "Promote", "Apply");
    }

    [TestMethod]
    public async Task AgentHandoffMigration_AddsA2aSchemaTablesProceduresTriggersAndConstraints()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN SCHEMA_ID(N'a2a') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'a2a.AgentHandoff', N'U') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'a2a.AgentHandoffEvidenceReference', N'U') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'a2a.AgentHandoffEvidenceAllowedUse', N'U') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'a2a.AgentHandoffConstraint', N'U') IS NULL THEN 0 ELSE 1 END"));

        var procedures = (await connection.QueryAsync<string>("SELECT name FROM sys.procedures WHERE schema_id = SCHEMA_ID(N'a2a')")).ToArray();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "usp_AgentHandoff_Create",
                "usp_AgentHandoff_Get",
                "usp_AgentHandoff_ListByProject",
                "usp_AgentHandoff_ListByCorrelation",
                "usp_AgentHandoff_ListBySubject"
            },
            procedures);

        var triggers = (await connection.QueryAsync<string>("SELECT name FROM sys.triggers WHERE parent_class_desc = N'OBJECT_OR_COLUMN' AND OBJECT_SCHEMA_NAME(object_id) = N'a2a'")).ToArray();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "TR_AgentHandoff_ValidateInsert",
                "TR_AgentHandoff_BlockUpdateDelete",
                "TR_AgentHandoffEvidenceReference_BlockUpdateDelete",
                "TR_AgentHandoffEvidenceAllowedUse_BlockUpdateDelete",
                "TR_AgentHandoffConstraint_BlockUpdateDelete"
            },
            triggers);

        var constraints = (await connection.QueryAsync<string>("SELECT name FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'a2a.AgentHandoff')")).ToArray();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "CK_AgentHandoff_NoApprovalGrant",
                "CK_AgentHandoff_NoExecutionGrant",
                "CK_AgentHandoff_NoSourceMutation",
                "CK_AgentHandoff_NoMemoryPromotion",
                "CK_AgentHandoff_NoWorkflowStart",
                "CK_AgentHandoff_NoPolicySatisfaction",
                "CK_AgentHandoff_NoAuthorityTransfer"
            },
            constraints);
    }

    [TestMethod]
    public async Task AgentHandoffStore_CreatesReadsAndPreservesEvidenceAllowedUsesConstraintsAndCorrelation()
    {
        var created = await _store.CreateAsync(ValidRequest());

        Assert.AreNotEqual(Guid.Empty, created.AgentHandoffId);
        Assert.AreEqual(ProjectId, created.ProjectId);
        Assert.AreEqual(AgentHandoffType.EvidenceTransfer, created.HandoffType);
        Assert.AreEqual(AgentHandoffStatus.ReadyForReview, created.Status);
        Assert.AreEqual("planner-agent", created.SourceAgent.AgentId);
        Assert.AreEqual("builder-agent", created.TargetAgent.AgentId);
        Assert.AreEqual(AgentHandoffSubjectType.ToolRequest, created.Subject.SubjectType);
        Assert.AreEqual("tool-request-1", created.Subject.SubjectId);
        Assert.AreEqual(CorrelationId, created.CorrelationId);
        Assert.AreEqual(CausationId, created.CausationId);
        Assert.IsFalse(created.GrantsApproval);
        Assert.IsFalse(created.GrantsExecution);
        Assert.IsFalse(created.MutatesSource);
        Assert.IsFalse(created.PromotesMemory);
        Assert.IsFalse(created.StartsWorkflow);
        Assert.IsFalse(created.SatisfiesPolicy);
        Assert.IsFalse(created.TransfersAuthority);

        Assert.AreEqual(4, created.EvidenceReferences.Count);
        CollectionAssert.Contains(created.EvidenceReferences.Select(reference => reference.EvidenceType).ToArray(), AgentHandoffEvidenceType.ToolGateDecision);
        CollectionAssert.Contains(created.EvidenceReferences.Select(reference => reference.EvidenceType).ToArray(), AgentHandoffEvidenceType.DogfoodReceipt);
        CollectionAssert.Contains(created.EvidenceReferences.Select(reference => reference.EvidenceType).ToArray(), AgentHandoffEvidenceType.ApprovalDecision);
        CollectionAssert.Contains(created.EvidenceReferences.Select(reference => reference.EvidenceType).ToArray(), AgentHandoffEvidenceType.CriticReview);
        CollectionAssert.Contains(created.EvidenceReferences.SelectMany(reference => reference.AllowedUses).ToArray(), AgentHandoffEvidenceAllowedUse.HumanDecisionSupport);

        Assert.AreEqual(3, created.Constraints.Count);
        CollectionAssert.Contains(created.Constraints.Select(constraint => constraint.ConstraintType).ToArray(), AgentHandoffConstraintType.EvidenceOnly);
        CollectionAssert.Contains(created.Constraints.Select(constraint => constraint.ConstraintType).ToArray(), AgentHandoffConstraintType.DoNotExecute);

        var read = await _store.GetAsync(ProjectId, created.AgentHandoffId);
        Assert.IsNotNull(read);
        Assert.AreEqual(created.AgentHandoffId, read.AgentHandoffId);
        Assert.AreEqual(created.EvidenceReferences.Count, read.EvidenceReferences.Count);
        Assert.AreEqual(created.Constraints.Count, read.Constraints.Count);

        var governanceEvent = await _governanceEvents.GetAsync(await ScalarAsync<Guid>("SELECT GovernanceEventId FROM a2a.AgentHandoff WHERE AgentHandoffId = @id", new { id = created.AgentHandoffId }));
        Assert.IsNotNull(governanceEvent);
        Assert.AreEqual("a2a.handoff.recorded", governanceEvent.EventType);
        Assert.AreEqual("agent_handoff", governanceEvent.SubjectType);
        Assert.AreEqual(created.AgentHandoffId.ToString(), governanceEvent.SubjectId, ignoreCase: true);
        StringAssert.Contains(governanceEvent.PayloadJson, "\"grantsApproval\":false");
        StringAssert.Contains(governanceEvent.PayloadJson, "\"grantsExecution\":false");
        StringAssert.Contains(governanceEvent.PayloadJson, "\"mutatesSource\":false");
        StringAssert.Contains(governanceEvent.PayloadJson, "\"promotesMemory\":false");
        StringAssert.Contains(governanceEvent.PayloadJson, "\"startsWorkflow\":false");
        StringAssert.Contains(governanceEvent.PayloadJson, "\"satisfiesPolicy\":false");
        StringAssert.Contains(governanceEvent.PayloadJson, "\"transfersAuthority\":false");
    }

    [TestMethod]
    public async Task AgentHandoffStore_ListsByProjectCorrelationAndSubjectWithoutCrossProjectLeakage()
    {
        var first = await _store.CreateAsync(ValidRequest() with { CorrelationId = CorrelationId, Subject = Subject("tool-request-A") });
        var second = await _store.CreateAsync(ValidRequest() with { CorrelationId = CorrelationId, Subject = Subject("tool-request-A") });
        _ = await _store.CreateAsync(ValidRequest(OtherProjectId) with { CorrelationId = CorrelationId, Subject = Subject("tool-request-A") });
        _ = await _store.CreateAsync(ValidRequest() with { CorrelationId = OtherCorrelationId, Subject = Subject("tool-request-B") });

        var byProject = await _store.ListByProjectAsync(ProjectId, 10);
        var byCorrelation = await _store.ListByCorrelationAsync(ProjectId, CorrelationId, 10);
        var bySubject = await _store.ListBySubjectAsync(ProjectId, nameof(AgentHandoffSubjectType.ToolRequest), "tool-request-A", 10);

        Assert.AreEqual(3, byProject.Count);
        Assert.AreEqual(2, byCorrelation.Count);
        Assert.AreEqual(2, bySubject.Count);
        Assert.IsTrue(byProject.All(summary => summary.ProjectId == ProjectId));
        CollectionAssert.AreEquivalent(new[] { first.AgentHandoffId, second.AgentHandoffId }, bySubject.Select(summary => summary.AgentHandoffId).ToArray());
        Assert.IsTrue(bySubject.All(summary => summary.EvidenceReferenceCount == 4));
        Assert.IsTrue(bySubject.All(summary => summary.ConstraintCount == 3));
    }

    [TestMethod]
    public async Task AgentHandoffStore_RejectsInvalidAuthorityTransferAndHiddenReasoningBeforePersistence()
    {
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { ProjectId = Guid.Empty }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { MetadataJson = "{not-json}" }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { MetadataJson = "{\"schema\":\"agent.handoff.metadata.v1\",\"canExecute\":true}" }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { MetadataJson = "{\"schema\":\"agent.handoff.metadata.v1\",\"hiddenReasoning\":\"nope\"}" }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { EvidenceReferences = [Evidence(AgentHandoffEvidenceType.ToolGateDecision, AgentHandoffEvidenceAllowedUse.Review) with { EvidenceSummary = "execution permission" }] }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { Constraints = [Constraint(AgentHandoffConstraintType.EvidenceOnly) with { Description = "source apply permission" }] }));

        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM a2a.AgentHandoff"));
        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.GovernanceEvent"));
    }

    [TestMethod]
    public async Task AgentHandoffSql_RejectsDirectAuthorityFlagsHiddenReasoningCrossProjectEvidenceUpdateAndDelete()
    {
        await ExpectThrowsAsync<SqlException>(() => DirectRecordProcedureAsync(Guid.NewGuid(), Guid.NewGuid(), metadataJson: SafeMetadataJson(authority: true)));
        await ExpectThrowsAsync<SqlException>(() => DirectRecordProcedureAsync(Guid.NewGuid(), Guid.NewGuid(), metadataJson: "{\"schema\":\"agent.handoff.metadata.v1\",\"chainOfThought\":\"nope\"}"));

        var otherEventId = await CreateGovernanceEventAsync(OtherProjectId);
        await ExpectThrowsAsync<SqlException>(() => DirectRecordProcedureAsync(Guid.NewGuid(), Guid.NewGuid(), evidenceGovernanceEventId: otherEventId));

        var created = await _store.CreateAsync(ValidRequest());
        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync("UPDATE a2a.AgentHandoff SET Status = N'Received' WHERE AgentHandoffId = @id", new { id = created.AgentHandoffId }));
        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync("DELETE FROM a2a.AgentHandoff WHERE AgentHandoffId = @id", new { id = created.AgentHandoffId }));
        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync("UPDATE a2a.AgentHandoffEvidenceReference SET EvidenceSummary = N'changed' WHERE AgentHandoffId = @id", new { id = created.AgentHandoffId }));
        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync("DELETE FROM a2a.AgentHandoffConstraint WHERE AgentHandoffId = @id", new { id = created.AgentHandoffId }));
    }

    [TestMethod]
    public async Task AgentHandoffStore_DoesNotCreateApprovalPolicyDogfoodWorkflowSourceApplyMemoryOrRuntimeSideEffects()
    {
        _ = await _store.CreateAsync(ValidRequest());

        Assert.AreEqual(0, await CountIfExistsAsync("governance.ApprovalDecision"));
        Assert.AreEqual(0, await CountIfExistsAsync("governance.PolicyDecisionEvent"));
        Assert.AreEqual(0, await CountIfExistsAsync("governance.DogfoodReceipt"));
        Assert.AreEqual(0, await CountIfExistsAsync("governance.ToolGateDecision"));
        Assert.AreEqual(0, await CountIfExistsAsync("governance.ToolRequest"));
        Assert.AreEqual(0, await CountIfExistsAsync("agent.CollectiveMemoryItem"));
        Assert.AreEqual(0, await CountIfExistsAsync("toolaudit.ToolExecutionAuditRecord"));
    }

    [TestMethod]
    public void AgentHandoffStore_StaticBoundary_DoesNotAddApiCliRuntimeTransportWorkflowOrExecutionPaths()
    {
        var root = RepositoryRoot();
        var storeText = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlAgentHandoffStore.cs"));
        var migrationText = File.ReadAllText(Path.Combine(root, "Database", "migrate_agent_handoff.sql"));
        var apiText = string.Join("\n", Directory.EnumerateFiles(Path.Combine(root, "IronDev.Api"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
        var cliText = string.Join("\n", Directory.EnumerateFiles(Path.Combine(root, "tools"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));

        StringAssert.Contains(storeText, "CommandType.StoredProcedure");
        StringAssert.Contains(storeText, "AgentHandoffAuthorityTransferValidator");
        StringAssert.Contains(migrationText, "a2a.usp_AgentHandoff_Create");
        StringAssert.Contains(migrationText, "a2a.handoff.recorded");

        AssertNoForbiddenTokens(storeText,
            "ControllerBase", "WebApplication", "IHostedService", "BackgroundService", "HttpClient", "ProcessStartInfo", "File.Copy", "File.Delete", "LangGraph", "MessageBus", "QueueClient", "Inbox", "Outbox", "IAgentToolExecutor", "PromoteCollectiveMemory", "ApplySource");
        AssertNoForbiddenTokens(apiText, "IAgentHandoffStore", "SqlAgentHandoffStore");
        AssertNoForbiddenTokens(cliText, "IAgentHandoffStore", "SqlAgentHandoffStore");
    }

    private static AgentHandoffCreateRequest ValidRequest(Guid? projectId = null) =>
        new()
        {
            ProjectId = projectId ?? ProjectId,
            HandoffType = AgentHandoffType.EvidenceTransfer,
            Status = AgentHandoffStatus.ReadyForReview,
            SourceAgent = new AgentHandoffParticipant
            {
                AgentId = "planner-agent",
                AgentRole = AgentHandoffParticipantRole.Planner,
                DisplayName = "Planner Agent"
            },
            TargetAgent = new AgentHandoffParticipant
            {
                AgentId = "builder-agent",
                AgentRole = AgentHandoffParticipantRole.Builder,
                DisplayName = "Builder Agent"
            },
            Subject = Subject("tool-request-1"),
            EvidenceReferences =
            [
                Evidence(AgentHandoffEvidenceType.ToolGateDecision, AgentHandoffEvidenceAllowedUse.Context, AgentHandoffEvidenceAllowedUse.Review, AgentHandoffEvidenceAllowedUse.Traceability),
                Evidence(AgentHandoffEvidenceType.DogfoodReceipt, AgentHandoffEvidenceAllowedUse.Validation, AgentHandoffEvidenceAllowedUse.Traceability),
                Evidence(AgentHandoffEvidenceType.ApprovalDecision, AgentHandoffEvidenceAllowedUse.HumanDecisionSupport, AgentHandoffEvidenceAllowedUse.Traceability),
                Evidence(AgentHandoffEvidenceType.CriticReview, AgentHandoffEvidenceAllowedUse.Review, AgentHandoffEvidenceAllowedUse.HumanDecisionSupport)
            ],
            Constraints =
            [
                Constraint(AgentHandoffConstraintType.EvidenceOnly),
                Constraint(AgentHandoffConstraintType.DoNotExecute),
                Constraint(AgentHandoffConstraintType.DoNotContinueWorkflow)
            ],
            CorrelationId = CorrelationId,
            CausationId = CausationId,
            CreatedByActorType = "system_test_fixture",
            CreatedByActorId = "agent-handoff-store-tests",
            MetadataVersion = 1,
            MetadataJson = SafeMetadataJson()
        };

    private static AgentHandoffSubject Subject(string subjectId) =>
        new()
        {
            SubjectType = AgentHandoffSubjectType.ToolRequest,
            SubjectId = subjectId,
            ActionName = "ReviewEvidence",
            Summary = "Evidence package for target-agent review."
        };

    private static AgentHandoffEvidenceReference Evidence(AgentHandoffEvidenceType type, params AgentHandoffEvidenceAllowedUse[] allowedUses) =>
        new()
        {
            EvidenceType = type,
            EvidenceId = $"{type}-1",
            EvidenceLabel = $"{type} evidence",
            EvidenceSummary = $"{type} is cited only as evidence.",
            AllowedUses = allowedUses,
            GovernanceEventId = null
        };

    private static AgentHandoffConstraint Constraint(AgentHandoffConstraintType type) =>
        new()
        {
            ConstraintType = type,
            ConstraintCode = type.ToString(),
            Description = "This handoff transfers context and evidence only."
        };

    private static string SafeMetadataJson(bool authority = false) =>
        authority
            ? "{\"schema\":\"agent.handoff.metadata.v1\",\"grantsExecution\":true}"
            : "{\"schema\":\"agent.handoff.metadata.v1\",\"notes\":\"Evidence package for review.\",\"grantsApproval\":false,\"grantsExecution\":false,\"mutatesSource\":false,\"promotesMemory\":false,\"startsWorkflow\":false,\"satisfiesPolicy\":false,\"transfersAuthority\":false}";

    private async Task DirectRecordProcedureAsync(Guid handoffId, Guid governanceEventId, string? metadataJson = null, Guid? evidenceGovernanceEventId = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            EXEC a2a.usp_AgentHandoff_Create
                @AgentHandoffId = @AgentHandoffId,
                @ProjectId = @ProjectId,
                @GovernanceEventId = @GovernanceEventId,
                @HandoffType = N'EvidenceTransfer',
                @Status = N'ReadyForReview',
                @SourceAgentId = N'planner-agent',
                @SourceAgentRole = N'Planner',
                @SourceAgentDisplayName = N'Planner Agent',
                @TargetAgentId = N'builder-agent',
                @TargetAgentRole = N'Builder',
                @TargetAgentDisplayName = N'Builder Agent',
                @SubjectType = N'ToolRequest',
                @SubjectId = N'tool-request-direct',
                @SubjectActionName = N'ReviewEvidence',
                @SubjectSummary = N'Evidence package for target-agent review.',
                @CorrelationId = @CorrelationId,
                @CausationId = @CausationId,
                @SupersedesHandoffId = NULL,
                @CreatedByActorType = N'system_test_fixture',
                @CreatedByActorId = N'direct-agent-handoff-test',
                @MetadataVersion = 1,
                @MetadataJson = @MetadataJson,
                @EvidenceReferencesJson = @EvidenceReferencesJson,
                @ConstraintsJson = @ConstraintsJson,
                @GovernanceEventPayloadJson = @GovernanceEventPayloadJson,
                @CreatedUtc = NULL;
            """,
            new
            {
                AgentHandoffId = handoffId,
                ProjectId,
                GovernanceEventId = governanceEventId,
                CorrelationId,
                CausationId,
                MetadataJson = metadataJson ?? SafeMetadataJson(),
                EvidenceReferencesJson = EvidenceJson(evidenceGovernanceEventId),
                ConstraintsJson = ConstraintsJson(),
                GovernanceEventPayloadJson = "{\"schema\":\"a2a.handoff.recorded.v1\",\"grantsApproval\":false,\"grantsExecution\":false,\"mutatesSource\":false,\"promotesMemory\":false,\"startsWorkflow\":false,\"satisfiesPolicy\":false,\"transfersAuthority\":false}"
            });
    }

    private static string EvidenceJson(Guid? governanceEventId) =>
        governanceEventId.HasValue
            ? $"[{{\"evidenceType\":\"ToolGateDecision\",\"EvidenceId\":\"ToolGateDecision-1\",\"EvidenceLabel\":\"ToolGateDecision evidence\",\"EvidenceSummary\":\"ToolGateDecision is cited only as evidence.\",\"GovernanceEventId\":\"{governanceEventId}\",\"allowedUses\":[\"Review\",\"Traceability\"]}}]"
            : "[{\"evidenceType\":\"ToolGateDecision\",\"EvidenceId\":\"ToolGateDecision-1\",\"EvidenceLabel\":\"ToolGateDecision evidence\",\"EvidenceSummary\":\"ToolGateDecision is cited only as evidence.\",\"allowedUses\":[\"Review\",\"Traceability\"]}]";

    private static string ConstraintsJson() =>
        "[{\"constraintType\":\"EvidenceOnly\",\"ConstraintCode\":\"EvidenceOnly\",\"Description\":\"This handoff transfers context and evidence only.\"}]";

    private async Task<Guid> CreateGovernanceEventAsync(Guid projectId)
    {
        var eventId = Guid.NewGuid();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            EXEC governance.AppendGovernanceEvent
                @EventId = @EventId,
                @ProjectId = @ProjectId,
                @EventType = N'test.evidence.recorded',
                @ActorType = N'system_test_fixture',
                @ActorId = N'agent-handoff-tests',
                @CorrelationId = NULL,
                @CausationId = NULL,
                @SubjectType = N'test_evidence',
                @SubjectId = N'test-evidence-1',
                @PayloadVersion = 1,
                @PayloadJson = N'{"schema":"test.evidence.v1"}';
            """,
            new { EventId = eventId, ProjectId = projectId });
        return eventId;
    }

    private async Task ApplyGovernanceMigrationsAsync()
    {
        await ApplySqlFileAsync("Database", "migrate_governance_event.sql");
        await ApplySqlFileAsync("Database", "migrate_tool_request.sql");
        await ApplySqlFileAsync("Database", "migrate_tool_gate_decision.sql");
        await ApplySqlFileAsync("Database", "migrate_approval_decision.sql");
        await ApplySqlFileAsync("Database", "migrate_policy_decision_event.sql");
        await ApplySqlFileAsync("Database", "migrate_dogfood_receipt.sql");
        await ApplySqlFileAsync("Database", "migrate_thoughtledger_governance_event_reference.sql");
        await ApplySqlFileAsync("Database", "migrate_agent_handoff.sql");
    }

    private async Task ApplySqlFileAsync(params string[] pathParts)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), Path.Combine(pathParts)));
        foreach (var batch in SplitSqlBatches(sql))
            await connection.ExecuteAsync(batch);
    }

    private async Task DropA2aAndGovernanceSchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(DropA2aSql + DropGovernanceSql);
    }

    private async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(sql, parameters);
    }

    private async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<T>(sql, parameters) ?? throw new InvalidOperationException("Scalar returned null.");
    }

    private async Task<int> CountIfExistsAsync(string schemaAndTable)
    {
        var parts = schemaAndTable.Split('.');
        if (parts.Length != 2)
            throw new ArgumentException("Expected schema.table.", nameof(schemaAndTable));

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var exists = await connection.ExecuteScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(@ObjectName, N'U') IS NULL THEN 0 ELSE 1 END", new { ObjectName = schemaAndTable });
        if (exists == 0)
            return 0;

        return await connection.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM {parts[0]}.{parts[1]}");
    }

    private static IReadOnlyList<string> SplitSqlBatches(string sql) =>
        System.Text.RegularExpressions.Regex.Split(
                sql.Replace("\r\n", "\n", StringComparison.Ordinal),
                @"(?im)^\s*GO\s*$")
            .Select(batch => batch.Trim())
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToArray();

    private static async Task ExpectThrowsAsync<TException>(Func<Task> action)
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

    private static void AssertNoForbiddenTokens(string text, params string[] forbidden)
    {
        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Unexpected forbidden token found: {token}");
    }

    private const string DropA2aSql = """
        IF OBJECT_ID(N'a2a.usp_AgentHandoff_Create', N'P') IS NOT NULL DROP PROCEDURE a2a.usp_AgentHandoff_Create;
        IF OBJECT_ID(N'a2a.usp_AgentHandoff_Get', N'P') IS NOT NULL DROP PROCEDURE a2a.usp_AgentHandoff_Get;
        IF OBJECT_ID(N'a2a.usp_AgentHandoff_ListByProject', N'P') IS NOT NULL DROP PROCEDURE a2a.usp_AgentHandoff_ListByProject;
        IF OBJECT_ID(N'a2a.usp_AgentHandoff_ListByCorrelation', N'P') IS NOT NULL DROP PROCEDURE a2a.usp_AgentHandoff_ListByCorrelation;
        IF OBJECT_ID(N'a2a.usp_AgentHandoff_ListBySubject', N'P') IS NOT NULL DROP PROCEDURE a2a.usp_AgentHandoff_ListBySubject;
        IF OBJECT_ID(N'a2a.TR_AgentHandoffEvidenceAllowedUse_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER a2a.TR_AgentHandoffEvidenceAllowedUse_BlockUpdateDelete;
        IF OBJECT_ID(N'a2a.TR_AgentHandoffEvidenceAllowedUse_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER a2a.TR_AgentHandoffEvidenceAllowedUse_ValidateInsert;
        IF OBJECT_ID(N'a2a.TR_AgentHandoffConstraint_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER a2a.TR_AgentHandoffConstraint_BlockUpdateDelete;
        IF OBJECT_ID(N'a2a.TR_AgentHandoffConstraint_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER a2a.TR_AgentHandoffConstraint_ValidateInsert;
        IF OBJECT_ID(N'a2a.TR_AgentHandoffEvidenceReference_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER a2a.TR_AgentHandoffEvidenceReference_BlockUpdateDelete;
        IF OBJECT_ID(N'a2a.TR_AgentHandoffEvidenceReference_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER a2a.TR_AgentHandoffEvidenceReference_ValidateInsert;
        IF OBJECT_ID(N'a2a.TR_AgentHandoff_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER a2a.TR_AgentHandoff_BlockUpdateDelete;
        IF OBJECT_ID(N'a2a.TR_AgentHandoff_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER a2a.TR_AgentHandoff_ValidateInsert;
        IF OBJECT_ID(N'a2a.AgentHandoffEvidenceAllowedUse', N'U') IS NOT NULL DROP TABLE a2a.AgentHandoffEvidenceAllowedUse;
        IF OBJECT_ID(N'a2a.AgentHandoffConstraint', N'U') IS NOT NULL DROP TABLE a2a.AgentHandoffConstraint;
        IF OBJECT_ID(N'a2a.AgentHandoffEvidenceReference', N'U') IS NOT NULL DROP TABLE a2a.AgentHandoffEvidenceReference;
        IF OBJECT_ID(N'a2a.AgentHandoff', N'U') IS NOT NULL DROP TABLE a2a.AgentHandoff;
        IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'a2a') DROP SCHEMA a2a;
        """;

    private const string DropGovernanceSql = """
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
        IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IronDevGovernanceEventRuntimeRole' AND type = N'R') DROP ROLE IronDevGovernanceEventRuntimeRole;
        IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance') DROP SCHEMA governance;
        """;

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
