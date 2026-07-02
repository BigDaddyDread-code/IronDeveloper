using Dapper;
using IronDev.Core.Workflow;
using IronDev.Data;
using IronDev.Infrastructure.Workflow;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("RequiresRealDatabase")]
[TestCategory("LongRunning")]
[TestClass]
[TestCategory("ApplyDryRunStore")]
[TestCategory("RealDatabaseApplyDryRunStoreSmoke")]
public sealed class ApplyDryRunStoreTests : IntegrationTestBase
{
    private SqlApplyDryRunStore _store = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropApplyDryRunSchemaAsync();
        await ApplySqlFileAsync("Database", "migrate_apply_dry_run_store.sql");

        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        _store = new SqlApplyDryRunStore(connectionFactory);
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropApplyDryRunSchemaAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public void ApplyDryRunStore_ExposesRecordOnlyContractWithoutActionMethods()
    {
        var methods = typeof(IApplyDryRunStore).GetMethods().Select(method => method.Name).OrderBy(name => name).ToArray();
        CollectionAssert.AreEquivalent(
            new[] { "CreateAsync", "GetByIdAsync", "ListByControlledApplyPlanAsync", "ListByWorkflowRunAsync" },
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
            "Rollback");
    }

    [TestMethod]
    public async Task ApplyDryRunMigration_AddsTableProceduresTriggersConstraintsAndRuntimeProtection()
    {
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.ApplyDryRunRecord', N'U') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.usp_ApplyDryRun_Create', N'P') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.usp_ApplyDryRun_Get', N'P') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.usp_ApplyDryRun_ListByWorkflowRun', N'P') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.usp_ApplyDryRun_ListByControlledApplyPlan', N'P') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.TR_ApplyDryRunRecord_ValidateInsert', N'TR') IS NULL THEN 0 ELSE 1 END"));
        Assert.AreEqual(1, await ScalarAsync<int>("SELECT CASE WHEN OBJECT_ID(N'workflow.TR_ApplyDryRunRecord_BlockUpdateDelete', N'TR') IS NULL THEN 0 ELSE 1 END"));

        var constraints = (await QueryAsync<string>("SELECT name FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'workflow.ApplyDryRunRecord')")).ToArray();
        CollectionAssert.Contains(constraints, "CK_ApplyDryRunRecord_RecordOnly");
        CollectionAssert.Contains(constraints, "CK_ApplyDryRunRecord_NoDryRunPerformed");
        CollectionAssert.Contains(constraints, "CK_ApplyDryRunRecord_NoApplySource");
        CollectionAssert.Contains(constraints, "CK_ApplyDryRunRecord_NoMemoryPromotion");
        CollectionAssert.Contains(constraints, "CK_ApplyDryRunRecord_NoWorkflowTransition");
    }

    [TestMethod]
    public async Task ApplyDryRunStore_CreatesReadsAndListsStoredReceiptWithoutAuthority()
    {
        var created = await _store.CreateAsync(ValidRequest());

        Assert.AreEqual(ApplyDryRunStoreStatus.Stored, created.Status);
        Assert.IsNotNull(created.Record);
        Assert.AreEqual("dryrun-pr140-main", created.Record.DryRunId);
        Assert.AreEqual(ApplyDryRunRecordStatus.Stored, created.Record.Status);
        Assert.AreEqual(ApplyDryRunOutcomeKind.NotPerformed, created.Record.OutcomeKind);
        Assert.AreEqual(2, created.Record.EvidenceReferences.Count);
        Assert.AreEqual(2, created.Record.GateReferences.Count);
        Assert.AreEqual(1, created.Record.ValidationReferences.Count);
        Assert.AreEqual(1, created.Record.RollbackReferences.Count);
        Assert.AreEqual(1, created.Record.Risks.Count);
        Assert.AreEqual(1, created.Record.MissingEvidence.Count);
        AssertNoAuthority(created.Record);

        var read = await _store.GetByIdAsync("dryrun-pr140-main");
        Assert.IsNotNull(read);
        Assert.AreEqual(created.Record.DryRunId, read.DryRunId);
        AssertNoAuthority(read);

        var byRun = await _store.ListByWorkflowRunAsync("workflow-run-pr140", 10);
        var byPlan = await _store.ListByControlledApplyPlanAsync("controlled-apply-plan-pr139", 10);

        Assert.AreEqual(1, byRun.Count);
        Assert.AreEqual(1, byPlan.Count);
        Assert.AreEqual("dryrun-pr140-main", byRun[0].DryRunId);
        Assert.AreEqual(2, byRun[0].EvidenceReferenceCount);
        Assert.AreEqual(2, byRun[0].GateReferenceCount);
        Assert.AreEqual(1, byRun[0].ValidationReferenceCount);
        Assert.AreEqual(1, byRun[0].RollbackReferenceCount);
    }

    [TestMethod]
    public async Task ApplyDryRunStore_RejectsInvalidUnsafeAndAuthorityFlaggedRequestsBeforePersistence()
    {
        var missing = await _store.CreateAsync(ValidRequest() with { DryRunId = "" });
        var unsafeText = await _store.CreateAsync(ValidRequest() with { SafeSummary = "rawPrompt leaked" });
        var authorityFlag = await _store.CreateAsync(ValidRequest() with { CanApplySource = true });
        var unsafeMetadata = await _store.CreateAsync(ValidRequest() with { MetadataJson = "{\"schema\":\"apply.dryrun.store.v1\",\"approvalGranted\":true}" });
        var missingEvidence = await _store.CreateAsync(ValidRequest() with { EvidenceReferences = [] });

        Assert.AreEqual(ApplyDryRunStoreStatus.InvalidRequest, missing.Status);
        Assert.AreEqual(ApplyDryRunStoreStatus.UnsafeMaterialRejected, unsafeText.Status);
        Assert.AreEqual(ApplyDryRunStoreStatus.InvalidRequest, authorityFlag.Status);
        Assert.AreEqual(ApplyDryRunStoreStatus.UnsafeMaterialRejected, unsafeMetadata.Status);
        Assert.AreEqual(ApplyDryRunStoreStatus.InvalidRequest, missingEvidence.Status);
        Assert.AreEqual(0, await ScalarAsync<int>("SELECT COUNT(1) FROM workflow.ApplyDryRunRecord"));
    }

    [TestMethod]
    public async Task ApplyDryRunSql_RejectsDuplicateUnsafeMarkersAuthorityFlagsUpdateAndDelete()
    {
        var created = await _store.CreateAsync(ValidRequest());
        Assert.AreEqual(ApplyDryRunStoreStatus.Stored, created.Status);

        var duplicate = await _store.CreateAsync(ValidRequest());
        Assert.AreEqual(ApplyDryRunStoreStatus.DuplicateRejected, duplicate.Status);

        await ExpectThrowsAsync<SqlException>(() => ExecuteDryRunCreateViaSqlAsync("direct-raw-prompt", safeSummary: "rawPrompt leaked"));
        await ExpectThrowsAsync<SqlException>(() => ExecuteDryRunCreateViaSqlAsync("direct-raw-completion", metadataJson: "{\"schema\":\"apply.dryrun.store.v1\",\"rawCompletion\":\"leaked\"}"));
        await ExpectThrowsAsync<SqlException>(() => ExecuteDryRunCreateViaSqlAsync("direct-raw-tool", evidenceReferencesJson: """[{"kind":"ControlledApplyPlan","referenceId":"evidence-1","safeSummary":"rawToolOutput leaked"}]"""));
        await ExpectThrowsAsync<SqlException>(() => ExecuteDryRunCreateViaSqlAsync("direct-entire-patch", validationReferencesJson: """[{"kind":"ValidationEvidence","referenceId":"validation-1","safeSummary":"entirePatch leaked"}]"""));
        await ExpectThrowsAsync<SqlException>(() => ExecuteDryRunCreateViaSqlAsync("direct-chain", gateReferencesJson: """[{"kind":"ReviewRequired","referenceId":"gate-1","safeSummary":"chainOfThought leaked"}]"""));
        await ExpectThrowsAsync<SqlException>(() => ExecuteDryRunCreateViaSqlAsync("direct-action-flag", canApplySource: true));

        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync("UPDATE workflow.ApplyDryRunRecord SET Status = N'InvalidRecord' WHERE DryRunId = @id", new { id = "dryrun-pr140-main" }));
        await ExpectThrowsAsync<SqlException>(() => ExecuteAsync("DELETE FROM workflow.ApplyDryRunRecord WHERE DryRunId = @id", new { id = "dryrun-pr140-main" }));
    }

    [TestMethod]
    public async Task ApplyDryRunStore_DoesNotCreateApprovalPolicyWorkflowToolA2aSourceMemoryOrAuditSideEffects()
    {
        var result = await _store.CreateAsync(ValidRequest());
        Assert.AreEqual(ApplyDryRunStoreStatus.Stored, result.Status);

        Assert.AreEqual(0, await CountIfExistsAsync("governance.ApprovalDecision"));
        Assert.AreEqual(0, await CountIfExistsAsync("governance.PolicyDecisionEvent"));
        Assert.AreEqual(0, await CountIfExistsAsync("governance.ToolGateDecision"));
        Assert.AreEqual(0, await CountIfExistsAsync("governance.ToolRequest"));
        Assert.AreEqual(0, await CountIfExistsAsync("a2a.AgentHandoff"));
        Assert.AreEqual(0, await CountIfExistsAsync("memory.MemoryProposal"));
        Assert.AreEqual(0, await CountIfExistsAsync("agent.AgentLocalMemory"));
        Assert.AreEqual(0, await CountIfExistsAsync("dbo.ToolExecutionAuditRecord"));
        Assert.AreEqual(0, await CountIfExistsAsync("dbo.AgentRunAuditEnvelope"));
    }

    [TestMethod]
    public async Task ApplyDryRunStore_ListQueriesAreScopedToRequestedRunOrPlan()
    {
        _ = await _store.CreateAsync(ValidRequest() with { DryRunId = "dryrun-A", WorkflowRunId = "workflow-run-A", ControlledApplyPlanReferenceId = "plan-A" });
        _ = await _store.CreateAsync(ValidRequest() with { DryRunId = "dryrun-B", WorkflowRunId = "workflow-run-B", ControlledApplyPlanReferenceId = "plan-A" });
        _ = await _store.CreateAsync(ValidRequest() with { DryRunId = "dryrun-C", WorkflowRunId = "workflow-run-A", ControlledApplyPlanReferenceId = "plan-C" });

        var byRun = await _store.ListByWorkflowRunAsync("workflow-run-A", 10);
        var byPlan = await _store.ListByControlledApplyPlanAsync("plan-A", 10);

        CollectionAssert.AreEquivalent(new[] { "dryrun-A", "dryrun-C" }, byRun.Select(summary => summary.DryRunId).ToArray());
        CollectionAssert.AreEquivalent(new[] { "dryrun-A", "dryrun-B" }, byPlan.Select(summary => summary.DryRunId).ToArray());
    }

    private static ApplyDryRunCreateRequest ValidRequest() =>
        new()
        {
            DryRunId = "dryrun-pr140-main",
            WorkflowRunId = "workflow-run-pr140",
            WorkflowStepId = "workflow-step-pr140",
            ControlledApplyPlanReferenceId = "controlled-apply-plan-pr139",
            SourceApplyApprovalRequirementReferenceId = "source-approval-requirement-pr137",
            PatchProposalEvidencePackageReferenceId = "patch-proposal-evidence-pr138",
            ProjectReferenceId = "project-pr140",
            TargetReferenceId = "target-pr140",
            Status = ApplyDryRunRecordStatus.Stored,
            OutcomeKind = ApplyDryRunOutcomeKind.NotPerformed,
            SafeSummary = "Stored dry-run receipt for human review only.",
            EvidenceReferences =
            [
                Reference(ApplyDryRunReferenceKind.ControlledApplyPlan, "controlled-apply-plan-pr139"),
                Reference(ApplyDryRunReferenceKind.PatchProposalEvidencePackage, "patch-proposal-evidence-pr138")
            ],
            GateReferences =
            [
                Gate(ApplyDryRunGateKind.SourceChangeForbidden, "source-change-gate-pr140"),
                Gate(ApplyDryRunGateKind.ReviewRequired, "human-review-gate-pr140")
            ],
            ValidationReferences = [Reference(ApplyDryRunReferenceKind.ValidationEvidence, "validation-reference-pr140")],
            RollbackReferences = [Reference(ApplyDryRunReferenceKind.RollbackEvidence, "rollback-reference-pr140")],
            Risks =
            [
                new ApplyDryRunRisk
                {
                    Kind = ApplyDryRunRiskKind.SourceChangeRisk,
                    Severity = ApplyDryRunRiskSeverity.Medium,
                    RiskId = "risk-pr140",
                    SafeSummary = "Source change requires human review before any later implementation."
                }
            ],
            MissingEvidence =
            [
                new ApplyDryRunMissingEvidence
                {
                    Kind = ApplyDryRunReferenceKind.ValidationEvidence,
                    ReferenceId = "validation-result-missing-pr140",
                    SafeSummary = "Separate validation evidence is still required."
                }
            ],
            CorrelationId = "correlation-pr140",
            MetadataJson = "{\"schema\":\"apply.dryrun.store.v1\",\"recordOnly\":true}"
        };

    private static ApplyDryRunReference Reference(ApplyDryRunReferenceKind kind, string id) =>
        new()
        {
            Kind = kind,
            ReferenceId = id,
            SafeSummary = "Reference supports dry-run review evidence only."
        };

    private static ApplyDryRunGateReference Gate(ApplyDryRunGateKind kind, string id) =>
        new()
        {
            Kind = kind,
            ReferenceId = id,
            SafeSummary = "Gate remains unsatisfied and does not permit action."
        };

    private static void AssertNoAuthority(ApplyDryRunRecord record)
    {
        Assert.IsTrue(record.IsStoreRecordOnly);
        Assert.IsFalse(record.IsDryRunPerformed);
        Assert.IsFalse(record.IsSourceApply);
        Assert.IsFalse(record.IsPatchApplication);
        Assert.IsFalse(record.IsApproval);
        Assert.IsFalse(record.IsApprovalSatisfied);
        Assert.IsFalse(record.CanPerformDryRun);
        Assert.IsFalse(record.CanApplySource);
        Assert.IsFalse(record.CanMutateFiles);
        Assert.IsFalse(record.CanReadSourceFiles);
        Assert.IsFalse(record.CanRunCommand);
        Assert.IsFalse(record.CanInvokeTool);
        Assert.IsFalse(record.CanRunValidation);
        Assert.IsFalse(record.CanRollback);
        Assert.IsFalse(record.CanSatisfyPolicy);
        Assert.IsFalse(record.CanTransitionWorkflow);
        Assert.IsFalse(record.CanPromoteMemory);
        Assert.IsFalse(record.CanActivateRetrieval);
    }

    private Task ExecuteDryRunCreateViaSqlAsync(
        string dryRunId,
        string safeSummary = "Direct SQL dry-run receipt.",
        string evidenceReferencesJson = """[{"kind":"ControlledApplyPlan","referenceId":"plan-direct","safeSummary":"Evidence only."}]""",
        string gateReferencesJson = """[{"kind":"ReviewRequired","referenceId":"gate-direct","safeSummary":"Review remains required."}]""",
        string validationReferencesJson = """[{"kind":"ValidationEvidence","referenceId":"validation-direct","safeSummary":"Validation evidence only."}]""",
        string rollbackReferencesJson = """[{"kind":"RollbackEvidence","referenceId":"rollback-direct","safeSummary":"Rollback evidence only."}]""",
        string risksJson = """[{"kind":"SourceChangeRisk","severity":"Low","riskId":"risk-direct","safeSummary":"Risk is informational."}]""",
        string missingEvidenceJson = "[]",
        string metadataJson = "{\"schema\":\"apply.dryrun.store.v1\"}",
        bool canApplySource = false) =>
        ExecuteAsync(
            """
            EXEC workflow.usp_ApplyDryRun_Create
                @DryRunId = @dryRunId,
                @WorkflowRunId = N'workflow-run-direct',
                @WorkflowStepId = N'workflow-step-direct',
                @ControlledApplyPlanReferenceId = N'controlled-plan-direct',
                @SourceApplyApprovalRequirementReferenceId = N'source-approval-direct',
                @PatchProposalEvidencePackageReferenceId = N'patch-evidence-direct',
                @ProjectReferenceId = N'project-direct',
                @TargetReferenceId = N'target-direct',
                @Status = N'Stored',
                @OutcomeKind = N'NotPerformed',
                @SafeSummary = @safeSummary,
                @EvidenceReferencesJson = @evidenceReferencesJson,
                @GateReferencesJson = @gateReferencesJson,
                @ValidationReferencesJson = @validationReferencesJson,
                @RollbackReferencesJson = @rollbackReferencesJson,
                @RisksJson = @risksJson,
                @MissingEvidenceJson = @missingEvidenceJson,
                @CorrelationId = N'correlation-direct',
                @MetadataJson = @metadataJson,
                @IsStoreRecordOnly = 1,
                @IsDryRunPerformed = 0,
                @IsSourceApply = 0,
                @IsPatchApplication = 0,
                @IsApproval = 0,
                @IsApprovalSatisfied = 0,
                @CanPerformDryRun = 0,
                @CanApplySource = @canApplySource,
                @CanMutateFiles = 0,
                @CanReadSourceFiles = 0,
                @CanRunCommand = 0,
                @CanInvokeTool = 0,
                @CanRunValidation = 0,
                @CanRollback = 0,
                @CanSatisfyPolicy = 0,
                @CanTransitionWorkflow = 0,
                @CanPromoteMemory = 0,
                @CanActivateRetrieval = 0;
            """,
            new
            {
                dryRunId,
                safeSummary,
                evidenceReferencesJson,
                gateReferencesJson,
                validationReferencesJson,
                rollbackReferencesJson,
                risksJson,
                missingEvidenceJson,
                metadataJson,
                canApplySource
            });

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

    private async Task DropApplyDryRunSchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            IF OBJECT_ID(N'workflow.usp_ApplyDryRun_Create', N'P') IS NOT NULL DROP PROCEDURE workflow.usp_ApplyDryRun_Create;
            IF OBJECT_ID(N'workflow.usp_ApplyDryRun_Get', N'P') IS NOT NULL DROP PROCEDURE workflow.usp_ApplyDryRun_Get;
            IF OBJECT_ID(N'workflow.usp_ApplyDryRun_ListByWorkflowRun', N'P') IS NOT NULL DROP PROCEDURE workflow.usp_ApplyDryRun_ListByWorkflowRun;
            IF OBJECT_ID(N'workflow.usp_ApplyDryRun_ListByControlledApplyPlan', N'P') IS NOT NULL DROP PROCEDURE workflow.usp_ApplyDryRun_ListByControlledApplyPlan;
            IF OBJECT_ID(N'workflow.TR_ApplyDryRunRecord_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_ApplyDryRunRecord_BlockUpdateDelete;
            IF OBJECT_ID(N'workflow.TR_ApplyDryRunRecord_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER workflow.TR_ApplyDryRunRecord_ValidateInsert;
            IF OBJECT_ID(N'workflow.ApplyDryRunRecord', N'U') IS NOT NULL DROP TABLE workflow.ApplyDryRunRecord;
            IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'workflow')
               AND NOT EXISTS (SELECT 1 FROM sys.objects WHERE schema_id = SCHEMA_ID(N'workflow'))
               DROP SCHEMA workflow;
            """);
    }

    private async Task<T> ScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<T>(sql, parameters) ?? throw new InvalidOperationException("Scalar query returned null.");
    }

    private async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return (await connection.QueryAsync<T>(sql, parameters)).ToArray();
    }

    private async Task ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
    }

    private async Task<int> CountIfExistsAsync(string tableName)
    {
        var exists = await ScalarAsync<int>($"SELECT CASE WHEN OBJECT_ID(N'{tableName}', N'U') IS NULL THEN 0 ELSE 1 END");
        if (exists == 0)
            return 0;

        return await ScalarAsync<int>($"SELECT COUNT(1) FROM {tableName}");
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

    private static void AssertNoForbiddenTokens(string text, params string[] forbidden)
    {
        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found: {token}");
    }

    private static string[] SplitSqlBatches(string sql) =>
        System.Text.RegularExpressions.Regex.Split(sql, @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToArray();

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
}
