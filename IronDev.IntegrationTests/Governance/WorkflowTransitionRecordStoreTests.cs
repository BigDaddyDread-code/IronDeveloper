using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;
using IronDev.Infrastructure.Governance;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowTransitionRecordStore")]
[TestCategory("RealDatabaseWorkflowTransitionRecordStoreSmoke")]
public sealed class WorkflowTransitionRecordStoreTests : IntegrationTestBase
{
    private static readonly DateTimeOffset TransitionedAtUtc = new(2026, 6, 17, 9, 30, 0, TimeSpan.Zero);
    private SqlWorkflowTransitionRecordStore _store = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropWorkflowTransitionRecordAsync();
        await ApplySqlFileAsync("Database", "migrate_workflow_transition_record.sql");
        _store = new SqlWorkflowTransitionRecordStore(ServiceProvider.GetRequiredService<IDbConnectionFactory>());
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropWorkflowTransitionRecordAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordStore_SaveAndGetRoundTripsRecord()
    {
        var record = ValidRecord("roundtrip");

        await _store.SaveAsync(record);
        var read = await _store.GetAsync(record.ProjectId, record.WorkflowTransitionRecordId);

        AssertRecord(record, read);
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordStore_GetByRecordHashRoundTripsRecord()
    {
        var record = ValidRecord("hash-read");

        await _store.SaveAsync(record);
        var read = await _store.GetByRecordHashAsync(record.ProjectId, record.WorkflowTransitionRecordHash);

        AssertRecord(record, read);
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordStore_ListByWorkflowRunReturnsProjectScopedRecords()
    {
        var projectId = Guid.NewGuid();
        var workflowRunId = "workflow-run-list";
        var matching = ValidRecord("run-a") with { ProjectId = projectId, WorkflowRunId = workflowRunId };
        matching = Rehash(matching);
        var otherProject = Rehash(ValidRecord("run-b") with { ProjectId = Guid.NewGuid(), WorkflowRunId = workflowRunId });
        var otherRun = Rehash(ValidRecord("run-c") with { ProjectId = projectId, WorkflowRunId = "workflow-run-other" });

        await SaveAllAsync(matching, otherProject, otherRun);

        AssertSingle(matching, await _store.ListByWorkflowRunAsync(projectId, workflowRunId));
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordStore_ListByWorkflowStepReturnsProjectScopedRecords()
    {
        var projectId = Guid.NewGuid();
        var workflowRunId = "workflow-run-step";
        var workflowStepId = "workflow-step-target";
        var matching = Rehash(ValidRecord("step-a") with { ProjectId = projectId, WorkflowRunId = workflowRunId, WorkflowStepId = workflowStepId });
        var otherProject = Rehash(ValidRecord("step-b") with { ProjectId = Guid.NewGuid(), WorkflowRunId = workflowRunId, WorkflowStepId = workflowStepId });
        var otherStep = Rehash(ValidRecord("step-c") with { ProjectId = projectId, WorkflowRunId = workflowRunId, WorkflowStepId = "workflow-step-other" });

        await SaveAllAsync(matching, otherProject, otherStep);

        AssertSingle(matching, await _store.ListByWorkflowStepAsync(projectId, workflowRunId, workflowStepId));
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordStore_ListByContinuationGateEvaluationReturnsProjectScopedRecords()
    {
        var projectId = Guid.NewGuid();
        var gateId = Guid.NewGuid();
        var matching = Rehash(ValidRecord("gate-a") with { ProjectId = projectId, WorkflowContinuationGateEvaluationId = gateId });
        var otherProject = Rehash(ValidRecord("gate-b") with { ProjectId = Guid.NewGuid(), WorkflowContinuationGateEvaluationId = gateId });
        var otherGate = Rehash(ValidRecord("gate-c") with { ProjectId = projectId, WorkflowContinuationGateEvaluationId = Guid.NewGuid() });

        await SaveAllAsync(matching, otherProject, otherGate);

        AssertSingle(matching, await _store.ListByContinuationGateEvaluationAsync(projectId, gateId));
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordStore_ListBySourceApplyReceiptReturnsProjectScopedRecords()
    {
        var projectId = Guid.NewGuid();
        var sourceApplyReceiptId = Guid.NewGuid();
        var matching = Rehash(ValidRecord("source-a") with { ProjectId = projectId, SourceApplyReceiptId = sourceApplyReceiptId });
        var otherProject = Rehash(ValidRecord("source-b") with { ProjectId = Guid.NewGuid(), SourceApplyReceiptId = sourceApplyReceiptId });
        var otherReceipt = Rehash(ValidRecord("source-c") with { ProjectId = projectId, SourceApplyReceiptId = Guid.NewGuid() });

        await SaveAllAsync(matching, otherProject, otherReceipt);

        AssertSingle(matching, await _store.ListBySourceApplyReceiptAsync(projectId, sourceApplyReceiptId));
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordStore_ListByRollbackExecutionReceiptReturnsProjectScopedRecords()
    {
        var projectId = Guid.NewGuid();
        var rollbackReceiptId = Guid.NewGuid();
        var matching = Rehash(ValidRecord("rollback-a") with { ProjectId = projectId, RollbackExecutionReceiptId = rollbackReceiptId, RollbackExecutionReceiptHash = H("rollback-shared") });
        var otherProject = Rehash(ValidRecord("rollback-b") with { ProjectId = Guid.NewGuid(), RollbackExecutionReceiptId = rollbackReceiptId, RollbackExecutionReceiptHash = H("rollback-shared") });
        var otherReceipt = Rehash(ValidRecord("rollback-c") with { ProjectId = projectId, RollbackExecutionReceiptId = Guid.NewGuid(), RollbackExecutionReceiptHash = H("rollback-other") });

        await SaveAllAsync(matching, otherProject, otherReceipt);

        AssertSingle(matching, await _store.ListByRollbackExecutionReceiptAsync(projectId, rollbackReceiptId));
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordStore_RejectsInvalidRecordBeforePersistence()
    {
        var invalid = ValidRecord("invalid") with { WorkflowRunId = " " };

        var ex = await AssertThrowsAsync<ArgumentException>(() => _store.SaveAsync(invalid));

        StringAssert.Contains(ex.Message, "Required");
        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.WorkflowTransitionRecord"));
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordStore_RejectsReleaseReadinessInference()
    {
        await AssertInvalidAsync(Rehash(ValidRecord("release-ready") with { ReleaseReadinessInferred = true }), "ReleaseReadinessInferenceRejected");
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordStore_RejectsReleaseApproval()
    {
        await AssertInvalidAsync(Rehash(ValidRecord("release-approval") with { ReleaseApproved = true }), "ReleaseApprovalRejected");
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordStore_RejectsSourceApplyExecutedFlag()
    {
        await AssertInvalidAsync(Rehash(ValidRecord("source-apply-executed") with { SourceApplyExecuted = true }), "SourceApplyExecutionRejected");
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordStore_RejectsRollbackExecutedFlag()
    {
        await AssertInvalidAsync(Rehash(ValidRecord("rollback-executed") with { RollbackExecuted = true }), "RollbackExecutionRejected");
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordStore_RejectsInvalidTruthTable()
    {
        await AssertInvalidAsync(Rehash(ValidRecord("truth") with { NextStepStarted = false }), "ContinueRequiresNextStepStarted");
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordStore_SaveSameIdSameHashIsIdempotent()
    {
        var record = ValidRecord("idempotent");

        await _store.SaveAsync(record);
        await _store.SaveAsync(record);

        Assert.AreEqual(1, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.WorkflowTransitionRecord WHERE WorkflowTransitionRecordId = @id", new { id = record.WorkflowTransitionRecordId }));
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordStore_SaveSameIdDifferentHashFails()
    {
        var record = ValidRecord("conflict-a");
        var conflict = Rehash(ValidRecord("conflict-b") with { WorkflowTransitionRecordId = record.WorkflowTransitionRecordId });

        await _store.SaveAsync(record);

        await AssertThrowsAsync<SqlException>(() => _store.SaveAsync(conflict));
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordStore_DirectSqlUpdateIsBlocked()
    {
        var record = ValidRecord("update-blocked");
        await _store.SaveAsync(record);

        await AssertSqlFailsAsync("UPDATE governance.WorkflowTransitionRecord SET WorkflowRunId = N'changed' WHERE WorkflowTransitionRecordId = @id", new SqlParameter("@id", record.WorkflowTransitionRecordId));
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordStore_DirectSqlDeleteIsBlocked()
    {
        var record = ValidRecord("delete-blocked");
        await _store.SaveAsync(record);

        await AssertSqlFailsAsync("DELETE FROM governance.WorkflowTransitionRecord WHERE WorkflowTransitionRecordId = @id", new SqlParameter("@id", record.WorkflowTransitionRecordId));
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordStore_DirectSqlInsertRejectsRawPrivateMaterial()
    {
        await AssertSqlFailsAsync(DirectInsertSql(), DirectInsertParameters("raw-direct", "private reasoning leaked"));
    }

    [TestMethod]
    public async Task WorkflowTransitionRecordStore_DirectSqlInsertRejectsAuthorityClaims()
    {
        await AssertSqlFailsAsync(DirectInsertSql(), DirectInsertParameters("authority-direct", "workflow continued automatically"));
    }

    [TestMethod]
    public void WorkflowTransitionRecordStore_RuntimeRoleCannotMutateTableDirectly()
    {
        var sql = File.ReadAllText(SqlMigrationPath());

        StringAssert.Contains(sql, "GRANT EXECUTE ON OBJECT::governance.usp_WorkflowTransitionRecord_Save");
        StringAssert.Contains(sql, "DENY INSERT, UPDATE, DELETE ON OBJECT::governance.WorkflowTransitionRecord");
        StringAssert.Contains(sql, "DENY ALTER ON SCHEMA::governance");
    }

    [TestMethod]
    public void WorkflowTransitionRecordStore_InterfaceIsAppendOnlyReadSurface()
    {
        var names = typeof(IWorkflowTransitionRecordStore)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .OrderBy(name => name)
            .ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                nameof(IWorkflowTransitionRecordStore.GetAsync),
                nameof(IWorkflowTransitionRecordStore.GetByRecordHashAsync),
                nameof(IWorkflowTransitionRecordStore.ListByContinuationGateEvaluationAsync),
                nameof(IWorkflowTransitionRecordStore.ListByRollbackExecutionReceiptAsync),
                nameof(IWorkflowTransitionRecordStore.ListBySourceApplyReceiptAsync),
                nameof(IWorkflowTransitionRecordStore.ListByWorkflowRunAsync),
                nameof(IWorkflowTransitionRecordStore.ListByWorkflowStepAsync),
                nameof(IWorkflowTransitionRecordStore.SaveAsync)
            },
            names);

        foreach (var forbidden in new[] { "Update", "Delete", "Upsert", "Continue", "Advance", "Approve", "Execute", "Complete", "StartNext" })
        {
            Assert.IsFalse(names.Any(name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)), $"Unexpected method token: {forbidden}");
        }
    }

    [TestMethod]
    public void WorkflowTransitionRecordStore_MigrationManifestInventoryVerifierAndReceiptAreUpdated()
    {
        var manifest = File.ReadAllText(Path.Combine(RepoRoot(), "Database", "migrations.json"));
        var inventory = File.ReadAllText(Path.Combine(RepoRoot(), "Database", "sql-inventory.json"));
        var verifier = File.ReadAllText(Path.Combine(RepoRoot(), "Database", "verify-migrations.ps1"));
        var sql = File.ReadAllText(SqlMigrationPath());
        var receipt = File.ReadAllText(ReceiptPath());

        StringAssert.Contains(manifest, "Database/migrate_workflow_transition_record.sql");
        StringAssert.Contains(inventory, "database.migrate-workflow-transition-record");
        StringAssert.Contains(inventory, "runtime.workflow-transition-record-store");
        StringAssert.Contains(verifier, "governance.WorkflowTransitionRecord table");
        StringAssert.Contains(sql, "CREATE TABLE governance.WorkflowTransitionRecord");
        StringAssert.Contains(sql, "TR_WorkflowTransitionRecord_ValidateInsert");
        StringAssert.Contains(sql, "TR_WorkflowTransitionRecord_BlockUpdateDelete");
        StringAssert.Contains(receipt, "PR212 adds durable workflow transition record storage only.");
        StringAssert.Contains(receipt, "Workflow transition store is not workflow transition.");
    }

    [TestMethod]
    public void WorkflowTransitionRecordStore_DoesNotAddApiCliUiRuntimeOrExecutionSurface()
    {
        foreach (var token in new[]
        {
            "File.WriteAllText",
            "File.WriteAllBytes",
            "File.Delete",
            "File.Move",
            "Directory.CreateDirectory",
            "Process.Start",
            "ProcessStartInfo",
            "git commit",
            "git push",
            "git merge",
            "gh pr",
            "ControllerBase",
            "HttpPost",
            "HttpPut",
            "HttpPatch",
            "HttpDelete",
            "IHostedService",
            "BackgroundService",
            "Scheduler",
            "WorkflowTransitionExecutor",
            "WorkflowContinuationExecutor",
            "ContinueWorkflow",
            "AdvanceWorkflow",
            "ControlledSourceApplyExecutor",
            "ControlledRollbackExecutor",
            "AgentDispatch",
            "ModelProvider",
            "ToolInvoker",
            "PromoteMemory",
            "ActivateRetrieval",
            "Weaviate",
            "Embedding"
        })
        {
            AssertNoProductionToken(token);
        }
    }

    [TestMethod]
    public void WorkflowTransitionRecordStore_ReceiptStatesBoundary()
    {
        var receipt = File.ReadAllText(ReceiptPath());

        foreach (var statement in new[]
        {
            "PR212 persists validated WorkflowTransitionRecord evidence.",
            "PR212 does not transition workflow.",
            "PR212 does not mutate workflow state.",
            "PR212 does not continue workflow.",
            "PR212 does not complete workflow steps.",
            "PR212 does not start next workflow steps.",
            "PR212 does not approve release.",
            "PR212 does not infer release readiness.",
            "PR212 does not declare rollback cleanup.",
            "PR212 does not execute rollback.",
            "Workflow transition store is not workflow continuation.",
            "Stored WorkflowTransitionRecord is evidence only.",
            "Stored WorkflowTransitionRecord is not ReleaseReady.",
            "Stored WorkflowTransitionRecord is not ReleaseApproved.",
            "Human review remains required for release readiness and release approval.",
            "PR212 puts the movement receipt in the vault. It does not move the workflow."
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    private async Task AssertInvalidAsync(WorkflowTransitionRecord invalid, string expectedCode)
    {
        var ex = await AssertThrowsAsync<ArgumentException>(() => _store.SaveAsync(invalid));
        StringAssert.Contains(ex.Message, expectedCode);
        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM governance.WorkflowTransitionRecord"));
    }

    private async Task SaveAllAsync(params WorkflowTransitionRecord[] records)
    {
        foreach (var record in records)
        {
            await _store.SaveAsync(record);
        }
    }

    private static WorkflowTransitionRecord ValidRecord(string suffix = "main") =>
        Rehash(new WorkflowTransitionRecord
        {
            WorkflowTransitionRecordId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            WorkflowRunId = $"workflow-run-{suffix}",
            WorkflowStepId = $"workflow-step-{suffix}",
            TransitionKind = WorkflowTransitionKinds.ContinueToNextStep,
            PreviousWorkflowStateHash = H($"workflow-before-{suffix}"),
            NewWorkflowStateHash = H($"workflow-after-{suffix}"),
            PreviousStepStateHash = H($"step-before-{suffix}"),
            NewStepStateHash = H($"step-after-{suffix}"),
            PreviousStepId = $"workflow-step-{suffix}",
            NextStepId = $"workflow-step-next-{suffix}",
            WorkflowContinuationGateEvaluationId = Guid.NewGuid(),
            WorkflowContinuationGateEvaluationHash = H($"gate-{suffix}"),
            SourceApplyRequestId = Guid.NewGuid(),
            SourceApplyRequestHash = H($"source-apply-request-{suffix}"),
            SourceApplyReceiptId = Guid.NewGuid(),
            SourceApplyReceiptHash = H($"source-apply-receipt-{suffix}"),
            RollbackExecutionReceiptId = Guid.NewGuid(),
            RollbackExecutionReceiptHash = H($"rollback-execution-receipt-{suffix}"),
            RollbackExecutionAuditReportId = Guid.NewGuid(),
            RollbackExecutionAuditReportHash = H($"rollback-execution-audit-{suffix}"),
            WorkflowStateMutated = true,
            StepCompleted = true,
            NextStepStarted = true,
            ReleaseReadinessInferred = false,
            ReleaseApproved = false,
            SourceApplyExecuted = false,
            RollbackExecuted = false,
            TransitionedAtUtc = TransitionedAtUtc.AddMinutes(Math.Abs(suffix.GetHashCode(StringComparison.Ordinal)) % 1000),
            WorkflowTransitionRecordHash = H($"placeholder-{suffix}"),
            EvidenceReferences = [$"gate:{suffix}", $"source-apply:{suffix}", $"rollback:{suffix}"],
            BoundaryMaxims = ["Workflow transition record is evidence only.", "Human review remains required for governed release decisions."],
            Boundary = WorkflowTransitionRecordBoundaryText.Boundary
        });

    private static WorkflowTransitionRecord Rehash(WorkflowTransitionRecord record) =>
        record with { WorkflowTransitionRecordHash = WorkflowTransitionRecordHashing.ComputeRecordHash(record) };

    private static string H(string suffix) => $"sha256:{suffix}";

    private static void AssertSingle(WorkflowTransitionRecord expected, IReadOnlyList<WorkflowTransitionRecord> actual)
    {
        Assert.AreEqual(1, actual.Count);
        AssertRecord(expected, actual[0]);
    }

    private static void AssertRecord(WorkflowTransitionRecord expected, WorkflowTransitionRecord? actual)
    {
        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.WorkflowTransitionRecordId, actual.WorkflowTransitionRecordId);
        Assert.AreEqual(expected.ProjectId, actual.ProjectId);
        Assert.AreEqual(expected.WorkflowRunId, actual.WorkflowRunId);
        Assert.AreEqual(expected.WorkflowStepId, actual.WorkflowStepId);
        Assert.AreEqual(expected.TransitionKind, actual.TransitionKind);
        Assert.AreEqual(expected.PreviousWorkflowStateHash, actual.PreviousWorkflowStateHash);
        Assert.AreEqual(expected.NewWorkflowStateHash, actual.NewWorkflowStateHash);
        Assert.AreEqual(expected.PreviousStepStateHash, actual.PreviousStepStateHash);
        Assert.AreEqual(expected.NewStepStateHash, actual.NewStepStateHash);
        Assert.AreEqual(expected.PreviousStepId, actual.PreviousStepId);
        Assert.AreEqual(expected.NextStepId, actual.NextStepId);
        Assert.AreEqual(expected.WorkflowContinuationGateEvaluationId, actual.WorkflowContinuationGateEvaluationId);
        Assert.AreEqual(expected.WorkflowContinuationGateEvaluationHash, actual.WorkflowContinuationGateEvaluationHash);
        Assert.AreEqual(expected.SourceApplyRequestId, actual.SourceApplyRequestId);
        Assert.AreEqual(expected.SourceApplyRequestHash, actual.SourceApplyRequestHash);
        Assert.AreEqual(expected.SourceApplyReceiptId, actual.SourceApplyReceiptId);
        Assert.AreEqual(expected.SourceApplyReceiptHash, actual.SourceApplyReceiptHash);
        Assert.AreEqual(expected.RollbackExecutionReceiptId, actual.RollbackExecutionReceiptId);
        Assert.AreEqual(expected.RollbackExecutionReceiptHash, actual.RollbackExecutionReceiptHash);
        Assert.AreEqual(expected.RollbackExecutionAuditReportId, actual.RollbackExecutionAuditReportId);
        Assert.AreEqual(expected.RollbackExecutionAuditReportHash, actual.RollbackExecutionAuditReportHash);
        Assert.AreEqual(expected.WorkflowStateMutated, actual.WorkflowStateMutated);
        Assert.AreEqual(expected.StepCompleted, actual.StepCompleted);
        Assert.AreEqual(expected.NextStepStarted, actual.NextStepStarted);
        Assert.AreEqual(expected.ReleaseReadinessInferred, actual.ReleaseReadinessInferred);
        Assert.AreEqual(expected.ReleaseApproved, actual.ReleaseApproved);
        Assert.AreEqual(expected.SourceApplyExecuted, actual.SourceApplyExecuted);
        Assert.AreEqual(expected.RollbackExecuted, actual.RollbackExecuted);
        Assert.AreEqual(expected.TransitionedAtUtc, actual.TransitionedAtUtc);
        Assert.AreEqual(expected.WorkflowTransitionRecordHash, actual.WorkflowTransitionRecordHash);
        CollectionAssert.AreEqual(expected.EvidenceReferences.ToArray(), actual.EvidenceReferences.ToArray());
        CollectionAssert.AreEqual(expected.BoundaryMaxims.ToArray(), actual.BoundaryMaxims.ToArray());
        Assert.AreEqual(expected.Boundary, actual.Boundary);
    }

    private static string DirectInsertSql() =>
        @"INSERT INTO governance.WorkflowTransitionRecord
          (WorkflowTransitionRecordId, ProjectId, WorkflowRunId, WorkflowStepId, TransitionKind, PreviousWorkflowStateHash, NewWorkflowStateHash, PreviousStepStateHash, NewStepStateHash, PreviousStepId, NextStepId, WorkflowContinuationGateEvaluationId, WorkflowContinuationGateEvaluationHash, SourceApplyRequestId, SourceApplyRequestHash, SourceApplyReceiptId, SourceApplyReceiptHash, RollbackExecutionReceiptId, RollbackExecutionReceiptHash, RollbackExecutionAuditReportId, RollbackExecutionAuditReportHash, WorkflowStateMutated, StepCompleted, NextStepStarted, ReleaseReadinessInferred, ReleaseApproved, SourceApplyExecuted, RollbackExecuted, TransitionedAtUtc, WorkflowTransitionRecordHash, EvidenceReferencesJson, BoundaryMaximsJson, BoundaryText)
          VALUES (NEWID(), NEWID(), CONCAT(N'workflow-run-', @suffix), CONCAT(N'workflow-step-', @suffix), N'ContinueToNextStep', CONCAT(N'sha256:workflow-before-', @suffix), CONCAT(N'sha256:workflow-after-', @suffix), CONCAT(N'sha256:step-before-', @suffix), CONCAT(N'sha256:step-after-', @suffix), CONCAT(N'workflow-step-', @suffix), CONCAT(N'workflow-step-next-', @suffix), NEWID(), CONCAT(N'sha256:gate-', @suffix), NEWID(), CONCAT(N'sha256:source-request-', @suffix), NEWID(), CONCAT(N'sha256:source-receipt-', @suffix), NEWID(), CONCAT(N'sha256:rollback-receipt-', @suffix), NEWID(), CONCAT(N'sha256:rollback-audit-', @suffix), 1, 1, 1, 0, 0, 0, 0, SYSUTCDATETIME(), CONCAT(N'sha256:transition-record-', @suffix), N'[""gate:evidence""]', N'[""store evidence only""]', @boundaryText)";

    private static SqlParameter[] DirectInsertParameters(string suffix, string boundaryText) =>
    [
        new SqlParameter("@suffix", suffix),
        new SqlParameter("@boundaryText", boundaryText)
    ];

    private async Task ApplySqlFileAsync(params string[] pathParts)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(new[] { RepoRoot() }.Concat(pathParts).ToArray()));
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(batch))
            {
                await connection.ExecuteAsync(batch);
            }
        }
    }

    private async Task DropWorkflowTransitionRecordAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            IF OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_Save', N'P') IS NOT NULL DROP PROCEDURE governance.usp_WorkflowTransitionRecord_Save;
            IF OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_Get', N'P') IS NOT NULL DROP PROCEDURE governance.usp_WorkflowTransitionRecord_Get;
            IF OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_GetByRecordHash', N'P') IS NOT NULL DROP PROCEDURE governance.usp_WorkflowTransitionRecord_GetByRecordHash;
            IF OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_ListByWorkflowRun', N'P') IS NOT NULL DROP PROCEDURE governance.usp_WorkflowTransitionRecord_ListByWorkflowRun;
            IF OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_ListByWorkflowStep', N'P') IS NOT NULL DROP PROCEDURE governance.usp_WorkflowTransitionRecord_ListByWorkflowStep;
            IF OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_ListByContinuationGateEvaluation', N'P') IS NOT NULL DROP PROCEDURE governance.usp_WorkflowTransitionRecord_ListByContinuationGateEvaluation;
            IF OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_ListBySourceApplyReceipt', N'P') IS NOT NULL DROP PROCEDURE governance.usp_WorkflowTransitionRecord_ListBySourceApplyReceipt;
            IF OBJECT_ID(N'governance.usp_WorkflowTransitionRecord_ListByRollbackExecutionReceipt', N'P') IS NOT NULL DROP PROCEDURE governance.usp_WorkflowTransitionRecord_ListByRollbackExecutionReceipt;
            IF OBJECT_ID(N'governance.TR_WorkflowTransitionRecord_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_WorkflowTransitionRecord_ValidateInsert;
            IF OBJECT_ID(N'governance.TR_WorkflowTransitionRecord_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_WorkflowTransitionRecord_BlockUpdateDelete;
            IF OBJECT_ID(N'governance.WorkflowTransitionRecord', N'U') IS NOT NULL DROP TABLE governance.WorkflowTransitionRecord;
            """);
    }

    private async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<T>(sql, parameters) ?? throw new InvalidOperationException("Scalar query returned null.");
    }

    private async Task AssertSqlFailsAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await AssertThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
    }

    private static async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }

        Assert.Fail($"Expected exception of type {typeof(TException).Name}.");
        throw new UnreachableException();
    }

    private static void AssertNoProductionToken(string token)
    {
        foreach (var file in ProductionFiles())
        {
            Assert.IsFalse(File.ReadAllText(file).Contains(token, StringComparison.Ordinal), $"Unexpected production token {token} in {file}.");
        }
    }

    private static string[] ProductionFiles()
    {
        var root = RepoRoot();
        return
        [
            Path.Combine(root, "IronDev.Core", "Governance", "WorkflowTransitionRecordStore.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Governance", "SqlWorkflowTransitionRecordStore.cs")
        ];
    }

    private static string SqlMigrationPath() =>
        Path.Combine(RepoRoot(), "Database", "migrate_workflow_transition_record.sql");

    private static string ReceiptPath() =>
        Path.Combine(RepoRoot(), "Docs", "receipts", "PR212_WORKFLOW_TRANSITION_STORE.md");

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
