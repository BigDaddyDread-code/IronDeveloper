using System.Data;
using System.Diagnostics;
using Dapper;
using IronDev.Core.Workflow;
using IronDev.Data;
using IronDev.Infrastructure.Workflow;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowStepStore")]
[TestCategory("RealDatabaseWorkflowStepSmoke")]
public sealed class WorkflowStepStoreTests : IntegrationTestBase
{
    private static readonly Guid ProjectId = Guid.Parse("10000000-bbbb-4444-8888-990000000001");
    private static readonly Guid OtherProjectId = Guid.Parse("10000000-bbbb-4444-8888-990000000002");
    private static readonly Guid CorrelationId = Guid.Parse("20000000-bbbb-4444-8888-990000000001");
    private static readonly Guid OtherCorrelationId = Guid.Parse("20000000-bbbb-4444-8888-990000000002");
    private static readonly Guid CausationId = Guid.Parse("30000000-bbbb-4444-8888-990000000001");
    private static readonly Guid GroundingReferenceId = Guid.Parse("40000000-bbbb-4444-8888-990000000001");

    private SqlWorkflowRunStore _runStore = default!;
    private SqlWorkflowStepStore _stepStore = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropWorkflowSchemaAsync();
        await ApplySqlFileAsync("Database", "migrate_workflow_run.sql");
        await ApplySqlFileAsync("Database", "migrate_workflow_step_store.sql");

        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        _runStore = new SqlWorkflowRunStore(connectionFactory);
        _stepStore = new SqlWorkflowStepStore(connectionFactory);
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropWorkflowSchemaAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public void WorkflowStepStore_ExposesDurableStepContractWithoutLifecycleAuthorityMethods()
    {
        var methods = typeof(IWorkflowStepStore).GetMethods().Select(method => method.Name).OrderBy(name => name).ToArray();
        CollectionAssert.AreEquivalent(
            new[] { "CreateAsync", "GetAsync", "ListByCorrelationAsync", "ListByRunAsync", "ListBySubjectAsync" },
            methods);

        AssertNoForbiddenTokens(
            string.Join("\n", methods),
            "Execute",
            "Dispatch",
            "Continue",
            "Start",
            "Approve",
            "SatisfyPolicy",
            "ApplySource",
            "PromoteMemory",
            "TransferAuthority");
    }

    [TestMethod]
    public async Task WorkflowStepMigration_AddsProceduresSequenceColumnConstraintAndRuntimeGrant()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN COL_LENGTH(N'workflow.WorkflowRunStep', N'SequenceNumber') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowStep_Create', N'P') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowStep_Get', N'P') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowStep_ListByRun', N'P') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowStep_ListByCorrelation', N'P') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowStep_ListBySubject', N'P') IS NULL THEN 0 ELSE 1 END"));

        var constraints = (await connection.QueryAsync<string>("SELECT name FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'workflow.WorkflowRunStep')")).ToArray();
        CollectionAssert.Contains(constraints, "CK_WorkflowRunStep_SequenceNumber_Positive");
        CollectionAssert.Contains(constraints, "CK_WorkflowRunStep_StepType_Allowed");
    }

    [TestMethod]
    public async Task WorkflowStepStore_CreatesReadsAndListsByRunCorrelationAndSubjectWithoutCrossProjectLeakage()
    {
        var workflowRunId = await CreateParentRunAsync();
        var first = await _stepStore.CreateAsync(ValidStepRequest(workflowRunId) with { StepKey = "policy", SequenceNumber = 2, StepType = WorkflowRunStepType.PolicyEvaluationInput, SubjectId = "subject-policy" });
        var second = await _stepStore.CreateAsync(ValidStepRequest(workflowRunId) with { StepKey = "review", SequenceNumber = 1, StepType = WorkflowRunStepType.ReviewFinding, SubjectId = "subject-A" });

        _ = await CreateParentRunAsync(OtherProjectId, CorrelationId, "subject-A");
        var otherCorrelationRunId = await CreateParentRunAsync(ProjectId, OtherCorrelationId, "subject-B");
        _ = await _stepStore.CreateAsync(ValidStepRequest(otherCorrelationRunId) with { StepKey = "other", SubjectId = "subject-B" });

        var read = await _stepStore.GetAsync(ProjectId, workflowRunId, first.WorkflowRunStepId);
        Assert.IsNotNull(read);
        Assert.AreEqual(first.WorkflowRunStepId, read.WorkflowRunStepId);
        Assert.AreEqual(2, read.SequenceNumber);
        Assert.AreEqual(1, read.EvidenceReferences.Count);
        Assert.AreEqual(1, read.GroundingReferences.Count);
        AssertNoAuthority(read);

        var byRun = await _stepStore.ListByRunAsync(ProjectId, workflowRunId, 10);
        var byCorrelation = await _stepStore.ListByCorrelationAsync(ProjectId, CorrelationId, 10);
        var bySubject = await _stepStore.ListBySubjectAsync(ProjectId, "workflow_step", "subject-A", 10);

        CollectionAssert.IsSubsetOf(new[] { first.WorkflowRunStepId, second.WorkflowRunStepId }, byRun.Select(summary => summary.WorkflowRunStepId).ToArray());
        CollectionAssert.IsSubsetOf(new[] { first.WorkflowRunStepId, second.WorkflowRunStepId }, byCorrelation.Select(summary => summary.WorkflowRunStepId).ToArray());
        CollectionAssert.AreEquivalent(new[] { second.WorkflowRunStepId }, bySubject.Select(summary => summary.WorkflowRunStepId).ToArray());
        Assert.IsTrue(byCorrelation.All(summary => summary.ProjectId == ProjectId));
        Assert.IsTrue(bySubject.All(summary => summary.ProjectId == ProjectId));
        var createdStepSummaries = byRun.Where(summary => summary.WorkflowRunStepId == first.WorkflowRunStepId || summary.WorkflowRunStepId == second.WorkflowRunStepId).ToArray();
        Assert.IsTrue(createdStepSummaries.All(summary => summary.EvidenceReferenceCount == 1));
        Assert.IsTrue(createdStepSummaries.All(summary => summary.GroundingReferenceCount == 1));
    }

    [TestMethod]
    public async Task WorkflowStepStore_RejectsInvalidPrivateReasoningAuthorityLanguageAndAuthorityFlagsBeforePersistence()
    {
        var workflowRunId = await CreateParentRunAsync();

        await ExpectThrowsAsync<ArgumentException>(() => _stepStore.CreateAsync(ValidStepRequest(workflowRunId) with { ProjectId = Guid.Empty }));
        await ExpectThrowsAsync<ArgumentException>(() => _stepStore.CreateAsync(ValidStepRequest(workflowRunId) with { WorkflowRunId = Guid.Empty }));
        await ExpectThrowsAsync<ArgumentException>(() => _stepStore.CreateAsync(ValidStepRequest(workflowRunId) with { SequenceNumber = 0 }));
        await ExpectThrowsAsync<ArgumentException>(() => _stepStore.CreateAsync(ValidStepRequest(workflowRunId) with { MetadataJson = "{not-json}" }));
        await ExpectThrowsAsync<ArgumentException>(() => _stepStore.CreateAsync(ValidStepRequest(workflowRunId) with { MetadataJson = "{\"schema\":\"workflow.step.metadata.v1\",\"executionAllowed\":true}" }));
        await ExpectThrowsAsync<ArgumentException>(() => _stepStore.CreateAsync(ValidStepRequest(workflowRunId) with { SafeSummary = "approval granted" }));
        await ExpectThrowsAsync<ArgumentException>(() => _stepStore.CreateAsync(ValidStepRequest(workflowRunId) with { GrantsApproval = true }));
        await ExpectThrowsAsync<ArgumentException>(() => _stepStore.CreateAsync(ValidStepRequest(workflowRunId) with { GrantsExecution = true }));
        await ExpectThrowsAsync<ArgumentException>(() => _stepStore.CreateAsync(ValidStepRequest(workflowRunId) with { MutatesSource = true }));
        await ExpectThrowsAsync<ArgumentException>(() => _stepStore.CreateAsync(ValidStepRequest(workflowRunId) with { PromotesMemory = true }));
        await ExpectThrowsAsync<ArgumentException>(() => _stepStore.CreateAsync(ValidStepRequest(workflowRunId) with { StartsWorkflow = true }));
        await ExpectThrowsAsync<ArgumentException>(() => _stepStore.CreateAsync(ValidStepRequest(workflowRunId) with { ContinuesWorkflow = true }));
        await ExpectThrowsAsync<ArgumentException>(() => _stepStore.CreateAsync(ValidStepRequest(workflowRunId) with { SatisfiesPolicy = true }));
        await ExpectThrowsAsync<ArgumentException>(() => _stepStore.CreateAsync(ValidStepRequest(workflowRunId) with { TransfersAuthority = true }));
        await ExpectThrowsAsync<ArgumentException>(() => _stepStore.CreateAsync(ValidStepRequest(workflowRunId) with { ApprovesRelease = true }));
        await ExpectThrowsAsync<ArgumentException>(() => _stepStore.CreateAsync(ValidStepRequest(workflowRunId) with { CreatesAcceptedMemory = true }));
        await ExpectThrowsAsync<ArgumentException>(() => _stepStore.CreateAsync(ValidStepRequest(workflowRunId) with { EvidenceReferences = [Evidence(WorkflowRunEvidenceType.DogfoodReceipt, "dogfood-1") with { SafeSummary = "tool executed" }] }));
        await ExpectThrowsAsync<ArgumentException>(() => _stepStore.CreateAsync(ValidStepRequest(workflowRunId) with { GroundingReferences = [Grounding() with { SafeSummary = "private reasoning" }] }));

        Assert.AreEqual(1, await ScalarAsync<int>("SELECT COUNT(1) FROM workflow.WorkflowRunStep WHERE WorkflowRunId = @workflowRunId", new { workflowRunId }));
    }

    [TestMethod]
    public async Task WorkflowStepSql_RejectsHiddenReasoningUpdateDeleteAndDuplicateStepKey()
    {
        var workflowRunId = await CreateParentRunAsync();
        var created = await _stepStore.CreateAsync(ValidStepRequest(workflowRunId));

        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync(
            """
            EXEC workflow.usp_WorkflowStep_Create
                @WorkflowRunStepId = @stepId,
                @WorkflowRunId = @runId,
                @ProjectId = @projectId,
                @StepKey = N'step-main',
                @StepName = N'Duplicate step key',
                @StepType = N'ReviewFinding',
                @Status = N'Created',
                @AgentRole = NULL,
                @AgentId = NULL,
                @SubjectType = N'workflow_step',
                @SubjectId = N'subject-duplicate',
                @SafeSummary = N'Duplicate should fail.',
                @SequenceNumber = 2,
                @CorrelationId = @correlationId,
                @CausationId = @causationId,
                @MetadataVersion = 1,
                @MetadataJson = N'{"schema":"workflow.step.metadata.v1"}',
                @EvidenceReferencesJson = N'[]',
                @GroundingReferencesJson = N'[]';
            """,
            new { stepId = Guid.NewGuid(), runId = workflowRunId, projectId = ProjectId, correlationId = CorrelationId, causationId = CausationId }));

        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync(
            """
            EXEC workflow.usp_WorkflowStep_Create
                @WorkflowRunStepId = @stepId,
                @WorkflowRunId = @runId,
                @ProjectId = @projectId,
                @StepKey = N'hidden',
                @StepName = N'Hidden reasoning step',
                @StepType = N'DebugFinding',
                @Status = N'Created',
                @AgentRole = NULL,
                @AgentId = NULL,
                @SubjectType = N'workflow_step',
                @SubjectId = N'subject-hidden',
                @SafeSummary = N'private reasoning leaked',
                @SequenceNumber = 3,
                @CorrelationId = @correlationId,
                @CausationId = @causationId,
                @MetadataVersion = 1,
                @MetadataJson = N'{"schema":"workflow.step.metadata.v1"}',
                @EvidenceReferencesJson = N'[]',
                @GroundingReferencesJson = N'[]';
            """,
            new { stepId = Guid.NewGuid(), runId = workflowRunId, projectId = ProjectId, correlationId = CorrelationId, causationId = CausationId }));

        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync("UPDATE workflow.WorkflowRunStep SET Status = N'Completed' WHERE WorkflowRunStepId = @id", new { id = created.WorkflowRunStepId }));
        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync("DELETE FROM workflow.WorkflowRunStep WHERE WorkflowRunStepId = @id", new { id = created.WorkflowRunStepId }));
    }

    [TestMethod]
    public async Task WorkflowStepStore_DoesNotCreateApprovalPolicyDogfoodToolA2aSourceApplyMemoryOrAuditSideEffects()
    {
        var workflowRunId = await CreateParentRunAsync();
        _ = await _stepStore.CreateAsync(ValidStepRequest(workflowRunId));

        Assert.AreEqual(0, await CountIfExistsAsync("governance.ApprovalDecision"));
        Assert.AreEqual(0, await CountIfExistsAsync("governance.PolicyDecisionEvent"));
        Assert.AreEqual(0, await CountIfExistsAsync("governance.DogfoodReceipt"));
        Assert.AreEqual(0, await CountIfExistsAsync("governance.ToolGateDecision"));
        Assert.AreEqual(0, await CountIfExistsAsync("governance.ToolRequest"));
        Assert.AreEqual(0, await CountIfExistsAsync("a2a.AgentHandoff"));
        Assert.AreEqual(0, await CountIfExistsAsync("agent.AgentLocalMemory"));
        Assert.AreEqual(0, await CountIfExistsAsync("agent.AgentMemoryHandoffSlice"));
        Assert.AreEqual(0, await CountIfExistsAsync("agent.AgentMemoryImprovementProposal"));
        Assert.AreEqual(0, await CountIfExistsAsync("dbo.ToolExecutionAuditRecord"));
        Assert.AreEqual(0, await CountIfExistsAsync("dbo.AgentRunAuditEnvelope"));
    }

    [TestMethod]
    public void WorkflowStepRuntimeBoundary_HasNoApiCliRunnerOrCapabilityWiring()
    {
        var root = RepositoryRoot();
        var storeText = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Workflow", "SqlWorkflowStepStore.cs"));
        AssertNoForbiddenTokens(
            storeText,
            "Process.Start",
            "IHostedService",
            "BackgroundService",
            "ControllerBase",
            "WebApplication",
            "HttpClient",
            "File.Copy",
            "File.Delete",
            "Directory.CreateDirectory",
            "IWorkflowRunner",
            "WorkflowOrchestrator",
            "TicketBuildWorkflowOrchestrator",
            "TicketBuildWorkflowNodes",
            "ApplySource",
            "PromoteCollectiveMemory");

        foreach (var path in new[]
        {
            Path.Combine(root, "IronDev.Api"),
            Path.Combine(root, "IronDev.Cli"),
            Path.Combine(root, "IronDev.Core", "Agents"),
            Path.Combine(root, "IronDev.Infrastructure", "Agents")
        })
        {
            if (!Directory.Exists(path))
                continue;

            foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
                AssertNoForbiddenTokens(File.ReadAllText(file), "IWorkflowStepStore", "SqlWorkflowStepStore");
        }
    }


    private async Task<Guid> CreateParentRunAsync(Guid? projectId = null, Guid? correlationId = null, string subjectId = "receipt-pr99")
    {
        var created = await _runStore.CreateAsync(ValidRunRequest(projectId ?? ProjectId) with
        {
            CorrelationId = correlationId ?? CorrelationId,
            SubjectId = subjectId
        });

        return created.WorkflowRunId;
    }
    private static WorkflowRunCreateRequest ValidRunRequest(Guid? projectId = null) =>
        new()
        {
            ProjectId = projectId ?? ProjectId,
            WorkflowType = "ManualDogfoodLoop",
            WorkflowName = "Manual dogfood receipt review",
            Status = WorkflowRunStatus.Created,
            SubjectType = "dogfood_receipt",
            SubjectId = "receipt-pr99",
            SubjectSummary = "Workflow run record for step evidence review.",
            CorrelationId = CorrelationId,
            CausationId = CausationId,
            CreatedByActorType = "human",
            CreatedByActorId = "reviewer-pr99",
            MetadataVersion = 1,
            MetadataJson = "{\"schema\":\"workflow.run.metadata.v1\",\"recordsEvidenceOnly\":true}",
            Steps =
            [
                new WorkflowRunStepCreateRequest
                {
                    StepKey = "run-anchor",
                    StepName = "Parent run anchor",
                    StepType = WorkflowRunStepType.Planning,
                    Status = WorkflowRunStatus.Created,
                    SubjectType = "workflow_run",
                    SubjectId = "parent-anchor",
                    SafeSummary = "Anchor step for parent workflow run evidence.",
                    MetadataVersion = 1,
                    MetadataJson = "{\"schema\":\"workflow.step.metadata.v1\",\"anchor\":true}"
                }
            ],
            EvidenceReferences =
            [
                new WorkflowRunEvidenceReferenceCreateRequest
                {
                    StepKey = "run-anchor",
                    EvidenceType = WorkflowRunEvidenceType.DogfoodReceipt,
                    EvidenceId = "parent-dogfood-receipt-pr99",
                    EvidenceLabel = "Parent dogfood receipt",
                    SafeSummary = "Parent workflow run evidence reference.",
                    AllowedUse = WorkflowRunEvidenceAllowedUse.Traceability
                }
            ],
            GroundingReferences = []
        };

    private static WorkflowStepCreateRequest ValidStepRequest(Guid workflowRunId) =>
        new()
        {
            WorkflowRunStepId = Guid.NewGuid(),
            WorkflowRunId = workflowRunId,
            ProjectId = ProjectId,
            StepKey = "step-main",
            StepName = "Review workflow step evidence",
            StepType = WorkflowRunStepType.DebugFinding,
            Status = WorkflowRunStatus.Created,
            AgentRole = "reviewer",
            AgentId = "human-reviewer-pr99",
            SubjectType = "workflow_step",
            SubjectId = "subject-A",
            SafeSummary = "Records workflow step evidence for human review.",
            SequenceNumber = 1,
            CorrelationId = CorrelationId,
            CausationId = CausationId,
            MetadataVersion = 1,
            MetadataJson = "{\"schema\":\"workflow.step.metadata.v1\",\"recordsStepEvidenceOnly\":true}",
            EvidenceReferences = [Evidence(WorkflowRunEvidenceType.DogfoodReceipt, "dogfood-receipt-pr99")],
            GroundingReferences = [Grounding()]
        };

    private static WorkflowRunEvidenceReferenceCreateRequest Evidence(WorkflowRunEvidenceType type, string evidenceId) =>
        new()
        {
            EvidenceType = type,
            EvidenceId = evidenceId,
            EvidenceLabel = type.ToString(),
            SafeSummary = "Evidence reference for workflow step review.",
            AllowedUse = WorkflowRunEvidenceAllowedUse.Review
        };

    private static WorkflowRunGroundingReferenceCreateRequest Grounding() =>
        new()
        {
            GroundingEvidenceReferenceId = GroundingReferenceId,
            ClaimType = WorkflowRunGroundingClaimType.EvidenceSupport,
            ClaimId = "claim-pr99",
            SafeSummary = "Grounding supports workflow step evidence review only."
        };

    private static void AssertNoAuthority(WorkflowStep step)
    {
        Assert.IsFalse(step.GrantsApproval);
        Assert.IsFalse(step.GrantsExecution);
        Assert.IsFalse(step.MutatesSource);
        Assert.IsFalse(step.PromotesMemory);
        Assert.IsFalse(step.StartsWorkflow);
        Assert.IsFalse(step.ContinuesWorkflow);
        Assert.IsFalse(step.SatisfiesPolicy);
        Assert.IsFalse(step.TransfersAuthority);
        Assert.IsFalse(step.ApprovesRelease);
        Assert.IsFalse(step.CreatesAcceptedMemory);
    }

    private static void AssertNoForbiddenTokens(string text, params string[] forbidden)
    {
        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found: {token}");
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

    private async Task ApplySqlFileAsync(params string[] pathParts)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(new[] { RepositoryRoot() }.Concat(pathParts).ToArray()));
        foreach (var batch in SplitSqlBatches(sql))
        {
            if (!string.IsNullOrWhiteSpace(batch))
                await connection.ExecuteAsync(batch);
        }
    }

    private async Task DropWorkflowSchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            IF OBJECT_ID(N'workflow.usp_WorkflowStep_Create', N'P') IS NOT NULL DROP PROCEDURE workflow.usp_WorkflowStep_Create;
            IF OBJECT_ID(N'workflow.usp_WorkflowStep_Get', N'P') IS NOT NULL DROP PROCEDURE workflow.usp_WorkflowStep_Get;
            IF OBJECT_ID(N'workflow.usp_WorkflowStep_ListByRun', N'P') IS NOT NULL DROP PROCEDURE workflow.usp_WorkflowStep_ListByRun;
            IF OBJECT_ID(N'workflow.usp_WorkflowStep_ListByCorrelation', N'P') IS NOT NULL DROP PROCEDURE workflow.usp_WorkflowStep_ListByCorrelation;
            IF OBJECT_ID(N'workflow.usp_WorkflowStep_ListBySubject', N'P') IS NOT NULL DROP PROCEDURE workflow.usp_WorkflowStep_ListBySubject;
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

        throw new InvalidOperationException("Could not locate repository root.");
    }
    private static string[] SplitSqlBatches(string sql) =>
        System.Text.RegularExpressions.Regex.Split(sql, @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToArray();

    private async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<T>(sql, parameters) ?? throw new InvalidOperationException("Scalar query returned null.");
    }

    private async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
    }

    private async Task<int> CountIfExistsAsync(string tableName)
    {
        await using var connection = new SqlConnection(ConnectionString);
        var exists = await connection.ExecuteScalarAsync<int>($"SELECT CASE WHEN OBJECT_ID(N'{tableName}', N'U') IS NULL THEN 0 ELSE 1 END");
        if (exists == 0)
            return 0;

        return await connection.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM {tableName}");
    }
}
