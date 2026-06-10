using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IronDev.Core.Agents;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class SpecialistMemoryImprovementProfileTests
{
    private static readonly AgentSpecialisationValidator Validator = new();

    private static readonly AgentCapability[] DangerousCapabilities =
    [
        AgentCapability.RunTool,
        AgentCapability.MutateSource,
        AgentCapability.CallExternalSystem,
        AgentCapability.PromoteCollectiveMemory,
        AgentCapability.RepresentHumanApproval,
        AgentCapability.RepresentHumanPromotionDecision,
        AgentCapability.BlockExecution
    ];

    private static readonly string[] CommonValidators =
    [
        "AgentDefinitionValidator",
        "AgentRunAuditEnvelopeValidator",
        "ThoughtLedgerSafetyValidator",
        "MemoryImprovementDetectionResultValidator",
        "AgentSpecialisationValidator"
    ];

    private static readonly string[] ProposalOnlyForbiddenBehaviours =
    [
        "PersistMemoryProposal",
        "CreateCollectiveMemory",
        "AcceptCollectiveMemory",
        "WriteWeaviateIndex"
    ];

    [TestMethod]
    public void AgentSpecialisationCatalog_ExposesMemoryImprovementProfiles()
    {
        Assert.IsNotNull(typeof(AgentSpecialisationCatalog));
        Assert.IsNotNull(AgentSpecialisationCatalog.MemoryImprovementProfiles);
        Assert.IsNotNull(AgentSpecialisationCatalog.All);

        Assert.AreEqual(7, AgentSpecialisationCatalog.MemoryImprovementProfiles.Count);
        CollectionAssert.IsSubsetOf(
            AgentSpecialisationCatalog.MemoryImprovementProfiles.Select(profile => profile.SpecialisationId).ToArray(),
            AgentSpecialisationCatalog.All.Select(profile => profile.SpecialisationId).ToArray());

        Assert.IsFalse(AgentSpecialisationCatalog.MemoryImprovementProfiles.Any(profile =>
            profile.Kind == AgentSpecialisationKind.CriticalReview));
        Assert.IsFalse(AgentSpecialisationCatalog.MemoryImprovementProfiles.Any(profile =>
            profile.RequiredAgentKind is AgentKind.ImplementationAgent or AgentKind.TestingAgent));
    }

    [TestMethod]
    public void AgentSpecialisationCatalog_LookupsReturnMemoryImprovementProfiles()
    {
        foreach (var profile in AgentSpecialisationCatalog.MemoryImprovementProfiles)
        {
            Assert.AreSame(profile, AgentSpecialisationCatalog.GetById(profile.SpecialisationId));
        }

        var memoryProfiles = AgentSpecialisationCatalog.GetForAgent(AgentDefinitionCatalog.MemoryImprovementAgent.AgentId);
        CollectionAssert.AreEquivalent(
            AgentSpecialisationCatalog.MemoryImprovementProfiles.Select(profile => profile.SpecialisationId).ToArray(),
            memoryProfiles.Select(profile => profile.SpecialisationId).ToArray());

        var detectionProfiles = AgentSpecialisationCatalog.GetByKind(AgentSpecialisationKind.MemoryImprovementDetection);
        CollectionAssert.AreEquivalent(
            AgentSpecialisationCatalog.MemoryImprovementProfiles.Select(profile => profile.SpecialisationId).ToArray(),
            detectionProfiles.Select(profile => profile.SpecialisationId).ToArray());

        Assert.IsFalse(AgentSpecialisationCatalog.MemoryImprovementProfiles.Any(profile =>
            string.Equals(profile.AppliesToAgentId, AgentDefinitionCatalog.IndependentCriticAgent.AgentId, StringComparison.Ordinal)));
    }

    [TestMethod]
    public void SpecialistMemoryImprovementProfiles_ExistWithStableBuiltinIds()
    {
        var expectedIds = new[]
        {
            "builtin.memory.repeated-failure-mode-detector",
            "builtin.memory.repeated-governance-block-detector",
            "builtin.memory.repeated-manual-correction-detector",
            "builtin.memory.stale-memory-detector",
            "builtin.memory.contradiction-detector",
            "builtin.memory.retrieval-miss-detector",
            "builtin.memory.duplicate-proposal-detector"
        };

        CollectionAssert.AreEquivalent(
            expectedIds,
            AgentSpecialisationCatalog.MemoryImprovementProfiles.Select(profile => profile.SpecialisationId).ToArray());

        Assert.AreSame(AgentSpecialisationCatalog.RepeatedFailureModeDetector, AgentSpecialisationCatalog.GetById("builtin.memory.repeated-failure-mode-detector"));
        Assert.AreSame(AgentSpecialisationCatalog.RepeatedGovernanceBlockDetector, AgentSpecialisationCatalog.GetById("builtin.memory.repeated-governance-block-detector"));
        Assert.AreSame(AgentSpecialisationCatalog.RepeatedManualCorrectionDetector, AgentSpecialisationCatalog.GetById("builtin.memory.repeated-manual-correction-detector"));
        Assert.AreSame(AgentSpecialisationCatalog.StaleMemoryDetector, AgentSpecialisationCatalog.GetById("builtin.memory.stale-memory-detector"));
        Assert.AreSame(AgentSpecialisationCatalog.ContradictionDetector, AgentSpecialisationCatalog.GetById("builtin.memory.contradiction-detector"));
        Assert.AreSame(AgentSpecialisationCatalog.RetrievalMissDetector, AgentSpecialisationCatalog.GetById("builtin.memory.retrieval-miss-detector"));
        Assert.AreSame(AgentSpecialisationCatalog.DuplicateProposalDetector, AgentSpecialisationCatalog.GetById("builtin.memory.duplicate-proposal-detector"));
    }

    [TestMethod]
    public void SpecialistMemoryImprovementProfiles_ApplyOnlyToMemoryImprovementAgent()
    {
        foreach (var profile in AgentSpecialisationCatalog.MemoryImprovementProfiles)
        {
            Assert.AreEqual(AgentDefinitionCatalog.MemoryImprovementAgent.AgentId, profile.AppliesToAgentId);
            Assert.AreEqual(AgentKind.ProposalAgent, profile.RequiredAgentKind);
            Assert.AreEqual(AgentExecutionMode.ProposalOnly, profile.RequiredExecutionMode);
            Assert.AreEqual(AgentSpecialisationKind.MemoryImprovementDetection, profile.Kind);
        }
    }

    [TestMethod]
    public void SpecialistMemoryImprovementProfiles_PassSpecialisationValidation()
    {
        foreach (var profile in AgentSpecialisationCatalog.MemoryImprovementProfiles)
        {
            var issues = Validator.Validate(profile);

            AssertNoErrors(issues, profile.SpecialisationId);
        }
    }

    [TestMethod]
    public void SpecialistMemoryImprovementProfiles_AreCompatibleWithMemoryImprovementAgent()
    {
        foreach (var profile in AgentSpecialisationCatalog.MemoryImprovementProfiles)
        {
            var result = Validator.ValidateCompatibility(
                AgentDefinitionCatalog.MemoryImprovementAgent,
                profile);

            Assert.IsTrue(result.IsCompatible, $"{profile.SpecialisationId}: {FormatIssues(result.Issues)}");
            AssertNoErrors(result.Issues, profile.SpecialisationId);
        }
    }

    [TestMethod]
    public void SpecialistMemoryImprovementProfiles_RequireProposalOnlyOutputsAndHumanReview()
    {
        foreach (var profile in AgentSpecialisationCatalog.MemoryImprovementProfiles)
        {
            var outputs = AssertMemoryImprovementOutputs(profile);

            foreach (var output in outputs)
            {
                Assert.IsTrue(output.MustBeProposalOnly, $"{profile.SpecialisationId}:{output.OutputType}");
                Assert.IsTrue(output.RequiresHumanReview, $"{profile.SpecialisationId}:{output.OutputType}");
                Assert.IsFalse(output.MustBeReviewOnly, $"{profile.SpecialisationId}:{output.OutputType}");
                Assert.IsFalse(output.MayCreateAuthority, $"{profile.SpecialisationId}:{output.OutputType}");
                Assert.IsFalse(output.MayCreateRuntimeAction, $"{profile.SpecialisationId}:{output.OutputType}");
                Assert.IsFalse(output.MayPromoteMemory, $"{profile.SpecialisationId}:{output.OutputType}");
            }
        }
    }

    [TestMethod]
    public void SpecialistMemoryImprovementProfiles_RequireMemoryProposalAndReportCapabilities()
    {
        foreach (var profile in AgentSpecialisationCatalog.MemoryImprovementProfiles)
        {
            Assert.IsTrue(profile.RequiredCapabilities.Contains(AgentCapability.CreateMemoryProposal), profile.SpecialisationId);
            Assert.IsTrue(profile.RequiredCapabilities.Contains(AgentCapability.CreateReport), profile.SpecialisationId);
        }
    }

    [TestMethod]
    public void SpecialistMemoryImprovementProfiles_IncludeCommonValidators()
    {
        foreach (var profile in AgentSpecialisationCatalog.MemoryImprovementProfiles)
        {
            var validators = profile.ValidationRequirements.Select(requirement => requirement.ValidatorName).ToHashSet();

            foreach (var validator in CommonValidators)
            {
                Assert.IsTrue(validators.Contains(validator), $"{profile.SpecialisationId} missing {validator}.");
            }
        }
    }

    [TestMethod]
    public void SpecialistMemoryImprovementProfiles_ForbidDangerousCapabilities()
    {
        foreach (var profile in AgentSpecialisationCatalog.MemoryImprovementProfiles)
        {
            foreach (var capability in DangerousCapabilities)
            {
                Assert.IsTrue(
                    profile.ForbiddenCapabilities.Contains(capability),
                    $"{profile.SpecialisationId} must forbid {capability}.");
            }
        }
    }

    [TestMethod]
    public void SpecialistMemoryImprovementProfiles_DoNotRequireDangerousCapabilities()
    {
        foreach (var profile in AgentSpecialisationCatalog.MemoryImprovementProfiles)
        {
            foreach (var capability in DangerousCapabilities)
            {
                Assert.IsFalse(
                    profile.RequiredCapabilities.Contains(capability),
                    $"{profile.SpecialisationId} must not require {capability}.");
            }
        }
    }

    [TestMethod]
    public void SpecialistMemoryImprovementProfiles_ForbidProposalPersistenceAndMemoryCreationBehaviours()
    {
        foreach (var profile in AgentSpecialisationCatalog.MemoryImprovementProfiles)
        {
            var behaviours = profile.ForbiddenBehaviours
                .Select(behaviour => behaviour.Behaviour)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var behaviour in ProposalOnlyForbiddenBehaviours)
            {
                Assert.IsTrue(behaviours.Contains(behaviour), $"{profile.SpecialisationId} missing forbidden behaviour {behaviour}.");
            }
        }
    }

    [TestMethod]
    public void SpecialistMemoryImprovementProfiles_GrantNoAuthority()
    {
        foreach (var profile in AgentSpecialisationCatalog.MemoryImprovementProfiles)
        {
            var boundary = profile.AuthorityBoundary;

            Assert.IsFalse(boundary.CanGrantApproval, profile.SpecialisationId);
            Assert.IsFalse(boundary.CanRepresentHumanDecision, profile.SpecialisationId);
            Assert.IsFalse(boundary.CanOverridePolicy, profile.SpecialisationId);
            Assert.IsFalse(boundary.CanExecuteTools, profile.SpecialisationId);
            Assert.IsFalse(boundary.CanMutateSource, profile.SpecialisationId);
            Assert.IsFalse(boundary.CanCallExternalSystems, profile.SpecialisationId);
            Assert.IsFalse(boundary.CanPromoteMemory, profile.SpecialisationId);
            Assert.IsFalse(boundary.CanCreateAuthority, profile.SpecialisationId);
            Assert.IsFalse(boundary.CanCreateRuntimeAction, profile.SpecialisationId);
            Assert.IsFalse(boundary.CanWriteMemory, profile.SpecialisationId);
        }
    }

    [TestMethod]
    public void SpecialistMemoryImprovementProfiles_RequireExpectedEvidenceFamilies()
    {
        AssertEvidenceContainsAny(AgentSpecialisationCatalog.RepeatedFailureModeDetector, "AgentRunAuditEnvelope", "RunReport");
        AssertEvidenceContainsAny(AgentSpecialisationCatalog.RepeatedFailureModeDetector, "TestReport", "BuildReport");

        AssertEvidenceContainsAny(AgentSpecialisationCatalog.RepeatedGovernanceBlockDetector, "AgentBoundaryDecision", "MemoryExecutionGateResult", "ConscienceMemoryGovernanceResult");

        AssertEvidenceContainsAny(AgentSpecialisationCatalog.RepeatedManualCorrectionDetector, "HumanCorrection", "HumanInstruction");

        AssertEvidenceContainsAny(AgentSpecialisationCatalog.StaleMemoryDetector, "CollectiveMemoryCandidate", "MemoryItemReference");
        AssertEvidenceContainsAny(AgentSpecialisationCatalog.StaleMemoryDetector, "MemoryInfluenceRecord", "RunReport", "HumanCorrection");

        AssertEvidenceContainsAny(AgentSpecialisationCatalog.ContradictionDetector, "MemoryItemReference", "CollectiveMemoryCandidate", "ConflictingEvidenceReference");

        AssertEvidenceContainsAny(AgentSpecialisationCatalog.RetrievalMissDetector, "RetrievalQuery", "RetrievalResult", "RetrievalCandidate");

        AssertEvidenceContainsAny(AgentSpecialisationCatalog.DuplicateProposalDetector, "MemoryImprovementProposalDraft", "MemoryImprovementProposal", "MemoryImprovementDetectionResult");
    }

    [TestMethod]
    public void SpecialistMemoryImprovementProfiles_ContainNoUnsafeTextMarkers()
    {
        foreach (var profile in AgentSpecialisationCatalog.MemoryImprovementProfiles)
        {
            var issues = Validator.Validate(profile);

            Assert.IsFalse(
                issues.Any(issue =>
                    issue.Code == AgentSpecialisationValidator.RawPrivateReasoningBlocked ||
                    issue.Code == AgentSpecialisationValidator.AuthorityClaimBlocked),
                $"{profile.SpecialisationId}: {FormatIssues(issues)}");
        }
    }

    [TestMethod]
    public void SpecialistMemoryImprovementProfiles_DoNotIntroduceRuntimeOrStorageBoundaries()
    {
        var productionPath = Path.Combine(FindRepositoryRoot(), "IronDev.Core", "Agents", "AgentSpecialisationCatalog.cs");
        var text = File.ReadAllText(productionPath);
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
            "IChatCompletion",
            "OpenAI",
            "Anthropic",
            "Gemini",
            "SqlConnection",
            "CREATE TABLE",
            "CREATE PROCEDURE",
            "WeaviateClient",
            "INSERT INTO",
            "UPDATE ",
            "DELETE ",
            "MERGE ",
            "AddHostedService",
            "IHostedService",
            "BackgroundService",
            "ManualIndependentCriticAgentService",
            "ManualMemoryImprovementAgentService",
            "SqlAgentRunAuditEnvelopeStore",
            "SqlAgentRunAuditEnvelopeReadRepository",
            "ICollectiveMemoryPromotionService",
            "SqlCollectiveMemoryPromotionService",
            "IMemoryImprovementProposalStore",
            "SqlMemoryImprovementProposalStore",
            "CollectiveMemoryPromotion"
        };

        foreach (var token in forbiddenTokens)
        {
            AssertDoesNotContain(text, token);
        }

        foreach (var line in text.Split(Environment.NewLine).Where(line =>
                     line.Contains("PromoteCollectiveMemory", StringComparison.Ordinal)))
        {
            Assert.IsTrue(
                line.Contains("AgentCapability.PromoteCollectiveMemory", StringComparison.Ordinal) ||
                line.Contains("Forbidden(\"PromoteCollectiveMemory\")", StringComparison.Ordinal) ||
                line.Contains("ForbiddenMemory(\"PromoteCollectiveMemory\")", StringComparison.Ordinal),
                $"Unexpected PromoteCollectiveMemory usage: {line}");
        }
    }

    [TestMethod]
    public void SpecialistMemoryImprovementProfiles_DoNotChangeManualOrRuntimeFiles()
    {
        var root = FindRepositoryRoot();
        var profileNames = new[]
        {
            "AgentSpecialisationCatalog.RepeatedFailureModeDetector",
            "AgentSpecialisationCatalog.RepeatedGovernanceBlockDetector",
            "AgentSpecialisationCatalog.RepeatedManualCorrectionDetector",
            "AgentSpecialisationCatalog.StaleMemoryDetector",
            "AgentSpecialisationCatalog.ContradictionDetector",
            "AgentSpecialisationCatalog.RetrievalMissDetector",
            "AgentSpecialisationCatalog.DuplicateProposalDetector"
        };

        var filesToScan = new[]
        {
            Path.Combine(root, "IronDev.Infrastructure"),
            Path.Combine(root, "IronDev.Api"),
            Path.Combine(root, "tools")
        }
        .Where(Directory.Exists)
        .SelectMany(directory => Directory.EnumerateFiles(
            directory,
            "*.cs",
            new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true
            }))
        .Where(path =>
            !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
            !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        .ToArray();

        foreach (var path in filesToScan)
        {
            var text = File.ReadAllText(path);

            foreach (var profileName in profileNames)
            {
                AssertDoesNotContain(text, profileName);
            }
        }
    }

    private static IReadOnlyList<AgentSpecialisationOutputRequirement> AssertMemoryImprovementOutputs(
        AgentSpecialisationDefinition profile)
    {
        var outputs = profile.OutputRequirements.Where(output =>
                string.Equals(output.OutputType, "MemoryImprovementDetectionResult", StringComparison.Ordinal) ||
                string.Equals(output.OutputType, "MemoryImprovementProposalDraft", StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(2, outputs.Length, profile.SpecialisationId);
        return outputs;
    }

    private static void AssertEvidenceContainsAny(AgentSpecialisationDefinition profile, params string[] evidenceTypes)
    {
        var profileEvidenceTypes = profile.EvidenceRequirements
            .Select(evidence => evidence.EvidenceType)
            .ToHashSet(StringComparer.Ordinal);

        Assert.IsTrue(
            evidenceTypes.Any(profileEvidenceTypes.Contains),
            $"{profile.SpecialisationId} missing one of: {string.Join(", ", evidenceTypes)}.");
    }

    private static void AssertNoErrors(
        IReadOnlyCollection<AgentDefinitionValidationIssue> issues,
        string profileId)
    {
        Assert.IsFalse(
            issues.Any(issue => issue.Severity == AgentDefinitionValidator.SeverityError),
            $"{profileId}: {FormatIssues(issues)}");
    }

    private static string FormatIssues(IEnumerable<AgentDefinitionValidationIssue> issues) =>
        string.Join("; ", issues.Select(issue => $"{issue.Severity}:{issue.Code}:{issue.Message}"));

    private static void AssertDoesNotContain(string text, string token)
    {
        Assert.IsFalse(
            text.Contains(token, StringComparison.Ordinal),
            $"Did not expect text to contain '{token}'.");
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

        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
