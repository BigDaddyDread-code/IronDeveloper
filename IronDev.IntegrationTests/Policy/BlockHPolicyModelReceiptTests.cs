using System.Globalization;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Policy;

[TestClass]
[TestCategory("BlockHPolicyModelReceipt")]
public sealed class BlockHPolicyModelReceiptTests
{
    private static readonly string[] RequiredSections =
    [
        "## 1. Summary",
        "## 2. What Block H delivered",
        "## 3. Current policy model components",
        "## 4. Authority boundary matrix",
        "## 5. Fail-closed model",
        "## 6. Approval-laundering boundary",
        "## 7. Sensitive scope policy",
        "## 8. Profile status",
        "## 9. API/CLI/runtime status",
        "## 10. Explicit non-claims",
        "## 11. Known gaps after Block H",
        "## 12. Validation evidence",
        "## 13. Merge standard",
        "## 14. Final receipt statement"
    ];

    private static readonly string[] RequiredPrs =
    [
        "PR 82",
        "PR 83",
        "PR 84",
        "PR 85",
        "PR 86",
        "PR 87",
        "PR 88",
        "PR 89"
    ];

    private static readonly string[] RequiredPolicyComponents =
    [
        "ProjectAutonomyPolicy",
        "ProjectApprovalRule",
        "ApprovalRequirementEvaluator",
        "ApprovalRequirementEvaluationResult",
        "ApprovalRequirement",
        "ApprovalPackage",
        "ApprovalPackageRequirement",
        "ApprovalPackageEvidenceReference",
        "ProjectPolicyProfile",
        "ProjectPolicyProfileFactory"
    ];

    private static readonly string[] RequiredNonClaims =
    [
        "IronDev is release-ready.",
        "L4 agents are ready.",
        "Workflow orchestration exists.",
        "A2A exists.",
        "LangGraph is integrated.",
        "Policy activation exists.",
        "Approval satisfaction exists.",
        "Approval decision lookup exists.",
        "Source apply is available.",
        "Memory promotion is available.",
        "Release approval is available.",
        "Policy profiles are active by default.",
        "Experimental mode is permission.",
        "ReadyForReview means approved.",
        "Gate pass means approved.",
        "Dogfood passed means release approved.",
        "Critic clean means approved.",
        "Validation passed means approved."
    ];

    private static readonly string[] RequiredBoundaryStatements =
    [
        "Block H does not activate policy, approve actions, execute tools, mutate source, promote memory, continue workflow, satisfy policy, or approve release.",
        "ApprovalRequirementEvaluator returns requirements, not approval.",
        "ApprovalPackage ReadyForReview means ready for review, not approved.",
        "ProjectPolicyProfile produces draft templates, not active policy.",
        "Experimental means less friction for non-sensitive scopes, not permission.",
        "Missing policy means fail closed.",
        "Only explicit approval decision records can be treated as approval evidence.",
        "Even an approval decision record does not execute, mutate source, promote memory, continue workflow, satisfy policy, or approve release by itself."
    ];

    private static readonly string[] RequiredFailClosedStatements =
    [
        "No active policy is not permission.",
        "No matching rule is not permission.",
        "Invalid policy is not permission.",
        "Invalid rule is not permission.",
        "Ambiguous rule selection is not permission.",
        "Draft generated profile policy is not active policy.",
        "ReadyForReview package does not override missing policy.",
        "No policy means fail closed.",
        "No matching approval rule means fail closed.",
        "Invalid policy means fail closed.",
        "Invalid rule means fail closed.",
        "Ambiguous rules mean fail closed."
    ];

    private static readonly string[] RequiredApprovalLaunderingStatements =
    [
        "Gate pass is not approval.",
        "Dogfood receipt is not approval.",
        "Critic output is not approval.",
        "Validation output is not approval.",
        "Model output is not approval.",
        "Retrieval output is not approval.",
        "ReadyForReview is not approved.",
        "Experimental is not permission."
    ];

    private static readonly string[] RequiredApprovalLaunderingSources =
    [
        "gate decisions",
        "policy decision events",
        "dogfood receipts",
        "critic output",
        "code standards output",
        "approval requirement evaluator results",
        "approval packages",
        "policy profiles",
        "ThoughtLedger references",
        "governance events",
        "validation output",
        "run reports",
        "model output",
        "retrieval/vector matches",
        "A2A evidence",
        "workflow-route evidence"
    ];

    private static readonly string[] RequiredSensitiveScopes =
    [
        "source_apply",
        "memory_promotion",
        "release_readiness",
        "external_side_effect",
        "destructive_operation"
    ];

    private static readonly string[] RequiredSensitiveScopeStatements =
    [
        "Every sensitive scope requires explicit human approval rules.",
        "Experimental profiles do not bypass sensitive approval.",
        "ApprovalType=None is not valid for sensitive scopes.",
        "Agent, System, Model, Critic, Workflow, Retrieval, A2A, validation, gate, policy-decision, and dogfood concepts cannot satisfy sensitive approval."
    ];

    private static readonly string[] RequiredValidationEvidence =
    [
        "BlockHPolicyModelReceipt",
        "ProjectPolicyProfile",
        "ApprovalPackage",
        "ApprovalRequirementEvaluator",
        "ProjectAutonomyPolicy\\|ProjectApprovalRule",
        "MissingPolicyFailsClosed",
        "ApprovalAuthorityBoundary",
        "GovernanceSubstrateContract\\|BlockGGovernanceSubstrateReceipt",
        "ToolRequestApi\\|ToolGateApi\\|DogfoodLoopApi",
        "ApiCliContract\\|ApiCliReleaseGate\\|ThoughtLedger",
        "dotnet build IronDev.slnx --no-restore -v:minimal",
        "git diff --check"
    ];

    private static readonly string[] ForbiddenTrophyPhrases =
    [
        "production ready",
        "release ready",
        "L4 complete",
        "workflow ready",
        "source apply ready",
        "memory promotion ready",
        "policy engine complete",
        "approval engine complete",
        "autonomous execution",
        "can execute",
        "can ship",
        "approved by gate",
        "approved by receipt",
        "approved by critic",
        "experimental means allowed"
    ];

    [TestMethod]
    public void BlockHReceipt_DocumentExistsAndContainsRequiredSections()
    {
        Assert.IsTrue(File.Exists(ReceiptPath()), ReceiptPath());
        var receipt = ReadReceipt();

        AssertContainsAll(receipt, RequiredSections);
    }

    [TestMethod]
    public void BlockHReceipt_ListsAllBlockHPrs()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(receipt, RequiredPrs);
    }

    [TestMethod]
    public void BlockHReceipt_ListsAllPolicyModelComponents()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(receipt, RequiredPolicyComponents);
    }

    [TestMethod]
    public void BlockHReceipt_ListsAllRequiredNonClaims()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(receipt, RequiredNonClaims);
    }

    [TestMethod]
    public void BlockHReceipt_StatesPolicyModelDoesNotActivateApproveExecuteOrMutate()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(receipt, RequiredBoundaryStatements);
        AssertContainsAll(receipt, new[]
        {
            "Block H adds no API endpoint.",
            "Block H adds no CLI command.",
            "Block H adds no SQL persistence.",
            "Block H adds no runtime DI wiring.",
            "Block H adds no workflow runner.",
            "Block H adds no policy activation path.",
            "Block H adds no approval decision lookup.",
            "Block H adds no approval satisfaction checker.",
            "Block H adds no A2A runtime.",
            "Block H adds no LangGraph runtime.",
            "Block H adds no source apply.",
            "Block H adds no memory promotion.",
            "Block H adds no release approval."
        });
    }

    [TestMethod]
    public void BlockHReceipt_StatesAuthorityMatrixHasNoGrantedAuthority()
    {
        var receipt = ReadReceipt();

        StringAssert.Contains(receipt, "| Component | Approval? | Execution? | Source apply? | Memory promotion? | Workflow? | Release approval? |");
        StringAssert.Contains(receipt, "| ProjectAutonomyPolicy | No | No | No | No | No | No |");
        StringAssert.Contains(receipt, "| ProjectApprovalRule | No | No | No | No | No | No |");
        StringAssert.Contains(receipt, "| ApprovalRequirementEvaluator | No | No | No | No | No | No |");
        StringAssert.Contains(receipt, "| ApprovalPackage | No | No | No | No | No | No |");
        StringAssert.Contains(receipt, "| ProjectPolicyProfile | No | No | No | No | No | No |");
        StringAssert.Contains(receipt, "| MissingPolicyFailsClosed tests | No | No | No | No | No | No |");
        StringAssert.Contains(receipt, "| ApprovalAuthorityBoundary tests | No | No | No | No | No | No |");
    }

    [TestMethod]
    public void BlockHReceipt_StatesMissingPolicyFailsClosed()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(receipt, RequiredFailClosedStatements);
    }

    [TestMethod]
    public void BlockHReceipt_StatesApprovalCannotBeLaunderedFromAdjacentEvidence()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(receipt, RequiredApprovalLaunderingSources);
        AssertContainsAll(receipt, RequiredApprovalLaunderingStatements);
    }

    [TestMethod]
    public void BlockHReceipt_ListsSensitiveScopesAndHumanApprovalBoundary()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(receipt, RequiredSensitiveScopes);
        AssertContainsAll(receipt, RequiredSensitiveScopeStatements);
    }

    [TestMethod]
    public void BlockHReceipt_StatesProfilesAreDraftTemplatesOnly()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(receipt, new[]
        {
            "Conservative, Balanced, and Experimental profiles are starter templates only.",
            "They generate draft policy and rule shapes.",
            "They do not activate policy.",
            "They do not evaluate policy.",
            "They do not approve anything.",
            "They do not become hidden defaults.",
            "They do not satisfy approval requirements.",
            "Experimental relaxes only non-sensitive draft settings."
        });
    }

    [TestMethod]
    public void BlockHReceipt_IncludesValidationEvidence()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(receipt, RequiredValidationEvidence);
        StringAssert.Contains(receipt, "Passed 15/15");
        StringAssert.Contains(receipt, "Passed 83/83");
        StringAssert.Contains(receipt, "Passed 69/69");
        StringAssert.Contains(receipt, "Passed 84/84");
        StringAssert.Contains(receipt, "Passed 91/91");
        StringAssert.Contains(receipt, "Passed 19/19");
        StringAssert.Contains(receipt, "Passed 44/44");
        StringAssert.Contains(receipt, "Passed 70/70");
        StringAssert.Contains(receipt, "Passed, 0 errors");
    }

    [TestMethod]
    public void BlockHReceipt_ListsKnownGapsAndNextBlocks()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(receipt, new[]
        {
            "No active policy storage yet.",
            "No policy activation path yet.",
            "No approval satisfaction checker yet.",
            "No approval decision lookup against requirements yet.",
            "No workflow checkpointing yet.",
            "No A2A handoff spine yet.",
            "No source apply path yet.",
            "No memory promotion path yet.",
            "No release approval gate yet.",
            "Block I - A2A Handoff Contract Spine",
            "Block J - Workflow State and Checkpoint Spine",
            "Block K - MemoryImprovementAgent L2/L3",
            "Block L - Minimal Governed Workflow Runner"
        });
    }

    [TestMethod]
    public void BlockHReceipt_DoesNotClaimForbiddenReadiness()
    {
        var receipt = ReadReceipt();

        foreach (var phrase in ForbiddenTrophyPhrases)
        {
            AssertPhraseOnlyAppearsInNegativeStatement(receipt, phrase);
        }

        Assert.IsFalse(receipt.Contains("policy engine is complete", StringComparison.OrdinalIgnoreCase), receipt);
        Assert.IsFalse(receipt.Contains("approval engine is complete", StringComparison.OrdinalIgnoreCase), receipt);
        Assert.IsFalse(receipt.Contains("workflow is ready", StringComparison.OrdinalIgnoreCase), receipt);
        Assert.IsFalse(receipt.Contains("source apply is ready", StringComparison.OrdinalIgnoreCase), receipt);
        Assert.IsFalse(receipt.Contains("memory promotion is ready", StringComparison.OrdinalIgnoreCase), receipt);
        Assert.IsFalse(receipt.Contains("experimental means allowed", StringComparison.OrdinalIgnoreCase), receipt);
        Assert.IsFalse(receipt.Contains("approved by gate", StringComparison.OrdinalIgnoreCase), receipt);
        Assert.IsFalse(receipt.Contains("approved by receipt", StringComparison.OrdinalIgnoreCase), receipt);
        Assert.IsFalse(receipt.Contains("approved by critic", StringComparison.OrdinalIgnoreCase), receipt);
    }

    [TestMethod]
    public void BlockHReceipt_FinalStatementClosesBlockWithoutGrantingAuthority()
    {
        var receipt = ReadReceipt();

        AssertContainsAll(receipt, new[]
        {
            "Block H is complete as a project authority and approval policy model.",
            "It gives IronDev bounded policy vocabulary, approval rule vocabulary, deterministic requirement evaluation, approval package modelling, safe starter profiles, fail-closed behaviour, and approval-laundering regression coverage.",
            "It does not activate policy.",
            "It does not approve actions.",
            "It does not execute tools.",
            "It does not mutate source.",
            "It does not promote memory.",
            "It does not continue workflow.",
            "It does not approve release.",
            "The policy model is now defined and guarded.",
            "The system still cannot act merely because the model exists."
        });
    }

    [TestMethod]
    public void BlockHReceipt_IsAsciiNoBomAndNoHiddenUnicode()
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
            Assert.IsFalse(category == UnicodeCategory.Format, $"Receipt contains hidden format character U+{(int)ch:X4}.");
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
            var isNegative = lower.Contains("does not", StringComparison.Ordinal)
                || lower.Contains("not ", StringComparison.Ordinal)
                || lower.Contains(" no ", StringComparison.Ordinal)
                || lower.Contains("cannot", StringComparison.Ordinal)
                || lower.Contains("without granting", StringComparison.Ordinal);

            Assert.IsTrue(isNegative, $"Forbidden trophy phrase must appear only in negative context: {phrase} / {line}");
        }
    }

    private static string ReadReceipt()
    {
        return File.ReadAllText(ReceiptPath());
    }

    private static string ReceiptPath()
    {
        return Path.Combine(RepositoryRoot(), "Docs", "receipts", "BLOCK_H_POLICY_MODEL_RECEIPT.md");
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
