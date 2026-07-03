using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("StaticBoundary")]
[TestClass]
[TestCategory("ReadOnlyToolGateDecisionUi")]
public sealed class ReadOnlyToolGateDecisionUiStaticBoundaryTests
{
    [TestMethod]
    public void ToolGateDecisionUi_UsesGetOnly()
    {
        var api = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.TauriShell", "src", "api", "ironDevApi.ts"));

        foreach (var endpoint in new[]
        {
            "/api/v1/tool-requests/",
            "/api/v1/tool-gates/evaluations/"
        })
        {
            StringAssert.Contains(api, endpoint);
        }

        foreach (var block in ToolGateApiBlocks(api))
        {
            StringAssert.Contains(block, "method: 'GET'");
            Assert.IsFalse(block.Contains("method: 'POST'", StringComparison.Ordinal), "Tool gate UI API facade must not POST.");
            Assert.IsFalse(block.Contains("method: 'PUT'", StringComparison.Ordinal), "Tool gate UI API facade must not PUT.");
            Assert.IsFalse(block.Contains("method: 'PATCH'", StringComparison.Ordinal), "Tool gate UI API facade must not PATCH.");
            Assert.IsFalse(block.Contains("method: 'DELETE'", StringComparison.Ordinal), "Tool gate UI API facade must not DELETE.");
        }
    }

    [TestMethod]
    public void ToolGateDecisionUi_DoesNotDeclareControlActions()
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
    public void ToolGateDecisionUi_DoesNotRenderMutatingButtons()
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
            "Copy Request ID",
            "Copy Decision ID",
            "Open Request",
            "Open Decision",
            "Open Trace",
            "Open Timeline",
            "Open Apply Preview"
        })
        {
            StringAssert.Contains(page, label);
        }
    }

    [TestMethod]
    public void ToolGateDecisionUi_DoesNotExposeRawPayloadFields()
    {
        var text = PageText() + "\n" + ToolGateTypeBlock();
        foreach (var field in ForbiddenPayloadFields())
        {
            Assert.IsFalse(text.Contains($".{field}", StringComparison.Ordinal), $"UI must not read raw field {field}.");
            Assert.IsFalse(text.Contains($"['{field}']", StringComparison.Ordinal), $"UI must not index raw field {field}.");
            Assert.IsFalse(text.Contains($"[\"{field}\"]", StringComparison.Ordinal), $"UI must not index raw field {field}.");
        }
    }

    [TestMethod]
    public void ToolGateDecisionUi_DoesNotReferenceApprovalActions()
    {
        var text = ProductionToolGateText();
        foreach (var token in new[] { "ApproveAsync", "RejectAsync", "GrantApproval", "SatisfyApproval", "CreateApprovalDecision" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Approval action token found: {token}");
    }

    [TestMethod]
    public void ToolGateDecisionUi_DoesNotReferenceGateOverrideActions()
    {
        var text = PageText();
        foreach (var token in new[] { "OverrideGate", "ReopenGate", "AllowGate", "DenyGate", "BlockGate" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Gate control token found: {token}");
    }

    [TestMethod]
    public void ToolGateDecisionUi_DoesNotReferenceWorkflowTransitionActions()
    {
        var text = PageText();
        foreach (var token in new[] { "TransitionWorkflow", "ContinueWorkflow", "ResumeWorkflow", "RetryWorkflow", "RerunWorkflow" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Workflow transition token found: {token}");
    }

    [TestMethod]
    public void ToolGateDecisionUi_DoesNotReferenceToolOrAgentExecutionActions()
    {
        var text = PageText();
        foreach (var token in new[] { "ExecuteTool", "InvokeTool", "ToolInvoker", "DispatchAgent", "AgentDispatcher", "CallModel", "BuildPrompt" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Tool/agent execution token found: {token}");
    }

    [TestMethod]
    public void ToolGateDecisionUi_DoesNotReferenceSourceApplyActions()
    {
        var text = ProductionToolGateText();
        foreach (var token in new[] { "ApplySourceAsync", "ApplyPatchAsync", "PatchApply", "SourceWriter", "PatchWriter" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Source apply token found: {token}");
    }

    [TestMethod]
    public void ToolGateDecisionUi_DoesNotReferenceCleanupActions()
    {
        var text = ProductionToolGateText();
        foreach (var token in new[] { "CleanupAsync", "DeleteAsync", "PurgeAsync", "ArchiveAsync", "RedactAsync", "RunMigrationAsync" })
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Cleanup/action token found: {token}");
    }

    [TestMethod]
    public void ToolGateDecisionUi_ContainsReadOnlyBoundaryLanguage()
    {
        var page = PageText();
        foreach (var required in new[]
        {
            "Read-only view",
            "Tool request visibility is not tool execution",
            "Gate decision visibility is not gate authority",
            "Approval requirement is not approval",
            "Policy evidence is not policy satisfaction",
            "This UI cannot approve, reject, execute tools, reopen gates, satisfy policy, transition workflow, apply source, or clean up data."
        })
        {
            StringAssert.Contains(page, required);
        }
    }

    [TestMethod]
    public void ToolGateDecisionUi_ReceiptStatesCorrectBoundary()
    {
        var receipt = File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR154_TOOL_REQUEST_AND_GATE_DECISION_UI.md"));

        foreach (var required in new[]
        {
            "PR154 adds the Tool Request and Gate Decision UI.",
            "Tool Request and Gate Decision UI is read-only.",
            "Tool request visibility is not tool execution.",
            "Gate decision visibility is not gate authority.",
            "Gate allowed status is not tool invocation.",
            "Gate denied status is not repair.",
            "Approval requirement is not approval.",
            "Policy evidence is not policy satisfaction.",
            "Refresh is not retry.",
            "Navigation is not workflow continuation.",
            "Copy request id is not approval.",
            "Copy decision id is not policy satisfaction.",
            "The UI consumes existing GET-only tool request and tool gate APIs.",
            "PR154 shows the gate ledger. It does not open the gate."
        })
        {
            StringAssert.Contains(receipt, required);
        }
    }

    private static IEnumerable<string> ToolGateApiBlocks(string api)
    {
        foreach (var marker in new[]
        {
            "getToolRequest(",
            "getToolGateDecision("
        })
        {
            var start = api.IndexOf(marker, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, $"Missing tool gate API method: {marker}");
            var nextMethod = api.IndexOf("\n  async ", start + marker.Length, StringComparison.Ordinal);
            yield return nextMethod > start ? api[start..nextMethod] : api[start..];
        }
    }

    private static string ProductionToolGateText() =>
        string.Join("\n", ProductionToolGateFiles().Select(File.ReadAllText)) + "\n" + ToolGateTypeBlock();

    private static string PageText() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.TauriShell", "src", "features", "governance", "ToolGateDecisionRoute.tsx"));

    private static string ToolGateTypeBlock()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.TauriShell", "src", "api", "types.ts"));
        var start = text.IndexOf("export interface ToolGateFilter", StringComparison.Ordinal);
        var end = text.IndexOf("export interface LoginRequest", StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, "Missing tool gate type block.");
        Assert.IsTrue(end > start, "Missing end of tool gate type block.");
        return text[start..end];
    }

    private static IReadOnlyList<string> ProductionToolGateFiles()
    {
        var root = RepositoryRoot();
        return
        [
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "governance", "ToolGateDecisionRoute.tsx"),
            Path.Combine(root, "IronDev.TauriShell", "src", "features", "governance", "ToolGateDecisionTypes.ts"),
            Path.Combine(root, "IronDev.TauriShell", "src", "app", "routes.ts"),
            Path.Combine(root, "IronDev.TauriShell", "src", "state", "useWorkspaceNavigation.tsx"),
            Path.Combine(root, "IronDev.TauriShell", "src", "flow", "FlowShell.tsx"),
            Path.Combine(root, "IronDev.TauriShell", "src", "flow", "library", "governanceRoutes.ts"),
            Path.Combine(root, "IronDev.TauriShell", "src", "flow", "library", "GovernanceHost.tsx"),
            Path.Combine(root, "IronDev.TauriShell", "src", "api", "ironDevApi.ts")
        ];
    }

    private static IReadOnlyList<string> ForbiddenButtonLabels() =>
    [
        "Approve",
        "Reject",
        "Grant",
        "Allow",
        "Deny",
        "Block",
        "Override",
        "Reopen Gate",
        "Satisfy Policy",
        "Continue Workflow",
        "Transition Workflow",
        "Retry",
        "Rerun",
        "Resume",
        "Repair",
        "Fix",
        "Restart",
        "Execute Tool",
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

    private static IReadOnlyList<string> ForbiddenFunctionNames() =>
    [
        "approve",
        "reject",
        "grantApproval",
        "allowGate",
        "denyGate",
        "blockGate",
        "overrideGate",
        "reopenGate",
        "satisfyPolicy",
        "continueWorkflow",
        "transitionWorkflow",
        "retryWorkflow",
        "rerunWorkflow",
        "resumeWorkflow",
        "repairWorkflow",
        "restartAgent",
        "restartBackend",
        "executeTool",
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
        "runCleanup",
        "deleteRecord",
        "purgeRecord",
        "archiveRecord",
        "redactRecord"
    ];

    private static IReadOnlyList<string> ForbiddenPayloadFields() =>
    [
        "PayloadJson",
        "payloadJson",
        "RawPayload",
        "rawPayload",
        "RequestPayloadJson",
        "requestPayloadJson",
        "DecisionPayloadJson",
        "decisionPayloadJson",
        "ToolInputJson",
        "toolInputJson",
        "ToolOutputJson",
        "toolOutputJson",
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
