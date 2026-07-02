using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.AgentMemory;

[TestCategory("Receipt")]
[TestClass]
public sealed class MemoryPromotionRequestBoundaryReceiptTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ReceiptPath = Path.Combine(RepositoryRoot, "Docs", "receipts", "PR113A_MEMORY_PROMOTION_REQUEST_BOUNDARY_RECEIPT.md");
    private static readonly string BlockKPath = Path.Combine(RepositoryRoot, "Docs", "BLOCK_K_GOVERNED_MEMORY_PROPOSAL_SUBSTRATE.md");

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_DocumentExists()
    {
        Assert.IsTrue(File.Exists(ReceiptPath));
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_IsLinkedFromBlockKDocument()
    {
        var blockK = ReadBlockK();

        StringAssert.Contains(blockK, "PR113A Memory Promotion Request Boundary Receipt");
        StringAssert.Contains(blockK, "receipts/PR113A_MEMORY_PROMOTION_REQUEST_BOUNDARY_RECEIPT.md");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_SaysPackageIsNotAcceptedMemory()
    {
        StringAssert.Contains(ReadReceipt(), "A memory promotion request package is not accepted memory.");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_SaysPackageIsNotPromotedMemory()
    {
        StringAssert.Contains(ReadReceipt(), "A memory promotion request package is not promoted memory.");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_SaysPackageIsNotPortableEngineeringMemory()
    {
        StringAssert.Contains(ReadReceipt(), "A memory promotion request package is not Portable Engineering Memory.");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_SaysPackageIsNotRetrievalAuthority()
    {
        StringAssert.Contains(ReadReceipt(), "A memory promotion request package is not retrieval authority.");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_SaysPackageIsNotApproval()
    {
        StringAssert.Contains(ReadReceipt(), "A memory promotion request package is not approval.");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_SaysPackageIsNotPolicySatisfaction()
    {
        StringAssert.Contains(ReadReceipt(), "A memory promotion request package is not policy satisfaction.");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_SaysPackageIsNotPromotionDecision()
    {
        StringAssert.Contains(ReadReceipt(), "A memory promotion request package is not a promotion decision.");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_SaysDuplicateSignalsRemainReviewOnly()
    {
        StringAssert.Contains(ReadReceipt(), "Duplicate signals remain review-only.");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_SaysStaleSignalsRemainReviewOnly()
    {
        StringAssert.Contains(ReadReceipt(), "Stale signals remain review-only.");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_SaysConflictSignalsRemainReviewOnly()
    {
        StringAssert.Contains(ReadReceipt(), "Conflict signals remain review-only.");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_SaysPatternSignalsRemainReviewOnly()
    {
        StringAssert.Contains(ReadReceipt(), "Cross-run pattern signals remain review-only.");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_SaysApprovalRequirementsRemainUnsatisfied()
    {
        StringAssert.Contains(ReadReceipt(), "Approval requirement references remain unsatisfied requirements only.");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_DoesNotAddSqlMigration()
    {
        AssertNoPr113ABoundaryReferencesUnder("Database");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_DoesNotAddApiEndpoint()
    {
        AssertNoPr113ABoundaryReferencesUnder("IronDev.Api");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_DoesNotAddCliCommand()
    {
        AssertNoPr113ABoundaryReferencesUnder(Path.Combine("tools", "IronDev.Cli"));
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_DoesNotAddAcceptedMemoryStore()
    {
        AssertNoForbiddenReceiptPhrase("accepted memory store");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_DoesNotAddPromotionPath()
    {
        AssertNoForbiddenReceiptPhrase("promotion path");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_DoesNotAddEmbeddingWriter()
    {
        AssertNoForbiddenReceiptPhrase("embedding writer");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_DoesNotAddVectorStoreWriter()
    {
        AssertNoForbiddenReceiptPhrase("vector store writer");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_DoesNotReferenceWeaviateWrite()
    {
        AssertNoForbiddenReceiptPhrase("Weaviate write");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_DoesNotReferenceRetrievalActivation()
    {
        AssertNoForbiddenReceiptPhrase("retrieval activation authority");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_DoesNotReferenceModelClient()
    {
        AssertNoPr113ABoundaryReferencesUnder("IronDev.Infrastructure");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_DoesNotReferenceSourceApply()
    {
        AssertNoForbiddenReceiptPhrase("source apply path");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_DoesNotReferenceWorkflowRunner()
    {
        AssertNoForbiddenReceiptPhrase("workflow runner");
    }

    [TestMethod]
    public void MemoryPromotionRequestBoundaryReceipt_DoesNotReferenceAgentDispatcher()
    {
        AssertNoForbiddenReceiptPhrase("agent dispatcher");
    }

    private static string ReadReceipt()
    {
        return File.ReadAllText(ReceiptPath);
    }

    private static string ReadBlockK()
    {
        return File.ReadAllText(BlockKPath);
    }

    private static void AssertNoForbiddenReceiptPhrase(string phrase)
    {
        var receipt = ReadReceipt();
        Assert.IsFalse(receipt.Contains($"adds {phrase}", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(receipt.Contains($"add {phrase}", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertNoPr113ABoundaryReferencesUnder(string relativeDirectory)
    {
        var directory = Path.Combine(RepositoryRoot, relativeDirectory);
        if (!Directory.Exists(directory))
        {
            return;
        }

        var references = Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(IsTextFile)
            .Where(path => File.ReadAllText(path).Contains("PR113A", StringComparison.OrdinalIgnoreCase)
                || File.ReadAllText(path).Contains("MemoryPromotionRequestBoundaryReceipt", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.AreEqual(0, references.Length, "PR113A boundary receipt should not be wired into forbidden production surfaces: " + string.Join(", ", references));
    }

    private static bool IsTextFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension is ".cs" or ".sql" or ".md" or ".json" or ".ps1";
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
