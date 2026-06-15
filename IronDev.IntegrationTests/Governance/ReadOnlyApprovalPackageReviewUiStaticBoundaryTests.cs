using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("ReadOnlyApprovalPackageReviewUi")]
public sealed class ReadOnlyApprovalPackageReviewUiStaticBoundaryTests
{
    [TestMethod]
    public void ApprovalPackageReviewUi_UsesExistingGetOnlyGovernanceTraceReads()
    {
        var api = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.TauriShell", "src", "api", "ironDevApi.ts"));
        var page = PageText();

        StringAssert.Contains(page, "searchGovernanceTraces");
        StringAssert.Contains(page, "getGovernanceTrace");

        foreach (var block in GovernanceTraceApiBlocks(api))
        {
            StringAssert.Contains(block, "method: 'GET'");
            Assert.IsFalse(block.Contains("method: 'POST'", StringComparison.Ordinal), "Approval package review must not POST.");
            Assert.IsFalse(block.Contains("method: 'PUT'", StringComparison.Ordinal), "Approval package review must not PUT.");
            Assert.IsFalse(block.Contains("method: 'PATCH'", StringComparison.Ordinal), "Approval package review must not PATCH.");
            Assert.IsFalse(block.Contains("method: 'DELETE'", StringComparison.Ordinal), "Approval package review must not DELETE.");
        }
    }

    [TestMethod]
    public void ApprovalPackageReviewUi_DoesNotDeclareControlActions()
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
    public void ApprovalPackageReviewUi_DoesNotRenderMutatingButtons()
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
            "Copy Package ID",
            "Copy Correlation ID",
            "Open Package",
            "Open Trace",
            "Open Timeline",
            "Open Correlation Report",
            "Open Tool Gate Ledger"
        })
        {
            StringAssert.Contains(page, label);
        }
    }

    [TestMethod]
    public void ApprovalPackageReviewUi_DoesNotExposeRawPayloadFields()
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
    public void ApprovalPackageReviewUi_DoesNotReferenceApprovalMutationActions()
    {
        var text = ProductionText();
        foreach (var token in new[] { "ApproveAsync", "RejectAsync", "AcceptApproval", "CreateAcceptedApproval", "CreateApprovalDecision", "GrantApproval", "SatisfyApproval" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Approval mutation token found: {token}");
    }

    [TestMethod]
    public void ApprovalPackageReviewUi_DoesNotReferencePolicySatisfactionActions()
    {
        var text = ProductionText();
        foreach (var token in new[] { "SatisfyPolicy", "OverridePolicy", "PolicySatisfied", "GrantPolicy", "ActivatePolicy" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Policy satisfaction token found: {token}");
    }

    [TestMethod]
    public void ApprovalPackageReviewUi_DoesNotReferenceWorkflowTransitionActions()
    {
        var text = PageText();
        foreach (var token in new[] { "TransitionWorkflow", "ContinueWorkflow", "ResumeWorkflow", "RetryWorkflow", "RerunWorkflow", "StartWorkflow" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Workflow transition token found: {token}");
    }

    [TestMethod]
    public void ApprovalPackageReviewUi_DoesNotReferenceToolOrAgentExecutionActions()
    {
        var text = PageText();
        foreach (var token in new[] { "ExecuteTool", "InvokeTool", "ToolInvoker", "DispatchAgent", "AgentDispatcher", "CallModel", "BuildPrompt" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Tool/agent execution token found: {token}");
    }

    [TestMethod]
    public void ApprovalPackageReviewUi_DoesNotReferenceSourceApplyOrReleaseActions()
    {
        var text = ProductionText();
        foreach (var token in new[] { "ApplySourceAsync", "ApplyPatchAsync", "PatchApply", "SourceWriter", "PatchWriter", "ReleaseSoftware", "ApproveRelease" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Source apply/release token found: {token}");
    }

    [TestMethod]
    public void ApprovalPackageReviewUi_ContainsReadOnlyBoundaryLanguage()
    {
        var page = PageText();
        foreach (var required in new[]
        {
            "Read-only view",
            "Approval package is not accepted approval",
            "Approval package review is not approval",
            "Requested decision is not decision made",
            "Policy evidence is not policy satisfaction",
            "This UI cannot approve, reject, accept approvals, satisfy policy, transition workflow, invoke tools, dispatch agents, apply source, or release software."
        })
        {
            StringAssert.Contains(page, required);
        }
    }

    [TestMethod]
    public void ApprovalPackageReviewUi_ReceiptStatesCorrectBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR155_APPROVAL_PACKAGE_REVIEW_UI.md"));

        foreach (var required in new[]
        {
            "PR155 adds the Approval Package Review UI.",
            "Approval Package Review UI is read-only.",
            "Approval package is not accepted approval.",
            "Approval package review is not approval.",
            "Requested decision is not decision made.",
            "Human approval note is not accepted approval record.",
            "Approval requirement is not approval.",
            "Policy evidence is not policy satisfaction.",
            "Refresh is not retry.",
            "Navigation is not workflow continuation.",
            "Copy package id is not approval.",
            "This PR is not Block P approval authority.",
            "The UI consumes existing GET-only governance trace APIs.",
            "PR155 puts the approval package on the review table. It does not sign it."
        })
        {
            StringAssert.Contains(receipt, required);
        }
    }

    [TestMethod]
    public void ApprovalPackageReviewUi_IsRoutedFromGovernancePathOnly()
    {
        var shell = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.TauriShell", "src", "shell", "IronDevShell.tsx"));
        StringAssert.Contains(shell, "/governance/approval-packages");
        StringAssert.Contains(shell, "ApprovalPackageReviewRoute");
    }

    private static IEnumerable<string> GovernanceTraceApiBlocks(string api)
    {
        foreach (var marker in new[]
        {
            "searchGovernanceTraces(",
            "getGovernanceTrace("
        })
        {
            var start = api.IndexOf(marker, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, $"Missing governance trace API method: {marker}");
            var nextMethod = api.IndexOf("\n  async ", start + marker.Length, StringComparison.Ordinal);
            yield return nextMethod > start ? api[start..nextMethod] : api[start..];
        }
    }

    private static string ProductionText() =>
        string.Join("\n", ProductionFiles().Select(File.ReadAllText));

    private static string PageText() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.TauriShell", "src", "features", "governance", "ApprovalPackageReviewRoute.tsx"));

    private static string TypeText() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.TauriShell", "src", "features", "governance", "ApprovalPackageReviewTypes.ts"));

    private static string ApiTypeBlock()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.TauriShell", "src", "api", "types.ts"));
        var start = text.IndexOf("export interface ApprovalPackageFilter", StringComparison.Ordinal);
        var end = text.IndexOf("export interface LoginRequest", StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, "Missing approval package type block.");
        Assert.IsTrue(end > start, "Missing end of approval package type block.");
        return text[start..end];
    }

    private static IReadOnlyList<string> ProductionFiles()
    {
        var root = RepositoryRoot();
        return
        [
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "governance", "ApprovalPackageReviewRoute.tsx"),
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "governance", "ApprovalPackageReviewTypes.ts"),
            Path.Combine(root, "IronDev.TauriShell", "src", "shell", "IronDevShell.tsx")
        ];
    }

    private static IReadOnlyList<string> ForbiddenButtonLabels() =>
    [
        "Approve",
        "Reject",
        "Accept Approval",
        "Create Accepted Approval",
        "Grant",
        "Allow",
        "Deny",
        "Block",
        "Override",
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
        "Apply Patch",
        "Approve Release",
        "Release Software"
    ];

    private static IReadOnlyList<string> ForbiddenFunctionNames() =>
    [
        "approve",
        "reject",
        "acceptApproval",
        "createAcceptedApproval",
        "grantApproval",
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
        "applyPatch",
        "approveRelease",
        "releaseSoftware"
    ];

    private static IReadOnlyList<string> ForbiddenPayloadFields() =>
    [
        "PayloadJson",
        "payloadJson",
        "ApprovalPayloadJson",
        "approvalPayloadJson",
        "ApprovalNotesRaw",
        "approvalNotesRaw",
        "RawApprovalText",
        "rawApprovalText",
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
