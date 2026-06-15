using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("ReadOnlyGovernanceTimelineUi")]
public sealed class ReadOnlyGovernanceTimelineUiStaticBoundaryTests
{
    [TestMethod]
    public void GovernanceTimelineUi_UsesGetOnly()
    {
        var api = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.TauriShell", "src", "api", "ironDevApi.ts"));

        foreach (var endpoint in new[]
        {
            "/api/v1/governance/traces",
            "/api/v1/governance/traces/",
            "/api/v1/governance/traces/by-correlation/",
            "/api/v1/governance/traces/by-workflow-run/"
        })
        {
            StringAssert.Contains(api, endpoint);
        }

        foreach (var block in GovernanceApiBlocks(api))
        {
            StringAssert.Contains(block, "method: 'GET'");
            Assert.IsFalse(block.Contains("method: 'POST'", StringComparison.Ordinal), "Governance timeline API facade must not POST.");
            Assert.IsFalse(block.Contains("method: 'PUT'", StringComparison.Ordinal), "Governance timeline API facade must not PUT.");
            Assert.IsFalse(block.Contains("method: 'DELETE'", StringComparison.Ordinal), "Governance timeline API facade must not DELETE.");
        }
    }

    [TestMethod]
    public void GovernanceTimelineUi_DoesNotDeclareControlActions()
    {
        var text = PageText();
        foreach (var token in ForbiddenMethodTokens())
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden control method token found: {token}");
    }

    [TestMethod]
    public void GovernanceTimelineUi_DoesNotRenderMutatingButtons()
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
            "Copy Reference",
            "Open Trace",
            "Open Diagnosis",
            "Open Correlation Report",
            "Open Agent Health",
            "Open Backend Health"
        })
        {
            StringAssert.Contains(page, label);
        }
    }

    [TestMethod]
    public void GovernanceTimelineUi_DoesNotExposeRawPayloadFields()
    {
        var text = ProductionUiText();
        foreach (var field in ForbiddenPayloadFields())
        {
            Assert.IsFalse(text.Contains($".{field}", StringComparison.Ordinal), $"UI must not read raw field {field}.");
            Assert.IsFalse(text.Contains($"['{field}']", StringComparison.Ordinal), $"UI must not index raw field {field}.");
            Assert.IsFalse(text.Contains($"[\"{field}\"]", StringComparison.Ordinal), $"UI must not index raw field {field}.");
        }
    }

    [TestMethod]
    public void GovernanceTimelineUi_DoesNotReferenceApprovalActions()
    {
        var text = PageText();
        foreach (var token in new[] { "ApproveAsync", "RejectAsync", "GrantApproval", "SatisfyApproval", "CreateApprovalDecision" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Approval action token found: {token}");
    }

    [TestMethod]
    public void GovernanceTimelineUi_DoesNotReferenceWorkflowTransitionActions()
    {
        var text = PageText();
        foreach (var token in new[] { "TransitionWorkflow", "ContinueWorkflow", "ResumeWorkflow", "RetryWorkflow", "RerunWorkflow" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Workflow transition token found: {token}");
    }

    [TestMethod]
    public void GovernanceTimelineUi_DoesNotReferenceToolOrAgentExecutionActions()
    {
        var text = PageText();
        foreach (var token in new[] { "InvokeTool", "ToolInvoker", "DispatchAgent", "AgentDispatcher", "CallModel", "BuildPrompt" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Tool/agent execution token found: {token}");
    }

    [TestMethod]
    public void GovernanceTimelineUi_DoesNotReferenceSourceApplyActions()
    {
        var text = PageText();
        foreach (var token in new[] { "ApplySourceAsync", "ApplyPatchAsync", "PatchApply", "SourceWriter", "PatchWriter" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Source apply token found: {token}");
    }

    [TestMethod]
    public void GovernanceTimelineUi_DoesNotReferenceCleanupActions()
    {
        var text = PageText();
        foreach (var token in new[] { "CleanupAsync", "DeleteAsync", "PurgeAsync", "ArchiveAsync", "RedactAsync", "RunMigrationAsync" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Cleanup/action token found: {token}");
    }

    [TestMethod]
    public void GovernanceTimelineUi_ContainsReadOnlyBoundaryLanguage()
    {
        var page = PageText();
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR153_READ_ONLY_GOVERNANCE_TIMELINE_UI.md"));

        foreach (var required in new[]
        {
            "Read-only view",
            "Timeline is not authority",
            "Observation is not approval",
            "Traceability is not mutation permission",
            "This UI cannot approve, execute, retry, repair, transition workflow, invoke tools, dispatch agents, apply source, or clean up data."
        })
        {
            StringAssert.Contains(page, required);
        }

        foreach (var required in new[]
        {
            "PR153 adds the Read-only Governance Timeline UI.",
            "Governance Timeline UI is read-only.",
            "Timeline is not authority.",
            "Observation is not approval.",
            "Traceability is not mutation permission.",
            "Refresh is not retry.",
            "Navigation is not workflow continuation.",
            "Search is not governance replay.",
            "Copy reference is not approval.",
            "The UI consumes existing GET-only governance trace APIs.",
            "PR153 draws the governance timeline. It does not add timeline controls."
        })
        {
            StringAssert.Contains(receipt, required);
        }
    }

    private static IEnumerable<string> GovernanceApiBlocks(string api)
    {
        foreach (var marker in new[]
        {
            "searchGovernanceTraces",
            "getGovernanceTrace(",
            "getGovernanceTraceByCorrelation",
            "getGovernanceTraceByWorkflowRun"
        })
        {
            var start = api.IndexOf(marker, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, $"Missing governance API method: {marker}");
            var nextMethod = api.IndexOf("\n  async ", start + marker.Length, StringComparison.Ordinal);
            yield return nextMethod > start ? api[start..nextMethod] : api[start..];
        }
    }

    private static string ProductionUiText() =>
        string.Join("\n", ProductionUiFiles().Select(File.ReadAllText));

    private static string PageText() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.TauriShell", "src", "features", "governance", "GovernanceTimelineRoute.tsx"));

    private static IReadOnlyList<string> ProductionUiFiles()
    {
        var root = RepositoryRoot();
        return
        [
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "governance", "GovernanceTimelineRoute.tsx"),
            Path.Combine(root, "IronDev.TauriShell", "src", "app", "routes.ts"),
            Path.Combine(root, "IronDev.TauriShell", "src", "state", "useWorkspaceNavigation.tsx"),
            Path.Combine(root, "IronDev.TauriShell", "src", "shell", "IronDevShell.tsx"),
            Path.Combine(root, "IronDev.TauriShell", "src", "api", "ironDevApi.ts"),
            Path.Combine(root, "IronDev.TauriShell", "src", "api", "types.ts")
        ];
    }

    private static IReadOnlyList<string> ForbiddenButtonLabels() =>
    [
        "Approve",
        "Reject",
        "Grant",
        "Satisfy Policy",
        "Continue Workflow",
        "Transition Workflow",
        "Retry",
        "Rerun",
        "Resume",
        "Repair",
        "Fix",
        "Restart",
        "Invoke Tool",
        "Dispatch Agent",
        "Call Model",
        "Build Prompt",
        "Create Ticket",
        "Promote Memory",
        "Activate Retrieval",
        "Apply Source",
        "Apply Patch",
        "Approve Release",
        "Mark Dogfood Passed",
        "Run Migration",
        "Cleanup",
        "Delete",
        "Purge",
        "Archive",
        "Redact"
    ];

    private static IReadOnlyList<string> ForbiddenMethodTokens() =>
    [
        "approveTrace",
        "rejectTrace",
        "grantTrace",
        "satisfyPolicy",
        "continueWorkflow",
        "transitionWorkflow",
        "retryWorkflow",
        "rerunWorkflow",
        "resumeWorkflow",
        "repairWorkflow",
        "invokeTool",
        "dispatchAgent",
        "callModel",
        "buildPrompt",
        "createTicket",
        "promoteMemory",
        "activateRetrieval",
        "applySource",
        "applyPatch",
        "approveRelease",
        "markDogfoodPassed",
        "runMigration",
        "cleanupTrace",
        "deleteTrace",
        "purgeTrace",
        "archiveTrace",
        "redactTrace"
    ];

    private static IReadOnlyList<string> ForbiddenPayloadFields() =>
    [
        "PayloadJson",
        "RawPayload",
        "RawPrompt",
        "RawCompletion",
        "RawToolOutput",
        "RawCommandOutput",
        "StdOut",
        "StdErr",
        "PrivateReasoning",
        "HiddenReasoning",
        "ChainOfThought",
        "Scratchpad",
        "SourceContent",
        "SourceFileContents",
        "PatchPayload",
        "DiffPayload",
        "ConnectionString",
        "Password",
        "Secret",
        "ApiKey",
        "Token",
        "Credential",
        "Bearer"
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
