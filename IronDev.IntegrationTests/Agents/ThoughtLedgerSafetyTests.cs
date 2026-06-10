using Audit = IronDev.Core.Agents.Audit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class ThoughtLedgerSafetyTests
{
    [TestMethod]
    public void ThoughtLedgerSafety_ValidSafeRationalePasses()
    {
        var entries = new[]
        {
            BuildEntry(Audit.ThoughtLedgerEntryType.EvidenceUsed, evidenceRefs: ["evidence-1"]),
            BuildEntry(Audit.ThoughtLedgerEntryType.Assumption, summary: "Assumes validation package exists because the report references it."),
            BuildEntry(Audit.ThoughtLedgerEntryType.RejectedAlternative, summary: "Rejected direct source mutation because the agent has no authority."),
            BuildEntry(Audit.ThoughtLedgerEntryType.Risk, summary: "Risk remains until human review completes."),
            BuildEntry(Audit.ThoughtLedgerEntryType.BoundaryDecision, summary: "Boundary check records that this is rationale only.", evidenceRefs: ["boundary-1"]),
            BuildEntry(Audit.ThoughtLedgerEntryType.OutputRationale, summary: "Output summarizes evidence without granting authority.", evidenceRefs: ["output-1"])
        };

        AssertNoIssues(new Audit.ThoughtLedgerSafetyValidator().Validate(entries));
    }

    [TestMethod]
    public void ThoughtLedgerSafety_RequiresCoreFieldsAndValidType()
    {
        var entry = BuildEntry((Audit.ThoughtLedgerEntryType)999) with
        {
            ThoughtLedgerEntryId = string.Empty,
            AgentRunId = string.Empty,
            Summary = string.Empty
        };

        var issues = new Audit.ThoughtLedgerSafetyValidator().Validate(entry);

        AssertHasIssue(issues, Audit.ThoughtLedgerSafetyValidator.EntryIdRequired);
        AssertHasIssue(issues, Audit.ThoughtLedgerSafetyValidator.AgentRunIdRequired);
        AssertHasIssue(issues, Audit.ThoughtLedgerSafetyValidator.EntryTypeInvalid);
        AssertHasIssue(issues, Audit.ThoughtLedgerSafetyValidator.SummaryRequired);
    }

    [TestMethod]
    public void ThoughtLedgerSafety_DecisionEvidenceAndOutputEntriesRequireEvidence()
    {
        foreach (var entryType in new[]
        {
            Audit.ThoughtLedgerEntryType.DecisionRationale,
            Audit.ThoughtLedgerEntryType.EvidenceUsed,
            Audit.ThoughtLedgerEntryType.BoundaryDecision,
            Audit.ThoughtLedgerEntryType.OutputRationale
        })
        {
            AssertHasIssue(
                new Audit.ThoughtLedgerSafetyValidator().Validate(BuildEntry(entryType, evidenceRefs: [])),
                Audit.ThoughtLedgerSafetyValidator.EvidenceRequired);
        }
    }

    [TestMethod]
    public void ThoughtLedgerSafety_BlocksRawPrivateReasoningMarkers()
    {
        foreach (var summary in new[]
        {
            "ChainOfThought: hidden deliberation.",
            "RawPrompt: do not persist this.",
            "RawCompletion: private model output.",
            "Scratchpad contains internal notes.",
            "SystemPrompt should never be stored."
        })
        {
            AssertHasIssue(
                new Audit.ThoughtLedgerSafetyValidator().Validate(BuildEntry(summary: summary)),
                Audit.ThoughtLedgerSafetyValidator.RawPrivateReasoningBlocked);
        }
    }

    [TestMethod]
    public void ThoughtLedgerSafety_BlocksAuthorityApprovalAndPromotionClaims()
    {
        AssertHasIssue(
            new Audit.ThoughtLedgerSafetyValidator().Validate(BuildEntry(summary: "I approve this action.")),
            Audit.ThoughtLedgerSafetyValidator.ApprovalClaimBlocked);

        AssertHasIssue(
            new Audit.ThoughtLedgerSafetyValidator().Validate(BuildEntry(summary: "This is authoritative for action.")),
            Audit.ThoughtLedgerSafetyValidator.AuthorityClaimBlocked);

        AssertHasIssue(
            new Audit.ThoughtLedgerSafetyValidator().Validate(BuildEntry(summary: "This promoted memory.")),
            Audit.ThoughtLedgerSafetyValidator.MemoryPromotionClaimBlocked);
    }

    [TestMethod]
    public void ThoughtLedgerSafety_BlocksAuthorityFlags()
    {
        var issues = new Audit.ThoughtLedgerSafetyValidator().Validate(BuildEntry() with
        {
            ContainsRawPrivateReasoning = true,
            GrantsAuthority = true,
            GrantsApproval = true,
            GrantsMemoryPromotion = true
        });

        AssertHasIssue(issues, Audit.ThoughtLedgerSafetyValidator.RawPrivateReasoningBlocked);
        AssertHasIssue(issues, Audit.ThoughtLedgerSafetyValidator.AuthorityClaimBlocked);
        AssertHasIssue(issues, Audit.ThoughtLedgerSafetyValidator.ApprovalClaimBlocked);
        AssertHasIssue(issues, Audit.ThoughtLedgerSafetyValidator.MemoryPromotionClaimBlocked);
    }

    private static Audit.ThoughtLedgerEntry BuildEntry(
        Audit.ThoughtLedgerEntryType entryType = Audit.ThoughtLedgerEntryType.EvidenceUsed,
        string summary = "Evidence was reviewed and no authority is granted by this rationale.",
        IReadOnlyList<string>? evidenceRefs = null) =>
        new()
        {
            ThoughtLedgerEntryId = "thought-1",
            AgentRunId = "agent-run-1",
            EntryType = entryType,
            Summary = summary,
            EvidenceRefs = evidenceRefs ?? ["evidence-1"],
            RecordedAtUtc = DateTimeOffset.UtcNow
        };

    private static void AssertHasIssue(IReadOnlyList<IronDev.Core.Agents.AgentDefinitionValidationIssue> issues, string code)
    {
        Assert.IsTrue(
            issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)),
            $"Expected validation issue '{code}' but got: {string.Join(", ", issues.Select(issue => issue.Code))}");
    }

    private static void AssertNoIssues(IReadOnlyList<IronDev.Core.Agents.AgentDefinitionValidationIssue> issues)
    {
        Assert.AreEqual(
            0,
            issues.Count,
            $"Expected no validation issues but got: {string.Join(", ", issues.Select(issue => $"{issue.Code}:{issue.Message}"))}");
    }
}
