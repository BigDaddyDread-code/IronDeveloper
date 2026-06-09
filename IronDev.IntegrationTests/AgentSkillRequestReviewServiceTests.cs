using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;
using IronDev.Infrastructure.Services.Agents.ApprovalPolicy;
using IronDev.Infrastructure.Services.Agents.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentSkillRequestReviewServiceTests
{
    [TestMethod]
    public void AgentSkillRequestReview_AllowedReadOnlyRequest_IsReadyForHumanReview()
    {
        var review = BuildReview(BuildRequest(AgentSkillIds.WorkspaceReadApplyContext));

        Assert.AreEqual(AgentSkillRequestReviewStatuses.ReadyForHumanReview, review.ReviewStatus);
        Assert.IsTrue(review.HumanReviewRequired);
        Assert.IsFalse(review.HumanApprovalRequired);
        Assert.IsFalse(review.ApprovalCanBeGrantedByReview);
        Assert.IsFalse(review.ExecutionCanStartFromReview);
    }

    [TestMethod]
    public void AgentSkillRequestReview_UnknownSkill_BlocksReview()
    {
        var review = BuildReview(BuildRequest("missing.skill"));

        Assert.AreEqual(AgentSkillRequestReviewStatuses.BlockedForUnknownSkill, review.ReviewStatus);
        CollectionAssert.Contains(review.Blockers.ToArray(), "Unknown skill.");
        CollectionAssert.Contains(review.ReviewChecklist.ToArray(), "Do not execute unknown skills.");
    }

    [TestMethod]
    public void AgentSkillRequestReview_PolicyBlockedRequest_BlocksByPolicy()
    {
        var policy = PolicyWithRule(
            ProjectApprovalRiskTiers.WorkspaceReporting,
            AgentSkillIds.WorkspaceReadApplyContext,
            ProjectApprovalModes.AlwaysBlock);
        var review = BuildReview(BuildRequest(AgentSkillIds.WorkspaceReadApplyContext, policy));

        Assert.AreEqual(AgentSkillRequestReviewStatuses.BlockedByPolicy, review.ReviewStatus);
        CollectionAssert.Contains(review.Blockers.ToArray(), "Blocked by project policy.");
        Assert.IsFalse(review.ExecutionCanStartFromReview);
    }

    [TestMethod]
    public void AgentSkillRequestReview_WorkspaceValidate_IsReadyButCannotExecuteFromReview()
    {
        var policy = PolicyWithRule(
            ProjectApprovalRiskTiers.WorkspaceValidation,
            AgentSkillIds.WorkspaceValidate,
            ProjectApprovalModes.AlwaysAllow);

        var review = BuildReview(BuildRequest(AgentSkillIds.WorkspaceValidate, policy));

        Assert.AreEqual(AgentSkillRequestReviewStatuses.ReadyForHumanReview, review.ReviewStatus);
        Assert.IsFalse(review.HumanApprovalRequired);
        Assert.IsFalse(review.ExecutionCanStartFromReview);
        Assert.IsFalse(review.ApprovalCanBeGrantedByReview);
        Assert.IsFalse(review.SourceMutationAllowed);
        Assert.IsTrue(review.WorkspaceMutationAllowed);
        CollectionAssert.Contains(review.ReviewChecklist.ToArray(), "Workspace mutation is limited to disposable workspace validation evidence.");
        CollectionAssert.Contains(review.ReviewChecklist.ToArray(), "Validation process execution must stay behind the governed validation service.");
    }

    [TestMethod]
    public void AgentSkillRequestReview_WorkspaceDiff_IsReadyButCannotExecuteFromReview()
    {
        var policy = PolicyWithRule(
            ProjectApprovalRiskTiers.WorkspaceReporting,
            AgentSkillIds.WorkspaceDiff,
            ProjectApprovalModes.AlwaysAllow);

        var review = BuildReview(BuildRequest(AgentSkillIds.WorkspaceDiff, policy));

        Assert.AreEqual(AgentSkillRequestReviewStatuses.ReadyForHumanReview, review.ReviewStatus);
        Assert.IsFalse(review.HumanApprovalRequired);
        Assert.IsFalse(review.ExecutionCanStartFromReview);
        Assert.IsFalse(review.SourceMutationAllowed);
        Assert.IsTrue(review.WorkspaceMutationAllowed);
        CollectionAssert.Contains(review.ReviewChecklist.ToArray(), "Workspace mutation is limited to disposable workspace diff evidence.");
    }

    [TestMethod]
    public void AgentSkillRequestReview_WorkspacePromotionPackage_IsReadyButCannotExecuteFromReview()
    {
        var policy = PolicyWithRule(
            ProjectApprovalRiskTiers.WorkspacePackaging,
            AgentSkillIds.WorkspacePromotionPackage,
            ProjectApprovalModes.AlwaysAllow);

        var review = BuildReview(BuildRequest(AgentSkillIds.WorkspacePromotionPackage, policy));

        Assert.AreEqual(AgentSkillRequestReviewStatuses.ReadyForHumanReview, review.ReviewStatus);
        Assert.IsFalse(review.HumanApprovalRequired);
        Assert.IsFalse(review.ExecutionCanStartFromReview);
        Assert.IsFalse(review.SourceMutationAllowed);
        Assert.IsTrue(review.WorkspaceMutationAllowed);
        CollectionAssert.Contains(review.ReviewChecklist.ToArray(), "Workspace mutation is limited to promotion package evidence.");
        CollectionAssert.Contains(review.ReviewChecklist.ToArray(), "Promotion package creation does not approve or apply source changes.");
    }
    [TestMethod]
    public void AgentSkillRequestReview_SourceMutationRequest_BlocksForDangerousCapability()
    {
        var review = BuildReview(BuildApprovalRequiredRequest(
            AgentSkillIds.WorkspaceApplyCopy,
            ProjectApprovalRiskTiers.SourceMutation,
            AgentSkillCategories.WorkspaceApply));

        Assert.AreEqual(AgentSkillRequestReviewStatuses.BlockedForDangerousCapability, review.ReviewStatus);
        Assert.IsTrue(review.Blockers.Any(item => item.Contains("Dangerous capability", StringComparison.OrdinalIgnoreCase)));
        CollectionAssert.Contains(
            review.ReviewChecklist.ToArray(),
            "Source mutation is not allowed from this review package.");
        Assert.IsFalse(review.ApprovalCanBeGrantedByReview);
        Assert.IsFalse(review.ExecutionCanStartFromReview);
    }

    [TestMethod]
    public void AgentSkillRequestReview_GitRequest_BlocksForDangerousCapability()
    {
        var review = BuildReview(BuildApprovalRequiredRequest(
            AgentSkillIds.GitCommit,
            ProjectApprovalRiskTiers.GitOperation,
            AgentSkillCategories.Git));

        Assert.AreEqual(AgentSkillRequestReviewStatuses.BlockedForDangerousCapability, review.ReviewStatus);
        CollectionAssert.Contains(
            review.ReviewChecklist.ToArray(),
            "Git operations are not allowed from this review package.");
    }

    [TestMethod]
    public void AgentSkillRequestReview_TicketRequest_BlocksForDangerousCapability()
    {
        var review = BuildReview(BuildApprovalRequiredRequest(
            AgentSkillIds.TicketCreate,
            ProjectApprovalRiskTiers.TicketWrite,
            AgentSkillCategories.Ticketing));

        Assert.AreEqual(AgentSkillRequestReviewStatuses.BlockedForDangerousCapability, review.ReviewStatus);
        CollectionAssert.Contains(
            review.ReviewChecklist.ToArray(),
            "Ticket creation is not allowed from this review package.");
    }

    [TestMethod]
    public void AgentSkillRequestReview_ExternalRequest_BlocksForDangerousCapability()
    {
        var review = BuildReview(BuildApprovalRequiredRequest(
            AgentSkillIds.GitHubPullRequestCreate,
            ProjectApprovalRiskTiers.GitOperation,
            AgentSkillCategories.Git));

        Assert.AreEqual(AgentSkillRequestReviewStatuses.BlockedForDangerousCapability, review.ReviewStatus);
        CollectionAssert.Contains(
            review.ReviewChecklist.ToArray(),
            "External system access is not allowed from this review package.");
    }

    [TestMethod]
    public void AgentSkillRequestReview_PreservesEvidencePathsAndParameters()
    {
        var request = BuildRequest(AgentSkillIds.WorkspaceReadApplyContext) with
        {
            EvidencePaths = ["a.json"],
            ParametersSummary = ["runId=123"]
        };

        var review = BuildReview(request);

        CollectionAssert.AreEqual(request.EvidencePaths.ToArray(), review.EvidencePaths.ToArray());
        CollectionAssert.AreEqual(request.ParametersSummary.ToArray(), review.ParametersSummary.ToArray());
    }

    [TestMethod]
    public void AgentSkillRequestReview_ReviewIdIsStable()
    {
        var request = BuildRequest(AgentSkillIds.WorkspaceReadApplyContext);

        var first = BuildReview(request);
        var second = BuildReview(request);
        var different = BuildReview(request with { RequestId = "skill-request-other" });

        Assert.AreEqual(first.ReviewId, second.ReviewId);
        Assert.AreNotEqual(first.ReviewId, different.ReviewId);
    }

    [TestMethod]
    public void AgentSkillRequestReviewService_HasNoExecutionOrMutationDependencies()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "IronDev.Infrastructure",
            "Services",
            "Agents",
            "Skills",
            "AgentSkillRequestReviewService.cs"));

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
    public void AgentSkillRequestReview_NoAgentsAreWiredToSkillRequestReviews()
    {
        var agentsDirectory = Path.Combine(FindRepositoryRoot(), "IronDev.Infrastructure", "Services", "Agents");
        var agentSources = Directory
            .EnumerateFiles(agentsDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();

        Assert.IsFalse(agentSources.Any(source => source.Contains("IAgentSkillRequestReviewService", StringComparison.Ordinal)));
    }

    private static AgentSkillRequestReview BuildReview(AgentSkillRequestPackage request) =>
        new AgentSkillRequestReviewService().Create(new AgentSkillRequestReviewInput
        {
            RequestPackage = request
        });

    private static AgentSkillRequestPackage BuildRequest(
        string skillId,
        ProjectApprovalPolicy? policy = null) =>
        BuildRequestService().Create(new AgentSkillRequestInput
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
        });

    private static AgentSkillRequestPackage BuildApprovalRequiredRequest(
        string skillId,
        string riskTier,
        string category) =>
        BuildRequest(AgentSkillIds.WorkspaceValidate) with
        {
            RequestId = $"skill-request-irondev-criticagent-{skillId.Replace('.', '-')}",
            SkillId = skillId,
            Decision = ProjectApprovalDecisions.ApprovalRequired,
            Reason = "Skill requires human approval.",
            RiskTier = riskTier,
            Category = category,
            HumanApprovalRequired = true,
            AutomaticExecutionAllowedByPolicy = false,
            SkillExecutionAllowedByPolicy = false,
            ExecutionCanStartFromRequest = false,
            ApprovalCanBeGrantedByRequest = false
        };

    private static AgentSkillRequestService BuildRequestService() =>
        new(new AgentSkillPolicyEvaluator(new StaticAgentSkillRegistry(), new ProjectApprovalPolicyEvaluator()));

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
