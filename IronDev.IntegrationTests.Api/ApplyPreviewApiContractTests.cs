using System.Net;
using System.Text.Json;
using IronDev.Core.Workflow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Api;

[TestClass]
public sealed class ApplyPreviewApiContractTests : ApiTestBase
{
    [TestMethod]
    public async Task ApplyPreviewApi_ReturnsReadOnlyPreviewFromDryRunReceiptSummaries()
    {
        await EnsureApplyDryRunSchemaAsync();
        using var client = await AuthedClientAsync();
        var seeded = await SeedDryRunAsync();

        var response = await client.GetAsync($"/api/workflow/apply-preview/{seeded.WorkflowRunId}/{seeded.WorkflowStepId}?controlledApplyPlanReferenceId={seeded.ControlledApplyPlanReferenceId}&takeDryRuns=10&includeDryRunSummaries=true");
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual("succeeded", json.RootElement.GetProperty("status").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
        Assert.AreEqual("preview_available", json.RootElement.GetProperty("previewStatus").GetString());
        AssertBoundary(json.RootElement.GetProperty("boundary"));

        var data = json.RootElement.GetProperty("data");
        Assert.AreEqual(seeded.WorkflowRunId, data.GetProperty("workflowRunId").GetString());
        Assert.AreEqual(seeded.WorkflowStepId, data.GetProperty("workflowStepId").GetString());
        Assert.AreEqual(seeded.ControlledApplyPlanReferenceId, data.GetProperty("controlledApplyPlanReferenceId").GetString());
        Assert.AreEqual(1, data.GetProperty("dryRunSummaries").GetArrayLength());
        Assert.IsTrue(data.GetProperty("isPreviewOnly").GetBoolean());
        AssertDataFlagsFalse(data);
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task ApplyPreviewApi_MissingDryRunEvidenceIsStillReadOnly()
    {
        await EnsureApplyDryRunSchemaAsync();
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/workflow/apply-preview/missing-run/missing-step?controlledApplyPlanReferenceId=missing-plan");
        var json = await ReadJsonAsync(response);
        var text = json.RootElement.ToString();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, text);
        Assert.AreEqual("missing_preview_evidence", json.RootElement.GetProperty("previewStatus").GetString());
        Assert.IsFalse(json.RootElement.GetProperty("mutationOccurred").GetBoolean());
        AssertBoundary(json.RootElement.GetProperty("boundary"));
        Assert.AreEqual(0, json.RootElement.GetProperty("data").GetProperty("dryRunSummaries").GetArrayLength());
        Assert.IsTrue(json.RootElement.GetProperty("data").GetProperty("missingEvidence").EnumerateArray().Any());
        AssertNoMisleadingAuthorityLanguage(text);
    }

    [TestMethod]
    public async Task ApplyPreviewApi_RejectsUnsafeIdentifiersWithoutLeakingHiddenReasoning()
    {
        using var client = await AuthedClientAsync();

        var response = await client.GetAsync("/api/workflow/apply-preview/workflow-run-rawPrompt-leaked/workflow-step-1");
        var text = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, text);
        AssertNoPrivateReasoningLeak(text);
    }

    [TestMethod]
    public async Task ApplyPreviewApi_GetDoesNotCreateSideEffects()
    {
        await EnsureApplyDryRunSchemaAsync();
        using var client = await AuthedClientAsync();
        var seeded = await SeedDryRunAsync();
        var before = await SideEffectCountsAsync();

        var response = await client.GetAsync($"/api/workflow/apply-preview/{seeded.WorkflowRunId}/{seeded.WorkflowStepId}?controlledApplyPlanReferenceId={seeded.ControlledApplyPlanReferenceId}");
        var after = await SideEffectCountsAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        CollectionAssert.AreEqual(before.ToArray(), after.ToArray(), "Apply preview GET must not create side effects.");
    }

    [TestMethod]
    public async Task ApplyPreviewApi_DoesNotExposeWriteOrCommandRoutes()
    {
        using var client = await AuthedClientAsync();
        var routes = new[]
        {
            (HttpMethod.Post, "/api/workflow/apply-preview/run-1/step-1"),
            (HttpMethod.Put, "/api/workflow/apply-preview/run-1/step-1"),
            (HttpMethod.Patch, "/api/workflow/apply-preview/run-1/step-1"),
            (HttpMethod.Delete, "/api/workflow/apply-preview/run-1/step-1"),
            (HttpMethod.Post, "/api/workflow/apply-preview/run-1/step-1/execute"),
            (HttpMethod.Post, "/api/workflow/apply-preview/run-1/step-1/apply"),
            (HttpMethod.Post, "/api/workflow/apply-preview/run-1/step-1/dry-run")
        };

        foreach (var (method, route) in routes)
        {
            using var request = new HttpRequestMessage(method, route);
            var response = await client.SendAsync(request);
            Assert.IsFalse(response.IsSuccessStatusCode, $"Unsupported apply preview route unexpectedly succeeded: {method} {route}");
        }
    }

    [TestMethod]
    public async Task ApplyPreviewApi_UnauthenticatedRequestsAreRejected()
    {
        var response = await Client.GetAsync("/api/workflow/apply-preview/run-1/step-1");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public void ApplyPreviewApi_ControllerAndProgramStayReadOnly()
    {
        var root = RepositoryRoot();
        var controller = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Controllers", "ApplyPreviewController.cs"));
        var program = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Program.cs"));

        foreach (var token in new[]
        {
            "[HttpPost",
            "[HttpPut",
            "[HttpPatch",
            "[HttpDelete",
            "WorkflowRunner",
            "WorkflowOrchestrator",
            "ManualTesterAgentToolExecutionService",
            "AgentToolExecutor",
            "IControlledWorktreeApplyService",
            "File.Copy",
            "File.Delete",
            "ProcessStartInfo",
            "SqlConnection",
            "INSERT INTO",
            "UPDATE ",
            "DELETE FROM",
            "ICollectiveMemoryPromotion",
            "PromoteCollectiveMemory",
            "WeaviateSemanticMemoryService"
        })
        {
            Assert.IsFalse(controller.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found in apply preview controller: {token}");
        }

        StringAssert.Contains(controller, "IApplyPreviewService");
        StringAssert.Contains(program, "IApplyPreviewService");
        StringAssert.Contains(program, "IApplyDryRunStore");
    }

    private static async Task<SeededDryRun> SeedDryRunAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IApplyDryRunStore>();
        var workflowRunId = $"workflow-run-pr141-{Guid.NewGuid():N}";
        var workflowStepId = $"workflow-step-pr141-{Guid.NewGuid():N}";
        var planId = $"controlled-plan-pr141-{Guid.NewGuid():N}";
        var dryRunId = $"dryrun-pr141-{Guid.NewGuid():N}";

        var result = await store.CreateAsync(new ApplyDryRunCreateRequest
        {
            DryRunId = dryRunId,
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            ControlledApplyPlanReferenceId = planId,
            SourceApplyApprovalRequirementReferenceId = "source-approval-pr141",
            PatchProposalEvidencePackageReferenceId = "patch-evidence-pr141",
            ProjectReferenceId = "project-pr141",
            TargetReferenceId = "target-pr141",
            Status = ApplyDryRunRecordStatus.Stored,
            OutcomeKind = ApplyDryRunOutcomeKind.NotPerformed,
            SafeSummary = "Stored dry-run receipt for preview inspection only.",
            EvidenceReferences =
            [
                new ApplyDryRunReference { Kind = ApplyDryRunReferenceKind.ControlledApplyPlan, ReferenceId = planId, SafeSummary = "Controlled apply plan evidence only." },
                new ApplyDryRunReference { Kind = ApplyDryRunReferenceKind.PatchProposalEvidencePackage, ReferenceId = "patch-evidence-pr141", SafeSummary = "Patch proposal evidence only." }
            ],
            GateReferences =
            [
                new ApplyDryRunGateReference { Kind = ApplyDryRunGateKind.ReviewRequired, ReferenceId = "review-gate-pr141", SafeSummary = "Review remains required." },
                new ApplyDryRunGateReference { Kind = ApplyDryRunGateKind.SourceChangeForbidden, ReferenceId = "source-change-gate-pr141", SafeSummary = "Source change remains forbidden." }
            ],
            ValidationReferences =
            [
                new ApplyDryRunReference { Kind = ApplyDryRunReferenceKind.ValidationEvidence, ReferenceId = "validation-pr141", SafeSummary = "Validation evidence reference only." }
            ],
            RollbackReferences =
            [
                new ApplyDryRunReference { Kind = ApplyDryRunReferenceKind.RollbackEvidence, ReferenceId = "rollback-pr141", SafeSummary = "Rollback evidence reference only." }
            ],
            Risks =
            [
                new ApplyDryRunRisk { Kind = ApplyDryRunRiskKind.SourceChangeRisk, Severity = ApplyDryRunRiskSeverity.Medium, RiskId = "risk-pr141", SafeSummary = "Source change requires human review." }
            ],
            MissingEvidence =
            [
                new ApplyDryRunMissingEvidence { Kind = ApplyDryRunReferenceKind.ValidationEvidence, ReferenceId = "validation-missing-pr141", SafeSummary = "Separate validation evidence remains required." }
            ],
            CorrelationId = "correlation-pr141",
            MetadataJson = "{\"schema\":\"apply.dryrun.store.v1\",\"recordOnly\":true}"
        });

        Assert.AreEqual(ApplyDryRunStoreStatus.Stored, result.Status, string.Join(",", result.Issues.Select(issue => issue.Message)));
        return new SeededDryRun(workflowRunId, workflowStepId, planId);
    }

    private static async Task EnsureApplyDryRunSchemaAsync()
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "Database", "migrate_apply_dry_run_store.sql"));
        foreach (var batch in SplitSqlBatches(sql))
        {
            if (!string.IsNullOrWhiteSpace(batch))
                await Dapper.SqlMapper.ExecuteAsync(connection, batch);
        }
    }

    private static async Task<IReadOnlyDictionary<string, long>> SideEffectCountsAsync()
    {
        var tables = new[]
        {
            "workflow.ApplyDryRunRecord",
            "workflow.WorkflowRun",
            "workflow.WorkflowRunStep",
            "workflow.WorkflowCheckpoint",
            "governance.ToolRequest",
            "governance.ToolGateDecision",
            "governance.ApprovalDecision",
            "governance.PolicyDecisionEvent",
            "governance.DogfoodReceipt",
            "a2a.AgentHandoff",
            "agent.AgentLocalMemory",
            "agent.AgentMemoryImprovementProposal",
            "dbo.ToolExecutionAuditRecord",
            "dbo.AgentRunAuditEnvelope"
        };

        var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var table in tables)
        {
            var exists = await Dapper.SqlMapper.ExecuteScalarAsync<int>(connection, "SELECT CASE WHEN OBJECT_ID(@TableName, 'U') IS NULL THEN 0 ELSE 1 END", new { TableName = table });
            counts[table] = exists == 0 ? 0 : await Dapper.SqlMapper.ExecuteScalarAsync<long>(connection, $"SELECT COUNT_BIG(*) FROM {table}");
        }

        return counts;
    }

    private static async Task<HttpClient> AuthedClientAsync()
    {
        var baseToken = await LoginAsync();
        var tenantToken = await SelectTenantAsync(baseToken);
        return GetAuthedClient(tenantToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    private static void AssertBoundary(JsonElement boundary)
    {
        Assert.IsTrue(boundary.GetProperty("readOnlyInspection").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("previewOnly").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("previewIsSourceApply").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("previewIsPatchApply").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("previewIsDryRunExecution").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("dryRunReceiptIsExecution").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("endpointAccessIsExecutionPermission").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("apiResponseStatusIsGovernance").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("auditEvidenceIsApproval").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("sourceApplied").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("patchApplied").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("dryRunExecuted").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("approvalSatisfied").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("policySatisfied").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("workflowTransitioned").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("memoryPromoted").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("retrievalActivated").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("agentDispatched").GetBoolean());
        Assert.IsFalse(boundary.GetProperty("modelOutputIsAuthority").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForSourceApply").GetBoolean());
        Assert.IsTrue(boundary.GetProperty("humanReviewRequiredForMemoryPromotion").GetBoolean());
    }

    private static void AssertDataFlagsFalse(JsonElement data)
    {
        foreach (var property in new[]
        {
            "canExecuteDryRun",
            "isDryRunExecution",
            "canApplySource",
            "appliesPatch",
            "readsSourceFiles",
            "mutatesFiles",
            "runsCommand",
            "invokesTool",
            "runsValidation",
            "runsRollback",
            "satisfiesApproval",
            "satisfiesPolicy",
            "transitionsWorkflow",
            "promotesMemory",
            "activatesRetrieval",
            "dispatchesAgent",
            "callsModel"
        })
        {
            Assert.IsFalse(data.GetProperty(property).GetBoolean(), $"Apply preview data flag must be false: {property}");
        }
    }

    private static void AssertNoPrivateReasoningLeak(string text)
    {
        foreach (var token in new[] { "rawPrompt", "rawCompletion", "rawToolOutput", "entirePatch", "chain-of-thought", "hidden reasoning", "private reasoning" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response leaked unsafe marker: {token}");
    }

    private static void AssertNoMisleadingAuthorityLanguage(string text)
    {
        var normalized = text.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        foreach (var token in new[]
        {
            "canexecutedryrun:true",
            "isdryrunexecution:true",
            "canapplysource:true",
            "appliespatch:true",
            "readssourcefiles:true",
            "mutatesfiles:true",
            "runscommand:true",
            "invokestool:true",
            "runsvalidation:true",
            "runsrollback:true",
            "satisfiesapproval:true",
            "satisfiespolicy:true",
            "transitionsworkflow:true",
            "promotesmemory:true",
            "activatesretrieval:true",
            "dispatchesagent:true",
            "callsmodel:true",
            "previewissourceapply:true",
            "previewispatchapply:true",
            "previewisdryrunexecution:true",
            "dryrunreceiptisexecution:true",
            "endpointaccessisexecutionpermission:true",
            "apiresponsestatusisgovernance:true",
            "auditevidenceisapproval:true",
            "sourceapplied:true",
            "patchapplied:true",
            "dryrunexecuted:true",
            "approvalsatisfied:true",
            "policysatisfied:true",
            "workflowtransitioned:true",
            "memorypromoted:true"
        })
        {
            Assert.IsFalse(normalized.Contains(token, StringComparison.OrdinalIgnoreCase), $"Response contained misleading authority/action flag: {token}");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string[] SplitSqlBatches(string sql) =>
        System.Text.RegularExpressions.Regex.Split(sql, @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToArray();

    private sealed record SeededDryRun(string WorkflowRunId, string WorkflowStepId, string ControlledApplyPlanReferenceId);
}
