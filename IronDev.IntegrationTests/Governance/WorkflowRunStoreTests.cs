using Dapper;
using IronDev.Core.Workflow;
using IronDev.Data;
using IronDev.Infrastructure.Workflow;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowRunStore")]
[TestCategory("RealDatabaseWorkflowRunSmoke")]
public sealed class WorkflowRunStoreTests : IntegrationTestBase
{
    private static readonly Guid ProjectId = Guid.Parse("10000000-aaaa-4444-8888-100000000001");
    private static readonly Guid OtherProjectId = Guid.Parse("10000000-aaaa-4444-8888-100000000002");
    private static readonly Guid CorrelationId = Guid.Parse("10000000-aaaa-4444-8888-100000000003");
    private static readonly Guid OtherCorrelationId = Guid.Parse("10000000-aaaa-4444-8888-100000000004");
    private static readonly Guid CausationId = Guid.Parse("10000000-aaaa-4444-8888-100000000005");
    private static readonly Guid GroundingReferenceId = Guid.Parse("10000000-aaaa-4444-8888-100000000006");

    private SqlWorkflowRunStore _store = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropWorkflowSchemaAsync();
        await ApplySqlFileAsync("Database", "migrate_workflow_run.sql");

        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        _store = new SqlWorkflowRunStore(connectionFactory);
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropWorkflowSchemaAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public void WorkflowRunStore_ExposesDurableRecordContractWithoutRunnerAuthorityMethods()
    {
        var methods = typeof(IWorkflowRunStore).GetMethods().Select(method => method.Name).OrderBy(name => name).ToArray();
        CollectionAssert.AreEquivalent(
            new[] { "CreateAsync", "GetAsync", "ListByCorrelationAsync", "ListByProjectAsync", "ListBySubjectAsync" },
            methods);

        AssertNoForbiddenTokens(
            string.Join("\n", methods),
            "Start",
            "Continue",
            "Dispatch",
            "Send",
            "Receive",
            "Execute",
            "RunAgent",
            "Approve",
            "SatisfyPolicy",
            "ApplySource",
            "PromoteMemory",
            "TransferAuthority");
    }

    [TestMethod]
    public async Task WorkflowRunMigration_AddsWorkflowSchemaTablesProceduresTriggersAndConstraints()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN SCHEMA_ID(N'workflow') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.WorkflowRun', N'U') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.WorkflowRunStep', N'U') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.WorkflowRunEvidenceReference', N'U') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.WorkflowRunGroundingReference', N'U') IS NULL THEN 0 ELSE 1 END"));

        var procedures = (await connection.QueryAsync<string>("SELECT name FROM sys.procedures WHERE schema_id = SCHEMA_ID(N'workflow')")).ToArray();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "usp_WorkflowRun_Create",
                "usp_WorkflowRun_Get",
                "usp_WorkflowRun_ListByProject",
                "usp_WorkflowRun_ListByCorrelation",
                "usp_WorkflowRun_ListBySubject"
            },
            procedures);

        var triggers = (await connection.QueryAsync<string>("SELECT name FROM sys.triggers WHERE parent_class_desc = N'OBJECT_OR_COLUMN' AND OBJECT_SCHEMA_NAME(object_id) = N'workflow'")).ToArray();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "TR_WorkflowRun_ValidateInsert",
                "TR_WorkflowRun_BlockUpdateDelete",
                "TR_WorkflowRunStep_ValidateInsert",
                "TR_WorkflowRunStep_BlockUpdateDelete",
                "TR_WorkflowRunEvidenceReference_ValidateInsert",
                "TR_WorkflowRunEvidenceReference_BlockUpdateDelete",
                "TR_WorkflowRunGroundingReference_ValidateInsert",
                "TR_WorkflowRunGroundingReference_BlockUpdateDelete"
            },
            triggers);

        var runConstraints = (await connection.QueryAsync<string>("SELECT name FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'workflow.WorkflowRun')")).ToArray();
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "CK_WorkflowRun_NoApprovalGrant",
                "CK_WorkflowRun_NoExecutionGrant",
                "CK_WorkflowRun_NoSourceMutation",
                "CK_WorkflowRun_NoMemoryPromotion",
                "CK_WorkflowRun_NoWorkflowStart",
                "CK_WorkflowRun_NoWorkflowContinuation",
                "CK_WorkflowRun_NoPolicySatisfaction",
                "CK_WorkflowRun_NoAuthorityTransfer",
                "CK_WorkflowRun_NoReleaseApproval",
                "CK_WorkflowRun_NoAcceptedMemory"
            },
            runConstraints);
    }

    [TestMethod]
    public async Task WorkflowRunStore_CreatesReadsAndPreservesStepsEvidenceGroundingAndCorrelation()
    {
        var created = await _store.CreateAsync(ValidRequest());

        Assert.AreNotEqual(Guid.Empty, created.WorkflowRunId);
        Assert.AreEqual(ProjectId, created.ProjectId);
        Assert.AreEqual("ManualDogfoodLoop", created.WorkflowType);
        Assert.AreEqual(WorkflowRunStatus.Created, created.Status);
        Assert.AreEqual("dogfood_receipt", created.SubjectType);
        Assert.AreEqual("receipt-pr98", created.SubjectId);
        Assert.AreEqual(CorrelationId, created.CorrelationId);
        Assert.AreEqual(CausationId, created.CausationId);
        AssertNoAuthority(created);
        Assert.AreEqual(2, created.Steps.Count);
        Assert.AreEqual(3, created.EvidenceReferences.Count);
        Assert.AreEqual(1, created.GroundingReferences.Count);
        CollectionAssert.Contains(created.EvidenceReferences.Select(reference => reference.EvidenceType).ToArray(), WorkflowRunEvidenceType.DogfoodReceipt);
        CollectionAssert.Contains(created.EvidenceReferences.Select(reference => reference.EvidenceType).ToArray(), WorkflowRunEvidenceType.AgentHandoff);
        CollectionAssert.Contains(created.EvidenceReferences.Select(reference => reference.EvidenceType).ToArray(), WorkflowRunEvidenceType.GroundingEvidenceReference);
        CollectionAssert.Contains(created.EvidenceReferences.Select(reference => reference.AllowedUse).ToArray(), WorkflowRunEvidenceAllowedUse.HumanDecisionSupport);

        var read = await _store.GetAsync(ProjectId, created.WorkflowRunId);
        Assert.IsNotNull(read);
        Assert.AreEqual(created.WorkflowRunId, read.WorkflowRunId);
        Assert.AreEqual(2, read.Steps.Count);
        Assert.AreEqual(3, read.EvidenceReferences.Count);
        Assert.AreEqual(1, read.GroundingReferences.Count);
        AssertNoAuthority(read);
    }

    [TestMethod]
    public async Task WorkflowRunStore_ListsByProjectCorrelationAndSubjectWithoutCrossProjectLeakage()
    {
        var first = await _store.CreateAsync(ValidRequest() with { SubjectId = "receipt-A", CorrelationId = CorrelationId });
        var second = await _store.CreateAsync(ValidRequest() with { SubjectId = "receipt-A", CorrelationId = CorrelationId });
        _ = await _store.CreateAsync(ValidRequest(OtherProjectId) with { SubjectId = "receipt-A", CorrelationId = CorrelationId });
        _ = await _store.CreateAsync(ValidRequest() with { SubjectId = "receipt-B", CorrelationId = OtherCorrelationId });

        var byProject = await _store.ListByProjectAsync(ProjectId, 10);
        var byCorrelation = await _store.ListByCorrelationAsync(ProjectId, CorrelationId, 10);
        var bySubject = await _store.ListBySubjectAsync(ProjectId, "dogfood_receipt", "receipt-A", 10);

        Assert.AreEqual(3, byProject.Count);
        Assert.AreEqual(2, byCorrelation.Count);
        Assert.AreEqual(2, bySubject.Count);
        Assert.IsTrue(byProject.All(summary => summary.ProjectId == ProjectId));
        CollectionAssert.AreEquivalent(new[] { first.WorkflowRunId, second.WorkflowRunId }, bySubject.Select(summary => summary.WorkflowRunId).ToArray());
        Assert.IsTrue(bySubject.All(summary => summary.StepCount == 2));
        Assert.IsTrue(bySubject.All(summary => summary.EvidenceReferenceCount == 3));
        Assert.IsTrue(bySubject.All(summary => summary.GroundingReferenceCount == 1));
    }

    [TestMethod]
    public async Task WorkflowRunStore_RejectsInvalidPrivateReasoningAuthorityLanguageAndAuthorityFlagsBeforePersistence()
    {
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { ProjectId = Guid.Empty }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { WorkflowType = "UnknownWorkflow" }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { MetadataJson = "{not-json}" }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { MetadataJson = "{\"schema\":\"workflow.run.metadata.v1\",\"executionAllowed\":true}" }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { MetadataJson = "{\"schema\":\"workflow.run.metadata.v1\",\"hiddenReasoning\":\"blocked\"}" }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { SubjectSummary = "approval granted" }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { GrantsApproval = true }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { GrantsExecution = true }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { MutatesSource = true }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { PromotesMemory = true }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { StartsWorkflow = true }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { ContinuesWorkflow = true }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { SatisfiesPolicy = true }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { TransfersAuthority = true }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { ApprovesRelease = true }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { CreatesAcceptedMemory = true }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { EvidenceReferences = [Evidence("review", WorkflowRunEvidenceType.DogfoodReceipt, "dogfood-1") with { SafeSummary = "tool executed" }] }));
        await ExpectThrowsAsync<ArgumentException>(() => _store.CreateAsync(ValidRequest() with { GroundingReferences = [Grounding("review") with { SafeSummary = "private reasoning" }] }));

        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM workflow.WorkflowRun"));
    }

    [TestMethod]
    public async Task WorkflowRunSql_RejectsDirectAuthorityFlagsHiddenReasoningUpdateAndDelete()
    {
        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync(DirectRunInsertSql(true), new { id = Guid.NewGuid(), projectId = ProjectId }));
        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync(DirectRunInsertSql(false, "chainOfThought"), new { id = Guid.NewGuid(), projectId = ProjectId }));

        var created = await _store.CreateAsync(ValidRequest());
        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync("UPDATE workflow.WorkflowRun SET Status = N'Completed' WHERE WorkflowRunId = @id", new { id = created.WorkflowRunId }));
        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync("DELETE FROM workflow.WorkflowRun WHERE WorkflowRunId = @id", new { id = created.WorkflowRunId }));
        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync("UPDATE workflow.WorkflowRunStep SET Status = N'Completed' WHERE WorkflowRunId = @id", new { id = created.WorkflowRunId }));
        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync("DELETE FROM workflow.WorkflowRunEvidenceReference WHERE WorkflowRunId = @id", new { id = created.WorkflowRunId }));
    }

    [TestMethod]
    public async Task WorkflowRunStore_DoesNotCreateApprovalPolicyDogfoodToolA2aSourceApplyMemoryOrAuditSideEffects()
    {
        _ = await _store.CreateAsync(ValidRequest());

        Assert.AreEqual(0, await CountIfExistsAsync("governance.ApprovalDecision"));
        Assert.AreEqual(0, await CountIfExistsAsync("governance.PolicyDecisionEvent"));
        Assert.AreEqual(0, await CountIfExistsAsync("governance.DogfoodReceipt"));
        Assert.AreEqual(0, await CountIfExistsAsync("governance.ToolGateDecision"));
        Assert.AreEqual(0, await CountIfExistsAsync("governance.ToolRequest"));
        Assert.AreEqual(0, await CountIfExistsAsync("a2a.AgentHandoff"));
        Assert.AreEqual(0, await CountIfExistsAsync("agent.CollectiveMemoryItem"));
        Assert.AreEqual(0, await CountIfExistsAsync("toolaudit.ToolExecutionAuditRecord"));
    }

    [TestMethod]
    public void WorkflowRunStatuses_AreReportingStatesNotAuthorityStates()
    {
        var statuses = Enum.GetNames<WorkflowRunStatus>();
        CollectionAssert.AreEquivalent(new[] { "Created", "ReadyForReview", "Blocked", "Completed", "Failed", "Cancelled", "Superseded" }, statuses);
        AssertNoForbiddenTokens(string.Join("\n", statuses), "Approved", "Executable", "Running", "Dispatched", "PolicySatisfied", "SourceApplied", "MemoryPromoted", "AuthorityTransferred");
    }

    [TestMethod]
    public void WorkflowRunRuntimeBoundary_HasNoApiCliRunnerOrCapabilityWiring()
    {
        var root = RepositoryRoot();
        var storeText = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Workflow", "SqlWorkflowRunStore.cs"));
        AssertNoForbiddenTokens(
            storeText,
            "ControllerBase",
            "WebApplication",
            "IHostedService",
            "BackgroundService",
            "ProcessStartInfo",
            "File.Copy",
            "File.Delete",
            "LangGraph",
            "MessageBus",
            "QueueClient",
            "Inbox",
            "Outbox",
            "IAgentToolExecutor",
            "PromoteCollectiveMemory",
            "ApplySource",
            "TicketBuildWorkflowOrchestrator",
            "TicketBuildWorkflowNodes");

        foreach (var relative in new[]
                 {
                     Path.Combine("IronDev.Api", "Program.cs"),
                     Path.Combine("IronDev.Api", "Controllers"),
                     Path.Combine("IronDev.Cli"),
                     Path.Combine("tools")
                 })
        {
            var path = Path.Combine(root, relative);
            if (File.Exists(path))
            {
                AssertNoForbiddenTokens(File.ReadAllText(path), "IWorkflowRunStore", "SqlWorkflowRunStore");
                continue;
            }

            if (!Directory.Exists(path))
                continue;

            foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
                AssertNoForbiddenTokens(File.ReadAllText(file), "IWorkflowRunStore", "SqlWorkflowRunStore");
        }
    }

    private static WorkflowRunCreateRequest ValidRequest(Guid? projectId = null) =>
        new()
        {
            ProjectId = projectId ?? ProjectId,
            WorkflowType = "ManualDogfoodLoop",
            WorkflowName = "Manual dogfood receipt review",
            Status = WorkflowRunStatus.Created,
            SubjectType = "dogfood_receipt",
            SubjectId = "receipt-pr98",
            SubjectSummary = "Workflow run record for evidence review.",
            CorrelationId = CorrelationId,
            CausationId = CausationId,
            CreatedByActorType = "system_test_fixture",
            CreatedByActorId = "workflow-run-store-tests",
            MetadataVersion = 1,
            MetadataJson = "{\"schema\":\"workflow.run.metadata.v1\",\"recordsEvidenceOnly\":true}",
            Steps =
            [
                Step("plan", "Plan review", WorkflowRunStepType.Planning, WorkflowRunStatus.Created),
                Step("review", "Human review support", WorkflowRunStepType.HumanDecisionSupport, WorkflowRunStatus.ReadyForReview)
            ],
            EvidenceReferences =
            [
                Evidence("plan", WorkflowRunEvidenceType.DogfoodReceipt, "dogfood-receipt-pr98", WorkflowRunEvidenceAllowedUse.Traceability),
                Evidence("review", WorkflowRunEvidenceType.AgentHandoff, "agent-handoff-pr98", WorkflowRunEvidenceAllowedUse.HandoffExplanation),
                Evidence("review", WorkflowRunEvidenceType.GroundingEvidenceReference, GroundingReferenceId.ToString(), WorkflowRunEvidenceAllowedUse.HumanDecisionSupport) with { GroundingEvidenceReferenceId = GroundingReferenceId }
            ],
            GroundingReferences = [Grounding("review")]
        };

    private static WorkflowRunStepCreateRequest Step(string key, string name, WorkflowRunStepType type, WorkflowRunStatus status) =>
        new()
        {
            StepKey = key,
            StepName = name,
            StepType = type,
            Status = status,
            AgentRole = "reviewer",
            AgentId = "workflow-run-store-tests",
            SubjectType = "dogfood_receipt",
            SubjectId = "receipt-pr98",
            SafeSummary = "Records workflow evidence for human review.",
            MetadataVersion = 1,
            MetadataJson = "{\"schema\":\"workflow.step.metadata.v1\"}"
        };

    private static WorkflowRunEvidenceReferenceCreateRequest Evidence(string stepKey, WorkflowRunEvidenceType type, string evidenceId, WorkflowRunEvidenceAllowedUse allowedUse = WorkflowRunEvidenceAllowedUse.Review) =>
        new()
        {
            StepKey = stepKey,
            EvidenceType = type,
            EvidenceId = evidenceId,
            EvidenceLabel = type.ToString(),
            SafeSummary = "Evidence reference for workflow review.",
            AllowedUse = allowedUse
        };

    private static WorkflowRunGroundingReferenceCreateRequest Grounding(string stepKey) =>
        new()
        {
            StepKey = stepKey,
            GroundingEvidenceReferenceId = GroundingReferenceId,
            ClaimType = WorkflowRunGroundingClaimType.EvidenceSupport,
            ClaimId = "claim-pr98",
            SafeSummary = "Grounding supports evidence review only."
        };

    private static void AssertNoAuthority(WorkflowRun run)
    {
        Assert.IsFalse(run.GrantsApproval);
        Assert.IsFalse(run.GrantsExecution);
        Assert.IsFalse(run.MutatesSource);
        Assert.IsFalse(run.PromotesMemory);
        Assert.IsFalse(run.StartsWorkflow);
        Assert.IsFalse(run.ContinuesWorkflow);
        Assert.IsFalse(run.SatisfiesPolicy);
        Assert.IsFalse(run.TransfersAuthority);
        Assert.IsFalse(run.ApprovesRelease);
        Assert.IsFalse(run.CreatesAcceptedMemory);
        Assert.IsTrue(run.Steps.All(step => !step.GrantsApproval && !step.GrantsExecution && !step.MutatesSource && !step.PromotesMemory && !step.StartsWorkflow && !step.ContinuesWorkflow && !step.SatisfiesPolicy && !step.TransfersAuthority && !step.ApprovesRelease && !step.CreatesAcceptedMemory));
    }

    private static string DirectRunInsertSql(bool grantsApproval, string metadataMarker = "safe")
    {
        var metadataJson = string.Equals(metadataMarker, "safe", StringComparison.Ordinal)
            ? "{\"schema\":\"workflow.run.metadata.v1\",\"marker\":\"safe\"}"
            : "{\"schema\":\"workflow.run.metadata.v1\",\"chainOfThought\":\"blocked\"}";

        return $"""
        INSERT INTO workflow.WorkflowRun
        (
            WorkflowRunId, ProjectId, WorkflowType, WorkflowName, Status, SubjectType, SubjectId, SubjectSummary,
            CorrelationId, CausationId, CreatedByActorType, CreatedByActorId, MetadataVersion, MetadataJson,
            GrantsApproval, GrantsExecution, MutatesSource, PromotesMemory, StartsWorkflow, ContinuesWorkflow,
            SatisfiesPolicy, TransfersAuthority, ApprovesRelease, CreatesAcceptedMemory
        )
        VALUES
        (
            @id, @projectId, N'ManualDogfoodLoop', N'Direct insert test', N'Created', N'dogfood_receipt', N'receipt-direct', N'Direct insert test',
            NULL, NULL, N'system_test_fixture', N'direct-sql-test', 1, N'{metadataJson.Replace("'", "''")}',
            {(grantsApproval ? 1 : 0)}, 0, 0, 0, 0, 0, 0, 0, 0, 0
        );
        """;
    }

    private async Task<int> CountIfExistsAsync(string tableName)
    {
        var exists = await ScalarAsync<int>($"SELECT CASE WHEN OBJECT_ID(N'{tableName}', N'U') IS NULL THEN 0 ELSE 1 END");
        if (exists == 0)
            return 0;

        return await ScalarAsync<int>($"SELECT COUNT(1) FROM {tableName}");
    }

    private async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<T>(sql, parameters);
    }

    private async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
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

        Assert.Fail($"Expected exception {typeof(TException).Name}.");
    }

    private static void AssertNoForbiddenTokens(string text, params string[] tokens)
    {
        foreach (var token in tokens)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found: {token}");
    }


    private async Task ApplySqlFileAsync(params string[] pathParts)
    {
        await using var connection = new SqlConnection(ConnectionString);
        var migration = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), Path.Combine(pathParts)));
        foreach (var batch in SplitSqlBatches(migration))
            await connection.ExecuteAsync(batch);
    }

    private static IReadOnlyList<string> SplitSqlBatches(string sql) =>
        System.Text.RegularExpressions.Regex.Split(
                sql.Replace("\r\n", "\n", StringComparison.Ordinal),
                @"(?im)^\s*GO\s*$")
            .Select(batch => batch.Trim())
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToArray();
    private async Task DropWorkflowSchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            IF OBJECT_ID(N'workflow.usp_WorkflowRun_Create', N'P') IS NOT NULL DROP PROCEDURE workflow.usp_WorkflowRun_Create;
            IF OBJECT_ID(N'workflow.usp_WorkflowRun_Get', N'P') IS NOT NULL DROP PROCEDURE workflow.usp_WorkflowRun_Get;
            IF OBJECT_ID(N'workflow.usp_WorkflowRun_ListByProject', N'P') IS NOT NULL DROP PROCEDURE workflow.usp_WorkflowRun_ListByProject;
            IF OBJECT_ID(N'workflow.usp_WorkflowRun_ListByCorrelation', N'P') IS NOT NULL DROP PROCEDURE workflow.usp_WorkflowRun_ListByCorrelation;
            IF OBJECT_ID(N'workflow.usp_WorkflowRun_ListBySubject', N'P') IS NOT NULL DROP PROCEDURE workflow.usp_WorkflowRun_ListBySubject;
            IF OBJECT_ID(N'workflow.TR_WorkflowRunGroundingReference_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_WorkflowRunGroundingReference_BlockUpdateDelete;
            IF OBJECT_ID(N'workflow.TR_WorkflowRunGroundingReference_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_WorkflowRunGroundingReference_ValidateInsert;
            IF OBJECT_ID(N'workflow.TR_WorkflowRunEvidenceReference_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_WorkflowRunEvidenceReference_BlockUpdateDelete;
            IF OBJECT_ID(N'workflow.TR_WorkflowRunEvidenceReference_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_WorkflowRunEvidenceReference_ValidateInsert;
            IF OBJECT_ID(N'workflow.TR_WorkflowRunStep_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_WorkflowRunStep_BlockUpdateDelete;
            IF OBJECT_ID(N'workflow.TR_WorkflowRunStep_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_WorkflowRunStep_ValidateInsert;
            IF OBJECT_ID(N'workflow.TR_WorkflowRun_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_WorkflowRun_BlockUpdateDelete;
            IF OBJECT_ID(N'workflow.TR_WorkflowRun_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_WorkflowRun_ValidateInsert;
            IF OBJECT_ID(N'workflow.WorkflowRunGroundingReference', N'U') IS NOT NULL DROP TABLE workflow.WorkflowRunGroundingReference;
            IF OBJECT_ID(N'workflow.WorkflowRunEvidenceReference', N'U') IS NOT NULL DROP TABLE workflow.WorkflowRunEvidenceReference;
            IF OBJECT_ID(N'workflow.WorkflowRunStep', N'U') IS NOT NULL DROP TABLE workflow.WorkflowRunStep;
            IF OBJECT_ID(N'workflow.WorkflowRun', N'U') IS NOT NULL DROP TABLE workflow.WorkflowRun;
            IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'workflow') DROP SCHEMA workflow;
            """);
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

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}