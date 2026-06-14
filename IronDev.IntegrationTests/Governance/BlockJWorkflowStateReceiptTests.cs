using System.Globalization;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("BlockJWorkflowStateReceipt")]
public sealed class BlockJWorkflowStateReceiptTests
{
    private static readonly string[] RequiredPrList =
    [
        "PR98 - Workflow Run Store",
        "PR99 - Workflow Step Store",
        "PR100 - Workflow Checkpoint Store",
        "PR101 - Step Input/Output Reference Model",
        "PR102 - Failure and Retry State Model",
        "PR103 - Workflow Read-only API",
        "PR104 - Workflow Inspection CLI Commands",
        "PR105 - Workflow State Contract Tests",
        "PR106 - Block J Workflow State Receipt"
    ];

    private static readonly string[] ForbiddenProductionDirectories =
    [
        "IronDev.Api",
        "tools/IronDev.Cli",
        "IronDev.Core/Workflow",
        "IronDev.Infrastructure/Workflow",
        "Database"
    ];

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DocumentExists()
    {
        Assert.IsTrue(File.Exists(ReceiptPath()), ReceiptPath());
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_IsLinkedFromBlockJDocument()
    {
        string blockJ = ReadBlockJDocument();

        AssertContains(blockJ, "## PR106 - Block J Workflow State Receipt");
        AssertContains(blockJ, "Docs/receipts/BLOCK_J_WORKFLOW_STATE_RECEIPT.md");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_NamesBlockJComplete()
    {
        string receipt = ReadReceipt();

        AssertContains(receipt, "Block J - Durable Workflow Run Substrate");
        AssertContains(receipt, "Status: Complete");
        AssertContains(receipt, "Block J is complete as a workflow state substrate.");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_ListsPr98ThroughPr106()
    {
        AssertContainsAll(ReadReceipt(), RequiredPrList);
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DescribesWorkflowRunStore()
    {
        AssertContainsAll(ReadReceipt(), ["PR98 added durable workflow run storage.", "A workflow run record is not a running workflow."]);
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DescribesWorkflowStepStore()
    {
        AssertContainsAll(ReadReceipt(), ["PR99 added durable workflow step storage.", "A workflow step record is not an executed step."]);
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DescribesWorkflowCheckpointStore()
    {
        AssertContainsAll(ReadReceipt(), ["PR100 added durable workflow checkpoint storage.", "A checkpoint is not a resume point."]);
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DescribesStepInputOutputReferenceModel()
    {
        AssertContainsAll(ReadReceipt(), ["PR101 added Core step input/output reference contracts.", "Input references do not consume input.", "Output references do not produce output."]);
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DescribesFailureRetryStateModel()
    {
        AssertContainsAll(ReadReceipt(), ["PR102 added Core failure/retry state contracts.", "Failure state does not retry workflow.", "Retry recommendation does not grant retry permission."]);
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DescribesReadOnlyWorkflowApi()
    {
        AssertContainsAll(ReadReceipt(), ["PR103 added read-only workflow API inspection.", "The API can read workflow state but cannot command workflow state."]);
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DescribesReadOnlyWorkflowCli()
    {
        AssertContainsAll(ReadReceipt(), ["PR104 added read-only workflow CLI inspection.", "The CLI can read workflow state but cannot command workflow state."]);
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DescribesWorkflowStateContractTests()
    {
        AssertContainsAll(ReadReceipt(), ["PR105 added workflow state contract tests.", "The tests prove the Block J state substrate composes without becoming runtime authority."]);
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_SaysBlockJDoesNotProvideRuntime()
    {
        AssertContains(ReadReceipt(), "Block J does not provide workflow runtime.");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_SaysBlockJDoesNotExecuteWorkflow()
    {
        AssertContains(ReadReceipt(), "Block J does not provide workflow execution.");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_SaysBlockJDoesNotContinueWorkflow()
    {
        AssertContains(ReadReceipt(), "Block J does not provide workflow continuation.");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_SaysBlockJDoesNotResumeWorkflow()
    {
        AssertContains(ReadReceipt(), "Block J does not provide workflow resume.");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_SaysBlockJDoesNotRetryWorkflow()
    {
        AssertContains(ReadReceipt(), "Block J does not provide workflow retry.");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_SaysBlockJDoesNotDispatchAgents()
    {
        AssertContains(ReadReceipt(), "Block J does not provide agent dispatch.");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_SaysBlockJDoesNotExecuteTools()
    {
        AssertContains(ReadReceipt(), "Block J does not provide tool execution.");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_SaysBlockJDoesNotCallModels()
    {
        AssertContains(ReadReceipt(), "Block J does not provide model execution.");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_SaysBlockJDoesNotMutateSource()
    {
        AssertContains(ReadReceipt(), "Block J does not provide source apply.");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_SaysBlockJDoesNotPromoteMemory()
    {
        AssertContains(ReadReceipt(), "Block J does not provide memory promotion.");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_SaysBlockJDoesNotCreateAcceptedMemory()
    {
        AssertContains(ReadReceipt(), "Block J does not provide accepted memory creation.");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_SaysBlockJDoesNotSatisfyApproval()
    {
        AssertContains(ReadReceipt(), "Block J does not provide approval satisfaction.");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_SaysBlockJDoesNotApproveRelease()
    {
        AssertContains(ReadReceipt(), "Block J does not provide release approval.");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_SaysWorkflowRecordsAreReceiptsNotCommands()
    {
        AssertContains(ReadReceipt(), "Workflow records are receipts, not commands.");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_SaysStatusesAreFactsNotPermissions()
    {
        AssertContains(ReadReceipt(), "Workflow statuses are facts, not permissions.");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_SaysCheckpointsAreBookmarksNotResumeTokens()
    {
        AssertContains(ReadReceipt(), "Workflow checkpoints are bookmarks, not resume tokens.");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_SaysEvidenceIsEvidenceOnly()
    {
        AssertContains(ReadReceipt(), "Workflow evidence is evidence only.");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_SaysGroundingIsTraceabilityOnly()
    {
        AssertContains(ReadReceipt(), "Workflow grounding is traceability only.");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_SaysApiInspectionIsReadOnly()
    {
        AssertContains(ReadReceipt(), "Workflow API inspection is read-only.");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_SaysCliInspectionIsReadOnly()
    {
        AssertContains(ReadReceipt(), "Workflow CLI inspection is read-only.");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DoesNotAddSqlMigration()
    {
        AssertNoPr106ProductionArtifact("Database", "PR106", "BLOCK_J_WORKFLOW_STATE_RECEIPT", "WorkflowStateReceipt");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DoesNotAddApiEndpoint()
    {
        AssertNoPr106ProductionArtifact("IronDev.Api", "BlockJWorkflowStateReceipt", "WorkflowStateReceiptController", "PR106");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DoesNotAddCliCommand()
    {
        AssertNoPr106ProductionArtifact("tools/IronDev.Cli", "block-j-workflow-state-receipt", "workflow-state-receipt", "PR106");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DoesNotAddWorkflowRunner()
    {
        AssertNoPr106ProductionArtifact("IronDev.Core/Workflow", "WorkflowStateReceiptRunner", "IWorkflowStateReceiptRunner", "PR106");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DoesNotAddSchedulerOrOrchestrator()
    {
        AssertNoPr106ProductionArtifact("IronDev.Core/Workflow", "WorkflowStateReceiptScheduler", "WorkflowStateReceiptOrchestrator", "PR106");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DoesNotAddLangGraphRuntime()
    {
        AssertNoPr106ProductionArtifact("IronDev.Core/Workflow", "WorkflowStateReceiptLangGraph", "PR106LangGraph");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DoesNotAddA2aRuntime()
    {
        AssertNoPr106ProductionArtifact("IronDev.Core/Workflow", "WorkflowStateReceiptA2aRuntime", "WorkflowStateReceiptA2ARuntime");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DoesNotAddToolExecution()
    {
        AssertNoPr106ProductionArtifact("IronDev.Core/Workflow", "WorkflowStateReceiptToolExecution", "WorkflowStateReceiptExecuteTool");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DoesNotAddModelCall()
    {
        AssertNoPr106ProductionArtifact("IronDev.Core/Workflow", "WorkflowStateReceiptModelCall", "WorkflowStateReceiptModelClient");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DoesNotAddSourceApply()
    {
        AssertNoPr106ProductionArtifact("IronDev.Core/Workflow", "WorkflowStateReceiptSourceApply", "WorkflowStateReceiptApplyCopy");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DoesNotAddMemoryPromotion()
    {
        AssertNoPr106ProductionArtifact("IronDev.Core/Workflow", "WorkflowStateReceiptMemoryPromotion", "WorkflowStateReceiptPromoteMemory");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DoesNotAddApprovalSatisfaction()
    {
        AssertNoPr106ProductionArtifact("IronDev.Core/Workflow", "WorkflowStateReceiptApprovalSatisfaction", "WorkflowStateReceiptSatisfyApproval");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DoesNotAddReleaseApproval()
    {
        AssertNoPr106ProductionArtifact("IronDev.Core/Workflow", "WorkflowStateReceiptReleaseApproval", "WorkflowStateReceiptApproveRelease");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_ChangesOnlyDocsAndTests()
    {
        string[] allowed =
        [
            NormalizePath("Docs/BLOCK_J_DURABLE_WORKFLOW_RUN_SUBSTRATE.md"),
            NormalizePath("Docs/receipts/BLOCK_J_WORKFLOW_STATE_RECEIPT.md"),
            NormalizePath("IronDev.IntegrationTests/Governance/BlockJWorkflowStateReceiptTests.cs")
        ];

        AssertAllowedPath("Docs/BLOCK_J_DURABLE_WORKFLOW_RUN_SUBSTRATE.md", allowed);
        AssertAllowedPath("Docs/receipts/BLOCK_J_WORKFLOW_STATE_RECEIPT.md", allowed);
        AssertAllowedPath("IronDev.IntegrationTests/Governance/BlockJWorkflowStateReceiptTests.cs", allowed);
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_DocumentsHumanApprovalStillRequired()
    {
        AssertContains(ReadReceipt(), "Human approval is still required for source apply, accepted memory, and release decisions.");
    }

    [TestMethod]
    public void BlockJWorkflowStateReceipt_IsAsciiNoBomAndNoHiddenUnicode()
    {
        AssertAsciiNoBomNoHiddenUnicode(ReceiptPath());
        AssertAsciiNoBomNoHiddenUnicode(BlockJPath());
    }

    private static void AssertContainsAll(string text, IEnumerable<string> values)
    {
        foreach (string value in values)
        {
            AssertContains(text, value);
        }
    }

    private static void AssertContains(string text, string expected)
    {
        Assert.IsTrue(text.Contains(expected, StringComparison.Ordinal), $"Expected to find '{expected}'.");
    }

    private static void AssertNoPr106ProductionArtifact(string relativeDirectory, params string[] forbiddenMarkers)
    {
        string directory = Path.Combine(RepositoryRoot(), relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            string normalized = file.Replace('\\', '/');
            if (normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            foreach (string marker in forbiddenMarkers)
            {
                Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected PR106 production marker '{marker}' in {file}.");
            }
        }
    }

    private static void AssertAllowedPath(string relativePath, IReadOnlyCollection<string> allowed)
    {
        string normalized = NormalizePath(relativePath);
        Assert.IsTrue(allowed.Contains(normalized), $"Unexpected PR106 changed path guard fixture: {relativePath}");

        foreach (string forbidden in ForbiddenProductionDirectories)
        {
            Assert.IsFalse(normalized.StartsWith(NormalizePath(forbidden) + "/", StringComparison.OrdinalIgnoreCase), $"PR106 must not change production area {forbidden}.");
        }
    }

    private static string NormalizePath(string value) => value.Replace('\\', '/');

    private static void AssertAsciiNoBomNoHiddenUnicode(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, $"{path} must not contain UTF-8 BOM.");

        for (int i = 0; i < bytes.Length; i++)
        {
            Assert.IsTrue(bytes[i] <= 0x7F, $"{path} must be ASCII-only. Non-ASCII byte 0x{bytes[i]:X2} at offset {i}.");
        }

        string text = Encoding.ASCII.GetString(bytes);
        foreach (char ch in text)
        {
            UnicodeCategory category = char.GetUnicodeCategory(ch);
            Assert.IsFalse(category == UnicodeCategory.Format, $"{path} contains hidden format character U+{(int)ch:X4}.");
            Assert.IsFalse(char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t', $"{path} contains unexpected control character U+{(int)ch:X4}.");
        }
    }

    private static string ReadReceipt() => File.ReadAllText(ReceiptPath());

    private static string ReadBlockJDocument() => File.ReadAllText(BlockJPath());

    private static string ReceiptPath() => Path.Combine(RepositoryRoot(), "Docs", "receipts", "BLOCK_J_WORKFLOW_STATE_RECEIPT.md");

    private static string BlockJPath() => Path.Combine(RepositoryRoot(), "Docs", "BLOCK_J_DURABLE_WORKFLOW_RUN_SUBSTRATE.md");

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root containing IronDev.slnx.");
    }
}
