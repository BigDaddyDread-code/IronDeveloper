using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;
using IronDev.Infrastructure.Services.Agents.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentSkillRegistryTests
{
    [TestMethod]
    public void AgentSkillRegistry_ListsInitialSkills()
    {
        var registry = new StaticAgentSkillRegistry();
        var skillIds = registry.List().Select(skill => skill.SkillId).ToHashSet(StringComparer.Ordinal);

        foreach (var requiredSkillId in RequiredSkillIds)
            CollectionAssert.Contains(skillIds.ToArray(), requiredSkillId, $"Expected registry to contain skill '{requiredSkillId}'.");
    }

    [TestMethod]
    public void AgentSkillRegistry_SkillIdsAreUnique()
    {
        var skills = new StaticAgentSkillRegistry().List();
        var duplicateSkillIds = skills
            .GroupBy(skill => skill.SkillId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateSkillIds.Length > 0)
            Assert.Fail($"Duplicate skill IDs: {string.Join(", ", duplicateSkillIds)}");
    }

    [TestMethod]
    public void AgentSkillRegistry_CategoriesAreKnown()
    {
        var skills = new StaticAgentSkillRegistry().List();

        foreach (var skill in skills)
            CollectionAssert.Contains(AgentSkillCategories.All.ToArray(), skill.Category, $"Unknown category for {skill.SkillId}: {skill.Category}");
    }

    [TestMethod]
    public void AgentSkillRegistry_RiskTiersAreKnown()
    {
        var skills = new StaticAgentSkillRegistry().List();

        foreach (var skill in skills)
            CollectionAssert.Contains(ProjectApprovalRiskTiers.All.ToArray(), skill.RiskTier, $"Unknown risk tier for {skill.SkillId}: {skill.RiskTier}");
    }

    [TestMethod]
    public void AgentSkillRegistry_ReadAndContextSkillsAreNonMutating()
    {
        var registry = new StaticAgentSkillRegistry();

        foreach (var skillId in ReadOnlySkillIds)
        {
            var skill = RequireSkill(registry, skillId);
            Assert.IsFalse(skill.CanExecuteProcess, skillId);
            Assert.IsFalse(skill.CanMutateWorkspace, skillId);
            Assert.IsFalse(skill.CanMutateSource, skillId);
            Assert.IsFalse(skill.CanWriteMemory, skillId);
            Assert.IsFalse(skill.CanCreateTicket, skillId);
            Assert.IsFalse(skill.CanUseExternalSystem, skillId);
        }
    }

    [TestMethod]
    public void AgentSkillRegistry_ApplyCopyIsSourceMutationAndApprovalRequired()
    {
        var skill = RequireSkill(new StaticAgentSkillRegistry(), AgentSkillIds.WorkspaceApplyCopy);

        Assert.AreEqual(ProjectApprovalRiskTiers.SourceMutation, skill.RiskTier);
        Assert.IsTrue(skill.CanMutateSource);
        Assert.IsTrue(skill.RequiresHumanApproval);
        Assert.IsFalse(skill.CanExecuteProcess);
    }

    [TestMethod]
    public void AgentSkillRegistry_GitSkillsAreDangerous()
    {
        var registry = new StaticAgentSkillRegistry();
        var gitCommit = RequireSkill(registry, AgentSkillIds.GitCommit);
        var pullRequest = RequireSkill(registry, AgentSkillIds.GitHubPullRequestCreate);

        Assert.AreEqual(ProjectApprovalRiskTiers.GitOperation, gitCommit.RiskTier);
        Assert.IsTrue(gitCommit.RequiresHumanApproval);
        Assert.AreEqual(ProjectApprovalRiskTiers.GitOperation, pullRequest.RiskTier);
        Assert.IsTrue(pullRequest.RequiresHumanApproval);
        Assert.IsTrue(pullRequest.CanUseExternalSystem);
    }

    [TestMethod]
    public void AgentSkillRegistry_TicketCreateRequiresApproval()
    {
        var skill = RequireSkill(new StaticAgentSkillRegistry(), AgentSkillIds.TicketCreate);

        Assert.AreEqual(ProjectApprovalRiskTiers.TicketWrite, skill.RiskTier);
        Assert.IsTrue(skill.CanCreateTicket);
        Assert.IsTrue(skill.RequiresHumanApproval);
    }

    [TestMethod]
    public void AgentSkillRegistry_FindWorks()
    {
        var registry = new StaticAgentSkillRegistry();

        Assert.IsNotNull(registry.Find(AgentSkillIds.WorkspaceReadApplyContext));
        Assert.IsNull(registry.Find("missing.skill"));
    }

    [TestMethod]
    public void AgentSkillRegistry_IsMetadataOnly()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "IronDev.Infrastructure",
            "Services",
            "Agents",
            "Skills",
            "StaticAgentSkillRegistry.cs"));

        Assert.IsFalse(source.Contains("File.Copy", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("File.Delete", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("Process.Start", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IDisposableWorkspaceApplyCopyService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IDisposableWorkspaceCommandService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains(" Run(", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains(".Run(", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains(" Execute(", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains(".Execute(", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains(" Invoke(", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains(".Invoke(", StringComparison.Ordinal));
    }

    private static AgentSkillDefinition RequireSkill(IAgentSkillRegistry registry, string skillId) =>
        registry.Find(skillId) ?? throw new AssertFailedException($"Expected skill '{skillId}'.");

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

    private static readonly IReadOnlyList<string> RequiredSkillIds =
    [
        AgentSkillIds.WorkspaceReadApplyContext,
        AgentSkillIds.WorkspaceRecommendApplyAction,
        AgentSkillIds.WorkspaceCreateActionRequest,
        AgentSkillIds.WorkspaceCreateActionReview,
        AgentSkillIds.WorkspaceEvaluatePolicyContext,
        AgentSkillIds.WorkspaceCheck,
        AgentSkillIds.WorkspacePrepare,
        AgentSkillIds.WorkspaceValidate,
        AgentSkillIds.WorkspaceDiff,
        AgentSkillIds.WorkspacePromotionPackage,
        AgentSkillIds.WorkspaceFailurePackage,
        AgentSkillIds.WorkspaceSourceReport,
        AgentSkillIds.WorkspaceApplyCopy,
        AgentSkillIds.MemorySearch,
        AgentSkillIds.TicketCreate,
        AgentSkillIds.GitCommit,
        AgentSkillIds.GitHubPullRequestCreate
    ];

    private static readonly IReadOnlyList<string> ReadOnlySkillIds =
    [
        AgentSkillIds.WorkspaceReadApplyContext,
        AgentSkillIds.WorkspaceReadSourceReport,
        AgentSkillIds.WorkspaceReadFailurePackage,
        AgentSkillIds.WorkspaceRecommendApplyAction,
        AgentSkillIds.WorkspaceCreateActionRequest,
        AgentSkillIds.WorkspaceCreateActionReview,
        AgentSkillIds.WorkspaceEvaluatePolicyContext,
        AgentSkillIds.MemorySearch
    ];

    [TestMethod]
    public void AgentSkillRegistry_DiffAndPromotionPackageAreWorkspaceLocalOnly()
    {
        var registry = new StaticAgentSkillRegistry();
        var diff = RequireSkill(registry, AgentSkillIds.WorkspaceDiff);
        var package = RequireSkill(registry, AgentSkillIds.WorkspacePromotionPackage);

        Assert.AreEqual(ProjectApprovalRiskTiers.WorkspaceReporting, diff.RiskTier);
        Assert.IsTrue(diff.CanMutateWorkspace);
        Assert.IsFalse(diff.CanMutateSource);
        Assert.IsFalse(diff.CanExecuteProcess);

        Assert.AreEqual(ProjectApprovalRiskTiers.WorkspacePackaging, package.RiskTier);
        Assert.IsTrue(package.CanReadEvidence);
        Assert.IsTrue(package.CanMutateWorkspace);
        Assert.IsFalse(package.CanMutateSource);
        Assert.IsFalse(package.CanExecuteProcess);
        Assert.IsTrue(package.RequiresHumanApproval);
    }
}
