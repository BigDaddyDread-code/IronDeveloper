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
[TestCategory("ThoughtLedgerGovernanceReference")]
public sealed class ThoughtLedgerGovernanceReferenceStoreTests : IntegrationTestBase
{
    private static readonly Guid ProjectId = Guid.Parse("ace00000-1111-4444-8888-000000000001");
    private static readonly Guid OtherProjectId = Guid.Parse("ace00000-1111-4444-8888-000000000002");
    private static readonly Guid CorrelationId = Guid.Parse("ace00000-1111-4444-8888-000000000003");
    private const string ThoughtLedgerEntryId = "thought-run-abc-review-001";

    private SqlGovernanceEventStore _eventStore = default!;
    private SqlThoughtLedgerGovernanceEventReferenceStore _store = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropGovernanceSchemaAsync();
        await ApplyGovernanceMigrationsAsync();

        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        _eventStore = new SqlGovernanceEventStore(connectionFactory);
        _store = new SqlThoughtLedgerGovernanceEventReferenceStore(connectionFactory);
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropGovernanceSchemaAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public void Contracts_ExposeEvidenceReferenceLedgerWithoutAuthorityMethodsOrOutcomes()
    {
        CollectionAssert.AreEquivalent(
            new[] { "Observed", "Explains", "Supports", "Cites", "CausedBy", "RelatedEvidence" },
            Enum.GetNames<ThoughtLedgerGovernanceReferenceType>());

        AssertNoForbiddenNames(
            Enum.GetNames<ThoughtLedgerGovernanceReferenceType>(),
            "Approves",
            "Authorizes",
            "Executes",
            "GrantsPermission",
            "SatisfiesPolicy",
            "PromotesMemory",
            "AppliesSource",
            "Releases",
            "Overrides",
            "Owns",
            "TransfersAuthority");

        var methods = typeof(IThoughtLedgerGovernanceEventReferenceStore).GetMethods().Select(method => method.Name).OrderBy(name => name).ToArray();
        CollectionAssert.AreEquivalent(
            new[] { "GetAsync", "ListForCorrelationAsync", "ListForGovernanceEventAsync", "ListForThoughtLedgerEntryAsync", "RecordAsync" },
            methods);
        AssertNoForbiddenNames(methods, "ApproveAsync", "AuthorizeAsync", "ExecuteAsync", "ApplyAsync", "PromoteAsync", "ContinueWorkflowAsync", "SatisfyPolicyAsync", "TransferAuthorityAsync");

        var summaryProperties = typeof(ThoughtLedgerGovernanceEventReferenceSummary).GetProperties().Select(property => property.Name).ToArray();
        CollectionAssert.DoesNotContain(summaryProperties, "MetadataJson");
        AssertNoForbiddenNames(summaryProperties, "ApprovalGranted", "ExecutionAllowed", "SourceApplied", "MemoryPromoted", "PolicySatisfied", "WorkflowStarted", "ReleaseApproved");
    }

    [TestMethod]
    public async Task Migration_CreatesReferenceTableProceduresTriggersAndConstraints()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'governance.ThoughtLedgerGovernanceEventReference', N'U') IS NULL THEN 0 ELSE 1 END"));

        var procedures = (await connection.QueryAsync<string>("SELECT name FROM sys.procedures WHERE schema_id = SCHEMA_ID(N'governance')")).ToArray();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "usp_ThoughtLedgerGovernanceEventReference_Record",
                "usp_ThoughtLedgerGovernanceEventReference_GetById",
                "usp_ThoughtLedgerGovernanceEventReference_ListForThoughtLedgerEntry",
                "usp_ThoughtLedgerGovernanceEventReference_ListForGovernanceEvent",
                "usp_ThoughtLedgerGovernanceEventReference_ListForCorrelation"
            },
            procedures);

        var constraints = (await connection.QueryAsync<string>("SELECT name FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'governance.ThoughtLedgerGovernanceEventReference')")).ToArray();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "CK_ThoughtLedgerGovernanceEventReference_ReferenceType_Allowed",
                "CK_ThoughtLedgerGovernanceEventReference_MetadataJson_IsJson",
                "CK_ThoughtLedgerGovernanceEventReference_MetadataJson_Versioned",
                "CK_ThoughtLedgerGovernanceEventReference_MetadataVersion_Positive"
            },
            constraints);

        var foreignKeys = (await connection.QueryAsync<string>("SELECT name FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'governance.ThoughtLedgerGovernanceEventReference')")).ToArray();
        CollectionAssert.Contains(foreignKeys, "FK_ThoughtLedgerGovernanceEventReference_GovernanceEvent");

        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'governance.TR_ThoughtLedgerGovernanceEventReference_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'governance.TR_ThoughtLedgerGovernanceEventReference_ValidateInsert', N'TR') IS NULL THEN 0 ELSE 1 END"));
    }

    [TestMethod]
    public async Task Record_PersistsReferenceToExistingGovernanceEventWithoutCreatingNewGovernanceEvent()
    {
        var governanceEvent = await AppendGovernanceEventAsync();
        var beforeEvents = await ScalarAsync<int>("SELECT COUNT(1) FROM governance.GovernanceEvent");

        var reference = await _store.RecordAsync(ValidRequest(governanceEvent) with
        {
            ReferenceType = " observed ",
            ThoughtLedgerEntryId = " thought-run-abc-review-001 "
        });

        Assert.AreEqual(ProjectId, reference.ProjectId);
        Assert.AreEqual(ThoughtLedgerEntryId, reference.ThoughtLedgerEntryId);
        Assert.AreEqual(governanceEvent.EventId, reference.GovernanceEventId);
        Assert.AreEqual("Observed", reference.ReferenceType);
        Assert.AreEqual("LEDGER_CITES_EVENT", reference.ReasonCode);
        Assert.AreEqual(CorrelationId, reference.CorrelationId);
        Assert.AreEqual("system_test_fixture", reference.CreatedByActorType);
        Assert.AreEqual("reference-test", reference.CreatedByActorId);
        Assert.AreEqual(SafeMetadataJson(), reference.MetadataJson);

        var read = await _store.GetAsync(reference.ThoughtLedgerGovernanceEventReferenceId);
        Assert.IsNotNull(read);
        Assert.AreEqual(reference.ThoughtLedgerGovernanceEventReferenceId, read.ThoughtLedgerGovernanceEventReferenceId);
        Assert.AreEqual(SafeMetadataJson(), read.MetadataJson);

        Assert.AreEqual(beforeEvents, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.GovernanceEvent"));
    }

    [TestMethod]
    public async Task Record_RejectsMissingGovernanceEventAndCrossProjectGovernanceEvent()
    {
        await ExpectThrowsAsync<SqlException>(() => _store.RecordAsync(ValidRequestForMissingEvent()));

        var otherEvent = await AppendGovernanceEventAsync(OtherProjectId);
        await ExpectThrowsAsync<SqlException>(() => _store.RecordAsync(ValidRequest(otherEvent) with { ProjectId = ProjectId }));

        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.ThoughtLedgerGovernanceEventReference"));
    }

    [TestMethod]
    public async Task Record_StoresThoughtLedgerEntryIdExactlyWithoutInventingLedgerTable()
    {
        var governanceEvent = await AppendGovernanceEventAsync();
        var entryId = "thought-run-42-critic-finding:001";

        var reference = await _store.RecordAsync(ValidRequest(governanceEvent) with { ThoughtLedgerEntryId = entryId });

        Assert.AreEqual(entryId, reference.ThoughtLedgerEntryId);
        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'governance') AND name IN (N'ThoughtLedger', N'ThoughtLedgerEntry')"));
    }

    [TestMethod]
    public async Task QueryPaths_ReturnScopedSummariesWithoutMetadataJson()
    {
        var firstEvent = await AppendGovernanceEventAsync(subjectId: "subject-one");
        var secondEvent = await AppendGovernanceEventAsync(subjectId: "subject-two");
        var otherProjectEvent = await AppendGovernanceEventAsync(OtherProjectId, subjectId: "subject-three");

        var first = await _store.RecordAsync(ValidRequest(firstEvent) with { ReferenceType = nameof(ThoughtLedgerGovernanceReferenceType.Supports) });
        var second = await _store.RecordAsync(ValidRequest(secondEvent) with { ReferenceType = nameof(ThoughtLedgerGovernanceReferenceType.Cites) });
        _ = await _store.RecordAsync(ValidRequest(otherProjectEvent, OtherProjectId) with { ThoughtLedgerEntryId = ThoughtLedgerEntryId });

        var byEntry = await _store.ListForThoughtLedgerEntryAsync(new ThoughtLedgerGovernanceReferencesForThoughtLedgerEntryQuery { ProjectId = ProjectId, ThoughtLedgerEntryId = ThoughtLedgerEntryId });
        var byEvent = await _store.ListForGovernanceEventAsync(new ThoughtLedgerGovernanceReferencesForGovernanceEventQuery { ProjectId = ProjectId, GovernanceEventId = firstEvent.EventId });
        var byCorrelation = await _store.ListForCorrelationAsync(new ThoughtLedgerGovernanceReferencesForCorrelationQuery { ProjectId = ProjectId, CorrelationId = CorrelationId });
        var bounded = await _store.ListForThoughtLedgerEntryAsync(new ThoughtLedgerGovernanceReferencesForThoughtLedgerEntryQuery { ProjectId = ProjectId, ThoughtLedgerEntryId = ThoughtLedgerEntryId, Take = 1 });

        Assert.AreEqual(2, byEntry.Count);
        Assert.AreEqual(1, byEvent.Count);
        Assert.AreEqual(first.ThoughtLedgerGovernanceEventReferenceId, byEvent.Single().ThoughtLedgerGovernanceEventReferenceId);
        Assert.AreEqual(2, byCorrelation.Count);
        Assert.AreEqual(1, bounded.Count);
        CollectionAssert.AreEquivalent(new[] { first.ThoughtLedgerGovernanceEventReferenceId, second.ThoughtLedgerGovernanceEventReferenceId }, byEntry.Select(item => item.ThoughtLedgerGovernanceEventReferenceId).ToArray());
        Assert.IsFalse(typeof(ThoughtLedgerGovernanceEventReferenceSummary).GetProperties().Any(property => property.Name.Equals("MetadataJson", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Record_RejectsUnsafeInputsBeforeSqlWrite()
    {
        var governanceEvent = await AppendGovernanceEventAsync();
        var valid = ValidRequest(governanceEvent);

        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { ProjectId = Guid.Empty }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { ThoughtLedgerEntryId = " " }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { GovernanceEventId = Guid.Empty }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { ReferenceType = "Approves" }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { ReferenceType = "Unknown" }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { ReasonCode = " " }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { Reason = "grants execution permission" }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { CreatedByActorType = " " }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { CreatedByActorId = " " }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { MetadataVersion = 0 }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { MetadataJson = "not-json" }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { MetadataJson = "{\"schema\":\"thoughtledger.reference.v1\",\"grantsExecution\":true}" }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.RecordAsync(valid with { MetadataJson = "{\"schema\":\"thoughtledger.reference.v1\",\"chainOfThought\":\"secret\"}" }));

        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.ThoughtLedgerGovernanceEventReference"));
    }

    [TestMethod]
    public async Task StoredProcedureAndSqlBoundary_BlockUnsafeMetadataAndMutation()
    {
        var governanceEvent = await AppendGovernanceEventAsync();

        await ExpectThrowsAsync<SqlException>(() => DirectRecordProcedureAsync(governanceEvent.EventId, "Approves", SafeMetadataJson()));
        await ExpectThrowsAsync<SqlException>(() => DirectRecordProcedureAsync(governanceEvent.EventId, "Observed", "{\"schema\":\"thoughtledger.reference.v1\",\"grantsApproval\":true}"));
        await ExpectThrowsAsync<SqlException>(() => DirectRecordProcedureAsync(governanceEvent.EventId, "Observed", "{\"schema\":\"thoughtledger.reference.v1\",\"chainOfThought\":\"secret\"}"));

        var reference = await _store.RecordAsync(ValidRequest(governanceEvent));

        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync("UPDATE governance.ThoughtLedgerGovernanceEventReference SET ReasonCode = N'changed' WHERE ThoughtLedgerGovernanceEventReferenceId = @id", new { id = reference.ThoughtLedgerGovernanceEventReferenceId }));
        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync("DELETE FROM governance.ThoughtLedgerGovernanceEventReference WHERE ThoughtLedgerGovernanceEventReferenceId = @id", new { id = reference.ThoughtLedgerGovernanceEventReferenceId }));
    }

    [TestMethod]
    public async Task Reference_DoesNotCreateApprovalPolicyGateDogfoodOrOtherAuthoritySideEffects()
    {
        var governanceEvent = await AppendGovernanceEventAsync();

        _ = await _store.RecordAsync(ValidRequest(governanceEvent));

        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.ApprovalDecision"));
        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.PolicyDecisionEvent"));
        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.ToolGateDecision"));
        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.ToolRequest"));
        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.DogfoodReceipt"));
    }

    [TestMethod]
    public void RuntimeStore_UsesStoredProceduresOnlyAndNoAuthorityRuntimePaths()
    {
        var root = RepositoryRoot();
        var storeText = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlThoughtLedgerGovernanceEventReferenceStore.cs"));
        var migrationText = File.ReadAllText(Path.Combine(root, "Database", "migrate_thoughtledger_governance_event_reference.sql"));

        StringAssert.Contains(storeText, "governance.usp_ThoughtLedgerGovernanceEventReference_Record");
        StringAssert.Contains(storeText, "governance.usp_ThoughtLedgerGovernanceEventReference_GetById");
        StringAssert.Contains(migrationText, "FK_ThoughtLedgerGovernanceEventReference_GovernanceEvent");

        AssertNoForbiddenNames(
            [storeText],
            "INSERT INTO governance.ThoughtLedgerGovernanceEventReference",
            "UPDATE governance.ThoughtLedgerGovernanceEventReference",
            "DELETE FROM governance.ThoughtLedgerGovernanceEventReference",
            "CREATE TABLE governance.ThoughtLedgerGovernanceEventReference",
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

    private async Task<GovernanceEvent> AppendGovernanceEventAsync(Guid? projectId = null, string subjectId = "thought-source") =>
        await _eventStore.AppendAsync(new GovernanceEventAppendRequest
        {
            ProjectId = projectId ?? ProjectId,
            EventType = "thoughtledger.reference.source",
            ActorType = "system_test_fixture",
            ActorId = "reference-test",
            CorrelationId = CorrelationId,
            SubjectType = "thought_ledger_entry",
            SubjectId = subjectId,
            PayloadVersion = 1,
            PayloadJson = "{\"schema\":\"thoughtledger.reference.source.v1\",\"schemaVersion\":1,\"source\":\"test\"}"
        });

    private ThoughtLedgerGovernanceEventReferenceRecordRequest ValidRequest(GovernanceEvent governanceEvent, Guid? projectId = null) => new()
    {
        ProjectId = projectId ?? governanceEvent.ProjectId,
        ThoughtLedgerEntryId = ThoughtLedgerEntryId,
        GovernanceEventId = governanceEvent.EventId,
        ReferenceType = nameof(ThoughtLedgerGovernanceReferenceType.Observed),
        ReasonCode = "LEDGER_CITES_EVENT",
        Reason = "Links a visible ThoughtLedger entry to durable governance evidence.",
        CorrelationId = CorrelationId,
        CreatedByActorType = "system_test_fixture",
        CreatedByActorId = "reference-test",
        MetadataVersion = 1,
        MetadataJson = SafeMetadataJson()
    };

    private ThoughtLedgerGovernanceEventReferenceRecordRequest ValidRequestForMissingEvent() => new()
    {
        ProjectId = ProjectId,
        ThoughtLedgerEntryId = ThoughtLedgerEntryId,
        GovernanceEventId = Guid.NewGuid(),
        ReferenceType = nameof(ThoughtLedgerGovernanceReferenceType.Observed),
        ReasonCode = "LEDGER_CITES_EVENT",
        Reason = "Links a visible ThoughtLedger entry to durable governance evidence.",
        CorrelationId = CorrelationId,
        CreatedByActorType = "system_test_fixture",
        CreatedByActorId = "reference-test",
        MetadataVersion = 1,
        MetadataJson = SafeMetadataJson()
    };

    private static string SafeMetadataJson() =>
        "{\"schema\":\"thoughtledger.governance_event_reference.v1\",\"schemaVersion\":1,\"source\":\"test\",\"grantsApproval\":false,\"grantsExecution\":false,\"mutatesSource\":false,\"promotesMemory\":false,\"startsWorkflow\":false,\"satisfiesPolicy\":false,\"transfersAuthority\":false}";

    private async Task DirectRecordProcedureAsync(Guid governanceEventId, string referenceType, string metadataJson)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(
            "governance.usp_ThoughtLedgerGovernanceEventReference_Record",
            new
            {
                ThoughtLedgerGovernanceEventReferenceId = Guid.NewGuid(),
                ProjectId,
                ThoughtLedgerEntryId,
                GovernanceEventId = governanceEventId,
                ReferenceType = referenceType,
                ReasonCode = "DIRECT_SQL_TEST",
                Reason = "Direct SQL boundary test.",
                CorrelationId,
                CausationId = (Guid?)null,
                CreatedByActorType = "system_test_fixture",
                CreatedByActorId = "direct-sql-test",
                MetadataVersion = 1,
                MetadataJson = metadataJson,
                CreatedUtc = (DateTime?)null
            },
            commandType: CommandType.StoredProcedure);
    }

    private async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
    }

    private async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<T>(sql, parameters) ?? throw new InvalidOperationException("Scalar query returned null.");
    }

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

        Assert.Fail($"Expected {typeof(TException).Name}.");
    }

    private static void AssertNoForbiddenNames(IEnumerable<string> values, params string[] forbidden)
    {
        var combined = string.Join("\n", values);
        foreach (var forbiddenValue in forbidden)
        {
            Assert.IsFalse(
                combined.Contains(forbiddenValue, StringComparison.OrdinalIgnoreCase),
                $"Unexpected forbidden value '{forbiddenValue}'.");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
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

    private async Task DropGovernanceSchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
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
        IF OBJECT_ID(N'governance.TR_ToolRequest_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_ToolRequest_BlockUpdateDelete;
        IF OBJECT_ID(N'governance.ToolRequest', N'U') IS NOT NULL DROP TABLE governance.ToolRequest;
        IF OBJECT_ID(N'governance.AppendGovernanceEvent', N'P') IS NOT NULL DROP PROCEDURE governance.AppendGovernanceEvent;
        IF OBJECT_ID(N'governance.GetGovernanceEvent', N'P') IS NOT NULL DROP PROCEDURE governance.GetGovernanceEvent;
        IF OBJECT_ID(N'governance.ListGovernanceEventsForProject', N'P') IS NOT NULL DROP PROCEDURE governance.ListGovernanceEventsForProject;
        IF OBJECT_ID(N'governance.ListGovernanceEventsForCorrelation', N'P') IS NOT NULL DROP PROCEDURE governance.ListGovernanceEventsForCorrelation;
        IF OBJECT_ID(N'governance.ListGovernanceEventsForSubject', N'P') IS NOT NULL DROP PROCEDURE governance.ListGovernanceEventsForSubject;
        IF OBJECT_ID(N'governance.ListGovernanceEventsCausedBy', N'P') IS NOT NULL DROP PROCEDURE governance.ListGovernanceEventsCausedBy;
        IF OBJECT_ID(N'governance.TR_GovernanceEvent_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_GovernanceEvent_BlockUpdateDelete;
        IF OBJECT_ID(N'governance.GovernanceEvent', N'U') IS NOT NULL DROP TABLE governance.GovernanceEvent;
        IF SCHEMA_ID(N'governance') IS NOT NULL DROP SCHEMA governance;
        """;
}
