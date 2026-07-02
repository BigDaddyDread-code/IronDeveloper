using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestCategory("StaticBoundary")]
[TestCategory("Receipt")]
[TestClass]
public sealed class BlockML4CandidateReceiptStaticBoundaryTests
{
    private static readonly string[] CandidateProductionFiles =
    [
        "TestFailureReviewCandidateWorkflowModels.cs",
        "CriticReviewRequestCandidateWorkflowModels.cs",
        "ImplementationProposalPackageCandidateWorkflowModels.cs",
        "ToolRequestGatePreviewCandidateWorkflowModels.cs",
        "MemoryImprovementPackageCandidateWorkflowModels.cs",
        "HumanApprovalPackageCandidateWorkflowModels.cs",
        "DogfoodEvidenceBundleCandidateWorkflowModels.cs",
        "RepeatedFailurePatternReviewCandidateWorkflowModels.cs"
    ];

    private static readonly string[] CandidateBoundaryTestFiles =
    [
        "TestFailureReviewCandidateWorkflowTests.cs",
        "CriticReviewRequestCandidateWorkflowTests.cs",
        "ImplementationProposalPackageCandidateWorkflowTests.cs",
        "ToolRequestGatePreviewCandidateWorkflowTests.cs",
        "MemoryImprovementPackageCandidateWorkflowTests.cs",
        "HumanApprovalPackageCandidateWorkflowTests.cs",
        "DogfoodEvidenceBundleCandidateWorkflowTests.cs",
        "RepeatedFailurePatternReviewCandidateWorkflowTests.cs",
        "L4CandidateCannotMutateSourceOrMemoryTests.cs",
        "L4CandidateAuthoritySubstitutionBoundaryTests.cs",
        "L4CandidateStaticMutationBoundaryTests.cs"
    ];

    private static readonly string[] Pr136Files =
    [
        Path.Combine("Docs", "receipts", "PR136_BLOCK_M_L4_CANDIDATE_RECEIPT.md"),
        Path.Combine("IronDev.IntegrationTests", "Governance", "BlockML4CandidateReceiptTests.cs"),
        Path.Combine("IronDev.IntegrationTests", "Governance", "BlockML4CandidateReceiptStaticBoundaryTests.cs")
    ];

    [TestMethod]
    public void BlockM_CandidateProductionFiles_Exist()
    {
        foreach (var fileName in CandidateProductionFiles)
            Assert.IsTrue(File.Exists(Path.Combine(RepositoryRoot(), "IronDev.Core", "Workflow", fileName)), $"Missing candidate production file: {fileName}");
    }

    [TestMethod]
    public void BlockM_CandidateBoundaryTestFiles_Exist()
    {
        foreach (var fileName in CandidateBoundaryTestFiles)
            Assert.IsTrue(File.Exists(Path.Combine(RepositoryRoot(), "IronDev.IntegrationTests", "Governance", fileName)), $"Missing candidate boundary test file: {fileName}");
    }

    [TestMethod]
    public void Receipt_DoesNotClaimExecutionOrAuthority()
    {
        var receipt = ReadReceipt();

        AssertDoesNotContainAny(
            receipt,
            "L4 execution is complete",
            "candidate workflows execute",
            "candidate workflows can mutate source",
            "candidate workflows can promote memory",
            "candidate workflows can approve",
            "candidate workflows can satisfy policy",
            "candidate workflows can continue workflow",
            "candidate workflows can invoke tools",
            "dogfood proves release readiness",
            "pattern is proven",
            "root cause is proven",
            "source apply is implemented",
            "memory promotion is implemented",
            "approval recording is implemented");
    }

    [TestMethod]
    public void Pr136_TestAndReceiptFiles_DoNotIntroduceRuntimeMarkers()
    {
        foreach (var relativePath in new[] { Path.Combine("Docs", "receipts", "PR136_BLOCK_M_L4_CANDIDATE_RECEIPT.md") })
        {
            var text = File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath));

            AssertDoesNotContainAny(
                text,
                "ControllerBase",
                "IHostedService",
                "BackgroundService",
                "WebApplication",
                "SqlConnection",
                "DbConnection",
                "HttpClient",
                "ProcessStartInfo",
                "ToolInvoker",
                "AgentDispatcher",
                "OpenAI",
                "ChatCompletion",
                "WorkflowTransitionWriter",
                "ApprovalMutation",
                "RetrievalActivation",
                "SourceMutation",
                "PatchApply");
        }
    }

    [TestMethod]
    public void Pr136_ReceiptAndTests_AreOnlyPr136NamedArtifacts()
    {
        foreach (var relativePath in Pr136Files)
            Assert.IsTrue(File.Exists(Path.Combine(RepositoryRoot(), relativePath)), $"Missing PR136 artifact: {relativePath}");

        var governanceFiles = Directory.GetFiles(Path.Combine(RepositoryRoot(), "IronDev.IntegrationTests", "Governance"), "BlockML4CandidateReceipt*.cs");
        CollectionAssert.AreEquivalent(
            new[]
            {
                Path.Combine(RepositoryRoot(), "IronDev.IntegrationTests", "Governance", "BlockML4CandidateReceiptTests.cs"),
                Path.Combine(RepositoryRoot(), "IronDev.IntegrationTests", "Governance", "BlockML4CandidateReceiptStaticBoundaryTests.cs")
            },
            governanceFiles);
    }

    private static string ReadReceipt() => File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR136_BLOCK_M_L4_CANDIDATE_RECEIPT.md"));

    private static void AssertDoesNotContainAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
            Assert.IsFalse(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"Unexpected marker '{marker}'.");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        Assert.IsNotNull(directory, "Could not locate repository root.");
        return directory!.FullName;
    }
}
