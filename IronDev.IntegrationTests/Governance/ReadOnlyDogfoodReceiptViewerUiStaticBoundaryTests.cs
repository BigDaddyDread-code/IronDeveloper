using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("StaticBoundary")]
[TestClass]
[TestCategory("ReadOnlyDogfoodReceiptViewerUi")]
public sealed class ReadOnlyDogfoodReceiptViewerUiStaticBoundaryTests
{
    [TestMethod]
    public void DogfoodReceiptViewerUi_UsesExistingGetOnlyReads()
    {
        var api = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.TauriShell", "src", "api", "ironDevApi.ts"));
        var page = PageText();

        StringAssert.Contains(page, "getDogfoodLoopReceipt");
        StringAssert.Contains(page, "searchGovernanceTraces");
        StringAssert.Contains(page, "getGovernanceTrace");

        foreach (var block in ApiReadBlocks(api))
        {
            StringAssert.Contains(block, "method: 'GET'");
            Assert.IsFalse(block.Contains("method: 'POST'", StringComparison.Ordinal), "Dogfood receipt viewer must not POST.");
            Assert.IsFalse(block.Contains("method: 'PUT'", StringComparison.Ordinal), "Dogfood receipt viewer must not PUT.");
            Assert.IsFalse(block.Contains("method: 'PATCH'", StringComparison.Ordinal), "Dogfood receipt viewer must not PATCH.");
            Assert.IsFalse(block.Contains("method: 'DELETE'", StringComparison.Ordinal), "Dogfood receipt viewer must not DELETE.");
        }
    }

    [TestMethod]
    public void DogfoodReceiptViewerUi_DoesNotDeclareControlActions()
    {
        var text = PageText();
        foreach (var token in ForbiddenFunctionNames())
        {
            Assert.IsFalse(text.Contains($"function {token}", StringComparison.OrdinalIgnoreCase), $"Forbidden control function found: {token}");
            Assert.IsFalse(text.Contains($"const {token}", StringComparison.OrdinalIgnoreCase), $"Forbidden control function found: {token}");
            Assert.IsFalse(text.Contains($"{token}(", StringComparison.OrdinalIgnoreCase), $"Forbidden control function call found: {token}");
        }
    }

    [TestMethod]
    public void DogfoodReceiptViewerUi_DoesNotRenderMutatingButtons()
    {
        var page = PageText();
        foreach (var label in ForbiddenButtonLabels())
        {
            Assert.IsFalse(page.Contains($">{label}<", StringComparison.OrdinalIgnoreCase), $"Forbidden button label rendered: {label}");
            Assert.IsFalse(page.Contains($"'{label}'", StringComparison.OrdinalIgnoreCase), $"Forbidden button label constant found: {label}");
            Assert.IsFalse(page.Contains($"\"{label}\"", StringComparison.OrdinalIgnoreCase), $"Forbidden button label constant found: {label}");
        }

        foreach (var label in new[]
        {
            "Search",
            "Refresh",
            "Clear Filters",
            "Copy Receipt ID",
            "Copy Correlation ID",
            "Open Receipt",
            "Open Trace",
            "Open Timeline",
            "Open Correlation Report",
            "Open Tool Gate Ledger",
            "Open Approval Package"
        })
        {
            StringAssert.Contains(page, label);
        }
    }

    [TestMethod]
    public void DogfoodReceiptViewerUi_DoesNotExposeRawPayloadFields()
    {
        var text = PageText() + "\n" + TypeText() + "\n" + ApiTypeBlock();
        foreach (var field in ForbiddenPayloadFields())
        {
            Assert.IsFalse(text.Contains($".{field}", StringComparison.Ordinal), $"UI must not read raw field {field}.");
            Assert.IsFalse(text.Contains($"['{field}']", StringComparison.Ordinal), $"UI must not index raw field {field}.");
            Assert.IsFalse(text.Contains($"[\"{field}\"]", StringComparison.Ordinal), $"UI must not index raw field {field}.");
        }
    }

    [TestMethod]
    public void DogfoodReceiptViewerUi_DoesNotReferenceReceiptCreationActions()
    {
        var text = ProductionText();
        foreach (var token in new[] { "CreateDogfoodReceipt", "RecordDogfoodReceipt", "SaveDogfoodReceipt", "DogfoodReceipt_Record", "PostDogfoodReceipt" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Dogfood receipt creation token found: {token}");
    }

    [TestMethod]
    public void DogfoodReceiptViewerUi_DoesNotReferenceDogfoodOutcomeActions()
    {
        var text = PageText();
        foreach (var token in new[] { "MarkDogfoodPassed", "MarkDogfoodFailed", "DogfoodPassed", "DogfoodFailed", "SetDogfoodOutcome" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Dogfood outcome control token found: {token}");
    }

    [TestMethod]
    public void DogfoodReceiptViewerUi_DoesNotReferenceApprovalOrPolicyActions()
    {
        var text = ProductionText();
        foreach (var token in new[] { "ApproveRelease", "ReleaseApproved", "SatisfyPolicy", "OverridePolicy", "PolicySatisfied", "CreateApprovalDecision", "GrantApproval" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Approval/policy action token found: {token}");
    }

    [TestMethod]
    public void DogfoodReceiptViewerUi_DoesNotReferenceWorkflowTransitionActions()
    {
        var text = PageText();
        foreach (var token in new[] { "TransitionWorkflow", "ContinueWorkflow", "ResumeWorkflow", "RetryWorkflow", "RerunWorkflow", "StartWorkflow" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Workflow transition token found: {token}");
    }

    [TestMethod]
    public void DogfoodReceiptViewerUi_DoesNotReferenceToolOrAgentExecutionActions()
    {
        var text = PageText();
        foreach (var token in new[] { "ExecuteTool", "InvokeTool", "ToolInvoker", "DispatchAgent", "AgentDispatcher", "CallModel", "BuildPrompt" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Tool/agent execution token found: {token}");
    }

    [TestMethod]
    public void DogfoodReceiptViewerUi_DoesNotReferenceSourceApplyOrMemoryPromotionActions()
    {
        var text = ProductionText();
        foreach (var token in new[] { "ApplySourceAsync", "ApplyPatchAsync", "PatchApply", "SourceWriter", "PatchWriter", "PromoteMemory", "ActivateRetrieval" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Source apply/memory promotion token found: {token}");
    }

    [TestMethod]
    public void DogfoodReceiptViewerUi_ContainsReadOnlyBoundaryLanguage()
    {
        var page = PageText();
        foreach (var required in new[]
        {
            "Read-only view",
            "Dogfood receipt is not release approval",
            "Dogfood pass is not release readiness",
            "Dogfood evidence is not policy satisfaction",
            "Receipt viewer is not dogfood execution",
            "This UI cannot create dogfood receipts, mark dogfood passed, approve release, satisfy policy, transition workflow, invoke tools, dispatch agents, apply source, or release software."
        })
        {
            StringAssert.Contains(page, required);
        }
    }

    [TestMethod]
    public void DogfoodReceiptViewerUi_ReceiptStatesCorrectBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR156_DOGFOOD_RECEIPT_VIEWER_UI.md"));

        foreach (var required in new[]
        {
            "PR156 adds the Dogfood Receipt Viewer UI.",
            "Dogfood Receipt Viewer UI is read-only.",
            "Dogfood receipt is not release approval.",
            "Dogfood pass is not release readiness.",
            "Dogfood evidence is not policy satisfaction.",
            "Receipt viewer is not dogfood execution.",
            "The UI consumes existing GET-only dogfood receipt and governance trace APIs.",
            "Copy receipt id is not release approval.",
            "Navigation is not workflow continuation.",
            "This PR is not Block P release authority.",
            "PR156 shows the dogfood receipt. It does not taste the food."
        })
        {
            StringAssert.Contains(receipt, required);
        }
    }

    [TestMethod]
    public void DogfoodReceiptViewerUi_IsRoutedFromGovernancePathOnly()
    {
        var shell = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.TauriShell", "src", "shell", "IronDevShell.tsx"));
        StringAssert.Contains(shell, "/governance/dogfood-receipts");
        StringAssert.Contains(shell, "DogfoodReceiptViewerRoute");
    }

    private static IEnumerable<string> ApiReadBlocks(string api)
    {
        foreach (var marker in new[]
        {
            "getDogfoodLoopReceipt(",
            "searchGovernanceTraces(",
            "getGovernanceTrace("
        })
        {
            var start = api.IndexOf(marker, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, $"Missing dogfood receipt viewer API method: {marker}");
            var nextMethod = api.IndexOf("\n  async ", start + marker.Length, StringComparison.Ordinal);
            yield return nextMethod > start ? api[start..nextMethod] : api[start..];
        }
    }

    private static string ProductionText() =>
        string.Join("\n", ProductionFiles().Select(File.ReadAllText));

    private static string PageText() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.TauriShell", "src", "features", "governance", "DogfoodReceiptViewerRoute.tsx"));

    private static string TypeText() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.TauriShell", "src", "features", "governance", "DogfoodReceiptViewerTypes.ts"));

    private static string ApiTypeBlock()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.TauriShell", "src", "api", "types.ts"));
        var start = text.IndexOf("export interface DogfoodLoopIssue", StringComparison.Ordinal);
        var end = text.IndexOf("export interface ToolGateFilter", StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, "Missing dogfood receipt type block.");
        Assert.IsTrue(end > start, "Missing end of dogfood receipt type block.");
        return text[start..end];
    }

    private static IReadOnlyList<string> ProductionFiles()
    {
        var root = RepositoryRoot();
        return
        [
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "governance", "DogfoodReceiptViewerRoute.tsx"),
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "governance", "DogfoodReceiptViewerTypes.ts"),
            Path.Combine(root, "IronDev.TauriShell", "src", "shell", "IronDevShell.tsx"),
            Path.Combine(root, "IronDev.TauriShell", "src", "api", "ironDevApi.ts")
        ];
    }

    private static IReadOnlyList<string> ForbiddenButtonLabels() =>
    [
        "Create Receipt",
        "Record Receipt",
        "Mark Passed",
        "Mark Dogfood Passed",
        "Mark Failed",
        "Approve Release",
        "Release",
        "Ship",
        "Ready To Ship",
        "Satisfy Policy",
        "Continue Workflow",
        "Transition Workflow",
        "Retry",
        "Rerun",
        "Resume",
        "Execute Tool",
        "Invoke Tool",
        "Dispatch Agent",
        "Call Model",
        "Build Prompt",
        "Promote Memory",
        "Activate Retrieval",
        "Apply Source",
        "Apply Patch"
    ];

    private static IReadOnlyList<string> ForbiddenFunctionNames() =>
    [
        "createReceipt",
        "recordReceipt",
        "createDogfoodReceipt",
        "recordDogfoodReceipt",
        "markPassed",
        "markDogfoodPassed",
        "markFailed",
        "approveRelease",
        "releaseSoftware",
        "shipSoftware",
        "satisfyPolicy",
        "continueWorkflow",
        "transitionWorkflow",
        "retryWorkflow",
        "rerunWorkflow",
        "resumeWorkflow",
        "executeTool",
        "invokeTool",
        "dispatchAgent",
        "callModel",
        "buildPrompt",
        "promoteMemory",
        "activateRetrieval",
        "applySource",
        "applyPatch"
    ];

    private static IReadOnlyList<string> ForbiddenPayloadFields() =>
    [
        "PayloadJson",
        "payloadJson",
        "DogfoodPayloadJson",
        "dogfoodPayloadJson",
        "DogfoodOutputJson",
        "dogfoodOutputJson",
        "ValidationOutputJson",
        "validationOutputJson",
        "RawDogfoodNotes",
        "rawDogfoodNotes",
        "RawPayload",
        "rawPayload",
        "RawPrompt",
        "rawPrompt",
        "RawCompletion",
        "rawCompletion",
        "RawToolOutput",
        "rawToolOutput",
        "RawCommandOutput",
        "rawCommandOutput",
        "PrivateReasoning",
        "privateReasoning",
        "HiddenReasoning",
        "hiddenReasoning",
        "ChainOfThought",
        "chainOfThought",
        "Scratchpad",
        "scratchpad",
        "SourceContent",
        "sourceContent",
        "SourceFileContents",
        "sourceFileContents",
        "PatchPayload",
        "patchPayload",
        "DiffPayload",
        "diffPayload",
        "ConnectionString",
        "connectionString",
        "Password",
        "password",
        "Secret",
        "secret",
        "ApiKey",
        "apiKey",
        "Credential",
        "credential",
        "Bearer",
        "bearer"
    ];

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
