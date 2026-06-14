using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
public sealed class BlockLGovernedRunnerReceiptTests
{
    [TestMethod]
    public void BlockLGovernedRunnerReceipt_Exists()
    {
        Assert.IsTrue(File.Exists(ReceiptPath()), "PR126 receipt must exist.");
    }

    [TestMethod]
    public void BlockLGovernedRunnerReceipt_ListsPr117ThroughPr125()
    {
        var receipt = ReadReceipt();

        for (var pr = 117; pr <= 125; pr++)
            StringAssert.Contains(receipt, $"PR{pr}");
    }

    [TestMethod]
    public void BlockLGovernedRunnerReceipt_StatesGovernedRunnerSubstrateOnly()
    {
        var receipt = ReadReceipt();

        StringAssert.Contains(receipt, "Block L establishes the governed runner substrate.");
        StringAssert.Contains(receipt, "Receipt is not capability.");
        StringAssert.Contains(receipt, "Block L closes the governed runner foundation");
        AssertDoesNotContainAny(receipt, "Block L completes the governed runner.", "Workflows can now run safely.", "IronDev can execute real workflows.");
    }

    [TestMethod]
    public void BlockLGovernedRunnerReceipt_RecordsRequiredBoundaries()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(
            receipt,
            "Contract is not execution",
            "Evaluation only",
            "Check is not approval",
            "Traceability is not authority",
            "Validation is not dispatch",
            "Halt is not approval",
            "Dry-run is not execution",
            "Route label is not decision ownership",
            "Workflow cannot mint authority");
    }

    [TestMethod]
    public void BlockLGovernedRunnerReceipt_ContainsInvariantSet()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(
            receipt,
            "Evidence is not approval.",
            "Traceability is not authority.",
            "Validation is not dispatch.",
            "Halt is not approval.",
            "Dry-run is not execution.",
            "Route label is not decision ownership.",
            "Receipt is not capability.");
    }

    [TestMethod]
    public void BlockLGovernedRunnerReceipt_ListsWhatBlockLCanDo()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(
            receipt,
            "represent workflow steps as typed contracts",
            "evaluate supplied step contracts",
            "report missing evidence",
            "report policy preflight blockers",
            "require ThoughtLedger traceability",
            "validate supplied A2A handoff snapshots",
            "report approval-required halt state",
            "execute a deterministic non-mutating dry-run action from supplied eligible snapshots",
            "map supplied runner/dry-run snapshots to advisory route labels",
            "prove workflow artifacts cannot grant authority");
    }

    [TestMethod]
    public void BlockLGovernedRunnerReceipt_ListsWhatBlockLCannotDo()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(
            receipt,
            "cannot execute real workflow steps",
            "cannot transition workflow state",
            "cannot complete workflow steps",
            "cannot create approvals",
            "cannot grant approvals",
            "cannot deny approvals",
            "cannot satisfy policy",
            "cannot dispatch agents",
            "cannot send A2A handoffs",
            "cannot invoke tools",
            "cannot call models",
            "cannot build prompts",
            "cannot mutate source",
            "cannot apply patches",
            "cannot promote memory",
            "cannot activate retrieval",
            "cannot write SQL",
            "cannot expose API/CLI/UI runtime execution",
            "cannot make LangGraph the route owner");
    }

    [TestMethod]
    public void BlockLGovernedRunnerReceipt_DoesNotOverclaimCapabilityOrReadiness()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(
            receipt,
            "does not claim controlled source apply exists",
            "does not claim L4 candidate workflows exist",
            "does not claim operational readiness exists",
            "does not claim UI consumption exists",
            "does not claim release readiness");

        AssertDoesNotContainAny(
            receipt,
            "real workflow execution exists",
            "workflow state transition is implemented",
            "agent dispatch is implemented",
            "tool invocation is implemented",
            "model calls are implemented",
            "source mutation is implemented",
            "memory promotion is implemented",
            "retrieval activation is implemented",
            "API/CLI/UI runtime execution is implemented",
            "release ready",
            "production workflow orchestration is ready",
            "LangGraph routing is ready");
    }

    [TestMethod]
    public void BlockLGovernedRunnerReceipt_ContainsBlockMHandoffWarning()
    {
        var receipt = ReadReceipt();

        StringAssert.Contains(receipt, "Block M may begin candidate L4 workflows only on top of this governed runner substrate.");
        StringAssert.Contains(receipt, "Candidate workflows must remain non-mutating until later controlled source-apply boundaries exist.");
        StringAssert.Contains(receipt, "Block M must not treat Block L receipt as permission to dispatch agents, invoke tools, mutate source, promote memory, activate retrieval, or transition workflow state.");
    }

    private static string ReadReceipt() => File.ReadAllText(ReceiptPath());

    private static string ReceiptPath() => Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR126_BLOCK_L_GOVERNED_RUNNER_RECEIPT.md");

    private static string RepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "IronDev.slnx")))
                return directory;

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static void AssertContainsAll(string text, params string[] expected)
    {
        foreach (var value in expected)
            StringAssert.Contains(text, value);
    }

    private static void AssertDoesNotContainAny(string text, params string[] forbidden)
    {
        foreach (var value in forbidden)
            Assert.IsFalse(text.Contains(value, StringComparison.OrdinalIgnoreCase), $"Receipt must not contain overclaim: {value}");
    }
}
