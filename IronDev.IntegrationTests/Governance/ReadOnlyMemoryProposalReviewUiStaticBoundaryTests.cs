using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("ReadOnlyMemoryProposalReviewUi")]
public sealed class ReadOnlyMemoryProposalReviewUiStaticBoundaryTests
{
    [TestMethod]
    public void MemoryProposalReviewUi_UsesGetOnly()
    {
        var page = PageText();
        var api = File.ReadAllText(Path.Combine(RepositoryRoot(), "IronDev.TauriShell", "src", "api", "ironDevApi.ts"));

        foreach (var method in new[]
        {
            "searchGovernanceTraces",
            "getGovernanceTrace",
            "getGovernanceTraceByCorrelation",
            "getGovernanceTraceByWorkflowRun"
        })
        {
            StringAssert.Contains(page, method);
            var block = ApiReadBlock(api, method);
            StringAssert.Contains(block, "method: 'GET'");
            AssertNoHttpMutation(block, method);
        }
    }

    [TestMethod]
    public void MemoryProposalReviewUi_DoesNotDeclareControlActions()
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
    public void MemoryProposalReviewUi_DoesNotRenderMemoryMutationButtons()
    {
        AssertNoRenderedButtons("Accept Memory", "Approve Memory", "Promote Memory", "Write Memory", "Merge Memory", "Commit Memory", "Publish Memory", "Apply Memory", "Create Memory");
    }

    [TestMethod]
    public void MemoryProposalReviewUi_DoesNotRenderRetrievalActivationButtons()
    {
        AssertNoRenderedButtons("Activate Retrieval");
    }

    [TestMethod]
    public void MemoryProposalReviewUi_DoesNotRenderCrossProjectApprovalButtons()
    {
        AssertNoRenderedButtons("Approve Cross-project Learning", "Accept Portable Memory", "Promote Portable Memory");
    }

    [TestMethod]
    public void MemoryProposalReviewUi_DoesNotRenderWorkflowTransitionButtons()
    {
        AssertNoRenderedButtons("Continue Workflow", "Transition Workflow", "Retry", "Repair");
    }

    [TestMethod]
    public void MemoryProposalReviewUi_DoesNotRenderToolOrAgentExecutionButtons()
    {
        AssertNoRenderedButtons("Execute Tool", "Invoke Tool", "Dispatch Agent", "Call Model", "Build Prompt");
    }

    [TestMethod]
    public void MemoryProposalReviewUi_DoesNotRenderSourceApplyButtons()
    {
        AssertNoRenderedButtons("Apply Source", "Apply Patch", "Approve Release", "Cleanup", "Delete", "Purge", "Archive", "Redact");
    }

    [TestMethod]
    public void MemoryProposalReviewUi_DoesNotExposeRawPayloadFields()
    {
        var combined = PageText() + "\n" + TypesText();
        foreach (var token in ForbiddenPayloadFields())
        {
            Assert.IsFalse(combined.Contains(token, StringComparison.Ordinal), $"Raw/private/confidential token exposed in UI source: {token}");
        }
    }

    [TestMethod]
    public void MemoryProposalReviewUi_DoesNotReferenceMemoryPromotionMutation()
    {
        var page = PageText();
        foreach (var token in new[]
        {
            "IMemoryPromotion",
            "PromoteMemoryAsync",
            "AcceptMemoryAsync",
            "CreateAcceptedMemory",
            "ActivateRetrievalAsync",
            "WriteMemoryAsync",
            "MemoryPromotionStore",
            "CollectiveMemoryWrite"
        })
        {
            Assert.IsFalse(page.Contains(token, StringComparison.Ordinal), $"Viewer must not reference mutation path: {token}");
        }
    }

    [TestMethod]
    public void MemoryProposalReviewUi_ContainsReadOnlyBoundaryLanguage()
    {
        var page = PageText();
        foreach (var text in new[]
        {
            "Read-only view",
            "Memory proposal is not accepted memory",
            "Proposed memory summary is not memory",
            "Memory review is not memory promotion",
            "Retrieval candidate is not retrieval activation",
            "This UI cannot accept memory, promote memory, write memory, activate retrieval"
        })
        {
            StringAssert.Contains(page, text);
        }
    }

    [TestMethod]
    public void MemoryProposalReviewUi_ReceiptStatesCorrectBoundary()
    {
        var receipt = ReceiptText();
        foreach (var text in new[]
        {
            "PR158 adds the Memory Proposal Review UI.",
            "Memory Proposal Review UI is read-only.",
            "Memory proposal is not accepted memory.",
            "Proposed memory summary is not memory.",
            "Memory review is not memory promotion.",
            "Candidate learning is not portable engineering memory.",
            "Retrieval candidate is not retrieval activation.",
            "Cross-project learning suggestion is not cross-project authority.",
            "Copy proposal id is not acceptance.",
            "This PR does not accept memory, promote memory, write memory, activate retrieval, approve cross-project learning, or create accepted memory records.",
            "The UI consumes existing GET-only memory proposal/governance evidence APIs.",
            "PR158 shows the memory proposal. It does not remember it."
        })
        {
            StringAssert.Contains(receipt, text);
        }
    }

    [TestMethod]
    public void MemoryProposalReviewUi_AddsNoBackendControllerSqlCliOrRuntimeSurface()
    {
        var controllers = Directory.GetFiles(Path.Combine(RepositoryRoot(), "IronDev.Api", "Controllers"), "*.cs", SearchOption.TopDirectoryOnly);
        Assert.IsFalse(controllers.Any(path => Path.GetFileName(path).Contains("MemoryProposalReview", StringComparison.OrdinalIgnoreCase)));

        var sqlFiles = Directory.GetFiles(Path.Combine(RepositoryRoot(), "Database"), "*.sql", SearchOption.TopDirectoryOnly);
        Assert.IsFalse(sqlFiles.Any(path => Path.GetFileName(path).Contains("memory_proposal_review", StringComparison.OrdinalIgnoreCase)));

        var cliRoot = Path.Combine(RepositoryRoot(), "tools", "IronDev.Cli");
        if (Directory.Exists(cliRoot))
        {
            var cliFiles = Directory.GetFiles(cliRoot, "*.*", SearchOption.AllDirectories)
                .Where(path =>
                    !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                    !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(cliFiles.Any(path => File.ReadAllText(path).Contains("MemoryProposalReview", StringComparison.Ordinal)));
        }
    }

    private static void AssertNoRenderedButtons(params string[] labels)
    {
        var page = PageText();
        foreach (var label in labels)
        {
            Assert.IsFalse(page.Contains($">{label}<", StringComparison.OrdinalIgnoreCase), $"Forbidden button label rendered: {label}");
        }
    }

    private static void AssertNoHttpMutation(string block, string method)
    {
        foreach (var token in new[] { "method: 'POST'", "method: \"POST\"", "method: 'PUT'", "method: \"PUT\"", "method: 'PATCH'", "method: \"PATCH\"", "method: 'DELETE'", "method: \"DELETE\"" })
        {
            Assert.IsFalse(block.Contains(token, StringComparison.Ordinal), $"{method} must not contain {token}.");
        }
    }

    private static string ApiReadBlock(string api, string method)
    {
        var start = api.IndexOf($"async {method}", StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, $"Missing API method {method}.");
        var nextMethod = api.IndexOf("\n  async ", start + 1, StringComparison.Ordinal);
        return nextMethod > start ? api[start..nextMethod] : api[start..];
    }

    private static IReadOnlyList<string> ForbiddenFunctionNames() =>
        new[]
        {
            "acceptMemory",
            "approveMemory",
            "promoteMemory",
            "writeMemory",
            "mergeMemory",
            "commitMemory",
            "publishMemory",
            "activateRetrieval",
            "acceptPortableMemory",
            "promotePortableMemory",
            "applyMemory",
            "createMemory",
            "approveCrossProjectLearning",
            "continueWorkflow",
            "transitionWorkflow",
            "retryWorkflow",
            "repairWorkflow",
            "executeTool",
            "invokeTool",
            "dispatchAgent",
            "callModel",
            "buildPrompt",
            "applySource",
            "applyPatch",
            "approveRelease",
            "runCleanup",
            "deleteRecord",
            "purgeRecord",
            "archiveRecord",
            "redactRecord"
        };

    private static IReadOnlyList<string> ForbiddenPayloadFields() =>
        new[]
        {
            "PayloadJson",
            "payloadJson",
            "MemoryPayloadJson",
            "memoryPayloadJson",
            "RawMemoryText",
            "rawMemoryText",
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
            "PatchPayload",
            "patchPayload",
            "ConfidentialClientDetail",
            "confidentialClientDetail",
            "EmployerDetail",
            "employerDetail",
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
        };

    private static string PageText() =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "IronDev.TauriShell",
            "src",
            "features",
            "governance",
            "MemoryProposalReviewRoute.tsx"));

    private static string TypesText() =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "IronDev.TauriShell",
            "src",
            "features",
            "governance",
            "MemoryProposalReviewTypes.ts"));

    private static string ReceiptText() =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR158_MEMORY_PROPOSAL_REVIEW_UI.md"));

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
