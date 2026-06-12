using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("BlockGGovernanceSubstrateReceipt")]
public sealed class BlockGGovernanceSubstrateReceiptTests
{
    private static readonly string[] RequiredBlockGPrs =
    [
        "PR 72",
        "PR 73",
        "PR 74",
        "PR 74a",
        "PR 74b",
        "PR 74c",
        "PR 75",
        "PR 76",
        "PR 77",
        "PR 78",
        "PR 79",
        "PR 80"
    ];

    private static readonly string[] RequiredLedgers =
    [
        "governance.GovernanceEvent",
        "governance.ToolRequest",
        "governance.ToolGateDecision",
        "governance.ApprovalDecision",
        "governance.PolicyDecisionEvent",
        "governance.DogfoodReceipt",
        "governance.ThoughtLedgerGovernanceEventReference"
    ];

    private static readonly string[] RequiredNonClaims =
    [
        "IronDev is release-ready.",
        "L4 agents are ready.",
        "workflow orchestration exists.",
        "A2A exists.",
        "LangGraph is integrated.",
        "memory promotion is safe or available.",
        "source apply is available.",
        "policy engine exists.",
        "approval evaluator exists.",
        "project autonomy model exists.",
        "UI is ready.",
        "dogfood receipts approve release.",
        "gate pass approves execution.",
        "approval records execute anything.",
        "policy decisions grant permission."
    ];

    private static readonly string[] RequiredBoundaryStatements =
    [
        "This report is a receipt, not a trophy.",
        "Block G created a durable governance substrate for IronDev.",
        "It records governance-relevant facts in SQL.",
        "It does not approve, execute, promote memory, mutate source, route workflow, transfer authority, or approve release.",
        "ApprovalDecision records approval, but does not execute.",
        "DogfoodReceipt Passed is not release approval.",
        "PolicyDecisionEvent NoPolicyBlock is not permission.",
        "ToolGateDecision Passed is not approval.",
        "ThoughtLedgerGovernanceEventReference is evidence link only.",
        "Real DB smoke proves storage and retrieval. It does not prove release readiness.",
        "API/CLI remain exposure surfaces only.",
        "CLI remains API client only.",
        "References do not store hidden chain-of-thought.",
        "Block G is complete as a durable governance substrate."
    ];

    private static readonly string[] RequiredValidationEvidence =
    [
        "GovernanceSubstrateContract",
        "DatabaseMigrationReceipt",
        "SqlInventory",
        "RealDatabaseToolRequestSmoke",
        "RealDatabaseToolGateDecisionSmoke",
        "RealDatabaseApprovalDecisionSmoke",
        "RealDatabasePolicyDecisionSmoke",
        "RealDatabaseDogfoodReceiptSmoke",
        "RealDatabaseThoughtLedgerGovernanceReferenceSmoke",
        "GovernanceEventStore",
        "ToolRequestStore",
        "ToolGateDecisionStore",
        "ApprovalDecisionStore",
        "PolicyDecisionEventStore",
        "DogfoodReceiptStore",
        "ThoughtLedgerGovernanceReference",
        "ToolRequestApi",
        "ToolGateApi",
        "DogfoodLoopApi",
        "ApiCliContract",
        "ApiCliReleaseGate",
        "ThoughtLedger"
    ];

    private static readonly string[] ForbiddenTrophyPhrases =
    [
        "production ready",
        "product release ready",
        "fully autonomous",
        "L4 complete",
        "workflow ready",
        "source apply ready",
        "memory promotion approved",
        "release approved",
        "can ship"
    ];

    [TestMethod]
    public void BlockGReceipt_DocumentExistsAndContainsRequiredSections()
    {
        Assert.IsTrue(File.Exists(ReceiptPath()), ReceiptPath());
        var receipt = ReadReceipt();

        foreach (var section in new[]
        {
            "## 1. Summary",
            "## 2. What Block G delivered",
            "## 3. Current durable governance ledgers",
            "## 4. Authority boundary matrix",
            "## 5. SQL and migration status",
            "## 6. Real DB proof",
            "## 7. API/CLI status",
            "## 8. ThoughtLedger status",
            "## 9. Explicit non-claims",
            "## 10. Known gaps after Block G",
            "## 11. Merge standard evidence",
            "## 12. Final receipt statement"
        })
        {
            StringAssert.Contains(receipt, section);
        }
    }

    [TestMethod]
    public void BlockGReceipt_ListsAllBlockGPrsAndLedgers()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(receipt, RequiredBlockGPrs);
        AssertContainsAll(receipt, RequiredLedgers);
    }

    [TestMethod]
    public void BlockGReceipt_ListsAllRequiredNonClaims()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(receipt, RequiredNonClaims);
    }

    [TestMethod]
    public void BlockGReceipt_StatesAuthorityBoundaries()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(receipt, RequiredBoundaryStatements);
        StringAssert.Contains(receipt, "| Record type | Approval? | Execution? | Source apply? | Memory promotion? | Workflow? | Release approval? |");
        StringAssert.Contains(receipt, "| ToolRequest | No | No | No | No | No | No |");
        StringAssert.Contains(receipt, "| ToolGateDecision | No | No | No | No | No | No |");
        StringAssert.Contains(receipt, "| ApprovalDecision | Record only | No | No | No | No | No unless a future scoped release approval record explicitly says so |");
        StringAssert.Contains(receipt, "| DogfoodReceipt | No | No | No | No | No | No |");
    }

    [TestMethod]
    public void BlockGReceipt_IncludesSqlMigrationRealDbAndApiCliStatus()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(receipt, new[]
        {
            "Database/migrations.json",
            "Database/apply-migrations.ps1",
            "Database/verify-migrations.ps1",
            "Database/sql-inventory.json",
            "Docs/BACKEND_SQL_INVENTORY.md",
            "Docs/BACKEND_INLINE_SQL_INVENTORY.md",
            "IronDeveloper",
            "IronDeveloper_Test",
            "Tool Request API is SQL-backed.",
            "Tool Gate API is SQL-backed after PR75 durable gate decision storage.",
            "Dogfood Loop API is SQL-backed after PR78 durable dogfood receipt storage.",
            "No CLI command bypasses backend policy or writes SQL directly."
        });
    }

    [TestMethod]
    public void BlockGReceipt_IncludesValidationEvidence()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(receipt, RequiredValidationEvidence);
        StringAssert.Contains(receipt, "Passed 10/10");
        StringAssert.Contains(receipt, "Passed 32/32");
        StringAssert.Contains(receipt, "Passed 85/85");
        StringAssert.Contains(receipt, "Passed 44/44");
        StringAssert.Contains(receipt, "Passed 64/64");
        StringAssert.Contains(receipt, "Passed, 0 errors");
        StringAssert.Contains(receipt, "git diff --check");
    }

    [TestMethod]
    public void BlockGReceipt_ListsKnownGapsAndNextBlocks()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(receipt, new[]
        {
            "Block H - Project Authority and Approval Policy Model",
            "Block I - A2A Handoff Contract Spine",
            "Block J - Workflow State and Checkpoint Spine",
            "Block K - MemoryImprovementAgent L2/L3",
            "Block L - Minimal Governed Workflow Runner",
            "Block M - L4 Candidate Workflows",
            "no workflow state yet",
            "no A2A handoff contracts yet",
            "no policy evaluator yet",
            "no memory proposal staging yet",
            "no source apply path yet",
            "no release approval gate yet"
        });
    }

    [TestMethod]
    public void BlockGReceipt_DoesNotClaimForbiddenReadiness()
    {
        var receipt = ReadReceipt();

        foreach (var phrase in ForbiddenTrophyPhrases)
        {
            AssertPhraseOnlyAppearsInNegativeStatement(receipt, phrase);
        }

        Assert.IsFalse(receipt.Contains("IronDev full release: ready", StringComparison.OrdinalIgnoreCase), receipt);
        Assert.IsFalse(receipt.Contains("L4 is available", StringComparison.OrdinalIgnoreCase), receipt);
        Assert.IsFalse(receipt.Contains("workflow is available", StringComparison.OrdinalIgnoreCase), receipt);
        Assert.IsFalse(receipt.Contains("source apply is available as a capability", StringComparison.OrdinalIgnoreCase), receipt);
        Assert.IsFalse(receipt.Contains("memory promotion is safe as a capability", StringComparison.OrdinalIgnoreCase), receipt);
    }

    [TestMethod]
    public void BlockGReceipt_IsAsciiNoBomAndNoHiddenUnicode()
    {
        var bytes = File.ReadAllBytes(ReceiptPath());

        Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "Receipt must not contain UTF-8 BOM.");

        for (var index = 0; index < bytes.Length; index++)
        {
            Assert.IsTrue(bytes[index] <= 0x7F, $"Receipt must be ASCII-only. Non-ASCII byte 0x{bytes[index]:X2} at offset {index}.");
        }

        var receipt = Encoding.ASCII.GetString(bytes);
        foreach (var ch in receipt)
        {
            var category = char.GetUnicodeCategory(ch);
            Assert.IsFalse(category == System.Globalization.UnicodeCategory.Format, $"Receipt contains hidden format character U+{(int)ch:X4}.");
            Assert.IsFalse(char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t', $"Receipt contains unexpected control character U+{(int)ch:X4}.");
        }
    }

    private static void AssertContainsAll(string text, IEnumerable<string> expected)
    {
        foreach (var value in expected)
        {
            StringAssert.Contains(text, value, value);
        }
    }

    private static void AssertPhraseOnlyAppearsInNegativeStatement(string text, string phrase)
    {
        var matchingLines = text
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(line => line.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var line in matchingLines)
        {
            var lower = line.ToLowerInvariant();
            var isNegative = lower.Contains("does not claim", StringComparison.Ordinal)
                || lower.Contains("not ", StringComparison.Ordinal)
                || lower.Contains(" no ", StringComparison.Ordinal)
                || lower.Contains("cannot", StringComparison.Ordinal);

            Assert.IsTrue(isNegative, $"Forbidden trophy phrase must appear only in negative context: {phrase} / {line}");
        }
    }

    private static string ReadReceipt()
    {
        return File.ReadAllText(ReceiptPath());
    }

    private static string ReceiptPath()
    {
        return Path.Combine(RepositoryRoot(), "Docs", "receipts", "BLOCK_G_GOVERNANCE_SUBSTRATE_RECEIPT.md");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root containing IronDev.slnx.");
    }
}
