using IronDev.Core.Agents;
using IronDev.Core.Agents.Concrete;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class BoxedAgentDefinitionTests
{
    private static readonly AgentDefinitionValidator AgentValidator = new();
    private static readonly CriticReviewResultValidator CriticValidator = new();
    private static readonly MemoryImprovementDetectionResultValidator MemoryValidator = new();

    [TestMethod]
    public void BoxedAgents_ExistInCatalogAndPassAgentDefinitionValidator()
    {
        Assert.IsNotNull(AgentDefinitionCatalog.IndependentCriticAgent);
        Assert.IsNotNull(AgentDefinitionCatalog.MemoryImprovementAgent);
        CollectionAssert.Contains(AgentDefinitionCatalog.All.ToArray(), AgentDefinitionCatalog.IndependentCriticAgent);
        CollectionAssert.Contains(AgentDefinitionCatalog.All.ToArray(), AgentDefinitionCatalog.MemoryImprovementAgent);

        AssertNoIssues(AgentValidator.Validate(AgentDefinitionCatalog.IndependentCriticAgent));
        AssertNoIssues(AgentValidator.Validate(AgentDefinitionCatalog.MemoryImprovementAgent));
    }

    [TestMethod]
    public void IndependentCriticAgent_IsReviewAgentOutOfBandReviewOnly()
    {
        var definition = AgentDefinitionCatalog.IndependentCriticAgent;

        Assert.AreEqual("builtin.independent-critic", definition.AgentId);
        Assert.AreEqual("IndependentCriticAgent", definition.Name);
        Assert.AreEqual(AgentKind.ReviewAgent, definition.Kind);
        Assert.AreEqual(AgentExecutionMode.OutOfBandReviewOnly, definition.ExecutionMode);
        CollectionAssert.Contains(definition.Capabilities!.ToArray(), AgentCapability.CreateCriticFinding);
        CollectionAssert.Contains(definition.Capabilities!.ToArray(), AgentCapability.WarnExecution);
    }

    [TestMethod]
    public void MemoryImprovementAgent_IsProposalAgentProposalOnly()
    {
        var definition = AgentDefinitionCatalog.MemoryImprovementAgent;

        Assert.AreEqual("builtin.memory-improvement", definition.AgentId);
        Assert.AreEqual("MemoryImprovementAgent", definition.Name);
        Assert.AreEqual(AgentKind.ProposalAgent, definition.Kind);
        Assert.AreEqual(AgentExecutionMode.ProposalOnly, definition.ExecutionMode);
        CollectionAssert.Contains(definition.Capabilities!.ToArray(), AgentCapability.CreateMemoryProposal);
    }

    [TestMethod]
    public void BoxedAgents_ExplicitlyForbidDangerousCapabilities()
    {
        var critic = AgentDefinitionCatalog.IndependentCriticAgent;
        var memory = AgentDefinitionCatalog.MemoryImprovementAgent;

        foreach (var capability in new[]
        {
            AgentCapability.RunTool,
            AgentCapability.MutateSource,
            AgentCapability.CallExternalSystem,
            AgentCapability.PromoteCollectiveMemory,
            AgentCapability.RepresentHumanApproval,
            AgentCapability.RepresentHumanRejection,
            AgentCapability.RepresentHumanPromotionDecision
        })
        {
            CollectionAssert.Contains(critic.ForbiddenCapabilities!.ToArray(), capability, $"Critic must forbid {capability}.");
            CollectionAssert.Contains(memory.ForbiddenCapabilities!.ToArray(), capability, $"Memory improvement must forbid {capability}.");
        }

        CollectionAssert.Contains(memory.ForbiddenCapabilities!.ToArray(), AgentCapability.BlockExecution);
        CollectionAssert.DoesNotContain(critic.Capabilities!.ToArray(), AgentCapability.RunTool);
        CollectionAssert.DoesNotContain(critic.Capabilities!.ToArray(), AgentCapability.MutateSource);
        CollectionAssert.DoesNotContain(critic.Capabilities!.ToArray(), AgentCapability.PromoteCollectiveMemory);
        CollectionAssert.DoesNotContain(memory.Capabilities!.ToArray(), AgentCapability.RunTool);
        CollectionAssert.DoesNotContain(memory.Capabilities!.ToArray(), AgentCapability.MutateSource);
        CollectionAssert.DoesNotContain(memory.Capabilities!.ToArray(), AgentCapability.PromoteCollectiveMemory);
        CollectionAssert.DoesNotContain(memory.Capabilities!.ToArray(), AgentCapability.BlockExecution);
    }

    [TestMethod]
    public void CriticReviewContracts_Exist()
    {
        Assert.AreEqual(nameof(CriticReviewRequest), typeof(CriticReviewRequest).Name);
        Assert.AreEqual(nameof(CriticReviewResult), typeof(CriticReviewResult).Name);
        Assert.AreEqual(nameof(CriticFinding), typeof(CriticFinding).Name);
        Assert.AreEqual(nameof(CriticSeverity), typeof(CriticSeverity).Name);
        Assert.AreEqual(nameof(CriticReviewVerdict), typeof(CriticReviewVerdict).Name);
        Assert.AreEqual(nameof(CriticReviewResultValidator), typeof(CriticReviewResultValidator).Name);
    }

    [TestMethod]
    public void CriticReviewResult_ValidResultPassesAndWarningsStateRecommendationsOnly()
    {
        var result = BuildCriticResult();

        AssertNoIssues(CriticValidator.Validate(result));
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("recommendations only", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("does not grant or deny approval", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("human approval remain separate", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void CriticReviewResult_RecommendBlockRequiresBlockingFinding()
    {
        AssertHasIssue(
            CriticValidator.Validate(BuildCriticResult(verdict: CriticReviewVerdict.RecommendBlock, findingBlocksMerge: false)),
            CriticReviewResultValidator.RecommendBlockRequiresBlockingFinding);
    }

    [TestMethod]
    public void CriticReviewResult_NoObjectionCannotIncludeBlockingFinding()
    {
        AssertHasIssue(
            CriticValidator.Validate(BuildCriticResult(verdict: CriticReviewVerdict.NoObjection, findingBlocksMerge: true)),
            CriticReviewResultValidator.NoObjectionCannotBlockMerge);
    }

    [TestMethod]
    public void CriticReviewResult_CriticalAndHighFindingsRequireRequiredFix()
    {
        foreach (var severity in new[] { CriticSeverity.Critical, CriticSeverity.High })
        {
            AssertHasIssue(
                CriticValidator.Validate(BuildCriticResult(findingSeverity: severity, requiredFix: string.Empty)),
                CriticReviewResultValidator.FindingRequiredFixRequired);
        }
    }

    [TestMethod]
    public void CriticReviewResult_CannotClaimApprovalHumanAuthorityOrMemoryPromotion()
    {
        AssertHasIssue(
            CriticValidator.Validate(BuildCriticResult(problem: "I approved this change.")),
            CriticReviewResultValidator.AuthorityClaimBlocked);
        AssertHasIssue(
            CriticValidator.Validate(BuildCriticResult(problem: "The human approved this.")),
            CriticReviewResultValidator.AuthorityClaimBlocked);
        AssertHasIssue(
            CriticValidator.Validate(BuildCriticResult(problem: "I promoted memory for this.")),
            CriticReviewResultValidator.AuthorityClaimBlocked);
        AssertHasIssue(
            CriticValidator.Validate(BuildCriticResult(problem: "RawPrompt: hidden text.")),
            CriticReviewResultValidator.RawPrivateReasoningBlocked);
    }

    [TestMethod]
    public void MemoryImprovementContracts_Exist()
    {
        Assert.AreEqual(nameof(MemoryImprovementDetectionResult), typeof(MemoryImprovementDetectionResult).Name);
        Assert.AreEqual(nameof(MemoryImprovementPatternFinding), typeof(MemoryImprovementPatternFinding).Name);
        Assert.AreEqual(nameof(MemoryImprovementProposalDraft), typeof(MemoryImprovementProposalDraft).Name);
        Assert.AreEqual(nameof(MemoryImprovementNoProposalReason), typeof(MemoryImprovementNoProposalReason).Name);
        Assert.AreEqual(nameof(MemoryImprovementDetectionResultValidator), typeof(MemoryImprovementDetectionResultValidator).Name);
    }

    [TestMethod]
    public void MemoryImprovementProposalDraft_IsProposalOnlyAndCannotCreateOrPromoteMemory()
    {
        var draft = BuildProposalDraft();

        Assert.IsTrue(draft.IsProposalOnly);
        Assert.IsFalse(draft.CreatesCollectiveMemory);
        Assert.IsFalse(draft.PromotesMemory);
        Assert.IsTrue(draft.RequiresHumanReview);
        AssertNoIssues(MemoryValidator.Validate(BuildDetectionResult(proposalDrafts: [draft])));
    }

    [TestMethod]
    public void MemoryImprovementProposalDraft_RequiresEvidenceAndSafeConstants()
    {
        AssertHasIssue(
            MemoryValidator.Validate(BuildDetectionResult(proposalDrafts: [BuildProposalDraft(evidenceRefs: [])])),
            MemoryImprovementDetectionResultValidator.ProposalEvidenceRequired);
        AssertHasIssue(
            MemoryValidator.Validate(BuildDetectionResult(proposalDrafts: [BuildProposalDraft(isProposalOnly: false)])),
            MemoryImprovementDetectionResultValidator.ProposalOnlyRequired);
        AssertHasIssue(
            MemoryValidator.Validate(BuildDetectionResult(proposalDrafts: [BuildProposalDraft(createsCollectiveMemory: true)])),
            MemoryImprovementDetectionResultValidator.CreatesCollectiveMemoryBlocked);
        AssertHasIssue(
            MemoryValidator.Validate(BuildDetectionResult(proposalDrafts: [BuildProposalDraft(promotesMemory: true)])),
            MemoryImprovementDetectionResultValidator.PromotesMemoryBlocked);
        AssertHasIssue(
            MemoryValidator.Validate(BuildDetectionResult(proposalDrafts: [BuildProposalDraft(requiresHumanReview: false)])),
            MemoryImprovementDetectionResultValidator.HumanReviewRequired);
    }

    [TestMethod]
    public void MemoryImprovementDetectionResult_RejectsInvalidFindingsAndConfidence()
    {
        AssertHasIssue(
            MemoryValidator.Validate(BuildDetectionResult(findings: [BuildPatternFinding(summary: string.Empty)])),
            MemoryImprovementDetectionResultValidator.PatternSummaryRequired);
        AssertHasIssue(
            MemoryValidator.Validate(BuildDetectionResult(findings: [BuildPatternFinding(patternType: (MemoryImprovementPatternType)999)])),
            MemoryImprovementDetectionResultValidator.PatternTypeInvalid);
        AssertHasIssue(
            MemoryValidator.Validate(BuildDetectionResult(findings: [BuildPatternFinding(confidence: 1.5m)])),
            MemoryImprovementDetectionResultValidator.PatternConfidenceInvalid);
    }

    [TestMethod]
    public void MemoryImprovementDetectionResult_CannotClaimAcceptedPromotedOrPolicyApproval()
    {
        AssertHasIssue(
            MemoryValidator.Validate(BuildDetectionResult(findings: [BuildPatternFinding(summary: "This is accepted memory now.")])),
            MemoryImprovementDetectionResultValidator.AuthorityClaimBlocked);
        AssertHasIssue(
            MemoryValidator.Validate(BuildDetectionResult(findings: [BuildPatternFinding(summary: "This promoted memory.")])),
            MemoryImprovementDetectionResultValidator.AuthorityClaimBlocked);
        AssertHasIssue(
            MemoryValidator.Validate(BuildDetectionResult(findings: [BuildPatternFinding(summary: "Policy cleared this action.")])),
            MemoryImprovementDetectionResultValidator.AuthorityClaimBlocked);
        AssertHasIssue(
            MemoryValidator.Validate(BuildDetectionResult(findings: [BuildPatternFinding(summary: "ChainOfThought: hidden.")])),
            MemoryImprovementDetectionResultValidator.RawPrivateReasoningBlocked);
    }

    [TestMethod]
    public void MemoryImprovementDetectionResult_NoProposalReasonCanBeUsedInsteadOfDrafts()
    {
        var result = BuildDetectionResult(
            proposalDrafts: [],
            noProposalReason: MemoryImprovementNoProposalReason.InsufficientEvidence);

        AssertNoIssues(MemoryValidator.Validate(result));
        Assert.AreEqual(MemoryImprovementNoProposalReason.InsufficientEvidence, result.NoProposalReason);
    }

    [TestMethod]
    public void BoxedAgentBoundary_DoesNotAddRuntimeSqlWeaviatePromotionPersistenceOrAgentWiring()
    {
        var repositoryRoot = FindRepositoryRoot();
        var productionFiles = Directory
            .EnumerateFiles(repositoryRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.Combine("bin"), StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains(Path.Combine("obj"), StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains(Path.Combine("IronDev.IntegrationTests"), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var newProductionFiles = productionFiles
            .Where(file => file.EndsWith(Path.Combine("IronDev.Core", "Agents", "AgentDefinitionCatalog.cs"), StringComparison.OrdinalIgnoreCase) ||
                           file.Contains(Path.Combine("IronDev.Core", "Agents", "Concrete"), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var forbiddenTokens = new[]
        {
            "IAgentRuntime",
            "AgentRuntime",
            "AgentScheduler",
            "AgentOrchestrator",
            "AgentPromptRunner",
            "AgentToolRouter",
            "ExecuteAgentAsync",
            "RunAgentAsync",
            "SqlConnection",
            "CREATE TABLE",
            "CREATE PROCEDURE",
            "Weaviate",
            "ICollectiveMemoryPromotionService",
            "SqlCollectiveMemoryPromotionService",
            "usp_",
            "INSERT INTO",
            "UPDATE ",
            "DELETE ",
            "MERGE "
        };

        foreach (var file in newProductionFiles)
        {
            var text = File.ReadAllText(file);
            foreach (var token in forbiddenTokens)
            {
                Assert.IsFalse(text.Contains(token, StringComparison.Ordinal),
                    $"Boxed-agent definition file contains forbidden runtime token '{token}': {file}");
            }
        }

        var runtimeRoots = new[] { "IronDev.Infrastructure", "IronDev.Api", "IronDev.Client", "tools" };
        var runtimeFiles = runtimeRoots
            .Select(root => Path.Combine(repositoryRoot, root))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains(Path.Combine("bin"), StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains(Path.Combine("obj"), StringComparison.OrdinalIgnoreCase));

        foreach (var file in runtimeFiles)
        {
            var text = File.ReadAllText(file);
            Assert.IsFalse(text.Contains("IndependentCriticAgent", StringComparison.Ordinal),
                $"Runtime file references IndependentCriticAgent: {file}");
            Assert.IsFalse(text.Contains("AgentDefinitionCatalog.MemoryImprovementAgent", StringComparison.Ordinal),
                $"Runtime file wires boxed MemoryImprovementAgent catalog definition: {file}");
            Assert.IsFalse(text.Contains("CriticReviewResult", StringComparison.Ordinal),
                $"Runtime file references boxed critic output contract: {file}");
            Assert.IsFalse(text.Contains("MemoryImprovementDetectionResult", StringComparison.Ordinal),
                $"Runtime file references boxed memory-improvement output contract: {file}");
        }
    }

    private static CriticReviewResult BuildCriticResult(
        CriticReviewVerdict verdict = CriticReviewVerdict.RequestChanges,
        CriticSeverity findingSeverity = CriticSeverity.High,
        bool findingBlocksMerge = true,
        string problem = "The plan misses a required evidence check.",
        string requiredFix = "Add the missing evidence validation before merge.") =>
        new()
        {
            ReviewResultId = "critic-result-1",
            ReviewRequestId = "critic-request-1",
            Verdict = verdict,
            Findings =
            [
                new CriticFinding
                {
                    FindingId = "finding-1",
                    Severity = findingSeverity,
                    Title = "Missing evidence validation",
                    Problem = problem,
                    WhyItMatters = "The change could pass review without proving the governed evidence boundary.",
                    RequiredFix = requiredFix,
                    EvidenceRefs = ["evidence-1"],
                    BlocksMerge = findingBlocksMerge,
                    RequiresHumanReview = true
                }
            ],
            ReviewedAt = DateTimeOffset.UtcNow,
            ReviewedByAgentId = "builtin.independent-critic",
            Warnings =
            [
                "Critic findings are recommendations only.",
                "Critic review does not grant or deny approval by itself.",
                "Governance and human approval remain separate."
            ]
        };

    private static MemoryImprovementDetectionResult BuildDetectionResult(
        IReadOnlyList<MemoryImprovementPatternFinding>? findings = null,
        IReadOnlyList<MemoryImprovementProposalDraft>? proposalDrafts = null,
        MemoryImprovementNoProposalReason? noProposalReason = null) =>
        new()
        {
            DetectionResultId = "detection-1",
            Findings = findings ?? [BuildPatternFinding()],
            ProposalDrafts = proposalDrafts ?? [BuildProposalDraft()],
            NoProposalReason = noProposalReason,
            DetectedAt = DateTimeOffset.UtcNow,
            DetectedByAgentId = "builtin.memory-improvement",
            Warnings =
            [
                "MemoryImprovementAgent output is proposal-only.",
                "Proposal drafts do not create accepted memory.",
                "Proposal drafts require governed review before persistence or promotion."
            ]
        };

    private static MemoryImprovementPatternFinding BuildPatternFinding(
        string summary = "Repeated validation blockers point to a missing memory note.",
        MemoryImprovementPatternType patternType = MemoryImprovementPatternType.RepeatedGovernanceBlock,
        decimal confidence = 0.75m) =>
        new()
        {
            PatternFindingId = "pattern-1",
            PatternType = patternType,
            Summary = summary,
            Confidence = confidence,
            EvidenceRefs = ["evidence-1"],
            RelatedMemoryIds = ["memory-1"],
            RequiresHumanReview = true
        };

    private static MemoryImprovementProposalDraft BuildProposalDraft(
        IReadOnlyList<string>? evidenceRefs = null,
        bool isProposalOnly = true,
        bool createsCollectiveMemory = false,
        bool promotesMemory = false,
        bool requiresHumanReview = true) =>
        new()
        {
            ProposalDraftId = "proposal-draft-1",
            Title = "Document repeated validation blocker",
            Summary = "Draft a reviewed memory-improvement proposal for the repeated blocker.",
            Rationale = "Multiple evidence refs show the same issue recurring.",
            SourcePattern = BuildPatternFinding(),
            EvidenceRefs = evidenceRefs ?? ["evidence-1"],
            IsProposalOnly = isProposalOnly,
            CreatesCollectiveMemory = createsCollectiveMemory,
            PromotesMemory = promotesMemory,
            RequiresHumanReview = requiresHumanReview
        };

    private static void AssertHasIssue(IReadOnlyList<AgentDefinitionValidationIssue> issues, string code)
    {
        Assert.IsTrue(
            issues.Any(issue => string.Equals(issue.Code, code, StringComparison.Ordinal)),
            $"Expected validation issue '{code}' but got: {string.Join(", ", issues.Select(issue => issue.Code))}");
    }

    private static void AssertNoIssues(IReadOnlyList<AgentDefinitionValidationIssue> issues)
    {
        Assert.AreEqual(
            0,
            issues.Count,
            $"Expected no validation issues but got: {string.Join(", ", issues.Select(issue => $"{issue.Code}:{issue.Message}"))}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "IronDev.Core")) &&
                Directory.Exists(Path.Combine(directory.FullName, "IronDev.Infrastructure")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AIDeveloper repository root.");
    }
}
