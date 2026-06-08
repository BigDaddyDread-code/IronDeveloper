using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;
using IronDev.Infrastructure.Services.Agents.ApprovalPolicy;
using IronDev.Infrastructure.Services.Agents.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentSkillRequestServiceTests
{
    [TestMethod]
    public void AgentSkillRequest_ReadOnlySkill_IsAllowedButNonExecutable()
    {
        var package = BuildService().Create(BuildInput(AgentSkillIds.WorkspaceReadApplyContext));

        Assert.AreEqual(ProjectApprovalDecisions.AllowedByPolicy, package.Decision);
        Assert.IsTrue(package.SkillKnown);
        Assert.IsTrue(package.SkillExecutionAllowedByPolicy);
        Assert.IsTrue(package.AutomaticExecutionAllowedByPolicy);
        Assert.IsFalse(package.ExecutionCanStartFromRequest);
        Assert.IsFalse(package.ApprovalCanBeGrantedByRequest);
        Assert.IsFalse(package.SourceMutationAllowed);
    }

    [TestMethod]
    public void AgentSkillRequest_RecommendActionSkill_IsAllowedButNonExecutable()
    {
        var package = BuildService().Create(BuildInput(AgentSkillIds.WorkspaceRecommendApplyAction));

        Assert.AreEqual(ProjectApprovalDecisions.AllowedByPolicy, package.Decision);
        Assert.AreEqual(ProjectApprovalRiskTiers.WorkspaceIntent, package.RiskTier);
        Assert.IsFalse(package.ExecutionCanStartFromRequest);
    }

    [TestMethod]
    public void AgentSkillRequest_UnknownSkill_Blocks()
    {
        var package = BuildService().Create(BuildInput("missing.skill"));

        Assert.AreEqual(ProjectApprovalDecisions.BlockedByPolicy, package.Decision);
        Assert.IsFalse(package.SkillKnown);
        Assert.IsFalse(package.ExecutionCanStartFromRequest);
        Assert.IsFalse(package.ApprovalCanBeGrantedByRequest);
        Assert.IsTrue(package.Warnings.Any(item => item.Contains("unknown skill", StringComparison.OrdinalIgnoreCase)));
        AssertChecklistContains(package, "Do not execute unknown skills.");
    }

    [TestMethod]
    public void AgentSkillRequest_CustomPolicyBlock_ProducesBlockedRequest()
    {
        var policy = PolicyWithRule(
            ProjectApprovalRiskTiers.WorkspaceReporting,
            AgentSkillIds.WorkspaceReadApplyContext,
            ProjectApprovalModes.AlwaysBlock);

        var package = BuildService().Create(BuildInput(AgentSkillIds.WorkspaceReadApplyContext, policy));

        Assert.AreEqual(ProjectApprovalDecisions.BlockedByPolicy, package.Decision);
        Assert.IsFalse(package.ExecutionCanStartFromRequest);
        AssertChecklistContains(package, "Do not execute this skill.");
    }

    [TestMethod]
    public void AgentSkillRequest_WorkspaceValidate_RequiresApprovalAndCannotExecute()
    {
        var policy = PolicyWithRule(
            ProjectApprovalRiskTiers.WorkspaceValidation,
            AgentSkillIds.WorkspaceValidate,
            ProjectApprovalModes.AlwaysAllow);

        var package = BuildService().Create(BuildInput(AgentSkillIds.WorkspaceValidate, policy));

        Assert.AreEqual(ProjectApprovalDecisions.ApprovalRequired, package.Decision);
        Assert.IsTrue(package.HumanApprovalRequired);
        Assert.IsFalse(package.SkillExecutionAllowedByPolicy);
        Assert.IsFalse(package.AutomaticExecutionAllowedByPolicy);
        Assert.IsFalse(package.ExecutionCanStartFromRequest);
        Assert.IsFalse(package.ApprovalCanBeGrantedByRequest);
    }

    [TestMethod]
    public void AgentSkillRequest_WorkspacePrepare_CannotMutateWorkspace()
    {
        var policy = PolicyWithRule(
            ProjectApprovalRiskTiers.WorkspacePreparation,
            AgentSkillIds.WorkspacePrepare,
            ProjectApprovalModes.AlwaysAllow);

        var package = BuildService().Create(BuildInput(AgentSkillIds.WorkspacePrepare, policy));

        Assert.IsFalse(package.WorkspaceMutationAllowed);
        Assert.IsFalse(package.ExecutionCanStartFromRequest);
        AssertChecklistContains(package, "Workspace mutation is not allowed from this request package.");
    }

    [TestMethod]
    public void AgentSkillRequest_ApplyCopy_CannotMutateSource()
    {
        var policy = PolicyWithRule(
            ProjectApprovalRiskTiers.SourceMutation,
            AgentSkillIds.WorkspaceApplyCopy,
            ProjectApprovalModes.AlwaysAllow);

        var package = BuildService().Create(BuildInput(AgentSkillIds.WorkspaceApplyCopy, policy));

        Assert.AreEqual(ProjectApprovalDecisions.ApprovalRequired, package.Decision);
        Assert.IsFalse(package.SourceMutationAllowed);
        Assert.IsFalse(package.ExecutionCanStartFromRequest);
        Assert.IsFalse(package.ApprovalCanBeGrantedByRequest);
        AssertChecklistContains(package, "Source mutation is not allowed from this request package.");
    }

    [TestMethod]
    public void AgentSkillRequest_GitCommit_CannotExecute()
    {
        var package = BuildService().Create(BuildInput(AgentSkillIds.GitCommit));

        Assert.IsTrue(
            string.Equals(package.Decision, ProjectApprovalDecisions.BlockedByPolicy, StringComparison.Ordinal) ||
            string.Equals(package.Decision, ProjectApprovalDecisions.ApprovalRequired, StringComparison.Ordinal));
        Assert.IsFalse(package.ExecutionCanStartFromRequest);
        Assert.IsFalse(package.SourceMutationAllowed);
        AssertChecklistContains(package, "Git operations are not allowed from this request package.");
    }

    [TestMethod]
    public void AgentSkillRequest_TicketCreate_CannotCreateTicket()
    {
        var policy = PolicyWithRule(
            ProjectApprovalRiskTiers.TicketWrite,
            AgentSkillIds.TicketCreate,
            ProjectApprovalModes.AlwaysAllow);

        var package = BuildService().Create(BuildInput(AgentSkillIds.TicketCreate, policy));

        Assert.IsFalse(package.CreatesTicketAllowed);
        Assert.IsFalse(package.ExecutionCanStartFromRequest);
        AssertChecklistContains(package, "Ticket creation is not allowed from this request package.");
    }

    [TestMethod]
    public void AgentSkillRequest_GitHubPullRequestCreate_CannotUseExternalSystem()
    {
        var policy = PolicyWithRule(
            ProjectApprovalRiskTiers.GitOperation,
            AgentSkillIds.GitHubPullRequestCreate,
            ProjectApprovalModes.AlwaysAllow);

        var package = BuildService().Create(BuildInput(AgentSkillIds.GitHubPullRequestCreate, policy));

        Assert.IsFalse(package.ExternalSystemAllowed);
        Assert.IsFalse(package.ExecutionCanStartFromRequest);
        AssertChecklistContains(package, "External system access is not allowed from this request package.");
    }

    [TestMethod]
    public void AgentSkillRequest_PreservesEvidencePathsAndParameters()
    {
        var input = BuildInput(AgentSkillIds.WorkspaceReadApplyContext) with
        {
            EvidencePaths = ["a.json", "b.json"],
            ParametersSummary = ["runId=123", "workspacePath=C:\\x"]
        };

        var package = BuildService().Create(input);

        CollectionAssert.AreEqual(input.EvidencePaths.ToArray(), package.EvidencePaths.ToArray());
        CollectionAssert.AreEqual(input.ParametersSummary.ToArray(), package.ParametersSummary.ToArray());
    }

    [TestMethod]
    public void AgentSkillRequest_RequestIdIsStable()
    {
        var input = BuildInput(AgentSkillIds.WorkspaceReadApplyContext);
        var service = BuildService();

        var first = service.Create(input);
        var second = service.Create(input);
        var differentSkill = service.Create(input with { SkillId = AgentSkillIds.WorkspaceRecommendApplyAction });
        var differentRun = service.Create(input with { RunId = "run-2" });

        Assert.AreEqual(first.RequestId, second.RequestId);
        Assert.AreNotEqual(first.RequestId, differentSkill.RequestId);
        Assert.AreNotEqual(first.RequestId, differentRun.RequestId);
    }

    [TestMethod]
    public void AgentSkillRequestService_HasNoExecutionOrMutationDependencies()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "IronDev.Infrastructure",
            "Services",
            "Agents",
            "Skills",
            "AgentSkillRequestService.cs"));

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
    public void AgentSkillRequest_NoAgentsAreWiredToSkillRequests()
    {
        var agentsDirectory = Path.Combine(FindRepositoryRoot(), "IronDev.Infrastructure", "Services", "Agents");
        var agentSources = Directory
            .EnumerateFiles(agentsDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.IsFalse(agentSources.Any(source => source.Contains("IAgentSkillRequestService", StringComparison.Ordinal)));
    }

    private static AgentSkillRequestService BuildService() =>
        new(new AgentSkillPolicyEvaluator(new StaticAgentSkillRegistry(), new ProjectApprovalPolicyEvaluator()));

    private static AgentSkillRequestInput BuildInput(
        string skillId,
        ProjectApprovalPolicy? policy = null) =>
        new()
        {
            ProjectId = "IronDev",
            AgentName = "CriticAgent",
            SkillId = skillId,
            Purpose = "Review governed skill request.",
            Policy = policy ?? ProjectApprovalPolicy.CreateDefault("IronDev"),
            RunId = "run-1",
            WorkspacePath = "C:\\workspaces\\run-1",
            SourceRepo = "C:\\repo\\IronDeveloper",
            EvidencePaths = ["evidence.json"],
            ParametersSummary = ["runId=run-1"]
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

    private static void AssertChecklistContains(
        AgentSkillRequestPackage package,
        string expected) =>
        CollectionAssert.Contains(package.ReviewChecklist.ToArray(), expected);

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
