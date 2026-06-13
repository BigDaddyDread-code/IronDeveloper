using System.Data;
using Dapper;
using IronDev.Core.Workflow;
using IronDev.Data;
using IronDev.Infrastructure.Workflow;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowCheckpointStore")]
[TestCategory("RealDatabaseWorkflowCheckpointSmoke")]
public sealed class WorkflowCheckpointStoreTests : IntegrationTestBase
{
    private static readonly Guid ProjectId = Guid.Parse("10000000-cccc-4444-8888-990000000001");
    private static readonly Guid CorrelationId = Guid.Parse("20000000-cccc-4444-8888-990000000001");
    private static readonly Guid CausationId = Guid.Parse("30000000-cccc-4444-8888-990000000001");
    private static readonly Guid GroundingReferenceId = Guid.Parse("40000000-cccc-4444-8888-990000000001");

    private SqlWorkflowRunStore _runStore = default!;
    private SqlWorkflowStepStore _stepStore = default!;
    private SqlWorkflowCheckpointStore _checkpointStore = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropWorkflowSchemaAsync();
        await ApplySqlFileAsync("Database", "migrate_workflow_run.sql");
        await ApplySqlFileAsync("Database", "migrate_workflow_step_store.sql");
        await ApplySqlFileAsync("Database", "migrate_workflow_checkpoint_store.sql");

        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        _runStore = new SqlWorkflowRunStore(connectionFactory);
        _stepStore = new SqlWorkflowStepStore(connectionFactory);
        _checkpointStore = new SqlWorkflowCheckpointStore(connectionFactory);
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropWorkflowSchemaAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public void WorkflowCheckpointStore_ExposesDurableCheckpointContractWithoutResumeOrExecutionMethods()
    {
        var methods = typeof(IWorkflowCheckpointStore).GetMethods().Select(method => method.Name).OrderBy(name => name).ToArray();
        CollectionAssert.AreEquivalent(
            new[] { "CreateAsync", "GetAsync", "ListByCorrelationAsync", "ListByRunAsync", "ListByStepAsync", "ListBySubjectAsync" },
            methods);

        AssertNoForbiddenTokens(
            string.Join("\n", methods),
            "Execute",
            "Dispatch",
            "Continue",
            "Start",
            "Resume",
            "Restore",
            "Approve",
            "SatisfyPolicy",
            "ApplySource",
            "PromoteMemory",
            "TransferAuthority");
    }

    [TestMethod]
    public async Task WorkflowCheckpointMigration_AddsTablesProceduresTriggersAndRuntimeGrants()
    {
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.WorkflowCheckpoint', N'U') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.WorkflowCheckpointEvidenceReference', N'U') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.WorkflowCheckpointGroundingReference', N'U') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowCheckpoint_Create', N'P') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowCheckpoint_Get', N'P') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowCheckpoint_ListByRun', N'P') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowCheckpoint_ListByStep', N'P') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowCheckpoint_ListByCorrelation', N'P') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.usp_WorkflowCheckpoint_ListBySubject', N'P') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.TR_WorkflowCheckpoint_ValidateInsert', N'TR') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.TR_WorkflowCheckpoint_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END"));
    }

    [TestMethod]
    public async Task WorkflowCheckpointStore_CreateReadAndList_PreservesSafeCheckpointEvidence()
    {
        var (run, step) = await CreateRunAndStepAsync();

        var checkpoint = await _checkpointStore.CreateAsync(ValidCheckpointRequest(run.WorkflowRunId, step.WorkflowRunStepId));

        Assert.AreEqual(run.WorkflowRunId, checkpoint.WorkflowRunId);
        Assert.AreEqual(step.WorkflowRunStepId, checkpoint.WorkflowRunStepId);
        Assert.AreEqual(ProjectId, checkpoint.ProjectId);
        Assert.AreEqual("checkpoint-main", checkpoint.CheckpointKey);
        Assert.AreEqual(WorkflowCheckpointType.ReviewSnapshot, checkpoint.CheckpointType);
        Assert.AreEqual(WorkflowCheckpointStatus.Captured, checkpoint.Status);
        Assert.AreEqual("workflow_subject", checkpoint.SubjectType);
        Assert.AreEqual("subject-pr100", checkpoint.SubjectId);
        Assert.AreEqual(CorrelationId, checkpoint.CorrelationId);
        Assert.AreEqual(CausationId, checkpoint.CausationId);
        Assert.AreEqual(1, checkpoint.EvidenceReferences.Count);
        Assert.AreEqual(1, checkpoint.GroundingReferences.Count);
        AssertNoAuthority(checkpoint);

        var loaded = await _checkpointStore.GetAsync(ProjectId, run.WorkflowRunId, checkpoint.WorkflowCheckpointId);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(checkpoint.WorkflowCheckpointId, loaded.WorkflowCheckpointId);
        Assert.AreEqual("dogfood-receipt-pr100", loaded.EvidenceReferences.Single().EvidenceId);
        Assert.AreEqual(GroundingReferenceId, loaded.GroundingReferences.Single().GroundingReferenceId);

        var byRun = await _checkpointStore.ListByRunAsync(ProjectId, run.WorkflowRunId, 10);
        var byStep = await _checkpointStore.ListByStepAsync(ProjectId, run.WorkflowRunId, step.WorkflowRunStepId, 10);
        var byCorrelation = await _checkpointStore.ListByCorrelationAsync(ProjectId, CorrelationId, 10);
        var bySubject = await _checkpointStore.ListBySubjectAsync(ProjectId, "workflow_subject", "subject-pr100", 10);

        Assert.AreEqual(1, byRun.Count);
        Assert.AreEqual(1, byStep.Count);
        Assert.AreEqual(1, byCorrelation.Count);
        Assert.AreEqual(1, bySubject.Count);
        Assert.AreEqual(checkpoint.WorkflowCheckpointId, byRun.Single().WorkflowCheckpointId);
        Assert.AreEqual(1, byRun.Single().EvidenceReferenceCount);
        Assert.AreEqual(1, byRun.Single().GroundingReferenceCount);
    }

    [TestMethod]
    public async Task WorkflowCheckpointStore_RejectsAuthorityResumeAndPrivateReasoningAtCSharpBoundary()
    {
        var (run, step) = await CreateRunAndStepAsync();

        await ExpectThrowsAsync<InvalidOperationException>(() =>
            _checkpointStore.CreateAsync(ValidCheckpointRequest(run.WorkflowRunId, step.WorkflowRunStepId) with { ResumesWorkflow = true }));

        await ExpectThrowsAsync<InvalidOperationException>(() =>
            _checkpointStore.CreateAsync(ValidCheckpointRequest(run.WorkflowRunId, step.WorkflowRunStepId) with { SafeSummary = "rawPrompt leaked" }));

        await ExpectThrowsAsync<InvalidOperationException>(() =>
            _checkpointStore.CreateAsync(ValidCheckpointRequest(run.WorkflowRunId, step.WorkflowRunStepId) with { StateJson = "{\"chainOfThought\":\"no\"}" }));
    }

    [TestMethod]
    public async Task WorkflowCheckpointStore_DirectSqlCreateRejectsRawPrivateAndResumeMarkers()
    {
        var (run, step) = await CreateRunAndStepAsync();

        await ExpectThrowsAsync<SqlException>(() => ExecuteWorkflowCheckpointCreateViaSqlAsync(run.WorkflowRunId, step.WorkflowRunStepId, "sql-raw-prompt", safeSummary: "rawPrompt leaked"));
        await ExpectThrowsAsync<SqlException>(() => ExecuteWorkflowCheckpointCreateViaSqlAsync(run.WorkflowRunId, step.WorkflowRunStepId, "sql-raw-completion", safeSummary: "rawCompletion leaked"));
        await ExpectThrowsAsync<SqlException>(() => ExecuteWorkflowCheckpointCreateViaSqlAsync(run.WorkflowRunId, step.WorkflowRunStepId, "sql-raw-tool", evidenceReferencesJson: "[{\"evidenceType\":\"DogfoodReceipt\",\"evidenceId\":\"receipt-raw-tool\",\"safeSummary\":\"rawToolOutput leaked\"}]"));
        await ExpectThrowsAsync<SqlException>(() => ExecuteWorkflowCheckpointCreateViaSqlAsync(run.WorkflowRunId, step.WorkflowRunStepId, "sql-entire-patch", groundingReferencesJson: "[{\"groundingReferenceId\":\"40000000-cccc-4444-8888-990000000001\",\"claimType\":\"EvidenceSupport\",\"claimId\":\"claim-direct\",\"safeSummary\":\"entirePatch leaked\"}]"));
        await ExpectThrowsAsync<SqlException>(() => ExecuteWorkflowCheckpointCreateViaSqlAsync(run.WorkflowRunId, step.WorkflowRunStepId, "sql-chain-of-thought", metadataJson: "{\"chainOfThought\":\"no\"}"));
        await ExpectThrowsAsync<SqlException>(() => ExecuteWorkflowCheckpointCreateViaSqlAsync(run.WorkflowRunId, step.WorkflowRunStepId, "sql-resume", safeSummary: "resume workflow now"));
    }

    [TestMethod]
    public async Task WorkflowCheckpointStore_DirectSqlAuthorityFlagsAreRejected()
    {
        var (run, step) = await CreateRunAndStepAsync();

        await ExpectThrowsAsync<SqlException>(() => ExecuteWorkflowCheckpointCreateViaSqlAsync(run.WorkflowRunId, step.WorkflowRunStepId, "sql-authority", grantsExecution: true));
        await ExpectThrowsAsync<SqlException>(() => ExecuteWorkflowCheckpointCreateViaSqlAsync(run.WorkflowRunId, step.WorkflowRunStepId, "sql-promote", promotesMemory: true));
        await ExpectThrowsAsync<SqlException>(() => ExecuteWorkflowCheckpointCreateViaSqlAsync(run.WorkflowRunId, step.WorkflowRunStepId, "sql-resume-flag", resumesWorkflow: true));
    }

    [TestMethod]
    public async Task WorkflowCheckpointTables_AreAppendOnly()
    {
        var (run, step) = await CreateRunAndStepAsync();
        var checkpoint = await _checkpointStore.CreateAsync(ValidCheckpointRequest(run.WorkflowRunId, step.WorkflowRunStepId));

        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync("UPDATE workflow.WorkflowCheckpoint SET SafeSummary = N'Changed' WHERE WorkflowCheckpointId = @id", new { id = checkpoint.WorkflowCheckpointId }));
        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync("DELETE FROM workflow.WorkflowCheckpoint WHERE WorkflowCheckpointId = @id", new { id = checkpoint.WorkflowCheckpointId }));
        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync("UPDATE workflow.WorkflowCheckpointEvidenceReference SET SafeSummary = N'Changed' WHERE WorkflowCheckpointId = @id", new { id = checkpoint.WorkflowCheckpointId }));
        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync("DELETE FROM workflow.WorkflowCheckpointGroundingReference WHERE WorkflowCheckpointId = @id", new { id = checkpoint.WorkflowCheckpointId }));
    }

    [TestMethod]
    public async Task WorkflowCheckpointStore_DoesNotCreateRuntimeOrAuthoritySideEffects()
    {
        var (run, step) = await CreateRunAndStepAsync();
        var checkpoint = await _checkpointStore.CreateAsync(ValidCheckpointRequest(run.WorkflowRunId, step.WorkflowRunStepId));

        AssertNoAuthority(checkpoint);
        Assert.AreEqual(0, await CountIfExistsAsync("governance.ApprovalDecision"));
        Assert.AreEqual(0, await CountIfExistsAsync("governance.PolicyDecisionEvent"));
        Assert.AreEqual(0, await CountIfExistsAsync("agent.CollectiveMemory"));
        Assert.AreEqual(0, await CountIfExistsAsync("agent.HandoffRecord"));
    }

    [TestMethod]
    public void WorkflowCheckpointProductionFiles_DoNotExposeRuntimeExecutionOrResumeBoundary()
    {
        var root = RepositoryRoot();
        var productionText = string.Join("\n", new[]
        {
            File.ReadAllText(Path.Combine(root, "IronDev.Core", "Workflow", "WorkflowCheckpointModels.cs")),
            File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "Workflow", "SqlWorkflowCheckpointStore.cs")),
            File.ReadAllText(Path.Combine(root, "Database", "migrate_workflow_checkpoint_store.sql"))
        });

        AssertNoForbiddenTokens(
            productionText,
            "IHostedService",
            "BackgroundService",
            "ControllerBase",
            "WebApplication",
            "LangGraph",
            "DispatchAgent",
            "ExecuteTool",
            "ApplyPatch",
            "ApplySource",
            "PromoteMemory",
            "ApproveRelease");
    }

    private async Task<(WorkflowRun Run, WorkflowStep Step)> CreateRunAndStepAsync()
    {
        var run = await _runStore.CreateAsync(ValidRunRequest());
        var step = await _stepStore.CreateAsync(ValidStepRequest(run.WorkflowRunId));
        return (run, step);
    }

    private static WorkflowRunCreateRequest ValidRunRequest() =>
        new()
        {
            WorkflowRunId = Guid.NewGuid(),
            ProjectId = ProjectId,
            WorkflowType = "EvidenceReview",
            WorkflowName = "PR100 checkpoint workflow",
            Status = WorkflowRunStatus.Created,
            SubjectType = "workflow_run",
            SubjectId = "subject-pr100-run",
            SubjectSummary = "Workflow run for checkpoint store tests.",
            CorrelationId = CorrelationId,
            CausationId = CausationId,
            CreatedByActorType = "system_test_fixture",
            CreatedByActorId = "workflow-checkpoint-tests",
            MetadataVersion = 1,
            MetadataJson = "{\"schema\":\"workflow.run.metadata.v1\",\"checkpointTest\":true}",
            Steps =
            [
                new WorkflowRunStepCreateRequest
                {
                    StepKey = "run-anchor",
                    StepName = "Run anchor",
                    StepType = WorkflowRunStepType.Planning,
                    Status = WorkflowRunStatus.Created,
                    SubjectType = "workflow_run",
                    SubjectId = "subject-pr100-run",
                    SafeSummary = "Anchor step for checkpoint workflow run.",
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
                    EvidenceId = "dogfood-receipt-run-pr100",
                    EvidenceLabel = "Run dogfood receipt",
                    SafeSummary = "Run evidence reference for checkpoint tests.",
                    AllowedUse = WorkflowRunEvidenceAllowedUse.Traceability
                }
            ]
        };

    private static WorkflowStepCreateRequest ValidStepRequest(Guid workflowRunId) =>
        new()
        {
            WorkflowRunStepId = Guid.NewGuid(),
            WorkflowRunId = workflowRunId,
            ProjectId = ProjectId,
            StepKey = "step-main",
            StepName = "Review workflow checkpoint evidence",
            StepType = WorkflowRunStepType.ReviewFinding,
            Status = WorkflowRunStatus.Created,
            AgentRole = "reviewer",
            AgentId = "human-reviewer-pr100",
            SubjectType = "workflow_step",
            SubjectId = "step-subject-pr100",
            SafeSummary = "Records workflow step evidence before checkpoint capture.",
            SequenceNumber = 1,
            CorrelationId = CorrelationId,
            CausationId = CausationId,
            MetadataVersion = 1,
            MetadataJson = "{\"schema\":\"workflow.step.metadata.v1\",\"checkpointTest\":true}",
            EvidenceReferences =
            [
                new WorkflowRunEvidenceReferenceCreateRequest
                {
                    EvidenceType = WorkflowRunEvidenceType.DogfoodReceipt,
                    EvidenceId = "dogfood-receipt-step-pr100",
                    EvidenceLabel = "Step dogfood receipt",
                    SafeSummary = "Evidence reference for checkpoint step review.",
                    AllowedUse = WorkflowRunEvidenceAllowedUse.Review
                }
            ],
            GroundingReferences =
            [
                new WorkflowRunGroundingReferenceCreateRequest
                {
                    GroundingEvidenceReferenceId = GroundingReferenceId,
                    ClaimType = WorkflowRunGroundingClaimType.EvidenceSupport,
                    ClaimId = "claim-step-pr100",
                    SafeSummary = "Grounding supports checkpoint step review only."
                }
            ]
        };

    private static WorkflowCheckpointCreateRequest ValidCheckpointRequest(Guid workflowRunId, Guid workflowRunStepId) =>
        new()
        {
            WorkflowCheckpointId = Guid.NewGuid(),
            WorkflowRunId = workflowRunId,
            WorkflowRunStepId = workflowRunStepId,
            ProjectId = ProjectId,
            CheckpointKey = "checkpoint-main",
            CheckpointName = "Review checkpoint snapshot",
            CheckpointType = WorkflowCheckpointType.ReviewSnapshot,
            Status = WorkflowCheckpointStatus.Captured,
            SubjectType = "workflow_subject",
            SubjectId = "subject-pr100",
            SafeSummary = "Checkpoint records safe workflow state for review only.",
            StateVersion = 1,
            StateJson = "{\"schema\":\"workflow.checkpoint.state.v1\",\"recordsCheckpointOnly\":true}",
            StateHashSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            CorrelationId = CorrelationId,
            CausationId = CausationId,
            CreatedByActorType = "system_test_fixture",
            CreatedByActorId = "workflow-checkpoint-tests",
            MetadataVersion = 1,
            MetadataJson = "{\"schema\":\"workflow.checkpoint.metadata.v1\",\"checkpointOnly\":true}",
            EvidenceReferences =
            [
                new WorkflowCheckpointEvidenceReferenceCreateRequest
                {
                    EvidenceType = WorkflowRunEvidenceType.DogfoodReceipt,
                    EvidenceId = "dogfood-receipt-pr100",
                    EvidenceLabel = "Dogfood receipt",
                    SafeSummary = "Receipt evidence supports checkpoint review only.",
                    AllowedUse = WorkflowRunEvidenceAllowedUse.Review
                }
            ],
            GroundingReferences =
            [
                new WorkflowCheckpointGroundingReferenceCreateRequest
                {
                    GroundingReferenceId = GroundingReferenceId,
                    ClaimType = WorkflowRunGroundingClaimType.EvidenceSupport,
                    ClaimId = "claim-pr100",
                    SafeSummary = "Grounding supports checkpoint review only."
                }
            ]
        };

    private static void AssertNoAuthority(WorkflowCheckpoint checkpoint)
    {
        Assert.IsFalse(checkpoint.GrantsApproval);
        Assert.IsFalse(checkpoint.GrantsExecution);
        Assert.IsFalse(checkpoint.MutatesSource);
        Assert.IsFalse(checkpoint.PromotesMemory);
        Assert.IsFalse(checkpoint.StartsWorkflow);
        Assert.IsFalse(checkpoint.ContinuesWorkflow);
        Assert.IsFalse(checkpoint.ResumesWorkflow);
        Assert.IsFalse(checkpoint.SatisfiesPolicy);
        Assert.IsFalse(checkpoint.TransfersAuthority);
        Assert.IsFalse(checkpoint.ApprovesRelease);
        Assert.IsFalse(checkpoint.CreatesAcceptedMemory);
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
            IF OBJECT_ID(N'workflow.usp_WorkflowCheckpoint_Create', N'P') IS NOT NULL DROP PROCEDURE workflow.usp_WorkflowCheckpoint_Create;
            IF OBJECT_ID(N'workflow.usp_WorkflowCheckpoint_Get', N'P') IS NOT NULL DROP PROCEDURE workflow.usp_WorkflowCheckpoint_Get;
            IF OBJECT_ID(N'workflow.usp_WorkflowCheckpoint_ListByRun', N'P') IS NOT NULL DROP PROCEDURE workflow.usp_WorkflowCheckpoint_ListByRun;
            IF OBJECT_ID(N'workflow.usp_WorkflowCheckpoint_ListByStep', N'P') IS NOT NULL DROP PROCEDURE workflow.usp_WorkflowCheckpoint_ListByStep;
            IF OBJECT_ID(N'workflow.usp_WorkflowCheckpoint_ListByCorrelation', N'P') IS NOT NULL DROP PROCEDURE workflow.usp_WorkflowCheckpoint_ListByCorrelation;
            IF OBJECT_ID(N'workflow.usp_WorkflowCheckpoint_ListBySubject', N'P') IS NOT NULL DROP PROCEDURE workflow.usp_WorkflowCheckpoint_ListBySubject;
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
            IF OBJECT_ID(N'workflow.TR_WorkflowCheckpointGroundingReference_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_WorkflowCheckpointGroundingReference_BlockUpdateDelete;
            IF OBJECT_ID(N'workflow.TR_WorkflowCheckpointGroundingReference_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_WorkflowCheckpointGroundingReference_ValidateInsert;
            IF OBJECT_ID(N'workflow.TR_WorkflowCheckpointEvidenceReference_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_WorkflowCheckpointEvidenceReference_BlockUpdateDelete;
            IF OBJECT_ID(N'workflow.TR_WorkflowCheckpointEvidenceReference_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_WorkflowCheckpointEvidenceReference_ValidateInsert;
            IF OBJECT_ID(N'workflow.TR_WorkflowCheckpoint_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_WorkflowCheckpoint_BlockUpdateDelete;
            IF OBJECT_ID(N'workflow.TR_WorkflowCheckpoint_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_WorkflowCheckpoint_ValidateInsert;
            IF OBJECT_ID(N'workflow.TR_WorkflowRunGroundingReference_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_WorkflowRunGroundingReference_BlockUpdateDelete;
            IF OBJECT_ID(N'workflow.TR_WorkflowRunGroundingReference_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_WorkflowRunGroundingReference_ValidateInsert;
            IF OBJECT_ID(N'workflow.TR_WorkflowRunEvidenceReference_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_WorkflowRunEvidenceReference_BlockUpdateDelete;
            IF OBJECT_ID(N'workflow.TR_WorkflowRunEvidenceReference_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_WorkflowRunEvidenceReference_ValidateInsert;
            IF OBJECT_ID(N'workflow.TR_WorkflowRunStep_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_WorkflowRunStep_BlockUpdateDelete;
            IF OBJECT_ID(N'workflow.TR_WorkflowRunStep_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_WorkflowRunStep_ValidateInsert;
            IF OBJECT_ID(N'workflow.TR_WorkflowRun_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_WorkflowRun_BlockUpdateDelete;
            IF OBJECT_ID(N'workflow.TR_WorkflowRun_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_WorkflowRun_ValidateInsert;
            IF OBJECT_ID(N'workflow.WorkflowCheckpointGroundingReference', N'U') IS NOT NULL DROP TABLE workflow.WorkflowCheckpointGroundingReference;
            IF OBJECT_ID(N'workflow.WorkflowCheckpointEvidenceReference', N'U') IS NOT NULL DROP TABLE workflow.WorkflowCheckpointEvidenceReference;
            IF OBJECT_ID(N'workflow.WorkflowCheckpoint', N'U') IS NOT NULL DROP TABLE workflow.WorkflowCheckpoint;
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

    private Task ExecuteWorkflowCheckpointCreateViaSqlAsync(
        Guid workflowRunId,
        Guid workflowRunStepId,
        string checkpointKey,
        string safeSummary = "Direct SQL workflow checkpoint.",
        string metadataJson = "{\"schema\":\"workflow.checkpoint.metadata.v1\"}",
        string evidenceReferencesJson = "[]",
        string groundingReferencesJson = "[]",
        bool grantsExecution = false,
        bool promotesMemory = false,
        bool resumesWorkflow = false) =>
        ExecuteAsync(
            """
            EXEC workflow.usp_WorkflowCheckpoint_Create
                @WorkflowCheckpointId = @checkpointId,
                @WorkflowRunId = @runId,
                @WorkflowRunStepId = @stepId,
                @ProjectId = @projectId,
                @CheckpointKey = @checkpointKey,
                @CheckpointName = N'Direct SQL workflow checkpoint',
                @CheckpointType = N'ReviewSnapshot',
                @Status = N'Captured',
                @SubjectType = N'workflow_subject',
                @SubjectId = N'direct-sql-subject',
                @SafeSummary = @safeSummary,
                @StateVersion = 1,
                @StateJson = N'{"schema":"workflow.checkpoint.state.v1"}',
                @StateHashSha256 = N'abcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcdefabcd',
                @CorrelationId = @correlationId,
                @CausationId = @causationId,
                @CreatedByActorType = N'system_test_fixture',
                @CreatedByActorId = N'workflow-checkpoint-direct-sql',
                @MetadataVersion = 1,
                @MetadataJson = @metadataJson,
                @EvidenceReferencesJson = @evidenceReferencesJson,
                @GroundingReferencesJson = @groundingReferencesJson,
                @GrantsExecution = @grantsExecution,
                @PromotesMemory = @promotesMemory,
                @ResumesWorkflow = @resumesWorkflow;
            """,
            new
            {
                checkpointId = Guid.NewGuid(),
                runId = workflowRunId,
                stepId = workflowRunStepId,
                projectId = ProjectId,
                checkpointKey,
                safeSummary,
                correlationId = CorrelationId,
                causationId = CausationId,
                metadataJson,
                evidenceReferencesJson,
                groundingReferencesJson,
                grantsExecution,
                promotesMemory,
                resumesWorkflow
            });

    private async Task<int> CountIfExistsAsync(string tableName)
    {
        await using var connection = new SqlConnection(ConnectionString);
        var exists = await connection.ExecuteScalarAsync<int>($"SELECT CASE WHEN OBJECT_ID(N'{tableName}', N'U') IS NULL THEN 0 ELSE 1 END");
        if (exists == 0)
            return 0;

        return await connection.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM {tableName}");
    }
}
