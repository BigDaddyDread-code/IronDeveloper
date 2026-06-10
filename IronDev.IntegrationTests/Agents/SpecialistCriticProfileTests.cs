using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IronDev.Core.Agents;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class SpecialistCriticProfileTests
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
        "CriticReviewResultValidator",
        "AgentSpecialisationValidator"
    ];

    [TestMethod]
    public void AgentSpecialisationCatalog_ExposesCriticProfilesOnly()
    {
        Assert.IsNotNull(typeof(AgentSpecialisationCatalog));
        Assert.IsNotNull(AgentSpecialisationCatalog.CriticProfiles);
        Assert.IsNotNull(AgentSpecialisationCatalog.All);

        CollectionAssert.AreEquivalent(
            AgentSpecialisationCatalog.CriticProfiles.Select(profile => profile.SpecialisationId).ToArray(),
            AgentSpecialisationCatalog.All.Select(profile => profile.SpecialisationId).ToArray());

        Assert.AreEqual(5, AgentSpecialisationCatalog.CriticProfiles.Count);
        Assert.AreEqual(5, AgentSpecialisationCatalog.All.Count);
        Assert.IsFalse(AgentSpecialisationCatalog.All.Any(profile =>
            profile.Kind == AgentSpecialisationKind.MemoryImprovementDetection));
        Assert.IsFalse(AgentSpecialisationCatalog.All.Any(profile =>
            profile.RequiredAgentKind is AgentKind.ImplementationAgent or AgentKind.TestingAgent));
    }

    [TestMethod]
    public void AgentSpecialisationCatalog_LookupsReturnExpectedProfiles()
    {
        foreach (var profile in AgentSpecialisationCatalog.CriticProfiles)
        {
            Assert.AreSame(profile, AgentSpecialisationCatalog.GetById(profile.SpecialisationId));
        }

        var criticProfiles = AgentSpecialisationCatalog.GetForAgent(AgentDefinitionCatalog.IndependentCriticAgent.AgentId);
        CollectionAssert.AreEquivalent(
            AgentSpecialisationCatalog.CriticProfiles.Select(profile => profile.SpecialisationId).ToArray(),
            criticProfiles.Select(profile => profile.SpecialisationId).ToArray());

        var criticalReviewProfiles = AgentSpecialisationCatalog.GetByKind(AgentSpecialisationKind.CriticalReview);
        CollectionAssert.AreEquivalent(
            AgentSpecialisationCatalog.CriticProfiles.Select(profile => profile.SpecialisationId).ToArray(),
            criticalReviewProfiles.Select(profile => profile.SpecialisationId).ToArray());

        Assert.IsNull(AgentSpecialisationCatalog.GetById("missing.profile"));
        Assert.AreEqual(0, AgentSpecialisationCatalog.GetForAgent(AgentDefinitionCatalog.MemoryImprovementAgent.AgentId).Count);
        Assert.AreEqual(0, AgentSpecialisationCatalog.GetByKind(AgentSpecialisationKind.MemoryImprovementDetection).Count);
    }

    [TestMethod]
    public void SpecialistCriticProfiles_ExistWithStableBuiltinIds()
    {
        var expectedIds = new[]
        {
            "builtin.critic.code-review",
            "builtin.critic.architecture-review",
            "builtin.critic.security-review",
            "builtin.critic.test-failure-review",
            "builtin.critic.build-failure-review"
        };

        CollectionAssert.AreEquivalent(
            expectedIds,
            AgentSpecialisationCatalog.CriticProfiles.Select(profile => profile.SpecialisationId).ToArray());

        Assert.AreSame(AgentSpecialisationCatalog.CodeReviewCritic, AgentSpecialisationCatalog.GetById("builtin.critic.code-review"));
        Assert.AreSame(AgentSpecialisationCatalog.ArchitectureCritic, AgentSpecialisationCatalog.GetById("builtin.critic.architecture-review"));
        Assert.AreSame(AgentSpecialisationCatalog.SecurityCritic, AgentSpecialisationCatalog.GetById("builtin.critic.security-review"));
        Assert.AreSame(AgentSpecialisationCatalog.TestFailureCritic, AgentSpecialisationCatalog.GetById("builtin.critic.test-failure-review"));
        Assert.AreSame(AgentSpecialisationCatalog.BuildFailureCritic, AgentSpecialisationCatalog.GetById("builtin.critic.build-failure-review"));
    }

    [TestMethod]
    public void SpecialistCriticProfiles_ApplyOnlyToIndependentCriticAgent()
    {
        foreach (var profile in AgentSpecialisationCatalog.CriticProfiles)
        {
            Assert.AreEqual(AgentDefinitionCatalog.IndependentCriticAgent.AgentId, profile.AppliesToAgentId);
            Assert.AreEqual(AgentKind.ReviewAgent, profile.RequiredAgentKind);
            Assert.AreEqual(AgentExecutionMode.OutOfBandReviewOnly, profile.RequiredExecutionMode);
            Assert.AreEqual(AgentSpecialisationKind.CriticalReview, profile.Kind);
        }
    }

    [TestMethod]
    public void SpecialistCriticProfiles_PassSpecialisationValidation()
    {
        foreach (var profile in AgentSpecialisationCatalog.CriticProfiles)
        {
            var issues = Validator.Validate(profile);

            AssertNoErrors(issues, profile.SpecialisationId);
        }
    }

    [TestMethod]
    public void SpecialistCriticProfiles_AreCompatibleWithIndependentCriticAgent()
    {
        foreach (var profile in AgentSpecialisationCatalog.CriticProfiles)
        {
            var result = Validator.ValidateCompatibility(
                AgentDefinitionCatalog.IndependentCriticAgent,
                profile);

            Assert.IsTrue(result.IsCompatible, $"{profile.SpecialisationId}: {FormatIssues(result.Issues)}");
            AssertNoErrors(result.Issues, profile.SpecialisationId);
        }
    }

    [TestMethod]
    public void SpecialistCriticProfiles_RequireReviewOnlyCriticOutputAndHumanReview()
    {
        foreach (var profile in AgentSpecialisationCatalog.CriticProfiles)
        {
            var output = AssertSingleCriticOutput(profile);

            Assert.IsTrue(output.MustBeReviewOnly, profile.SpecialisationId);
            Assert.IsTrue(output.RequiresHumanReview, profile.SpecialisationId);
            Assert.IsFalse(output.MayCreateAuthority, profile.SpecialisationId);
            Assert.IsFalse(output.MayCreateRuntimeAction, profile.SpecialisationId);
            Assert.IsFalse(output.MayPromoteMemory, profile.SpecialisationId);
        }
    }

    [TestMethod]
    public void SpecialistCriticProfiles_IncludeCommonValidators()
    {
        foreach (var profile in AgentSpecialisationCatalog.CriticProfiles)
        {
            var validators = profile.ValidationRequirements.Select(requirement => requirement.ValidatorName).ToHashSet();

            foreach (var validator in CommonValidators)
            {
                Assert.IsTrue(validators.Contains(validator), $"{profile.SpecialisationId} missing {validator}.");
            }
        }
    }

    [TestMethod]
    public void SpecialistCriticProfiles_ForbidDangerousCapabilities()
    {
        foreach (var profile in AgentSpecialisationCatalog.CriticProfiles)
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
    public void SpecialistCriticProfiles_DoNotRequireDangerousCapabilities()
    {
        foreach (var profile in AgentSpecialisationCatalog.CriticProfiles)
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
    public void SpecialistCriticProfiles_GrantNoAuthority()
    {
        foreach (var profile in AgentSpecialisationCatalog.CriticProfiles)
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
    public void SpecialistCriticProfiles_RequireExpectedEvidenceFamilies()
    {
        AssertEvidenceContainsAny(AgentSpecialisationCatalog.CodeReviewCritic, "Patch", "PullRequestDiff");
        AssertEvidenceContainsAny(AgentSpecialisationCatalog.CodeReviewCritic, "Ticket", "ImplementationPlan");

        AssertEvidenceContainsAny(AgentSpecialisationCatalog.ArchitectureCritic, "DesignDocument", "ArchitecturePlan");
        AssertEvidenceContainsAny(AgentSpecialisationCatalog.ArchitectureCritic, "DecisionRecord");

        AssertEvidenceContainsAny(AgentSpecialisationCatalog.SecurityCritic, "ThreatModel", "PermissionModel", "PolicyDecision", "GovernanceDecision");

        AssertEvidenceContainsAny(AgentSpecialisationCatalog.TestFailureCritic, "TestReport", "TestFailureLog");

        AssertEvidenceContainsAny(AgentSpecialisationCatalog.BuildFailureCritic, "BuildReport", "CompilerError");
        AssertEvidenceContainsAny(AgentSpecialisationCatalog.BuildFailureCritic, "PackageRestoreLog");
    }

    [TestMethod]
    public void SpecialistCriticProfiles_SecurityMayConsumeAuthorityEvidenceAsWarningsOnly()
    {
        var issues = Validator.Validate(AgentSpecialisationCatalog.SecurityCritic);

        AssertNoErrors(issues, AgentSpecialisationCatalog.SecurityCritic.SpecialisationId);
        Assert.IsTrue(issues.Any(issue =>
            issue.Code == AgentSpecialisationValidator.InputAuthorityConsumptionDeclared &&
            issue.Severity == AgentDefinitionValidator.SeverityWarning));
        Assert.IsTrue(issues.Any(issue =>
            issue.Code == AgentSpecialisationValidator.EvidenceAuthorityConsumptionDeclared &&
            issue.Severity == AgentDefinitionValidator.SeverityWarning));
    }

    [TestMethod]
    public void SpecialistCriticProfiles_NormalEvidenceIsNotMarkedAsAuthority()
    {
        var normalEvidenceTypes = new[]
        {
            "Patch",
            "PullRequestDiff",
            "TestReport",
            "TestFailureLog",
            "BuildReport",
            "CompilerError",
            "PackageRestoreLog"
        };

        foreach (var profile in AgentSpecialisationCatalog.CriticProfiles)
        {
            foreach (var evidence in profile.EvidenceRequirements.Where(evidence =>
                         normalEvidenceTypes.Contains(evidence.EvidenceType)))
            {
                Assert.AreEqual(0, evidence.AllowedAuthorityEvidenceTypes.Count, $"{profile.SpecialisationId}:{evidence.EvidenceType}");
            }
        }
    }

    [TestMethod]
    public void SpecialistCriticProfiles_ContainNoUnsafeTextMarkers()
    {
        foreach (var profile in AgentSpecialisationCatalog.CriticProfiles)
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
    public void SpecialistCriticProfiles_DoNotIntroduceRuntimeOrStorageBoundaries()
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
            "Weaviate",
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
            "SqlMemoryImprovementProposalStore"
        };

        foreach (var token in forbiddenTokens)
        {
            AssertDoesNotContain(text, token);
        }
    }

    [TestMethod]
    public void SpecialistCriticProfiles_DoNotChangeManualOrRuntimeFiles()
    {
        var root = FindRepositoryRoot();
        var runtimeFiles = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}IronDev.Core{Path.DirectorySeparatorChar}Agents{Path.DirectorySeparatorChar}AgentSpecialisationCatalog.cs", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}IronDev.IntegrationTests{Path.DirectorySeparatorChar}Agents{Path.DirectorySeparatorChar}SpecialistCriticProfileTests.cs", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var path in runtimeFiles)
        {
            var text = File.ReadAllText(path);

            AssertDoesNotContain(text, "AgentSpecialisationCatalog.CodeReviewCritic");
            AssertDoesNotContain(text, "AgentSpecialisationCatalog.ArchitectureCritic");
            AssertDoesNotContain(text, "AgentSpecialisationCatalog.SecurityCritic");
            AssertDoesNotContain(text, "AgentSpecialisationCatalog.TestFailureCritic");
            AssertDoesNotContain(text, "AgentSpecialisationCatalog.BuildFailureCritic");
        }
    }

    private static AgentSpecialisationOutputRequirement AssertSingleCriticOutput(AgentSpecialisationDefinition profile)
    {
        var outputs = profile.OutputRequirements.Where(output =>
                string.Equals(output.OutputType, "CriticReviewResult", StringComparison.Ordinal))
            .ToArray();

        Assert.AreEqual(1, outputs.Length, profile.SpecialisationId);
        return outputs[0];
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
