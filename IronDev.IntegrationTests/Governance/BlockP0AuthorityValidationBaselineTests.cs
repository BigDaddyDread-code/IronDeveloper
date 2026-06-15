using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("BlockP0AuthorityValidationBaseline")]
public sealed class BlockP0AuthorityValidationBaselineTests
{
    private static readonly string[] RequiredLaneCodes =
    [
        "L4_CAPABILITY_MATRIX",
        "L4_INVARIANT_REGRESSION",
        "L4_FAILURE_MODE_REPORT",
        "L4_BACKEND_READINESS_REPORT",
        "GOVERNED_DOGFOOD_CAMPAIGN",
        "UI_AUTHORITY_FIREWALL",
        "THIN_UI_RECEIPT",
        "API_CLI_CONTRACT",
        "THOUGHTLEDGER_BOUNDARY",
        "SOLUTION_BUILD",
        "DIFF_CHECK"
    ];

    [TestMethod]
    public void BlockP0AuthorityValidationBaseline_ReceiptExists()
    {
        Assert.IsTrue(File.Exists(ReceiptPath()));
        StringAssert.Contains(ReceiptText(), "PR167 adds the Block P0 Authority Validation Baseline.");
    }

    [TestMethod]
    public void BlockP0AuthorityValidationBaseline_UsesP0Naming()
    {
        var receipt = ReceiptText();

        StringAssert.Contains(receipt, "Block P remains the thin UI receipt checkpoint.");
        StringAssert.Contains(receipt, "Block P0 starts the backend authority validation baseline.");

        foreach (var forbiddenClaim in new[]
        {
            "Block Q0 starts the backend authority validation baseline",
            "Q1 Accepted Approval Record Contract",
            "Block P starts accepted approval"
        })
        {
            Assert.IsFalse(receipt.Contains(forbiddenClaim, StringComparison.OrdinalIgnoreCase), $"Receipt must not claim: {forbiddenClaim}");
        }
    }

    [TestMethod]
    public void BlockP0AuthorityValidationBaseline_ListsRequiredValidationLanes()
    {
        var receipt = ReceiptText();

        foreach (var laneCode in RequiredLaneCodes)
        {
            StringAssert.Contains(receipt, laneCode);
        }
    }

    [TestMethod]
    public void BlockP0AuthorityValidationBaseline_StatesMustPassBeforeAuthorityWork()
    {
        StringAssert.Contains(ReceiptText(), "These lanes must pass before backend authority implementation proceeds.");
    }

    [TestMethod]
    public void BlockP0AuthorityValidationBaseline_DoesNotClaimAuthority()
    {
        var receipt = ReceiptText();

        foreach (var boundary in new[]
        {
            "Validation baseline is not authority.",
            "Passing tests is not approval.",
            "Passing tests is not policy satisfaction.",
            "Passing tests is not dry-run execution.",
            "Passing tests is not patch artifact creation.",
            "Passing tests is not source apply.",
            "Passing tests is not workflow continuation.",
            "Passing tests is not release readiness.",
            "Authority implementation must still create backend-owned records."
        })
        {
            StringAssert.Contains(receipt, boundary);
        }
    }

    [TestMethod]
    public void BlockP0AuthorityValidationBaseline_StatesNextImplementationTarget()
    {
        StringAssert.Contains(ReceiptText(), "The next backend implementation target is P1 Accepted Approval Record Contract.");
    }

    [TestMethod]
    public void BlockP0AuthorityValidationBaseline_ContainsValidationCommands()
    {
        var receipt = ReceiptText();

        StringAssert.Contains(receipt, "FullyQualifiedName~BlockP0AuthorityValidationBaseline");
        StringAssert.Contains(receipt, "FullyQualifiedName~L4CapabilityMatrix");
        StringAssert.Contains(receipt, "FullyQualifiedName~L4InvariantRegression");
        StringAssert.Contains(receipt, "FullyQualifiedName~L4FailureModeReport");
        StringAssert.Contains(receipt, "FullyQualifiedName~L4BackendReadinessReport");
        StringAssert.Contains(receipt, "FullyQualifiedName~EndToEndGovernedDogfoodCampaign");
        StringAssert.Contains(receipt, "FullyQualifiedName~UiCannotOwnBackendAuthority");
        StringAssert.Contains(receipt, "FullyQualifiedName~BlockPThinUiReceipt");
        StringAssert.Contains(receipt, "ApiCliContract|ApiCliReleaseGate");
        StringAssert.Contains(receipt, "ThoughtLedger");
        StringAssert.Contains(receipt, "dotnet build IronDev.slnx --no-restore -v:minimal");
        StringAssert.Contains(receipt, "git diff --check");
    }

    [TestMethod]
    public void BlockP0AuthorityValidationBaseline_IncludesRequiredAuthorityChain()
    {
        StringAssert.Contains(
            ReceiptText(),
            "accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate");
    }

    [TestMethod]
    public void BlockP0AuthorityValidationBaseline_IsTestsAndReceiptOnly()
    {
        foreach (var directory in ProductionDirectories())
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                Assert.IsFalse(text.Contains("PR167", StringComparison.Ordinal), $"{file} must not contain PR167 marker text.");
                Assert.IsFalse(text.Contains("Block P0 Authority Validation Baseline", StringComparison.Ordinal), $"{file} must not contain Block P0 marker text.");
            }
        }
    }

    [TestMethod]
    public void BlockP0AuthorityValidationBaseline_ReviewLineIsPresent()
    {
        StringAssert.Contains(ReceiptText(), "PR167 paints the authority lanes. It does not drive in them.");
    }

    private static string ReceiptText() => File.ReadAllText(ReceiptPath());

    private static string ReceiptPath() =>
        Path.Combine(RepositoryRoot(), "Docs", "receipts", "PR167_BLOCK_P0_AUTHORITY_VALIDATION_BASELINE.md");

    private static IReadOnlyList<string> ProductionDirectories()
    {
        var root = RepositoryRoot();
        return
        [
            Path.Combine(root, "IronDev.TauriShell", "src"),
            Path.Combine(root, "IronDev.Api"),
            Path.Combine(root, "IronDev.Core"),
            Path.Combine(root, "IronDev.Infrastructure"),
            Path.Combine(root, "tools", "IronDev.Cli"),
            Path.Combine(root, "Database")
        ];
    }

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
