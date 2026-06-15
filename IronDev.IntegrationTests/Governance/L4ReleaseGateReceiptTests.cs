using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("L4ReleaseGateReceipt")]
public sealed class L4ReleaseGateReceiptTests
{
    private const string RequiredChain =
        "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate";

    private static readonly string[] FutureGateInputs =
    [
        "accepted approval record",
        "policy satisfaction record",
        "controlled dry-run proof",
        "patch artifact record",
        "controlled source apply record or explicit no-apply proof",
        "rollback record or explicit rollback-not-required proof",
        "workflow completion evidence",
        "validation proof",
        "dogfood evidence",
        "known limitations",
        "open risk summary",
        "release decision id"
    ];

    [TestMethod]
    public void L4ReleaseGateReceipt_Exists()
    {
        Assert.IsTrue(File.Exists(ReceiptPath()));
        StringAssert.Contains(ReceiptText(), "PR166 adds the L4 release gate receipt.");
    }

    [TestMethod]
    public void L4ReleaseGateReceipt_StatesReleaseGateIsNotImplemented()
    {
        var receipt = ReceiptText();

        StringAssert.Contains(receipt, "PR166 does not implement release readiness.");
        StringAssert.Contains(receipt, "PR166 does not approve release.");
        StringAssert.Contains(receipt, "PR166 does not mark release ready.");
        StringAssert.Contains(receipt, "PR166 does not ship software.");
        StringAssert.Contains(receipt, "PR166 does not activate L4.");
    }

    [TestMethod]
    public void L4ReleaseGateReceipt_StatesReleaseGateIsLastInChain()
    {
        var receipt = ReceiptText();

        StringAssert.Contains(receipt, RequiredChain);
        StringAssert.Contains(receipt, "Release readiness gate is last.");
        StringAssert.Contains(receipt, "Nothing before release readiness gate is release readiness.");
    }

    [TestMethod]
    public void L4ReleaseGateReceipt_RequiresAllFutureGateInputs()
    {
        var receipt = ReceiptText();

        foreach (var input in FutureGateInputs)
        {
            StringAssert.Contains(receipt, input);
        }
    }

    [TestMethod]
    public void L4ReleaseGateReceipt_RejectsEvidenceOnlyReadiness()
    {
        var receipt = ReceiptText();

        foreach (var statement in new[]
        {
            "Dogfood pass is not release readiness.",
            "Health check is not release readiness.",
            "Validation summary is not release readiness.",
            "UI review is not release readiness.",
            "Correlation report is not release readiness.",
            "Campaign success is not release readiness.",
            "Workflow completion evidence is not release readiness."
        })
        {
            StringAssert.Contains(receipt, statement);
        }
    }

    [TestMethod]
    public void L4ReleaseGateReceipt_DoesNotClaimCurrentReadiness()
    {
        var receipt = ReceiptText();

        foreach (var claim in new[]
        {
            "release readiness is implemented",
            "release gate is implemented",
            "release is approved",
            "release is ready",
            "ready to ship",
            "L4 release gate passed"
        })
        {
            Assert.IsFalse(receipt.Contains(claim, StringComparison.OrdinalIgnoreCase), $"Receipt must not claim: {claim}");
        }
    }

    [TestMethod]
    public void L4ReleaseGateReceipt_DoesNotReferenceMutationServices()
    {
        var forbiddenTokens = new[]
        {
            "Apply" + "Source",
            "Apply" + "Patch",
            "Workflow" + "Runner",
            "Workflow" + "Dispatcher",
            "Tool" + "Executor",
            "Tool" + "Invoker",
            "Agent" + "Dispatcher",
            "Release" + "Publisher",
            "De" + "ploy",
            "Tag" + "Release",
            "Sql" + "Connection",
            "Db" + "Command",
            "File." + "Write",
            "File." + "Delete",
            "Process." + "Start",
            "git " + "commit",
            "git " + "push"
        };

        foreach (var file in Pr166Files())
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbiddenTokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"{Path.GetFileName(file)} must not reference {token}.");
            }
        }
    }

    [TestMethod]
    public void L4ReleaseGateReceipt_ReviewLineIsPresent()
    {
        StringAssert.Contains(ReceiptText(), "PR166 defines the release gate finish line. It does not cross it.");
    }

    [TestMethod]
    public void L4ReleaseGateReceipt_StatesRequiredBoundary()
    {
        var receipt = ReceiptText();

        StringAssert.Contains(receipt, "This PR is tests/receipt only.");
        StringAssert.Contains(receipt, "Release gate receipt is not release readiness.");
        StringAssert.Contains(receipt, "Release gate requirement is not release gate execution.");
        StringAssert.Contains(receipt, "Release gate definition is not release approval.");
        StringAssert.Contains(receipt, "Backend release readiness must be backend-decided.");
        StringAssert.Contains(
            receipt,
            "PR166 does not implement accepted approval records, policy satisfaction records, dry-run execution, patch artifacts, source apply, rollback, workflow continuation, release readiness, release approval, deployment, memory promotion, or retrieval activation.");
    }

    private static string ReceiptText() => File.ReadAllText(ReceiptPath());

    private static string ReceiptPath() =>
        Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR166_L4_RELEASE_GATE_RECEIPT.md");

    private static IReadOnlyList<string> Pr166Files() =>
    [
        ReceiptPath(),
        Path.Combine(RepositoryRoot(), "IronDev.IntegrationTests", "Governance", "L4ReleaseGateReceiptTests.cs")
    ];

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IronDev.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing IronDev.slnx.");
    }
}
