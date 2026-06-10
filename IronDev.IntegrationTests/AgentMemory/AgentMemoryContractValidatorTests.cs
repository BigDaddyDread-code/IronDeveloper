using IronDev.Core.AgentMemory;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentMemoryContractValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 0, 0, 0, TimeSpan.Zero);

    private readonly AgentMemoryContractValidator _validator = new();

    [TestMethod]
    public void ValidObservedEpisodicMemoryPassesValidation()
    {
        var result = _validator.Validate(BuildMemoryItem());

        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(0, result.Issues.Count);
    }

    [TestMethod]
    public void MemoryWithoutScopeFails()
    {
        var result = _validator.Validate(BuildMemoryItem() with { Scope = null! });

        AssertHasIssue(result, AgentMemoryContractValidator.ScopeRequired);
    }

    [TestMethod]
    public void MemoryWithoutTenantFails()
    {
        var result = _validator.Validate(BuildMemoryItem() with
        {
            Scope = BuildScope() with { TenantId = string.Empty }
        });

        AssertHasIssue(result, AgentMemoryContractValidator.TenantRequired);
    }

    [TestMethod]
    public void MemoryWithoutProjectFails()
    {
        var result = _validator.Validate(BuildMemoryItem() with
        {
            Scope = BuildScope() with { ProjectId = string.Empty }
        });

        AssertHasIssue(result, AgentMemoryContractValidator.ProjectRequired);
    }

    [TestMethod]
    public void MemoryWithoutCampaignFails()
    {
        var result = _validator.Validate(BuildMemoryItem() with
        {
            Scope = BuildScope() with { CampaignId = string.Empty }
        });

        AssertHasIssue(result, AgentMemoryContractValidator.CampaignRequired);
    }

    [TestMethod]
    public void MemoryWithoutRunFails()
    {
        var result = _validator.Validate(BuildMemoryItem() with
        {
            Scope = BuildScope() with { RunId = string.Empty }
        });

        AssertHasIssue(result, AgentMemoryContractValidator.RunRequired);
    }

    [TestMethod]
    public void MemoryWithoutAgentFails()
    {
        var result = _validator.Validate(BuildMemoryItem() with
        {
            Scope = BuildScope() with { AgentId = string.Empty }
        });

        AssertHasIssue(result, AgentMemoryContractValidator.AgentRequired);
    }

    [DataTestMethod]
    [DataRow("below", "-0.01")]
    [DataRow("above", "1.01")]
    public void MemoryConfidenceOutsideRangeFails(string _, string confidenceText)
    {
        var confidence = decimal.Parse(confidenceText, System.Globalization.CultureInfo.InvariantCulture);
        var result = _validator.Validate(BuildMemoryItem() with { Confidence = confidence });

        AssertHasIssue(result, AgentMemoryContractValidator.ConfidenceOutOfRange);
    }

    [TestMethod]
    public void AcceptedLocalMemoryFails()
    {
        var result = _validator.Validate(BuildMemoryItem() with
        {
            AuthorityLevel = MemoryAuthorityLevel.Accepted
        });

        AssertHasIssue(result, AgentMemoryContractValidator.LocalMemoryCannotBeAccepted);
    }

    [TestMethod]
    public void SystemRuleLocalMemoryFails()
    {
        var result = _validator.Validate(BuildMemoryItem() with
        {
            AuthorityLevel = MemoryAuthorityLevel.SystemRule
        });

        AssertHasIssue(result, AgentMemoryContractValidator.LocalMemoryCannotBeSystemRule);
    }

    [TestMethod]
    public void CandidatePatternWithoutKnownLimitationsFails()
    {
        var result = _validator.Validate(BuildCandidatePattern() with { KnownLimitations = null });

        AssertHasIssue(result, AgentMemoryContractValidator.CandidatePatternRequiresLimitations);
    }

    [TestMethod]
    public void CandidatePatternWithoutEvidenceFails()
    {
        var result = _validator.Validate(BuildCandidatePattern() with
        {
            EvidenceRefs = Array.Empty<EvidenceRef>()
        });

        AssertHasIssue(result, AgentMemoryContractValidator.EvidenceRequired);
    }

    [TestMethod]
    public void ObservedOnlyFailedAttemptWithEvidencePasses()
    {
        var result = _validator.Validate(BuildMemoryItem() with
        {
            MemoryType = AgentMemoryType.FailedAttempt,
            AuthorityLevel = MemoryAuthorityLevel.ObservedOnly,
            Title = "Build failed after namespace change",
            Summary = "BuilderAgent tried fix A and test compilation still failed."
        });

        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public void WorkingMemoryWithoutEvidenceMayPassOnlyIfShortLived()
    {
        var result = _validator.Validate(BuildMemoryItem() with
        {
            MemoryType = AgentMemoryType.Working,
            EvidenceRefs = Array.Empty<EvidenceRef>(),
            ExpiresAt = Now.AddMinutes(30)
        });

        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public void WorkingMemoryWithoutEvidenceAndExpiryFails()
    {
        var result = _validator.Validate(BuildMemoryItem() with
        {
            MemoryType = AgentMemoryType.Working,
            EvidenceRefs = Array.Empty<EvidenceRef>(),
            ExpiresAt = null
        });

        AssertHasIssue(result, AgentMemoryContractValidator.EvidenceRequired);
    }

    [TestMethod]
    public void SelfReferentialEvidenceFails()
    {
        var result = _validator.Validate(BuildMemoryItem() with
        {
            MemoryItemId = "memory-self",
            EvidenceRefs =
            [
                BuildEvidence() with { SourceId = "memory-self" }
            ]
        });

        AssertHasIssue(result, AgentMemoryContractValidator.SelfReferentialEvidenceBlocked);
    }

    [TestMethod]
    public void ValidInfluenceRecordPasses()
    {
        var result = _validator.Validate(BuildInfluenceRecord());

        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public void InfluenceWithoutDecisionIdFails()
    {
        var result = _validator.Validate(BuildInfluenceRecord() with { DecisionId = string.Empty });

        AssertHasIssue(result, AgentMemoryContractValidator.InfluenceDecisionRequired);
    }

    [TestMethod]
    public void InfluenceWithoutMemoryItemIdFails()
    {
        var result = _validator.Validate(BuildInfluenceRecord() with { MemoryItemId = string.Empty });

        AssertHasIssue(result, AgentMemoryContractValidator.InfluenceMemoryItemRequired);
    }

    [TestMethod]
    public void InfluenceWithoutScopeFails()
    {
        var result = _validator.Validate(BuildInfluenceRecord() with { Scope = null! });

        AssertHasIssue(result, AgentMemoryContractValidator.ScopeRequired);
    }

    [TestMethod]
    public void InfluenceConfidenceOutsideRangeFails()
    {
        var result = _validator.Validate(BuildInfluenceRecord() with { Confidence = 1.5m });

        AssertHasIssue(result, AgentMemoryContractValidator.ConfidenceOutOfRange);
    }

    [TestMethod]
    public void InfluenceWithoutEvidenceFails()
    {
        var result = _validator.Validate(BuildInfluenceRecord() with
        {
            EvidenceRefs = Array.Empty<EvidenceRef>()
        });

        AssertHasIssue(result, AgentMemoryContractValidator.EvidenceRequired);
    }

    [TestMethod]
    public void ValidHandoffMemorySlicePasses()
    {
        var result = _validator.Validate(BuildHandoffSlice());

        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public void HandoffMemorySliceMissingSourceAgentFails()
    {
        var result = _validator.Validate(BuildHandoffSlice() with { SourceAgentId = string.Empty });

        AssertHasIssue(result, AgentMemoryContractValidator.HandoffSourceRequired);
    }

    [TestMethod]
    public void HandoffMemorySliceMissingTargetAgentFails()
    {
        var result = _validator.Validate(BuildHandoffSlice() with { TargetAgentId = string.Empty });

        AssertHasIssue(result, AgentMemoryContractValidator.HandoffTargetRequired);
    }

    [TestMethod]
    public void HandoffMemorySliceMissingMemoryItemIdsFails()
    {
        var result = _validator.Validate(BuildHandoffSlice() with
        {
            MemoryItemIds = Array.Empty<string>()
        });

        AssertHasIssue(result, AgentMemoryContractValidator.HandoffMemoryItemRequired);
    }

    [TestMethod]
    public void HandoffMemorySliceMissingAllowedUseFails()
    {
        var result = _validator.Validate(BuildHandoffSlice() with
        {
            AllowedUse = default
        });

        AssertHasIssue(result, AgentMemoryContractValidator.HandoffAllowedUseRequired);
    }

    [TestMethod]
    public void HandoffMemorySliceMissingEvidenceFails()
    {
        var result = _validator.Validate(BuildHandoffSlice() with
        {
            EvidenceRefs = Array.Empty<EvidenceRef>()
        });

        AssertHasIssue(result, AgentMemoryContractValidator.EvidenceRequired);
    }

    [TestMethod]
    public void HandoffMemorySliceConfidenceOutsideRangeFails()
    {
        var result = _validator.Validate(BuildHandoffSlice() with { Confidence = -0.1m });

        AssertHasIssue(result, AgentMemoryContractValidator.ConfidenceOutOfRange);
    }

    private static AgentLocalMemoryItem BuildMemoryItem() =>
        new()
        {
            MemoryItemId = "memory-1",
            Scope = BuildScope(),
            MemoryType = AgentMemoryType.Episodic,
            AuthorityLevel = MemoryAuthorityLevel.ObservedOnly,
            Title = "Observed build failure",
            Summary = "TesterAgent observed a reproducible build failure.",
            EvidenceRefs = [BuildEvidence()],
            Confidence = 0.8m,
            Status = MemoryLifecycleStatus.Active,
            CreatedAt = Now
        };

    private static AgentLocalMemoryItem BuildCandidatePattern() =>
        BuildMemoryItem() with
        {
            MemoryItemId = "memory-candidate-1",
            MemoryType = AgentMemoryType.CandidatePattern,
            AuthorityLevel = MemoryAuthorityLevel.CandidatePattern,
            Title = "Potential package restore pattern",
            Summary = "Missing namespace failures may require restore inspection first.",
            KnownLimitations = "Observed in one run only. Not accepted memory."
        };

    private static MemoryInfluenceRecord BuildInfluenceRecord() =>
        new()
        {
            InfluenceId = "influence-1",
            MemoryItemId = "memory-1",
            Scope = BuildScope(),
            DecisionId = "decision-1",
            InfluenceType = MemoryInfluenceType.ToolCallJustified,
            InfluenceSummary = "Memory justified the selected tool call.",
            EvidenceRefs = [BuildEvidence()],
            Confidence = 0.7m,
            MemoryAuthorityLevelAtInfluence = MemoryAuthorityLevel.ObservedOnly,
            MemoryStatusAtInfluence = MemoryLifecycleStatus.Active,
            CreatedAt = Now
        };

    private static HandoffMemorySlice BuildHandoffSlice() =>
        new()
        {
            HandoffMemorySliceId = "handoff-1",
            SourceAgentId = "builder-agent",
            TargetAgentId = "tester-agent",
            CampaignId = "campaign-1",
            RunId = "run-1",
            MemoryItemIds = ["memory-1"],
            Summary = "Builder observed the attempted fix and hands context to Tester.",
            EvidenceRefs = [BuildEvidence()],
            AllowedUse = HandoffMemoryAllowedUse.NeedsVerification,
            Confidence = 0.6m,
            CreatedAt = Now,
            ExpiresAt = Now.AddHours(2)
        };

    private static AgentMemoryScope BuildScope() =>
        new()
        {
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RunId = "run-1",
            AgentId = "builder-agent"
        };

    private static EvidenceRef BuildEvidence() =>
        new()
        {
            EvidenceId = "evidence-1",
            EvidenceType = EvidenceType.TestResult,
            SourceId = "test-result-1",
            SourceUri = "workspace://run-1/test-result.json",
            Summary = "Focused test result captured during the run.",
            CapturedAt = Now
        };

    private static void AssertHasIssue(MemoryValidationResult result, string code)
    {
        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)),
            $"Expected validation issue '{code}' but got: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
    }
}
