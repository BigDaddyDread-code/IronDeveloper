using System.Text.Json;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;
using IronDev.Infrastructure.Governance;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("RequiresRealDatabase")]
[TestCategory("LongRunning")]
[TestClass]
[TestCategory("DogfoodReceiptStore")]
public sealed class DogfoodReceiptStoreTests : IntegrationTestBase
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-1111-4444-8888-aaaaaaaaaaaa");
    private static readonly Guid OtherProjectId = Guid.Parse("bbbbbbbb-1111-4444-8888-bbbbbbbbbbbb");
    private static readonly Guid CorrelationId = Guid.Parse("cccccccc-1111-4444-8888-cccccccccccc");
    private SqlDogfoodReceiptStore _store = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropGovernanceSchemaAsync();
        await ApplyGovernanceMigrationsAsync();

        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        _store = new SqlDogfoodReceiptStore(connectionFactory);
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropGovernanceSchemaAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public void Contracts_ExposeReceiptLedgerWithoutAuthorityMethodsOrOutcomes()
    {
        CollectionAssert.AreEquivalent(
            new[] { "Passed", "Failed", "Partial", "Inconclusive", "NotRun" },
            Enum.GetNames<DogfoodReceiptOutcome>());

        var forbiddenOutcomeNames = new[]
        {
            "Approved", "ReleaseApproved", "ReadyToRelease", "ReleaseReady", "Authorized", "Accepted", "Promoted", "Certified", "CanShip"
        };
        foreach (var forbidden in forbiddenOutcomeNames)
            CollectionAssert.DoesNotContain(Enum.GetNames<DogfoodReceiptOutcome>(), forbidden);

        var methods = typeof(IDogfoodReceiptStore).GetMethods().Select(method => method.Name).OrderBy(name => name).ToArray();
        CollectionAssert.AreEquivalent(
            new[] { "GetAsync", "ListForCorrelationAsync", "ListForProjectAsync", "ListForSubjectAsync", "RecordAsync" },
            methods);
        AssertNoForbiddenNames(methods, "ApproveAsync", "AuthorizeAsync", "ExecuteAsync", "ApplyAsync", "PromoteAsync", "ContinueWorkflowAsync", "SatisfyPolicyAsync");

        var summaryProperties = typeof(DogfoodReceiptSummary).GetProperties().Select(property => property.Name).ToArray();
        CollectionAssert.DoesNotContain(summaryProperties, "EvidenceJson");
        AssertNoForbiddenNames(summaryProperties, "ReleaseApproved", "ExecutionAllowed", "SourceApplied", "MemoryPromoted", "PolicySatisfied", "WorkflowStarted");
    }

    [TestMethod]
    public async Task Migration_CreatesDogfoodReceiptTableProceduresTriggersAndConstraints()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'governance.DogfoodReceipt', N'U') IS NULL THEN 0 ELSE 1 END"));

        var procedures = (await connection.QueryAsync<string>("SELECT name FROM sys.procedures WHERE schema_id = SCHEMA_ID(N'governance')")).ToArray();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "usp_DogfoodReceipt_Record",
                "usp_DogfoodReceipt_GetById",
                "usp_DogfoodReceipt_ListForSubject",
                "usp_DogfoodReceipt_ListForProject",
                "usp_DogfoodReceipt_ListForCorrelation"
            },
            procedures);

        var constraints = (await connection.QueryAsync<string>("SELECT name FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.DogfoodReceipt')")).ToArray();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "CK_DogfoodReceipt_Outcome_Allowed",
                "CK_DogfoodReceipt_Outcome_NotAuthority",
                "CK_DogfoodReceipt_EvidenceJson_IsJson",
                "CK_DogfoodReceipt_EvidenceJson_Versioned"
            },
            constraints);
    }

    [TestMethod]
    public async Task Record_PersistsDogfoodReceiptAndGovernanceEventWithoutAuthoritySideEffects()
    {
        var receipt = await _store.RecordAsync(ValidRequest() with
        {
            Outcome = "Passed",
            Summary = "Durable dogfood evidence for human review."
        });

        Assert.AreEqual(ProjectId, receipt.ProjectId);
        Assert.AreEqual("Passed", receipt.Outcome);
        Assert.AreEqual("dogfood_loop", receipt.SubjectType);
        Assert.AreEqual("dogfood-loop-123", receipt.SubjectId);
        Assert.AreEqual(CorrelationId, receipt.CorrelationId);
        Assert.AreEqual("DOGFOOD_LOOP_RECORDED", receipt.SummaryCode);

        var read = await _store.GetAsync(receipt.DogfoodReceiptId);
        Assert.IsNotNull(read);
        Assert.AreEqual(receipt.DogfoodReceiptId, read.DogfoodReceiptId);
        Assert.AreEqual(SafeEvidenceJson(), read.EvidenceJson);

        var eventType = await ScalarAsync<string>("SELECT EventType FROM governance.GovernanceEvent WHERE EventId = @id", new { id = receipt.GovernanceEventId });
        var payload = await ScalarAsync<string>("SELECT PayloadJson FROM governance.GovernanceEvent WHERE EventId = @id", new { id = receipt.GovernanceEventId });

        Assert.AreEqual("dogfood.receipt.recorded", eventType);
        StringAssert.Contains(payload, "\"approvesRelease\":false");
        StringAssert.Contains(payload, "\"grantsApproval\":false");
        StringAssert.Contains(payload, "\"grantsExecution\":false");
        StringAssert.Contains(payload, "\"mutatesSource\":false");
        StringAssert.Contains(payload, "\"promotesMemory\":false");
        StringAssert.Contains(payload, "\"startsWorkflow\":false");
        StringAssert.Contains(payload, "\"satisfiesPolicy\":false");

        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.ApprovalDecision"));
        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.PolicyDecisionEvent"));
        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.ToolGateDecision"));
        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.ToolRequest"));
    }

    [TestMethod]
    public async Task Record_AllowsGenericFutureSubjectsWithoutSubjectTable()
    {
        var receipt = await _store.RecordAsync(ValidRequest() with
        {
            ReceiptType = "release_readiness_dogfood",
            SubjectType = "release_readiness_package",
            SubjectId = "release-package-123",
            Outcome = nameof(DogfoodReceiptOutcome.Partial)
        });

        Assert.AreEqual("release_readiness_package", receipt.SubjectType);
        Assert.AreEqual("release-package-123", receipt.SubjectId);
        Assert.AreEqual("Partial", receipt.Outcome);
        Assert.IsNull(receipt.RelatedToolRequestId);
        Assert.IsNull(receipt.RelatedToolGateDecisionId);
        Assert.IsNull(receipt.RelatedApprovalDecisionId);
        Assert.IsNull(receipt.RelatedPolicyDecisionEventId);
    }

    [TestMethod]
    public async Task QueryPaths_ReturnScopedSummariesWithoutEvidenceJson()
    {
        var first = await _store.RecordAsync(ValidRequest() with { SubjectId = "dogfood-loop-A", CorrelationId = CorrelationId, Outcome = nameof(DogfoodReceiptOutcome.Passed) });
        var second = await _store.RecordAsync(ValidRequest() with { SubjectId = "dogfood-loop-A", CorrelationId = CorrelationId, Outcome = nameof(DogfoodReceiptOutcome.Failed) });
        _ = await _store.RecordAsync(ValidRequest(OtherProjectId) with { SubjectId = "dogfood-loop-A", CorrelationId = CorrelationId });

        var bySubject = await _store.ListForSubjectAsync(new DogfoodReceiptsForSubjectQuery
        {
            ProjectId = ProjectId,
            ReceiptType = "dogfood_loop",
            SubjectType = "dogfood_loop",
            SubjectId = "dogfood-loop-A"
        });
        var byProject = await _store.ListForProjectAsync(new DogfoodReceiptsForProjectQuery { ProjectId = ProjectId });
        var byCorrelation = await _store.ListForCorrelationAsync(new DogfoodReceiptsForCorrelationQuery { ProjectId = ProjectId, CorrelationId = CorrelationId });
        var bounded = await _store.ListForProjectAsync(new DogfoodReceiptsForProjectQuery { ProjectId = ProjectId, Take = 1 });

        Assert.AreEqual(2, bySubject.Count);
        Assert.AreEqual(2, byProject.Count);
        Assert.AreEqual(2, byCorrelation.Count);
        Assert.AreEqual(1, bounded.Count);
        CollectionAssert.AreEquivalent(new[] { first.DogfoodReceiptId, second.DogfoodReceiptId }, bySubject.Select(item => item.DogfoodReceiptId).ToArray());
        Assert.IsFalse(typeof(DogfoodReceiptSummary).GetProperties().Any(property => property.Name.Equals("EvidenceJson", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Record_RejectsUnsafeInputsBeforeSqlWrite()
    {
        var valid = ValidRequest();

        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { ProjectId = Guid.Empty }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { ReceiptType = " " }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { SubjectType = " " }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { SubjectId = " " }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { Outcome = "Approved" }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { Outcome = "ReadyToRelease" }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { SummaryCode = " " }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { RecordedByActorType = " " }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { RecordedByActorId = " " }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { EvidenceVersion = 0 }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { EvidenceJson = "not-json" }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { EvidenceJson = "{\"schema\":\"dogfood.receipt.evidence.v1\",\"grantsExecution\":true}" }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { EvidenceJson = "{\"schema\":\"dogfood.receipt.evidence.v1\",\"rawPrompt\":\"secret\"}" }));

        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.DogfoodReceipt"));
        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.GovernanceEvent"));
    }

    [TestMethod]
    public async Task StoredProcedureAndSqlBoundary_BlockUnsafeEvidenceMissingRelatedRecordsAndMutation()
    {
        await ExpectThrowsAsync<SqlException>(() => DirectRecordProcedureAsync(Guid.NewGuid(), Guid.NewGuid(), SafeEvidenceJson(), relatedToolRequestId: Guid.NewGuid()));
        await ExpectThrowsAsync<SqlException>(() => DirectRecordProcedureAsync(Guid.NewGuid(), Guid.NewGuid(), "{\"schema\":\"dogfood.receipt.evidence.v1\",\"schemaVersion\":1,\"grantsApproval\":true}"));
        await ExpectThrowsAsync<SqlException>(() => DirectRecordProcedureAsync(Guid.NewGuid(), Guid.NewGuid(), "{\"schema\":\"dogfood.receipt.evidence.v1\",\"schemaVersion\":1,\"chainOfThought\":\"secret\"}"));

        var receipt = await _store.RecordAsync(ValidRequest());

        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync("UPDATE governance.DogfoodReceipt SET Summary = N'changed' WHERE DogfoodReceiptId = @id", new { id = receipt.DogfoodReceiptId }));
        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync("DELETE FROM governance.DogfoodReceipt WHERE DogfoodReceiptId = @id", new { id = receipt.DogfoodReceiptId }));
    }

    [TestMethod]
    public void RuntimeStore_UsesStoredProceduresOnlyAndNoAuthorityRuntimePaths()
    {
        var root = RepositoryRoot();
        var storeText = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlDogfoodReceiptStore.cs"));
        var apiStoreText = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Controllers", "SqlDogfoodLoopApiStore.cs"));
        var combined = storeText + apiStoreText;

        StringAssert.Contains(storeText, "governance.usp_DogfoodReceipt_Record");
        StringAssert.Contains(storeText, "governance.usp_DogfoodReceipt_GetById");
        StringAssert.Contains(apiStoreText, "IDogfoodReceiptStore");

        AssertNoForbiddenNames(
            [combined],
            "INSERT INTO governance.DogfoodReceipt",
            "UPDATE governance.DogfoodReceipt",
            "DELETE FROM governance.DogfoodReceipt",
            "CREATE TABLE governance.DogfoodReceipt",
            "IWorkflow",
            "IA2A",
            "ApplySource",
            "PromoteMemory",
            "ReleaseApproved",
            "ExecuteTool");
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
    }

    private DogfoodReceiptRecordRequest ValidRequest(Guid? projectId = null) => new()
    {
        ProjectId = projectId ?? ProjectId,
        ReceiptType = "dogfood_loop",
        SubjectType = "dogfood_loop",
        SubjectId = "dogfood-loop-123",
        Outcome = nameof(DogfoodReceiptOutcome.Inconclusive),
        SummaryCode = "DOGFOOD_LOOP_RECORDED",
        Summary = "Dogfood receipt evidence for human review.",
        RecordedByActorType = "system_test_fixture",
        RecordedByActorId = "dogfood-receipt-tests",
        CorrelationId = CorrelationId,
        EvidenceVersion = 1,
        EvidenceJson = SafeEvidenceJson()
    };

    private static string SafeEvidenceJson() =>
        "{\"schema\":\"dogfood.receipt.evidence.v1\",\"schemaVersion\":1,\"approvesRelease\":false,\"grantsApproval\":false,\"grantsExecution\":false,\"satisfiesPolicy\":false,\"mutatesSource\":false,\"promotesMemory\":false,\"startsWorkflow\":false,\"transfersAuthority\":false}";

    private async Task DirectRecordProcedureAsync(Guid receiptId, Guid eventId, string evidenceJson, Guid? relatedToolRequestId = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            EXEC governance.usp_DogfoodReceipt_Record
                @DogfoodReceiptId = @ReceiptId,
                @GovernanceEventId = @EventId,
                @ProjectId = @ProjectId,
                @ReceiptType = N'dogfood_loop',
                @SubjectType = N'dogfood_loop',
                @SubjectId = N'dogfood-loop-direct',
                @Outcome = N'Passed',
                @SummaryCode = N'DIRECT_SQL_RECEIPT',
                @Summary = N'Direct stored procedure test.',
                @RecordedByActorType = N'system_test_fixture',
                @RecordedByActorId = N'direct-sql-test',
                @RelatedToolRequestId = @RelatedToolRequestId,
                @RelatedToolGateDecisionId = NULL,
                @RelatedApprovalDecisionId = NULL,
                @RelatedPolicyDecisionEventId = NULL,
                @CorrelationId = @CorrelationId,
                @CausationId = NULL,
                @EvidenceVersion = 1,
                @EvidenceJson = @EvidenceJson,
                @CreatedUtc = NULL;
            """,
            new
            {
                ReceiptId = receiptId,
                EventId = eventId,
                ProjectId,
                CorrelationId,
                RelatedToolRequestId = relatedToolRequestId,
                EvidenceJson = evidenceJson
            });
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
        return await connection.ExecuteScalarAsync<T>(sql, parameters)
            ?? throw new InvalidOperationException("Scalar query returned null.");
    }


    private async Task ApplySqlFileAsync(params string[] pathParts)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), Path.Combine(pathParts)));
        foreach (var batch in SplitSqlBatches(sql))
            await connection.ExecuteAsync(batch);
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

    private async Task DropGovernanceSchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(DropGovernanceSql);
    }

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
        IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'governance') DROP SCHEMA governance;
        """;

    private static void AssertNoForbiddenNames(IEnumerable<string> values, params string[] forbidden)
    {
        foreach (var value in values)
        foreach (var token in forbidden)
            Assert.IsFalse(value.Contains(token, StringComparison.OrdinalIgnoreCase), $"Unexpected token {token} in {value}");
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

        throw new InvalidOperationException("Could not find repository root.");
    }
}
