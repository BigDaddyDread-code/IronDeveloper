using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Infrastructure.Services.Agents.ApprovalPolicy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class ProjectApprovalPolicyTests
{
    [TestMethod]
    public void ProjectApprovalPolicy_DefaultPolicy_AllowsReadOnly()
    {
        var result = EvaluateDefault(ProjectApprovalRiskTiers.ReadOnly);

        Assert.AreEqual(ProjectApprovalDecisions.AllowedByPolicy, result.Decision);
        Assert.IsFalse(result.HumanApprovalRequired);
        Assert.IsTrue(result.AutomaticExecutionAllowed);
        Assert.IsFalse(result.SourceMutationAllowed);
    }

    [TestMethod]
    public void ProjectApprovalPolicy_DefaultPolicy_AllowsWorkspaceIntent()
    {
        var result = EvaluateDefault(ProjectApprovalRiskTiers.WorkspaceIntent);

        Assert.AreEqual(ProjectApprovalDecisions.AllowedByPolicy, result.Decision);
        Assert.IsTrue(result.AutomaticExecutionAllowed);
    }

    [TestMethod]
    public void ProjectApprovalPolicy_DefaultPolicy_RequiresApprovalForWorkspaceValidation()
    {
        var result = EvaluateDefault(ProjectApprovalRiskTiers.WorkspaceValidation);

        Assert.AreEqual(ProjectApprovalDecisions.ApprovalRequired, result.Decision);
        Assert.IsTrue(result.HumanApprovalRequired);
        Assert.IsFalse(result.AutomaticExecutionAllowed);
    }

    [TestMethod]
    public void ProjectApprovalPolicy_DefaultPolicy_RequiresApprovalForSourceMutation()
    {
        var result = EvaluateDefault(ProjectApprovalRiskTiers.SourceMutation);

        Assert.AreEqual(ProjectApprovalDecisions.ApprovalRequired, result.Decision);
        Assert.IsFalse(result.SourceMutationAllowed);
        Assert.IsFalse(result.AutomaticExecutionAllowed);
    }

    [TestMethod]
    public void ProjectApprovalPolicy_DefaultPolicy_BlocksOperation()
    {
        var result = EvaluateDefault(ProjectApprovalRiskTiers.GitOperation);

        Assert.AreEqual(ProjectApprovalDecisions.BlockedByPolicy, result.Decision);
        Assert.IsFalse(result.AutomaticExecutionAllowed);
    }

    [TestMethod]
    public void ProjectApprovalPolicy_ExactRequestedActionRuleWins()
    {
        var policy = new ProjectApprovalPolicy
        {
            ProjectId = "IronDev",
            DefaultMode = ProjectApprovalModes.AskEveryTime,
            Rules =
            [
                new ProjectApprovalPolicyRule
                {
                    RiskTier = ProjectApprovalRiskTiers.WorkspaceIntent,
                    RequestedAction = "human_review_source_changes",
                    Mode = ProjectApprovalModes.AlwaysAllow
                }
            ]
        };

        var result = Evaluate(policy, ProjectApprovalRiskTiers.WorkspaceIntent, requestedAction: "human_review_source_changes");

        Assert.AreEqual(ProjectApprovalDecisions.AllowedByPolicy, result.Decision);
        Assert.IsTrue(result.AutomaticExecutionAllowed);
    }

    [TestMethod]
    public void ProjectApprovalPolicy_BlockWinsOverAllowAtSameSpecificity()
    {
        var policy = new ProjectApprovalPolicy
        {
            ProjectId = "IronDev",
            DefaultMode = ProjectApprovalModes.AskEveryTime,
            Rules =
            [
                new ProjectApprovalPolicyRule
                {
                    RiskTier = ProjectApprovalRiskTiers.WorkspaceIntent,
                    RequestedAction = "human_review_source_changes",
                    Mode = ProjectApprovalModes.AlwaysAllow
                },
                new ProjectApprovalPolicyRule
                {
                    RiskTier = ProjectApprovalRiskTiers.WorkspaceIntent,
                    RequestedAction = "human_review_source_changes",
                    Mode = ProjectApprovalModes.AlwaysBlock
                }
            ]
        };

        var result = Evaluate(policy, ProjectApprovalRiskTiers.WorkspaceIntent, requestedAction: "human_review_source_changes");

        Assert.AreEqual(ProjectApprovalDecisions.BlockedByPolicy, result.Decision);
    }

    [TestMethod]
    public void ProjectApprovalPolicy_DangerousAlwaysAllow_IsDowngraded()
    {
        var policy = new ProjectApprovalPolicy
        {
            ProjectId = "IronDev",
            DefaultMode = ProjectApprovalModes.AskEveryTime,
            Rules =
            [
                new ProjectApprovalPolicyRule
                {
                    RiskTier = ProjectApprovalRiskTiers.SourceMutation,
                    Mode = ProjectApprovalModes.AlwaysAllow
                }
            ]
        };

        var result = Evaluate(policy, ProjectApprovalRiskTiers.SourceMutation);

        Assert.AreEqual(ProjectApprovalDecisions.ApprovalRequired, result.Decision);
        Assert.IsFalse(result.AutomaticExecutionAllowed);
        Assert.IsFalse(result.SourceMutationAllowed);
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("cannot be auto-allowed", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ProjectApprovalPolicy_ExternalSystemAlwaysAllow_IsBlocked()
    {
        var policy = new ProjectApprovalPolicy
        {
            ProjectId = "IronDev",
            DefaultMode = ProjectApprovalModes.AskEveryTime,
            Rules =
            [
                new ProjectApprovalPolicyRule
                {
                    RiskTier = ProjectApprovalRiskTiers.ExternalSystem,
                    Mode = ProjectApprovalModes.AlwaysAllow
                }
            ]
        };

        var result = Evaluate(policy, ProjectApprovalRiskTiers.ExternalSystem);

        Assert.AreEqual(ProjectApprovalDecisions.BlockedByPolicy, result.Decision);
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("External system auto-allow is not supported", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ProjectApprovalPolicy_UnknownRiskTier_RequiresApproval()
    {
        var result = EvaluateDefault("banana");

        Assert.AreEqual(ProjectApprovalDecisions.ApprovalRequired, result.Decision);
        Assert.IsTrue(result.HumanApprovalRequired);
        Assert.IsTrue(result.Warnings.Any(warning => warning.Contains("Unknown risk tier", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ProjectApprovalPolicyEvaluator_HasNoExecutionOrMutationDependencies()
    {
        var repoRoot = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            repoRoot,
            "IronDev.Infrastructure",
            "Services",
            "Agents",
            "ApprovalPolicy",
            "ProjectApprovalPolicyEvaluator.cs"));

        Assert.IsFalse(source.Contains("File.Copy", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("File.Delete", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("Process.Start", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("\"git\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("IDisposableWorkspaceApplyCopyService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IDisposableWorkspaceCommandService", StringComparison.Ordinal));
    }

    private static ProjectApprovalEvaluationResult EvaluateDefault(string riskTier) =>
        Evaluate(ProjectApprovalPolicy.CreateDefault("IronDev"), riskTier);

    private static ProjectApprovalEvaluationResult Evaluate(
        ProjectApprovalPolicy policy,
        string riskTier,
        string? actionType = null,
        string? requestedAction = null)
    {
        var evaluator = new ProjectApprovalPolicyEvaluator();
        return evaluator.Evaluate(new ProjectApprovalEvaluationRequest
        {
            ProjectId = "IronDev",
            RiskTier = riskTier,
            ActionType = actionType,
            RequestedAction = requestedAction,
            EvidenceHash = "hash",
            RunId = "run-1",
            WorkspacePath = "C:\\workspaces\\run-1",
            SourceRepo = "C:\\repos\\IronDev",
            Policy = policy
        });
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
