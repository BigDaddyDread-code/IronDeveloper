using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("ReadOnlyWorkflowRunStepViewerUi")]
public sealed class ReadOnlyWorkflowRunStepViewerUiStaticBoundaryTests
{
    [TestMethod]
    public void WorkflowRunStepViewerUi_RouteIsRegisteredAsReadOnlyInspection()
    {
        var routes = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.TauriShell", "src", "app", "routes.ts"));
        var shell = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.TauriShell", "src", "shell", "IronDevShell.tsx"));

        StringAssert.Contains(routes, "/workflows/");
        StringAssert.Contains(shell, "WorkflowRunStepViewerRoute");
        StringAssert.Contains(shell, "startsWith('/workflows/runs')");
    }

    [TestMethod]
    public void WorkflowRunStepViewerUi_UsesExistingGetOnlyWorkflowReads()
    {
        var api = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.TauriShell", "src", "api", "ironDevApi.ts"));
        var page = PageText();

        foreach (var method in new[]
        {
            "listWorkflowRuns",
            "listWorkflowRunsByCorrelation",
            "getWorkflowRun",
            "listWorkflowSteps",
            "getWorkflowStep"
        })
        {
            StringAssert.Contains(page, method);
            var block = ApiReadBlock(api, method);
            StringAssert.Contains(block, "method: 'GET'");
            Assert.IsFalse(block.Contains("method: 'POST'", StringComparison.Ordinal), $"{method} must not POST.");
            Assert.IsFalse(block.Contains("method: 'PUT'", StringComparison.Ordinal), $"{method} must not PUT.");
            Assert.IsFalse(block.Contains("method: 'PATCH'", StringComparison.Ordinal), $"{method} must not PATCH.");
            Assert.IsFalse(block.Contains("method: 'DELETE'", StringComparison.Ordinal), $"{method} must not DELETE.");
        }

        StringAssert.Contains(api, "/api/v1/workflow/runs");
    }

    [TestMethod]
    public void WorkflowRunStepViewerUi_DoesNotRenderWorkflowControlButtons()
    {
        var page = PageText();
        foreach (var label in ForbiddenButtonLabels())
        {
            Assert.IsFalse(page.Contains($">{label}<", StringComparison.OrdinalIgnoreCase), $"Forbidden button label rendered: {label}");
        }
    }

    [TestMethod]
    public void WorkflowRunStepViewerUi_OnlyRendersAllowedActions()
    {
        var page = PageText();
        foreach (var label in new[]
        {
            "Search",
            "Refresh",
            "Clear Filters",
            "Copy Workflow ID",
            "Copy Step ID",
            "Copy Correlation ID",
            "Open Run",
            "Open Step",
            "Open Trace",
            "Open Timeline",
            "Open Diagnosis",
            "Open Agent Health",
            "Open Tool Gate Ledger",
            "Open Dogfood Receipts",
            "Open Approval Packages"
        })
        {
            StringAssert.Contains(page, label);
        }
    }

    [TestMethod]
    public void WorkflowRunStepViewerUi_ContainsRequiredBoundaryBanner()
    {
        var page = PageText();
        foreach (var warning in new[]
        {
            "Read-only view",
            "Workflow visibility is not workflow authority",
            "Workflow status is not transition permission",
            "Step status is not execution permission",
            "Refresh is not retry"
        })
        {
            StringAssert.Contains(page, warning);
        }
    }

    [TestMethod]
    public void WorkflowRunStepViewerUi_ContainsRequiredFooterBoundary()
    {
        StringAssert.Contains(
            PageText(),
            "This UI cannot start, continue, transition, retry, repair, execute workflow, invoke tools, dispatch agents, apply");
    }

    [TestMethod]
    public void WorkflowRunStepViewerUi_DoesNotExposeRawWorkflowPayloadFields()
    {
        var page = PageText();
        var model = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "IronDev.TauriShell",
            "src",
            "features",
            "governance",
            "WorkflowRunStepViewerTypes.ts"));

        foreach (var token in new[]
        {
            "workflowPayloadJson",
            "stepPayloadJson",
            "rawWorkflowPayload",
            "rawStepPayload",
            "rawPrompt",
            "rawCompletion",
            "rawToolOutput",
            "entirePatch",
            "privateReasoning",
            "chainOfThought"
        })
        {
            Assert.IsFalse(page.Contains(token, StringComparison.Ordinal), $"Viewer page must not expose raw field token: {token}");
            Assert.IsFalse(model.Contains(token, StringComparison.Ordinal), $"Viewer model must not expose raw field token: {token}");
        }
    }

    [TestMethod]
    public void WorkflowRunStepViewerUi_DoesNotAddBackendControllerOrCliSurface()
    {
        var apiControllers = Directory.GetFiles(Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers"), "*.cs", SearchOption.TopDirectoryOnly);
        Assert.IsFalse(apiControllers.Any(path => Path.GetFileName(path).Contains("WorkflowRunStepViewer", StringComparison.OrdinalIgnoreCase)));

        var cliRoot = Path.Combine(RepositoryRoot(), "IronDev.Cli");
        if (Directory.Exists(cliRoot))
        {
            var cliFiles = Directory.GetFiles(cliRoot, "*.*", SearchOption.AllDirectories);
            Assert.IsFalse(cliFiles.Any(path => File.ReadAllText(path).Contains("WorkflowRunStepViewer", StringComparison.Ordinal)));
        }
    }

    [TestMethod]
    public void WorkflowRunStepViewerUi_DoesNotReferenceRuntimeOrMutationServices()
    {
        var page = PageText();
        foreach (var token in new[]
        {
            "IWorkflowRunner",
            "WorkflowRunner",
            "IWorkflowDispatcher",
            "IToolExecutor",
            "ISourceApply",
            "IMemoryPromotion",
            "ReleaseApproval",
            "ApplyPatch",
            "StartWorkflow",
            "ContinueWorkflow",
            "RetryWorkflow"
        })
        {
            Assert.IsFalse(page.Contains(token, StringComparison.Ordinal), $"Viewer must not reference runtime or mutation service: {token}");
        }
    }

    [TestMethod]
    public void WorkflowRunStepViewerUi_RedactsUnsafeMaterialBeforeRender()
    {
        var page = PageText();
        StringAssert.Contains(page, "redacted workflow viewer text");
        StringAssert.Contains(page, "Safe summaries only");
    }

    [TestMethod]
    public void WorkflowRunStepViewerUi_ReceiptExistsAndRecordsBoundary()
    {
        var receipt = ReceiptText();
        StringAssert.Contains(receipt, "PR157 shows the workflow path. It does not move the workflow forward.");
        StringAssert.Contains(receipt, "Workflow visibility is not workflow authority.");
        StringAssert.Contains(receipt, "Workflow status is not transition permission.");
        StringAssert.Contains(receipt, "Step status is not execution permission.");
        StringAssert.Contains(receipt, "Refresh is not retry.");
    }

    [TestMethod]
    public void WorkflowRunStepViewerUi_ReceiptForbidsRuntimeAndMutationWork()
    {
        var receipt = ReceiptText();
        foreach (var sentence in new[]
        {
            "No backend controller was added.",
            "No SQL migration was added.",
            "No workflow runner was added.",
            "No workflow transition behaviour was added.",
            "No tool invocation was added.",
            "No agent dispatch was added.",
            "No source apply path was added.",
            "No release approval path was added.",
            "No API write endpoint was added.",
            "No CLI command was added."
        })
        {
            StringAssert.Contains(receipt, sentence);
        }
    }

    private static string PageText() =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "IronDev.TauriShell",
            "src",
            "features",
            "governance",
            "WorkflowRunStepViewerRoute.tsx"));

    private static string ReceiptText() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR157_WORKFLOW_RUN_STEP_VIEWER_UI.md"));

    private static string ApiReadBlock(string api, string method)
    {
        var start = api.IndexOf($"async {method}", StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, $"Missing API method {method}.");

        var nextMethod = api.IndexOf("\n  async ", start + 1, StringComparison.Ordinal);
        return nextMethod > start ? api[start..nextMethod] : api[start..];
    }

    private static IReadOnlyList<string> ForbiddenButtonLabels() =>
        new[]
        {
        "Start",
        "Continue",
        "Transition",
        "Execute",
        "Invoke",
        "Dispatch",
        "Retry",
        "Rerun",
        "Resume",
        "Repair",
        "Fix",
        "Heal",
        "Restart",
        "Approve",
        "Reject",
        "Accept",
        "Deny",
        "Grant",
        "Satisfy",
        "Release",
        "Apply",
        "Patch",
        "Cleanup",
        "Delete",
        "Purge",
        "Archive",
        "Redact",
        "Promote",
        "Activate",
        "Override",
        "Reopen"
        };

    private static string RepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "IronDev.slnx")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
