using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;
using IronDev.Infrastructure.Services.Agents.ApprovalPolicy;
using IronDev.Infrastructure.Services.Agents.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentSkillPolicyEvaluatorTests
{
    [TestMethod]
    public void AgentSkillPolicy_UnknownSkill_Blocks()
    {
        var result = BuildEvaluator().Evaluate(BuildRequest("missing.skill"));

        Assert.AreEqual(ProjectApprovalDecisions.BlockedByPolicy, result.Decision);
        Assert.IsFalse(result.SkillKnown);
        Assert.IsFalse(result.SkillExecutionAllowedByPolicy);
        Assert.IsFalse(result.AutomaticExecutionAllowed);
        Assert.IsTrue(result.Warnings.Any(item => item.Contains("unknown skill", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void AgentSkillPolicy_ReadApplyContext_AllowedByDefault()
    {
        var result = BuildEvaluator().Evaluate(BuildRequest(AgentSkillIds.WorkspaceReadApplyContext));

        Assert.AreEqual(ProjectApprovalDecisions.AllowedByPolicy, result.Decision);
        Assert.IsTrue(result.SkillKnown);
        Assert.AreEqual(ProjectApprovalRiskTiers.WorkspaceReporting, result.RiskTier);
        Assert.IsTrue(result.SkillExecutionAllowedByPolicy);
        Assert.IsTrue(result.AutomaticExecutionAllowed);
        Assert.IsFalse(result.SourceMutationAllowed);
        Assert.IsFalse(result.WorkspaceMutationAllowed);
    }

    [TestMethod]
    public void AgentSkillPolicy_RecommendApplyAction_AllowedByDefault()
    {
        var result = BuildEvaluator().Evaluate(BuildRequest(AgentSkillIds.WorkspaceRecommendApplyAction));

        Assert.AreEqual(ProjectApprovalRiskTiers.WorkspaceIntent, result.RiskTier);
        Assert.AreEqual(ProjectApprovalDecisions.AllowedByPolicy, result.Decision);
        Assert.IsTrue(result.SkillExecutionAllowedByPolicy);
        Assert.IsTrue(result.AutomaticExecutionAllowed);
    }

    [TestMethod]
    public void AgentSkillPolicy_CustomPolicyBlocksReadSkill()
    {
        var policy = PolicyWithRule(
            ProjectApprovalRiskTiers.WorkspaceReporting,
            AgentSkillIds.WorkspaceReadApplyContext,
            ProjectApprovalModes.AlwaysBlock);

        var result = BuildEvaluator().Evaluate(BuildRequest(AgentSkillIds.WorkspaceReadApplyContext, policy));

        Assert.AreEqual(ProjectApprovalDecisions.BlockedByPolicy, result.Decision);
        Assert.IsFalse(result.SkillExecutionAllowedByPolicy);
        Assert.IsFalse(result.AutomaticExecutionAllowed);
    }

    [TestMethod]
    public void AgentSkillPolicy_WorkspaceValidate_RequiresApprovalEvenIfPolicyAllows()
    {
        var policy = PolicyWithRule(
            ProjectApprovalRiskTiers.WorkspaceValidation,
            AgentSkillIds.WorkspaceValidate,
            ProjectApprovalModes.AlwaysAllow);

        var result = BuildEvaluator().Evaluate(BuildRequest(AgentSkillIds.WorkspaceValidate, policy));

        Assert.AreEqual(ProjectApprovalDecisions.ApprovalRequired, result.Decision);
        Assert.IsTrue(result.HumanApprovalRequired);
        Assert.IsFalse(result.SkillExecutionAllowedByPolicy);
        Assert.IsFalse(result.AutomaticExecutionAllowed);
        Assert.IsTrue(
            result.Reason.Contains("process execution", StringComparison.OrdinalIgnoreCase) ||
            result.Warnings.Any(item => item.Contains("process execution", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void AgentSkillPolicy_WorkspacePrepare_AllowsGovernedWorkspaceMutationWhenPolicyAllows()
    {
        var policy = PolicyWithRule(
            ProjectApprovalRiskTiers.WorkspacePreparation,
            AgentSkillIds.WorkspacePrepare,
            ProjectApprovalModes.AlwaysAllow);

        var result = BuildEvaluator().Evaluate(BuildRequest(AgentSkillIds.WorkspacePrepare, policy));

        Assert.AreEqual(ProjectApprovalDecisions.AllowedByPolicy, result.Decision);
        Assert.IsTrue(result.WorkspaceMutationAllowed);
        Assert.IsTrue(result.SkillExecutionAllowedByPolicy);
        Assert.IsFalse(result.AutomaticExecutionAllowed);
        Assert.IsFalse(result.SourceMutationAllowed);
    }

    [TestMethod]
    public void AgentSkillPolicy_ApplyCopy_CannotAutoAllow()
    {
        var policy = PolicyWithRule(
            ProjectApprovalRiskTiers.SourceMutation,
            AgentSkillIds.WorkspaceApplyCopy,
            ProjectApprovalModes.AlwaysAllow);

        var result = BuildEvaluator().Evaluate(BuildRequest(AgentSkillIds.WorkspaceApplyCopy, policy));

        Assert.AreEqual(ProjectApprovalDecisions.ApprovalRequired, result.Decision);
        Assert.IsTrue(result.HumanApprovalRequired);
        Assert.IsFalse(result.SourceMutationAllowed);
        Assert.IsFalse(result.SkillExecutionAllowedByPolicy);
        Assert.IsFalse(result.AutomaticExecutionAllowed);
    }

    [TestMethod]
    public void AgentSkillPolicy_GitCommit_DefaultPolicyBlocks()
    {
        var result = BuildEvaluator().Evaluate(BuildRequest(AgentSkillIds.GitCommit));

        Assert.AreEqual(ProjectApprovalDecisions.BlockedByPolicy, result.Decision);
        Assert.IsFalse(result.SkillExecutionAllowedByPolicy);
        Assert.IsFalse(result.AutomaticExecutionAllowed);
        Assert.IsFalse(result.SourceMutationAllowed);
    }

    [TestMethod]
    public void AgentSkillPolicy_GitCommit_CustomAllowDowngradesToApprovalRequired()
    {
        var policy = PolicyWithRule(
            ProjectApprovalRiskTiers.GitOperation,
            AgentSkillIds.GitCommit,
            ProjectApprovalModes.AlwaysAllow);

        var result = BuildEvaluator().Evaluate(BuildRequest(AgentSkillIds.GitCommit, policy));

        Assert.AreEqual(ProjectApprovalDecisions.ApprovalRequired, result.Decision);
        Assert.IsTrue(result.HumanApprovalRequired);
        Assert.IsFalse(result.SkillExecutionAllowedByPolicy);
        Assert.IsFalse(result.AutomaticExecutionAllowed);
        Assert.IsFalse(result.SourceMutationAllowed);
    }

    [TestMethod]
    public void AgentSkillPolicy_TicketCreate_RequiresApproval()
    {
        var policy = PolicyWithRule(
            ProjectApprovalRiskTiers.TicketWrite,
            AgentSkillIds.TicketCreate,
            ProjectApprovalModes.AlwaysAllow);

        var result = BuildEvaluator().Evaluate(BuildRequest(AgentSkillIds.TicketCreate, policy));

        Assert.IsTrue(result.HumanApprovalRequired);
        Assert.IsFalse(result.CreatesTicketAllowed);
        Assert.IsFalse(result.SkillExecutionAllowedByPolicy);
    }

    [TestMethod]
    public void AgentSkillPolicy_GitHubPullRequestCreate_ExternalSystemNotAllowed()
    {
        var policy = PolicyWithRule(
            ProjectApprovalRiskTiers.GitOperation,
            AgentSkillIds.GitHubPullRequestCreate,
            ProjectApprovalModes.AlwaysAllow);

        var result = BuildEvaluator().Evaluate(BuildRequest(AgentSkillIds.GitHubPullRequestCreate, policy));

        Assert.IsFalse(result.ExternalSystemAllowed);
        Assert.IsFalse(result.SkillExecutionAllowedByPolicy);
        Assert.IsFalse(result.AutomaticExecutionAllowed);
        Assert.IsTrue(result.HumanApprovalRequired || string.Equals(result.Decision, ProjectApprovalDecisions.BlockedByPolicy, StringComparison.Ordinal));
    }

    [TestMethod]
    public void AgentSkillPolicyEvaluator_HasNoExecutionOrMutationDependencies()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "IronDev.Infrastructure",
            "Services",
            "Agents",
            "Skills",
            "AgentSkillPolicyEvaluator.cs"));

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

    [TestMethod]
    public void AgentSkillPolicy_NoAgentsAreWiredToExecuteSkills()
    {
        var agentsDirectory = Path.Combine(FindRepositoryRoot(), "IronDev.Infrastructure", "Services", "Agents");
        var agentSources = Directory
            .EnumerateFiles(agentsDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.IsFalse(agentSources.Any(source => source.Contains("IAgentSkillPolicyEvaluator", StringComparison.Ordinal)));
    }

    private static AgentSkillPolicyEvaluator BuildEvaluator() =>
        new(new StaticAgentSkillRegistry(), new ProjectApprovalPolicyEvaluator());

    private static AgentSkillPolicyEvaluationRequest BuildRequest(
        string skillId,
        ProjectApprovalPolicy? policy = null) =>
        new()
        {
            ProjectId = "IronDev",
            SkillId = skillId,
            Policy = policy ?? ProjectApprovalPolicy.CreateDefault("IronDev"),
            RunId = "run-1",
            WorkspacePath = "C:\\workspaces\\run-1",
            SourceRepo = "C:\\repo\\IronDeveloper"
        };

    private static ProjectApprovalPolicy PolicyWithRule(
        string riskTier,
        string requestedAction,
        string mode) =>
        ProjectApprovalPolicy.CreateDefault("IronDev") with
        {
            Rules =
            [
                new ProjectApprovalPolicyRule
                {
                    RiskTier = riskTier,
                    ActionType = AgentSkillPolicyActionTypes.AgentSkill,
                    RequestedAction = requestedAction,
                    Mode = mode
                }
            ]
        };

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
