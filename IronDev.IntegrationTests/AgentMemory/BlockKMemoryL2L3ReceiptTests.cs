using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.AgentMemory;

[TestClass]
[TestCategory("BlockKMemoryL2L3Receipt")]
public sealed class BlockKMemoryL2L3ReceiptTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ReceiptPath = Path.Combine(RepositoryRoot, "Docs", "receipts", "PR116_BLOCK_K_MEMORY_L2_L3_RECEIPT.md");
    private static readonly string BlockKPath = Path.Combine(RepositoryRoot, "Docs", "BLOCK_K_GOVERNED_MEMORY_PROPOSAL_SUBSTRATE.md");

    [TestMethod] public void BlockKMemoryL2L3Receipt_DocumentExists() => Assert.IsTrue(File.Exists(ReceiptPath));
    [TestMethod] public void BlockKMemoryL2L3Receipt_IsLinkedFromBlockKDocument() => AssertContains(BlockKDocument(), "receipts/PR116_BLOCK_K_MEMORY_L2_L3_RECEIPT.md");
    [TestMethod] public void BlockKMemoryL2L3Receipt_ListsPr107ThroughPr115() => AssertContainsAll(Receipt(), "PR107", "PR107.5", "PR108", "PR109", "PR110", "PR111", "PR112", "PR113", "PR113A", "PR114", "PR115");
    [TestMethod] public void BlockKMemoryL2L3Receipt_StatesBlockKIsProposalSubstrateOnly() => AssertContainsAll(Receipt(), "Block K creates governed memory proposal infrastructure.", "Block K is proposal substrate only.");

    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysBlockKDoesNotCreateAcceptedL2Memory() => AssertContains(ReceiptAndBlockKPr116Section(), "Block K does not create accepted L2 memory.");
    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysBlockKDoesNotCreateAcceptedL3Memory() => AssertContains(ReceiptAndBlockKPr116Section(), "Block K does not create accepted L3 memory.");
    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysBlockKDoesNotCreateActiveL2Memory() => AssertContains(ReceiptAndBlockKPr116Section(), "It is not active L2 memory.");
    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysBlockKDoesNotCreateActiveL3Memory() => AssertContains(ReceiptAndBlockKPr116Section(), "It is not active L3 memory.");
    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysBlockKDoesNotPromoteMemory() => AssertContains(ReceiptAndBlockKPr116Section(), "Block K does not promote memory.");
    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysBlockKDoesNotCreatePortableEngineeringMemory() => AssertContains(ReceiptAndBlockKPr116Section(), "Block K does not create Portable Engineering Memory.");
    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysBlockKDoesNotActivateRetrieval() => AssertContains(ReceiptAndBlockKPr116Section(), "Block K does not activate retrieval.");
    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysBlockKDoesNotCreateEmbeddings() => AssertContains(ReceiptAndBlockKPr116Section(), "Block K does not create embeddings.");
    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysBlockKDoesNotWriteVectorStorage() => AssertContains(ReceiptAndBlockKPr116Section(), "Block K does not write to vector storage.");
    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysBlockKDoesNotApproveMemory() => AssertContains(ReceiptAndBlockKPr116Section(), "Block K does not approve memory.");
    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysBlockKDoesNotSatisfyPolicy() => AssertContains(ReceiptAndBlockKPr116Section(), "Block K does not satisfy policy.");

    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysStagedProposalIsReviewMaterialOnly() => AssertContains(Receipt(), "A staged proposal is review material only.");
    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysEvidencePackageIsReviewMaterialOnly() => AssertContains(Receipt(), "An evidence package is review material only.");
    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysDuplicateSignalIsReviewOnly() => AssertContains(Receipt(), "A duplicate signal is review only.");
    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysStaleSignalIsReviewOnly() => AssertContains(Receipt(), "A stale signal is review only.");
    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysConflictSignalIsReviewOnly() => AssertContains(Receipt(), "A conflict signal is review only.");
    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysCrossRunPatternSignalIsReviewOnly() => AssertContains(Receipt(), "A cross-run pattern signal is review only.");
    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysPromotionRequestPackageIsReviewOnly() => AssertContains(Receipt(), "A promotion request package is review only.");
    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysThoughtLedgerTraceIsEvidenceOnly() => AssertContains(Receipt(), "A ThoughtLedger trace is evidence only.");
    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysMemoryCannotPromoteItself() => AssertContains(Receipt(), "No memory proposal artifact can promote itself.");

    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysAcceptedMemoryStoreIsFutureWork() => AssertContains(ReceiptAndBlockKPr116Section(), "Accepted memory storage is future work.");
    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysPromotionDecisionFlowIsFutureWork() => AssertContains(ReceiptAndBlockKPr116Section(), "Governed acceptance and promotion decisions are future work.");
    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysRetrievalActivationRulesAreFutureWork() => AssertContains(ReceiptAndBlockKPr116Section(), "Retrieval activation rules are future work.");
    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysPortableEngineeringMemoryApprovalIsFutureWork() => AssertContains(ReceiptAndBlockKPr116Section(), "Portable Engineering Memory approval is future work.");
    [TestMethod] public void BlockKMemoryL2L3Receipt_SaysGovernedReviewStillRequired() => AssertContains(ReceiptAndBlockKPr116Section(), "Human or governed review remains required before accepted memory.");

    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotClaimAcceptedL2MemoryExists() => AssertNoForbiddenTokens(ReceiptAndBlockKPr116Section(), "accepted L2 memory exists");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotClaimAcceptedL3MemoryExists() => AssertNoForbiddenTokens(ReceiptAndBlockKPr116Section(), "accepted L3 memory exists");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotClaimActiveL2Memory() => AssertNoForbiddenTokens(ReceiptAndBlockKPr116Section(), "L2 memory is active");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotClaimActiveL3Memory() => AssertNoForbiddenTokens(ReceiptAndBlockKPr116Section(), "L3 memory is active");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotClaimRetrievalActivation() => AssertNoForbiddenTokens(ReceiptAndBlockKPr116Section(), "retrieval activated");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotClaimEmbeddingsCreated() => AssertNoForbiddenTokens(ReceiptAndBlockKPr116Section(), "embeddings created");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotClaimVectorWrites() => AssertNoForbiddenTokens(ReceiptAndBlockKPr116Section(), "vector store written");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotClaimPortableMemoryApproved() => AssertNoForbiddenTokens(ReceiptAndBlockKPr116Section(), "Portable Engineering Memory approved");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotClaimApprovalSatisfied() => AssertNoForbiddenTokens(ReceiptAndBlockKPr116Section(), "approval satisfied");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotClaimPolicySatisfied() => AssertNoForbiddenTokens(ReceiptAndBlockKPr116Section(), "policy satisfied");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotClaimMemoryPromoted() => AssertNoForbiddenTokens(ReceiptAndBlockKPr116Section(), "memory promoted");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotClaimReleaseApproved() => AssertNoForbiddenTokens(ReceiptAndBlockKPr116Section(), "release approved");

    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotAddSqlMigration() => AssertNoPr116ReferencesUnder("Database");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotAddApiEndpoint() => AssertNoPr116ReferencesUnder("IronDev.Api");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotAddCliCommand() => AssertNoPr116ReferencesUnder(Path.Combine("tools", "IronDev.Cli"));
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotAddAcceptedMemoryStore() => AssertChangedDocsDoNotContain("IAcceptedMemoryStore", "AcceptedMemoryStore", "CreateAcceptedMemoryAsync");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotAddL2MemoryStore() => AssertChangedDocsDoNotContain("IL2MemoryStore", "L2MemoryStore", "CreateL2MemoryAsync");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotAddL3MemoryStore() => AssertChangedDocsDoNotContain("IL3MemoryStore", "L3MemoryStore", "CreateL3MemoryAsync");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotAddPromotionService() => AssertChangedDocsDoNotContain("IMemoryPromotionService", "MemoryPromotionService", "PromoteMemoryAsync");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotAddEmbeddingWriter() => AssertChangedDocsDoNotContain("IEmbeddingWriter", "EmbeddingWriter", "CreateEmbeddingAsync");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotAddVectorStoreWriter() => AssertChangedDocsDoNotContain("IVectorStoreWriter", "VectorStoreWriter", "WriteVectorStoreAsync");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotReferenceWeaviateWrite() => AssertChangedDocsDoNotContain("WeaviateWriter", "WriteWeaviateAsync", "IWeaviateWriter");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotReferenceRetrievalActivation() => AssertChangedDocsDoNotContain("IRetrievalActivation", "RetrievalActivationService", "ActivateRetrievalAsync");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotReferenceModelClient() => AssertChangedDocsDoNotContain("IAgentModelAdapter", "IModelClient", "ChatCompletion", "OpenAI");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotReferenceSourceApply() => AssertChangedDocsDoNotContain("SourceApplyService", "ApplyToSource", "ApplySourceAsync");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotReferenceWorkflowRunner() => AssertChangedDocsDoNotContain("WorkflowRunner", "IWorkflowRunner", "RunWorkflowAsync", "ContinueWorkflowAsync");
    [TestMethod] public void BlockKMemoryL2L3Receipt_DoesNotReferenceAgentDispatcher() => AssertChangedDocsDoNotContain("AgentDispatcher", "IAgentDispatcher", "DispatchAgentAsync");

    private static string Receipt() => File.ReadAllText(ReceiptPath);
    private static string BlockKDocument() => File.ReadAllText(BlockKPath);

    private static string ReceiptAndBlockKPr116Section()
    {
        var blockK = BlockKDocument();
        var marker = "## PR116 - Block K Memory L2/L3 Receipt";
        var index = blockK.IndexOf(marker, StringComparison.Ordinal);
        Assert.IsTrue(index >= 0, "Block K document is missing the PR116 section.");
        return Receipt() + "\n" + blockK[index..];
    }

    private static void AssertChangedDocsDoNotContain(params string[] forbiddenTokens)
    {
        var text = ReceiptAndBlockKPr116Section();
        AssertNoForbiddenTokens(text, forbiddenTokens);
    }

    private static void AssertNoPr116ReferencesUnder(string relativeDirectory)
    {
        var directory = Path.Combine(RepositoryRoot, relativeDirectory);
        if (!Directory.Exists(directory))
        {
            return;
        }

        var references = Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(IsTextFile)
            .Where(path => File.ReadAllText(path).Contains("BlockKMemoryL2L3Receipt", StringComparison.OrdinalIgnoreCase)
                || File.ReadAllText(path).Contains("PR116_BLOCK_K_MEMORY_L2_L3_RECEIPT", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.AreEqual(0, references.Length, "PR116 should not be wired into forbidden surfaces: " + string.Join(", ", references));
    }

    private static void AssertContains(string text, string expected)
    {
        Assert.IsTrue(text.Contains(expected, StringComparison.Ordinal), "Missing required text: " + expected);
    }

    private static void AssertContainsAll(string text, params string[] expectedValues)
    {
        foreach (var expected in expectedValues)
        {
            AssertContains(text, expected);
        }
    }

    private static void AssertNoForbiddenTokens(string text, params string[] forbiddenTokens)
    {
        foreach (var token in forbiddenTokens)
        {
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), "Forbidden token '" + token + "' was present.");
        }
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
