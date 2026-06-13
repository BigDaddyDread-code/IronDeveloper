using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
[TestCategory("BlockIA2aSpineReceipt")]
[TestCategory("A2aEvidenceOnlySemantics")]
[TestCategory("A2aAuthoritySeparation")]
[TestCategory("A2aStaticNoRuntimeBoundary")]
public sealed class BlockIA2aSpineReceiptTests
{
    private const string ReceiptPath = "Docs/BLOCK_I_A2A_SPINE_RECEIPT.md";
    private const string SpinePath = "Docs/BLOCK_I_A2A_HANDOFF_CONTRACT_SPINE.md";

    [TestMethod]
    public void BlockIA2aSpineReceipt_DocumentExists()
    {
        Assert.IsTrue(File.Exists(Path.Combine(RepositoryRoot(), ReceiptPath.Replace('/', Path.DirectorySeparatorChar))));
    }

    [TestMethod]
    public void BlockIA2aSpineReceipt_IsReferencedFromBlockIHandoffSpine()
    {
        var spine = ReadRepositoryFile(SpinePath);

        StringAssert.Contains(spine, "PR97 Block I A2A Spine Receipt");
        StringAssert.Contains(spine, "PR97 closes Block I with a receipt.");
        StringAssert.Contains(spine, "The receipt states that Block I delivered the A2A Handoff Contract Spine, not A2A runtime.");
        StringAssert.Contains(spine, "The receipt preserves the evidence-only and no-authority-transfer boundaries.");
        StringAssert.Contains(spine, "The receipt explicitly refuses runtime, transport, workflow, source apply, memory promotion, accepted memory, release approval, approval satisfaction, and execution claims.");
    }

    [TestMethod]
    public void BlockIA2aSpineReceipt_NamesPr90ThroughPr97()
    {
        var receipt = Receipt();

        foreach (var pr in Enumerable.Range(90, 8).Select(number => $"PR{number}"))
            StringAssert.Contains(receipt, pr);
    }

    [TestMethod]
    public void BlockIA2aSpineReceipt_ContainsValidationMatrix()
    {
        var receipt = Receipt();

        StringAssert.Contains(receipt, "## Validation Receipt");
        StringAssert.Contains(receipt, "| PR | Receipt |");
        StringAssert.Contains(receipt, "A2A contract validation tests.");
        StringAssert.Contains(receipt, "Block H policy boundary tests.");
        StringAssert.Contains(receipt, "Block G governance substrate tests.");
        StringAssert.Contains(receipt, "API governance surface tests.");
    }

    [DataTestMethod]
    [DataRow("Agent Handoff contract delivered.")]
    [DataRow("Allowed-use evidence model delivered.")]
    [DataRow("No-authority-transfer validator delivered.")]
    [DataRow("Durable AgentHandoff store delivered.")]
    [DataRow("ThoughtLedger handoff entry contract delivered.")]
    [DataRow("Grounding evidence reference contract delivered.")]
    [DataRow("A2A contract validation pack delivered.")]
    public void BlockIA2aSpineReceipt_StatesDeliveredScope(string expected)
    {
        StringAssert.Contains(Receipt(), expected);
    }

    [DataTestMethod]
    [DataRow("A2A runtime.")]
    [DataRow("Handoff transport.")]
    [DataRow("Inbox/outbox.")]
    [DataRow("Queue or message bus.")]
    [DataRow("Dispatcher.")]
    [DataRow("Receiver.")]
    [DataRow("Workflow runner.")]
    [DataRow("LangGraph runtime.")]
    [DataRow("Source apply.")]
    [DataRow("Memory promotion.")]
    [DataRow("Accepted memory.")]
    [DataRow("Release approval.")]
    [DataRow("Approval satisfaction.")]
    [DataRow("Execution engine.")]
    public void BlockIA2aSpineReceipt_StatesNonGoals(string expected)
    {
        StringAssert.Contains(Receipt(), expected);
    }

    [DataTestMethod]
    [DataRow("Block I handoffs remain evidence and context records.")]
    [DataRow("AllowedUse remains non-authoritative.")]
    [DataRow("AllowedUse PolicyInput does not satisfy policy.")]
    [DataRow("AllowedUse ClaimSupport does not prove a claim.")]
    [DataRow("AllowedUse HumanDecisionSupport does not replace a human decision.")]
    [DataRow("Gate decision evidence does not mean approval.")]
    [DataRow("Approval decision evidence does not mean execution permission")]
    [DataRow("Dogfood receipt evidence does not mean release approval.")]
    [DataRow("Critic review evidence does not mean approval.")]
    [DataRow("Model output evidence does not mean approval.")]
    [DataRow("Retrieval evidence does not mean truth.")]
    [DataRow("Source file range evidence does not mean source apply.")]
    [DataRow("Memory candidate claim does not create accepted memory.")]
    public void BlockIA2aSpineReceipt_StatesEvidenceOnlySemantics(string expected)
    {
        StringAssert.Contains(Receipt(), expected);
    }

    [DataTestMethod]
    [DataRow("Block I does not transfer approval.")]
    [DataRow("Block I does not transfer execution permission.")]
    [DataRow("Block I does not transfer workflow continuation.")]
    [DataRow("Block I does not transfer source apply permission.")]
    [DataRow("Block I does not transfer memory promotion permission.")]
    [DataRow("Block I does not transfer release approval.")]
    [DataRow("Block I does not transfer policy satisfaction.")]
    [DataRow("Block I does not transfer authority.")]
    public void BlockIA2aSpineReceipt_StatesNoAuthorityTransfer(string expected)
    {
        StringAssert.Contains(Receipt(), expected);
    }

    [DataTestMethod]
    [DataRow("A durable handoff row does not mean sent.")]
    [DataRow("A durable handoff row does not mean received.")]
    [DataRow("A durable handoff row does not mean accepted.")]
    [DataRow("A durable handoff row does not mean dispatched.")]
    [DataRow("A durable handoff row does not mean executed.")]
    [DataRow("A durable handoff row does not continue workflow.")]
    [DataRow("A durable handoff row does not transfer authority.")]
    public void BlockIA2aSpineReceipt_StatesDurabilityBoundary(string expected)
    {
        StringAssert.Contains(Receipt(), expected);
    }

    [DataTestMethod]
    [DataRow("A ThoughtLedger handoff entry does not mean delivery.")]
    [DataRow("A ThoughtLedger handoff entry does not mean target-agent receipt.")]
    [DataRow("A ThoughtLedger handoff entry does not continue workflow.")]
    [DataRow("A ThoughtLedger handoff entry does not authorize the target agent.")]
    [DataRow("A ThoughtLedger handoff entry does not approve source apply.")]
    [DataRow("A ThoughtLedger handoff entry does not promote memory.")]
    [DataRow("A ThoughtLedger handoff entry does not approve release.")]
    public void BlockIA2aSpineReceipt_StatesThoughtLedgerBoundary(string expected)
    {
        StringAssert.Contains(Receipt(), expected);
    }

    [DataTestMethod]
    [DataRow("A grounding reference does not make a claim true.")]
    [DataRow("A grounding reference does not approve a claim.")]
    [DataRow("A grounding reference does not make a claim executable.")]
    [DataRow("A grounding reference does not promote memory.")]
    [DataRow("A grounding reference does not approve release.")]
    [DataRow("A grounding reference does not create accepted memory.")]
    [DataRow("A grounding reference does not satisfy policy.")]
    [DataRow("A grounding reference does not continue workflow.")]
    public void BlockIA2aSpineReceipt_StatesGroundingBoundary(string expected)
    {
        StringAssert.Contains(Receipt(), expected);
    }

    [DataTestMethod]
    [DataRow("Block I does not persist hidden reasoning.")]
    [DataRow("Block I does not persist raw prompt dumps.")]
    [DataRow("Block I does not persist raw completion dumps.")]
    [DataRow("Block I does not persist raw tool output dumps.")]
    [DataRow("Block I does not persist scratchpad content.")]
    [DataRow("Block I does not persist a whole patch as reasoning payload.")]
    public void BlockIA2aSpineReceipt_StatesHiddenReasoningBoundary(string expected)
    {
        StringAssert.Contains(Receipt(), expected);
    }

    [DataTestMethod]
    [DataRow("no API endpoint")]
    [DataRow("CLI command")]
    [DataRow("SQL migration in PR97")]
    [DataRow("repository in PR97")]
    [DataRow("runtime dispatcher")]
    [DataRow("workflow runner")]
    [DataRow("executor")]
    [DataRow("memory promotion path")]
    [DataRow("source apply path")]
    [DataRow("LangGraph runtime")]
    [DataRow("A2A runtime")]
    [DataRow("message bus")]
    [DataRow("queue")]
    [DataRow("model client")]
    [DataRow("scheduler")]
    [DataRow("orchestrator")]
    public void BlockIA2aSpineReceipt_StatesStaticNoRuntimeBoundary(string expected)
    {
        StringAssert.Contains(Receipt(), expected);
    }

    [TestMethod]
    public void BlockIA2aSpineReceipt_ContractChainIsCompleteAndNonAuthoritative()
    {
        var receipt = Receipt();

        foreach (var expected in new[]
                 {
                     "AgentHandoff",
                     "Evidence References",
                     "AllowedUse",
                     "NoAuthorityTransferValidator",
                     "Durable AgentHandoff Store",
                     "ThoughtLedger Handoff Entry",
                     "Grounding Evidence Reference",
                     "A2A Contract Validation Test Pack",
                     "Block I Receipt",
                     "Each link records, summarizes, cites, validates, or preserves evidence.",
                     "No link grants authority."
                 })
        {
            StringAssert.Contains(receipt, expected);
        }
    }

    [DataTestMethod]
    [DataRow("IronDev.Api", "BlockIA2aSpineReceipt")]
    [DataRow("tools/IronDev.Cli", "BlockIA2aSpineReceipt")]
    [DataRow("Database", "BlockIA2aSpineReceipt")]
    [DataRow("IronDev.Api", "BLOCK_I_A2A_SPINE_RECEIPT")]
    [DataRow("tools/IronDev.Cli", "BLOCK_I_A2A_SPINE_RECEIPT")]
    [DataRow("Database", "BLOCK_I_A2A_SPINE_RECEIPT")]
    public void BlockIA2aSpineReceipt_DoesNotWireReceiptIntoRuntimeSurfaces(string relativeRoot, string token)
    {
        AssertNoReference(relativeRoot, token);
    }

    [TestMethod]
    public void BlockIA2aSpineReceipt_DocumentationIsAsciiAndHasNoHiddenUnicode()
    {
        foreach (var relativePath in new[] { ReceiptPath, SpinePath })
        {
            var bytes = File.ReadAllBytes(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, relativePath);
            Assert.IsTrue(bytes.All(b => b is 9 or 10 or 13 || (b >= 32 && b <= 126)), relativePath);
        }
    }

    private static string Receipt() => ReadRepositoryFile(ReceiptPath);

    private static string ReadRepositoryFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static void AssertNoReference(string relativeRoot, string token)
    {
        var root = Path.Combine(RepositoryRoot(), relativeRoot.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(root))
            return;

        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"{file} must not reference {token}.");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
