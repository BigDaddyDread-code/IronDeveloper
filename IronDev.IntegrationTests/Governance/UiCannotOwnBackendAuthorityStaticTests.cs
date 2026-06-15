using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("UiCannotOwnBackendAuthority")]
public sealed class UiCannotOwnBackendAuthorityStaticTests
{
    private static readonly IReadOnlyList<string> SurfaceFiles =
    [
        Path.Combine("IronDev.TauriShell", "src", "features", "governance", "GovernanceTimelineRoute.tsx"),
        Path.Combine("IronDev.TauriShell", "src", "features", "governance", "ToolGateDecisionRoute.tsx"),
        Path.Combine("IronDev.TauriShell", "src", "features", "governance", "ApprovalPackageReviewRoute.tsx"),
        Path.Combine("IronDev.TauriShell", "src", "features", "governance", "DogfoodReceiptViewerRoute.tsx"),
        Path.Combine("IronDev.TauriShell", "src", "features", "governance", "WorkflowRunStepViewerRoute.tsx"),
        Path.Combine("IronDev.TauriShell", "src", "features", "governance", "MemoryProposalReviewRoute.tsx")
    ];

    [TestMethod]
    public void UiObservabilitySurfaces_AreKnownAndPresent()
    {
        foreach (var relativePath in SurfaceFiles)
        {
            Assert.IsTrue(File.Exists(FullPath(relativePath)), $"Missing expected read-only UI surface: {relativePath}");
        }

        Assert.IsTrue(File.Exists(FullPath(Path.Combine("IronDev.TauriShell", "src", "shell", "IronDevShell.tsx"))));
        Assert.IsTrue(File.Exists(FullPath(Path.Combine("IronDev.TauriShell", "src", "app", "routes.ts"))));
        Assert.IsTrue(File.Exists(FullPath(Path.Combine("IronDev.TauriShell", "src", "api", "ironDevApi.ts"))));
    }

    [TestMethod]
    public void UiObservabilitySurfaces_DoNotRenderAuthorityButtons()
    {
        foreach (var (relativePath, text) in SurfaceTexts())
        {
            foreach (var label in ForbiddenButtonLabels())
            {
                foreach (var pattern in RenderedLabelPatterns(label))
                {
                    Assert.IsFalse(
                        text.Contains(pattern, StringComparison.OrdinalIgnoreCase),
                        $"{relativePath} renders forbidden authority label {label} via pattern {pattern}.");
                }
            }
        }
    }

    [TestMethod]
    public void UiObservabilitySurfaces_DoNotDeclareAuthorityHandlers()
    {
        foreach (var (relativePath, text) in SurfaceTexts())
        {
            foreach (var name in ForbiddenFunctionNames())
            {
                foreach (var pattern in HandlerPatterns(name))
                {
                    Assert.IsFalse(
                        Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                        $"{relativePath} declares forbidden authority handler pattern {pattern}.");
                }
            }
        }
    }

    [TestMethod]
    public void UiObservabilitySurfaces_UseReadOnlyApiMethodsOnly()
    {
        var api = File.ReadAllText(FullPath(Path.Combine("IronDev.TauriShell", "src", "api", "ironDevApi.ts")));
        var allowedMethods = ReadOnlyViewerApiMethods();
        var usedMethods = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (_, text) in SurfaceTexts())
        {
            foreach (Match match in Regex.Matches(text, @"session\.client\.(?<method>[A-Za-z0-9_]+)\s*\(", RegexOptions.CultureInvariant))
            {
                usedMethods.Add(match.Groups["method"].Value);
            }
        }

        CollectionAssert.IsSubsetOf(usedMethods.ToList(), allowedMethods.ToList(), "Viewer route used an API method that is not in the read-only viewer allowlist.");

        foreach (var method in usedMethods)
        {
            var block = ApiMethodBlock(api, method);
            StringAssert.Contains(block, "method: 'GET'");
            AssertNoHttpMutation(block, method);
        }
    }

    [TestMethod]
    public void UiObservabilityRoutes_DoNotDeclareAuthorityPaths()
    {
        var routeText = File.ReadAllText(FullPath(Path.Combine("IronDev.TauriShell", "src", "shell", "IronDevShell.tsx"))) +
            "\n" +
            File.ReadAllText(FullPath(Path.Combine("IronDev.TauriShell", "src", "app", "routes.ts")));

        foreach (var fragment in ForbiddenRouteFragments())
        {
            var singleQuoted = $"'{fragment}";
            var doubleQuoted = $"\"{fragment}";
            Assert.IsFalse(routeText.Contains(singleQuoted, StringComparison.OrdinalIgnoreCase), $"Forbidden route fragment found: {singleQuoted}");
            Assert.IsFalse(routeText.Contains(doubleQuoted, StringComparison.OrdinalIgnoreCase), $"Forbidden route fragment found: {doubleQuoted}");
            Assert.IsFalse(routeText.Contains($"/{fragment}", StringComparison.OrdinalIgnoreCase), $"Forbidden route action path found: /{fragment}");
        }

        foreach (var allowed in new[]
        {
            "/governance/timeline",
            "/governance/tool-gates",
            "/governance/approval-packages",
            "/governance/dogfood-receipts",
            "/governance/memory-proposals",
            "/workflows/runs"
        })
        {
            StringAssert.Contains(routeText, allowed);
        }
    }

    [TestMethod]
    public void UiObservabilitySurfaces_DoNotExposeRawPayloadFields()
    {
        foreach (var (relativePath, text) in SurfaceAndTypeTexts())
        {
            var scanText = StripUnsafeMarkerArrays(text);
            foreach (var token in ForbiddenPayloadFields())
            {
                Assert.IsFalse(
                    scanText.Contains(token, StringComparison.Ordinal),
                    $"{relativePath} exposes raw/private/confidential field token: {token}");
            }
        }
    }

    [TestMethod]
    public void UiObservabilitySurfaces_ContainBoundaryLanguage()
    {
        AssertContainsBoundary("GovernanceTimelineRoute.tsx", "Read-only", "Timeline is not authority");
        AssertContainsBoundary(
            "ToolGateDecisionRoute.tsx",
            "Read-only",
            "Tool request visibility is not tool execution",
            "Gate decision visibility is not gate authority");
        AssertContainsBoundary(
            "ApprovalPackageReviewRoute.tsx",
            "Read-only",
            "Approval package review is not approval",
            "Approval package is not accepted approval");
        AssertContainsBoundary(
            "DogfoodReceiptViewerRoute.tsx",
            "Read-only",
            "Dogfood receipt is not release approval",
            "Dogfood pass is not release readiness");
        AssertContainsBoundary(
            "WorkflowRunStepViewerRoute.tsx",
            "Read-only",
            "Workflow visibility is not workflow authority",
            "Workflow status is not transition permission",
            "Step status is not execution permission");
        AssertContainsBoundary(
            "MemoryProposalReviewRoute.tsx",
            "Read-only",
            "Memory proposal is not accepted memory",
            "Memory review is not memory promotion",
            "Retrieval candidate is not retrieval activation");
    }

    [TestMethod]
    public void UiCannotOwnBackendAuthority_ReceiptStatesGlobalBoundary()
    {
        var receipt = File.ReadAllText(FullPath(Path.Combine("Docs", "receipts", "PR159_UI_CANNOT_OWN_BACKEND_AUTHORITY_TESTS.md")));
        foreach (var required in new[]
        {
            "PR159 adds UI Cannot Own Backend Authority tests.",
            "This PR is tests/receipt only.",
            "UI visibility is not backend authority.",
            "UI refresh is not retry.",
            "UI navigation is not workflow continuation.",
            "UI search is not governance replay.",
            "UI copy is not approval.",
            "UI selection is not decision.",
            "UI route is not capability.",
            "UI view model is not authority.",
            "UI cannot approve.",
            "UI cannot satisfy policy.",
            "UI cannot transition workflow.",
            "UI cannot execute workflow.",
            "UI cannot invoke tools.",
            "UI cannot dispatch agents.",
            "UI cannot apply source.",
            "UI cannot promote memory.",
            "UI cannot activate retrieval.",
            "UI cannot approve release.",
            "UI cannot own release readiness.",
            "PR159 bolts the cockpit glass down. It does not add a steering wheel."
        })
        {
            StringAssert.Contains(receipt, required);
        }
    }

    private static void AssertContainsBoundary(string fileName, params string[] requiredText)
    {
        var text = File.ReadAllText(FullPath(Path.Combine("IronDev.TauriShell", "src", "features", "governance", fileName)));
        foreach (var required in requiredText)
        {
            StringAssert.Contains(text, required);
        }
    }

    private static IEnumerable<(string RelativePath, string Text)> SurfaceTexts() =>
        SurfaceFiles.Select(relativePath => (relativePath, File.ReadAllText(FullPath(relativePath))));

    private static IEnumerable<(string RelativePath, string Text)> SurfaceAndTypeTexts()
    {
        foreach (var surface in SurfaceTexts())
        {
            yield return surface;
        }

        var typeFiles = Directory.GetFiles(
            FullPath(Path.Combine("IronDev.TauriShell", "src", "features", "governance")),
            "*Types.ts",
            SearchOption.TopDirectoryOnly);

        foreach (var typeFile in typeFiles)
        {
            yield return (Path.GetRelativePath(RepositoryRoot(), typeFile), File.ReadAllText(typeFile));
        }
    }

    private static IEnumerable<string> RenderedLabelPatterns(string label) =>
    [
        $">{label}<",
        $"\"{label}\"",
        $"'{label}'",
        $"label: '{label}'",
        $"label: \"{label}\""
    ];

    private static IEnumerable<string> HandlerPatterns(string name)
    {
        var pascal = char.ToUpperInvariant(name[0]) + name[1..];
        return
        [
            $@"\bfunction\s+{Regex.Escape(name)}\b",
            $@"\bconst\s+{Regex.Escape(name)}\b",
            $@"\b{Regex.Escape(name)}\s*\(",
            $@"\bhandle{Regex.Escape(pascal)}\b",
            $@"\bon{Regex.Escape(pascal)}\b"
        ];
    }

    private static string StripUnsafeMarkerArrays(string text) =>
        Regex.Replace(
            text,
            @"const\s+\w*unsafe\w*Markers\s*=\s*\[(.|\n)*?\];",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static void AssertNoHttpMutation(string block, string method)
    {
        foreach (var token in new[]
        {
            "method: 'POST'",
            "method: \"POST\"",
            "method: 'PUT'",
            "method: \"PUT\"",
            "method: 'PATCH'",
            "method: \"PATCH\"",
            "method: 'DELETE'",
            "method: \"DELETE\""
        })
        {
            Assert.IsFalse(block.Contains(token, StringComparison.Ordinal), $"{method} must not contain {token}.");
        }
    }

    private static string ApiMethodBlock(string api, string method)
    {
        var start = api.IndexOf($"async {method}", StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, $"Missing API method {method}.");
        var nextMethod = api.IndexOf("\n  async ", start + 1, StringComparison.Ordinal);
        return nextMethod > start ? api[start..nextMethod] : api[start..];
    }

    private static IReadOnlySet<string> ReadOnlyViewerApiMethods() =>
        new HashSet<string>(StringComparer.Ordinal)
        {
            "searchGovernanceTraces",
            "getGovernanceTrace",
            "getGovernanceTraceByCorrelation",
            "getGovernanceTraceByWorkflowRun",
            "getToolRequest",
            "getToolGateDecision",
            "getDogfoodLoopReceipt",
            "listWorkflowRuns",
            "listWorkflowRunsByCorrelation",
            "getWorkflowRun",
            "listWorkflowSteps",
            "getWorkflowStep"
        };

    private static IReadOnlyList<string> ForbiddenButtonLabels() =>
    [
        "Approve",
        "Approve Request",
        "Reject",
        "Reject Request",
        "Accept",
        "Accept Approval",
        "Grant Approval",
        "Deny",
        "Override",
        "Override Gate",
        "Reopen Gate",
        "Satisfy Policy",
        "Mark Policy Satisfied",
        "Start Workflow",
        "Continue Workflow",
        "Transition Workflow",
        "Retry",
        "Retry Workflow",
        "Rerun",
        "Rerun Workflow",
        "Resume",
        "Resume Workflow",
        "Repair",
        "Repair Workflow",
        "Fix",
        "Heal",
        "Restart",
        "Execute",
        "Execute Workflow",
        "Invoke Tool",
        "Dispatch Agent",
        "Call Model",
        "Build Prompt",
        "Apply Source",
        "Apply Patch",
        "Promote Memory",
        "Accept Memory",
        "Approve Memory",
        "Write Memory",
        "Merge Memory",
        "Commit Memory",
        "Publish Memory",
        "Activate Retrieval",
        "Approve Cross-project Learning",
        "Approve Release",
        "Release",
        "Ship",
        "Mark Dogfood Passed",
        "Mark Passed",
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
        "acceptApproval",
        "grantApproval",
        "denyApproval",
        "overrideGate",
        "reopenGate",
        "satisfyPolicy",
        "markPolicySatisfied",
        "startWorkflow",
        "continueWorkflow",
        "transitionWorkflow",
        "retryWorkflow",
        "rerunWorkflow",
        "resumeWorkflow",
        "repairWorkflow",
        "fixWorkflow",
        "healWorkflow",
        "restartWorkflow",
        "executeWorkflow",
        "invokeTool",
        "dispatchAgent",
        "callModel",
        "buildPrompt",
        "applySource",
        "applyPatch",
        "promoteMemory",
        "acceptMemory",
        "approveMemory",
        "writeMemory",
        "mergeMemory",
        "commitMemory",
        "publishMemory",
        "activateRetrieval",
        "approveCrossProjectLearning",
        "approveRelease",
        "releaseSoftware",
        "markDogfoodPassed",
        "runMigration",
        "runCleanup",
        "deleteRecord",
        "purgeRecord",
        "archiveRecord",
        "redactRecord"
    ];

    private static IReadOnlyList<string> ForbiddenRouteFragments() =>
    [
        "/approve",
        "/reject",
        "/accept",
        "/grant",
        "/deny",
        "/override",
        "/reopen",
        "/satisfy",
        "/start",
        "/continue",
        "/transition",
        "/retry",
        "/rerun",
        "/resume",
        "/repair",
        "/execute",
        "/invoke",
        "/dispatch",
        "/apply-source",
        "/apply-patch",
        "/promote-memory",
        "/accept-memory",
        "/write-memory",
        "/activate-retrieval",
        "/approve-release",
        "/release",
        "/cleanup",
        "/delete",
        "/purge",
        "/archive",
        "/redact"
    ];

    private static IReadOnlyList<string> ForbiddenPayloadFields() =>
    [
        "PayloadJson",
        "payloadJson",
        "RawPayload",
        "rawPayload",
        "WorkflowPayloadJson",
        "workflowPayloadJson",
        "StepPayloadJson",
        "stepPayloadJson",
        "ExecutionPayloadJson",
        "executionPayloadJson",
        "MemoryPayloadJson",
        "memoryPayloadJson",
        "RawMemoryText",
        "rawMemoryText",
        "RawPrompt",
        "rawPrompt",
        "RawCompletion",
        "rawCompletion",
        "RawToolOutput",
        "rawToolOutput",
        "RawCommandOutput",
        "rawCommandOutput",
        "StdOut",
        "stdOut",
        "StdErr",
        "stdErr",
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
        "ConfidentialClientDetail",
        "confidentialClientDetail",
        "EmployerDetail",
        "employerDetail",
        "ConnectionString",
        "connectionString",
        "Password",
        "password",
        "Secret",
        "secret",
        "ApiKey",
        "apiKey",
        "AccessToken",
        "accessToken",
        "AuthToken",
        "authToken",
        "Credential",
        "credential",
        "Bearer",
        "bearer"
    ];

    private static string FullPath(string relativePath) => Path.Combine(RepositoryRoot(), relativePath);

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
