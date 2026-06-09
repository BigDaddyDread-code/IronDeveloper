using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;
using IronDev.Infrastructure.Services.Agents.ApprovalPolicy;
using IronDev.Infrastructure.Services.Agents.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentSkillRequestContextServiceTests
{
    [TestMethod]
    public void AgentSkillRequestContext_AllowedReadOnlyRequest_IsReviewableButNonExecutable()
    {
        var context = BuildContext(AgentSkillIds.WorkspaceReadApplyContext);

        Assert.IsTrue(context.PolicyAllowed);
        Assert.AreEqual(AgentSkillRequestContextRecommendedActions.ReviewRequest, context.RecommendedNextAction);
        Assert.IsFalse(context.ExecutionCanStartFromContext);
        Assert.IsFalse(context.ApprovalCanBeGrantedByContext);
        Assert.IsFalse(context.DangerousCapability);
    }

    [TestMethod]
    public void AgentSkillRequestContext_UnknownSkill_StopsUnknownSkill()
    {
        var context = BuildContext("missing.skill");

        Assert.IsFalse(context.SkillKnown);
        Assert.AreEqual(AgentSkillRequestContextRecommendedActions.StopUnknownSkill, context.RecommendedNextAction);
        Assert.IsTrue(context.PolicyBlocked);
        Assert.IsFalse(context.ExecutionCanStartFromContext);
        Assert.IsTrue(context.Interpretation.Any(item => item.Contains("unknown", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void AgentSkillRequestContext_PolicyBlockedRequest_StopsPolicyBlockedSkill()
    {
        var policy = PolicyWithRule(
            ProjectApprovalRiskTiers.WorkspaceReporting,
            AgentSkillIds.WorkspaceReadApplyContext,
            ProjectApprovalModes.AlwaysBlock);
        var context = BuildContext(AgentSkillIds.WorkspaceReadApplyContext, policy);

        Assert.IsTrue(context.PolicyBlocked);
        Assert.AreEqual(AgentSkillRequestContextRecommendedActions.StopBlockedByPolicy, context.RecommendedNextAction);
        CollectionAssert.Contains(context.Blockers.ToArray(), "Blocked by project policy.");
    }

    [TestMethod]
    public void AgentSkillRequestContext_ApprovalRequiredRequest_RequestsSeparateApproval()
    {
        var policy = PolicyWithRule(
            ProjectApprovalRiskTiers.WorkspacePreparation,
            AgentSkillIds.WorkspaceCheck,
            ProjectApprovalModes.AlwaysAllow);
        var context = BuildContext(AgentSkillIds.WorkspaceCheck, policy);

        Assert.IsTrue(context.HumanApprovalRequired);
        Assert.AreEqual(AgentSkillRequestContextRecommendedActions.RequestSeparateApproval, context.RecommendedNextAction);
        Assert.IsFalse(context.ExecutionCanStartFromContext);
        Assert.IsFalse(context.ApprovalCanBeGrantedByContext);
    }
    [TestMethod]
    public void AgentSkillRequestContext_WorkspacePrepare_AllowsDisposableWorkspaceMutationButNotExecutionAuthority()
    {
        var policy = PolicyWithRule(
            ProjectApprovalRiskTiers.WorkspacePreparation,
            AgentSkillIds.WorkspacePrepare,
            ProjectApprovalModes.AlwaysAllow);
        var context = BuildContext(AgentSkillIds.WorkspacePrepare, policy);

        Assert.IsTrue(context.PolicyAllowed);
        Assert.AreEqual(AgentSkillRequestContextRecommendedActions.ReviewRequest, context.RecommendedNextAction);
        Assert.IsFalse(context.ExecutionCanStartFromContext);
        Assert.IsFalse(context.ApprovalCanBeGrantedByContext);
        Assert.IsFalse(context.SourceMutationAllowed);
        Assert.IsTrue(context.WorkspaceMutationAllowed);
        Assert.IsFalse(context.DangerousCapability);
    }


    [TestMethod]
    public void AgentSkillRequestContext_WorkspaceValidate_AllowsDisposableWorkspaceValidationButNotExecutionAuthority()
    {
        var policy = PolicyWithRule(
            ProjectApprovalRiskTiers.WorkspaceValidation,
            AgentSkillIds.WorkspaceValidate,
            ProjectApprovalModes.AlwaysAllow);
        var context = BuildContext(AgentSkillIds.WorkspaceValidate, policy);

        Assert.IsTrue(context.PolicyAllowed);
        Assert.AreEqual(AgentSkillRequestContextRecommendedActions.ReviewRequest, context.RecommendedNextAction);
        Assert.IsFalse(context.ExecutionCanStartFromContext);
        Assert.IsFalse(context.ApprovalCanBeGrantedByContext);
        Assert.IsFalse(context.SourceMutationAllowed);
        Assert.IsTrue(context.WorkspaceMutationAllowed);
        Assert.IsFalse(context.DangerousCapability);
    }[TestMethod]
    public void AgentSkillRequestContext_DangerousSourceMutation_StopsDangerousCapability()
    {
        var context = BuildContext(BuildApprovalRequiredRequest(
            AgentSkillIds.WorkspaceApplyCopy,
            ProjectApprovalRiskTiers.SourceMutation,
            AgentSkillCategories.WorkspaceApply));

        Assert.IsTrue(context.DangerousCapability);
        Assert.AreEqual(AgentSkillRequestContextRecommendedActions.StopDangerousCapability, context.RecommendedNextAction);
        Assert.IsFalse(context.SourceMutationAllowed);
        Assert.IsTrue(context.Interpretation.Any(item => item.Contains("dangerous capability", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void AgentSkillRequestContext_GitHubRequest_IsDangerous()
    {
        var context = BuildContext(BuildApprovalRequiredRequest(
            AgentSkillIds.GitHubPullRequestCreate,
            ProjectApprovalRiskTiers.GitOperation,
            AgentSkillCategories.Git));

        Assert.IsTrue(context.DangerousCapability);
        Assert.IsFalse(context.ExternalSystemAllowed);
        Assert.AreEqual(AgentSkillRequestContextRecommendedActions.StopDangerousCapability, context.RecommendedNextAction);
    }

    [TestMethod]
    public void AgentSkillRequestContext_InconsistentRequestReviewIds_FailsClosed()
    {
        var request = BuildRequest(AgentSkillIds.WorkspaceReadApplyContext);
        var review = BuildReview(request) with
        {
            RequestId = "skill-request-b"
        };

        var context = BuildContext(request, review);

        Assert.AreEqual(AgentSkillRequestContextRecommendedActions.CollectMissingEvidence, context.RecommendedNextAction);
        Assert.IsTrue(context.PolicyBlocked);
        CollectionAssert.Contains(context.Blockers.ToArray(), "Inconsistent request/review package.");
        Assert.IsFalse(context.ExecutionCanStartFromContext);
    }

    [TestMethod]
    public void AgentSkillRequestContext_MergesEvidenceChecklistsWarningsAndBlockers()
    {
        var request = BuildRequest(AgentSkillIds.WorkspaceReadApplyContext) with
        {
            EvidencePaths = ["a.json", "b.json"],
            ParametersSummary = ["runId=123", "workspacePath=C:\\x"],
            ReviewChecklist = ["check request", "shared check"],
            Warnings = ["request warning", "shared warning"]
        };
        var review = BuildReview(request) with
        {
            EvidencePaths = ["b.json", "c.json"],
            ParametersSummary = ["workspacePath=C:\\x", "sourceRepo=C:\\repo"],
            ReviewChecklist = ["shared check", "check review"],
            Warnings = ["shared warning", "review warning"],
            Blockers = ["review blocker"]
        };

        var context = BuildContext(request, review);

        CollectionAssert.AreEqual(new[] { "a.json", "b.json", "c.json" }, context.EvidencePaths.ToArray());
        CollectionAssert.AreEqual(
            new[] { "runId=123", "workspacePath=C:\\x", "sourceRepo=C:\\repo" },
            context.ParametersSummary.ToArray());
        CollectionAssert.Contains(context.ReviewChecklist.ToArray(), "check request");
        CollectionAssert.Contains(context.ReviewChecklist.ToArray(), "check review");
        CollectionAssert.AreEqual(
            new[] { "request warning", "shared warning", "review warning" },
            context.Warnings.ToArray());
        CollectionAssert.Contains(context.Blockers.ToArray(), "review blocker");
    }

    [TestMethod]
    public void AgentSkillRequestContext_ContextIdIsStable()
    {
        var request = BuildRequest(AgentSkillIds.WorkspaceReadApplyContext);

        var first = BuildContext(request);
        var second = BuildContext(request);
        var different = BuildContext(request with { RequestId = "skill-request-other" });

        Assert.AreEqual(first.ContextId, second.ContextId);
        Assert.AreNotEqual(first.ContextId, different.ContextId);
    }

    [TestMethod]
    public void AgentSkillRequestContextService_HasNoExecutionOrMutationDependencies()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "IronDev.Infrastructure",
            "Services",
            "Agents",
            "Skills",
            "AgentSkillRequestContextService.cs"));

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
    public void AgentSkillRequestContext_OnlyCriticAgentIsWiredToSkillRequestContext()
    {
        var agentsDirectory = Path.Combine(FindRepositoryRoot(), "IronDev.Infrastructure", "Services", "Agents");
        var wiredAgentFiles = Directory
            .EnumerateFiles(agentsDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path => File.ReadAllText(path).Contains("IAgentSkillRequestContextService", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "CriticAgent.cs" }, wiredAgentFiles);
    }

    private static AgentSkillRequestContext BuildContext(
        string skillId,
        ProjectApprovalPolicy? policy = null)
    {
        var request = BuildRequest(skillId, policy);
        return BuildContext(request);
    }

    private static AgentSkillRequestContext BuildContext(AgentSkillRequestPackage request) =>
        BuildContext(request, BuildReview(request));

    private static AgentSkillRequestContext BuildContext(
        AgentSkillRequestPackage request,
        AgentSkillRequestReview review) =>
        new AgentSkillRequestContextService().Create(new AgentSkillRequestContextInput
        {
            RequestPackage = request,
            ReviewPackage = review
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

    private static AgentSkillRequestReview BuildReview(AgentSkillRequestPackage request) =>
        new AgentSkillRequestReviewService().Create(new AgentSkillRequestReviewInput
        {
            RequestPackage = request
        });

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
