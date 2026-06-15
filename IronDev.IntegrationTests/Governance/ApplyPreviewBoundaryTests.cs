using IronDev.Core.Workflow;
using IronDev.Infrastructure.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("ApplyPreview")]
public sealed class ApplyPreviewBoundaryTests
{
    [TestMethod]
    public void ApplyPreviewContract_IsReadOnlyAndHasNoActionMethods()
    {
        var methods = typeof(IApplyPreviewService).GetMethods().Select(method => method.Name).OrderBy(name => name).ToArray();

        CollectionAssert.AreEquivalent(new[] { "GetPreviewAsync" }, methods);
        AssertNoForbiddenTokens(
            string.Join("\n", methods),
            "Create",
            "Update",
            "Delete",
            "Append",
            "Execute",
            "Dispatch",
            "Continue",
            "Start",
            "Approve",
            "ApplySource",
            "PromoteMemory",
            "Rollback");
    }

    [TestMethod]
    public async Task ApplyPreviewService_ReturnsPreviewOnlyFlagsAndDryRunSummaries()
    {
        var dryRun = new ApplyDryRunSummary
        {
            DryRunId = "dryrun-1",
            WorkflowRunId = "workflow-run-1",
            WorkflowStepId = "workflow-step-1",
            ControlledApplyPlanReferenceId = "controlled-plan-1",
            ProjectReferenceId = "project-1",
            TargetReferenceId = "target-1",
            Status = ApplyDryRunRecordStatus.Stored,
            OutcomeKind = ApplyDryRunOutcomeKind.NotPerformed,
            EvidenceReferenceCount = 2,
            GateReferenceCount = 2,
            ValidationReferenceCount = 1,
            RollbackReferenceCount = 1,
            RiskCount = 1,
            MissingEvidenceCount = 1,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        var service = new ApplyPreviewService(new FakeDryRunStore([dryRun]));

        var preview = await service.GetPreviewAsync(new ApplyPreviewRequest
        {
            WorkflowRunId = "workflow-run-1",
            WorkflowStepId = "workflow-step-1",
            ControlledApplyPlanReferenceId = "controlled-plan-1",
            TakeDryRuns = 10
        });

        Assert.AreEqual(ApplyPreviewStatus.PreviewAvailable, preview.Status);
        Assert.AreEqual(1, preview.DryRunSummaries.Count);
        Assert.IsTrue(preview.IsPreviewOnly);
        AssertPreviewFlagsFalse(preview);
        Assert.IsTrue(preview.SafeSummaryLines.Any(line => line.Contains("not source apply", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(preview.Gates.All(gate => !gate.IsSatisfied && !gate.IsApproval && !gate.IsExecutionPermission));
    }

    [TestMethod]
    public async Task ApplyPreviewService_MissingDryRunEvidenceIsNotExecutionPermission()
    {
        var service = new ApplyPreviewService(new FakeDryRunStore([]));

        var preview = await service.GetPreviewAsync(new ApplyPreviewRequest
        {
            WorkflowRunId = "workflow-run-missing",
            WorkflowStepId = "workflow-step-missing",
            ControlledApplyPlanReferenceId = "controlled-plan-missing"
        });

        Assert.AreEqual(ApplyPreviewStatus.MissingPreviewEvidence, preview.Status);
        Assert.AreEqual(0, preview.DryRunSummaries.Count);
        Assert.IsTrue(preview.MissingEvidence.Any(missing => missing.EvidenceKind == "ApplyDryRunReceipt"));
        AssertPreviewFlagsFalse(preview);
    }

    [TestMethod]
    public async Task ApplyPreviewService_RejectsUnsafeRequestWithoutLeakingMarker()
    {
        var service = new ApplyPreviewService(new FakeDryRunStore([]));

        var preview = await service.GetPreviewAsync(new ApplyPreviewRequest
        {
            WorkflowRunId = "workflow-run rawPrompt leaked",
            WorkflowStepId = "workflow-step-1"
        });

        var text = System.Text.Json.JsonSerializer.Serialize(preview);

        Assert.AreEqual(ApplyPreviewStatus.InvalidRequest, preview.Status);
        Assert.AreEqual(string.Empty, preview.WorkflowRunId);
        Assert.IsTrue(preview.Issues.Any(issue => issue.Kind is ApplyPreviewIssueKind.UnsafeRequestText));
        AssertNoPrivateReasoningLeak(text);
    }

    [TestMethod]
    public void ApplyPreviewProductionFiles_DoNotExposeExecutionMutationOrPersistenceSurface()
    {
        var root = RepositoryRoot();
        var files = new[]
        {
            Path.Combine(root, "IronDev.Core", "Workflow", "ApplyPreviewModels.cs"),
            Path.Combine(root, "IronDev.Core", "Workflow", "IApplyPreviewService.cs"),
            Path.Combine(root, "IronDev.Infrastructure", "Workflow", "ApplyPreviewService.cs"),
            Path.Combine(root, "IronDev.Api", "Controllers", "ApplyPreviewController.cs")
        };

        var text = string.Join("\n", files.Select(File.ReadAllText));
        foreach (var token in new[]
        {
            "[HttpPost",
            "[HttpPut",
            "[HttpPatch",
            "[HttpDelete",
            "SqlConnection",
            "INSERT INTO",
            "UPDATE ",
            "DELETE FROM",
            "WorkflowRunner",
            "WorkflowOrchestrator",
            "IWorkflowRunner",
            "ManualTesterAgentToolExecutionService",
            "IControlledWorktreeApplyService",
            "File.Copy",
            "File.Delete",
            "ProcessStartInfo",
            "ICollectiveMemoryPromotion",
            "PromoteCollectiveMemory",
            "WeaviateSemanticMemoryService"
        })
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found in apply preview production files: {token}");
        }

        StringAssert.Contains(text, "IApplyDryRunStore");
        StringAssert.Contains(text, "GetPreviewAsync");
    }

    [TestMethod]
    public void ApplyPreviewReceiptDocumentsBoundary()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR141_APPLY_PREVIEW_API.md"));

        StringAssert.Contains(text, "Apply Preview API");
        StringAssert.Contains(text, "GET /api/workflow/apply-preview/{workflowRunId}/{workflowStepId}");
        StringAssert.Contains(text, "perform an apply dry-run");
        StringAssert.Contains(text, "apply source");
        StringAssert.Contains(text, "satisfy approval");
        StringAssert.Contains(text, "satisfy policy");
        StringAssert.Contains(text, "transition workflow state");
        StringAssert.Contains(text, "promote memory");
        StringAssert.Contains(text, "The preview service reads existing `IApplyDryRunStore` summaries only.");
    }

    private static void AssertPreviewFlagsFalse(ApplyPreviewResponse preview)
    {
        Assert.IsFalse(preview.CanExecuteDryRun);
        Assert.IsFalse(preview.IsDryRunExecution);
        Assert.IsFalse(preview.CanApplySource);
        Assert.IsFalse(preview.AppliesPatch);
        Assert.IsFalse(preview.ReadsSourceFiles);
        Assert.IsFalse(preview.MutatesFiles);
        Assert.IsFalse(preview.RunsCommand);
        Assert.IsFalse(preview.InvokesTool);
        Assert.IsFalse(preview.RunsValidation);
        Assert.IsFalse(preview.RunsRollback);
        Assert.IsFalse(preview.SatisfiesApproval);
        Assert.IsFalse(preview.SatisfiesPolicy);
        Assert.IsFalse(preview.TransitionsWorkflow);
        Assert.IsFalse(preview.PromotesMemory);
        Assert.IsFalse(preview.ActivatesRetrieval);
        Assert.IsFalse(preview.DispatchesAgent);
        Assert.IsFalse(preview.CallsModel);
    }

    private static void AssertNoPrivateReasoningLeak(string text)
    {
        foreach (var token in new[] { "rawPrompt", "rawCompletion", "rawToolOutput", "entirePatch", "chain-of-thought", "hidden reasoning", "private reasoning" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Output leaked unsafe marker: {token}");
    }

    private static void AssertNoForbiddenTokens(string text, params string[] forbidden)
    {
        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found: {token}");
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

    private sealed class FakeDryRunStore : IApplyDryRunStore
    {
        private readonly IReadOnlyList<ApplyDryRunSummary> _summaries;

        public FakeDryRunStore(IReadOnlyList<ApplyDryRunSummary> summaries)
        {
            _summaries = summaries;
        }

        public Task<ApplyDryRunStoreResult> CreateAsync(ApplyDryRunCreateRequest? request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Apply preview tests must not create dry-run records.");

        public Task<ApplyDryRunRecord?> GetByIdAsync(string dryRunId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Apply preview must not hydrate dry-run records.");

        public Task<IReadOnlyList<ApplyDryRunSummary>> ListByWorkflowRunAsync(string workflowRunId, int take = ApplyDryRunStoreValidator.DefaultTake, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplyDryRunSummary>>(_summaries.Where(summary => summary.WorkflowRunId == workflowRunId).Take(take).ToArray());

        public Task<IReadOnlyList<ApplyDryRunSummary>> ListByControlledApplyPlanAsync(string controlledApplyPlanReferenceId, int take = ApplyDryRunStoreValidator.DefaultTake, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApplyDryRunSummary>>(_summaries.Where(summary => summary.ControlledApplyPlanReferenceId == controlledApplyPlanReferenceId).Take(take).ToArray());
    }
}
