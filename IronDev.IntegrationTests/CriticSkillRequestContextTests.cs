using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;
using IronDev.Core.Interfaces;
using IronDev.Infrastructure.Services.Agents;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class CriticSkillRequestContextTests
{
    [TestMethod]
    public async Task CriticAgent_ConsumesReadyLowRiskSkillContext()
    {
        using var package = TestFailurePackage.Create();
        var context = BuildContext(
            AgentSkillIds.WorkspaceReadApplyContext,
            ProjectApprovalDecisions.AllowedByPolicy,
            AgentSkillRequestReviewStatuses.ReadyForHumanReview,
            AgentSkillRequestContextRecommendedActions.ReviewRequest);
        var service = new FakeAgentSkillRequestContextService(context);
        var agent = BuildCriticAgent(service);

        var result = await agent.RunAsync(BuildRequest(package.Path, context.RequestId, context.ReviewId));

        using var document = JsonDocument.Parse(result.OutputJson);
        var skillContext = document.RootElement.GetProperty("skillRequestContext");
        Assert.IsTrue(skillContext.GetProperty("available").GetBoolean());
        Assert.AreEqual(AgentSkillIds.WorkspaceReadApplyContext, skillContext.GetProperty("skillId").GetString());
        Assert.AreEqual(AgentSkillRequestContextRecommendedActions.ReviewRequest, skillContext.GetProperty("recommendedNextAction").GetString());
        Assert.IsFalse(skillContext.GetProperty("executionAllowedByThisAgent").GetBoolean());
        Assert.IsFalse(skillContext.GetProperty("approvalAllowedByThisAgent").GetBoolean());
        Assert.IsFalse(skillContext.GetProperty("sourceMutationAllowedByThisAgent").GetBoolean());
        Assert.AreEqual(1, service.CallCount);
    }

    [TestMethod]
    public async Task CriticAgent_ConsumesApprovalRequiredSkillContext()
    {
        using var package = TestFailurePackage.Create();
        var context = BuildContext(
            AgentSkillIds.WorkspaceValidate,
            ProjectApprovalDecisions.ApprovalRequired,
            AgentSkillRequestReviewStatuses.ApprovalRequired,
            AgentSkillRequestContextRecommendedActions.RequestSeparateApproval,
            humanApprovalRequired: true);
        var agent = BuildCriticAgent(new FakeAgentSkillRequestContextService(context));

        var result = await agent.RunAsync(BuildRequest(package.Path, context.RequestId, context.ReviewId));

        using var document = JsonDocument.Parse(result.OutputJson);
        var skillContext = document.RootElement.GetProperty("skillRequestContext");
        Assert.IsTrue(skillContext.GetProperty("humanApprovalRequired").GetBoolean());
        Assert.IsFalse(skillContext.GetProperty("approvalAllowedByThisAgent").GetBoolean());
        AssertJsonArrayContains(skillContext.GetProperty("interpretation"), "separate approval");
    }

    [TestMethod]
    public async Task CriticAgent_ConsumesPolicyBlockedSkillContext()
    {
        using var package = TestFailurePackage.Create();
        var context = BuildContext(
            AgentSkillIds.WorkspaceReadApplyContext,
            ProjectApprovalDecisions.BlockedByPolicy,
            AgentSkillRequestReviewStatuses.BlockedByPolicy,
            AgentSkillRequestContextRecommendedActions.StopBlockedByPolicy,
            policyBlocked: true);
        var agent = BuildCriticAgent(new FakeAgentSkillRequestContextService(context));

        var result = await agent.RunAsync(BuildRequest(package.Path, context.RequestId, context.ReviewId));

        using var document = JsonDocument.Parse(result.OutputJson);
        var skillContext = document.RootElement.GetProperty("skillRequestContext");
        Assert.IsTrue(skillContext.GetProperty("policyBlocked").GetBoolean());
        Assert.IsFalse(skillContext.GetProperty("executionAllowedByThisAgent").GetBoolean());
        AssertJsonArrayContains(skillContext.GetProperty("interpretation"), "project policy blocks");
    }

    [TestMethod]
    public async Task CriticAgent_ConsumesUnknownSkillContext()
    {
        using var package = TestFailurePackage.Create();
        var context = BuildContext(
            "missing.skill",
            ProjectApprovalDecisions.BlockedByPolicy,
            AgentSkillRequestReviewStatuses.BlockedForUnknownSkill,
            AgentSkillRequestContextRecommendedActions.StopUnknownSkill,
            skillKnown: false,
            policyBlocked: true);
        var agent = BuildCriticAgent(new FakeAgentSkillRequestContextService(context));

        var result = await agent.RunAsync(BuildRequest(package.Path, context.RequestId, context.ReviewId));

        using var document = JsonDocument.Parse(result.OutputJson);
        var skillContext = document.RootElement.GetProperty("skillRequestContext");
        Assert.IsFalse(skillContext.GetProperty("skillKnown").GetBoolean());
        AssertJsonArrayContains(skillContext.GetProperty("interpretation"), "unknown");
        Assert.IsFalse(skillContext.GetProperty("executionAllowedByThisAgent").GetBoolean());
    }

    [TestMethod]
    public async Task CriticAgent_ConsumesDangerousCapabilityContext()
    {
        using var package = TestFailurePackage.Create();
        var context = BuildContext(
            AgentSkillIds.WorkspaceApplyCopy,
            ProjectApprovalDecisions.ApprovalRequired,
            AgentSkillRequestReviewStatuses.BlockedForDangerousCapability,
            AgentSkillRequestContextRecommendedActions.StopDangerousCapability,
            riskTier: ProjectApprovalRiskTiers.SourceMutation,
            category: AgentSkillCategories.WorkspaceApply,
            humanApprovalRequired: true,
            dangerousCapability: true);
        var agent = BuildCriticAgent(new FakeAgentSkillRequestContextService(context));

        var result = await agent.RunAsync(BuildRequest(package.Path, context.RequestId, context.ReviewId));

        using var document = JsonDocument.Parse(result.OutputJson);
        var skillContext = document.RootElement.GetProperty("skillRequestContext");
        Assert.IsTrue(skillContext.GetProperty("dangerousCapability").GetBoolean());
        Assert.IsFalse(skillContext.GetProperty("sourceMutationAllowedByThisAgent").GetBoolean());
        Assert.IsFalse(skillContext.GetProperty("approvalAllowedByThisAgent").GetBoolean());
        Assert.IsFalse(skillContext.GetProperty("executionAllowedByThisAgent").GetBoolean());
        AssertJsonArrayContains(skillContext.GetProperty("interpretation"), "dangerous capability");
    }

    [TestMethod]
    public async Task CriticAgent_WithoutSkillContextInput_LeavesContextNull()
    {
        using var package = TestFailurePackage.Create();
        var context = BuildContext(
            AgentSkillIds.WorkspaceReadApplyContext,
            ProjectApprovalDecisions.AllowedByPolicy,
            AgentSkillRequestReviewStatuses.ReadyForHumanReview,
            AgentSkillRequestContextRecommendedActions.ReviewRequest);
        var service = new FakeAgentSkillRequestContextService(context);
        var agent = BuildCriticAgent(service);

        var result = await agent.RunAsync(BuildRequest(package.Path, includeSkillInputs: false));

        using var document = JsonDocument.Parse(result.OutputJson);
        Assert.AreEqual(JsonValueKind.Null, document.RootElement.GetProperty("skillRequestContext").ValueKind);
        Assert.AreEqual(0, service.CallCount);
    }

    [TestMethod]
    public async Task CriticAgent_MissingPairedSkillContextInput_ReturnsUnavailableContext()
    {
        using var package = TestFailurePackage.Create();
        var context = BuildContext(
            AgentSkillIds.WorkspaceReadApplyContext,
            ProjectApprovalDecisions.AllowedByPolicy,
            AgentSkillRequestReviewStatuses.ReadyForHumanReview,
            AgentSkillRequestContextRecommendedActions.ReviewRequest);
        var service = new FakeAgentSkillRequestContextService(context);
        var agent = BuildCriticAgent(service);

        var result = await agent.RunAsync(BuildRequest(package.Path, context.RequestId, context.ReviewId, includeReviewJson: false));

        using var document = JsonDocument.Parse(result.OutputJson);
        var skillContext = document.RootElement.GetProperty("skillRequestContext");
        Assert.IsFalse(skillContext.GetProperty("available").GetBoolean());
        Assert.AreEqual(AgentSkillRequestContextRecommendedActions.CollectMissingEvidence, skillContext.GetProperty("recommendedNextAction").GetString());
        Assert.IsFalse(skillContext.GetProperty("executionAllowedByThisAgent").GetBoolean());
        AssertJsonArrayContains(skillContext.GetProperty("warnings"), "skill_request_review_json");
        Assert.AreEqual(0, service.CallCount);
    }

    [TestMethod]
    public async Task CriticAgent_ContextCreationFailure_IsUnavailableNotFatal()
    {
        using var package = TestFailurePackage.Create();
        var context = BuildContext(
            AgentSkillIds.WorkspaceReadApplyContext,
            ProjectApprovalDecisions.AllowedByPolicy,
            AgentSkillRequestReviewStatuses.ReadyForHumanReview,
            AgentSkillRequestContextRecommendedActions.ReviewRequest);
        var service = new FakeAgentSkillRequestContextService(context) { ThrowOnCreate = true };
        var agent = BuildCriticAgent(service);

        var result = await agent.RunAsync(BuildRequest(package.Path, context.RequestId, context.ReviewId));

        Assert.AreEqual(AgentRunStatus.Succeeded, result.Status);
        using var document = JsonDocument.Parse(result.OutputJson);
        var skillContext = document.RootElement.GetProperty("skillRequestContext");
        Assert.IsFalse(skillContext.GetProperty("available").GetBoolean());
        Assert.AreEqual(AgentSkillRequestContextRecommendedActions.CollectMissingEvidence, skillContext.GetProperty("recommendedNextAction").GetString());
        Assert.AreEqual(1, service.CallCount);
    }

    [TestMethod]
    public void CriticAgentBoundary_DependsOnlyOnSkillRequestContextService()
    {
        var source = ReadCriticAgentSource();

        Assert.IsTrue(source.Contains("IAgentSkillRequestContextService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IAgentSkillRequestService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IAgentSkillRequestReviewService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IAgentSkillPolicyEvaluator", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IAgentSkillRegistry", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CriticAgentBoundary_HasNoSkillExecutionOrMutationDependencies()
    {
        var source = ReadCriticAgentSource();

        Assert.IsFalse(source.Contains("IDisposableWorkspaceApplyCopyService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IDisposableWorkspaceCommandService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("File.Copy", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("File.Delete", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("Process.Start", StringComparison.Ordinal));
    }

    private static CriticAgent BuildCriticAgent(IAgentSkillRequestContextService? service) =>
        new(
            new AgentDefinition
            {
                Name = "CriticAgent",
                Purpose = "Review evidence without mutation.",
                DefaultModelProfile = "strong-reviewer"
            },
            new AgentModelResolver(),
            skillRequestContextService: service);

    private static AgentRequest BuildRequest(
        string packagePath,
        bool includeSkillInputs = true) =>
        BuildRequest(packagePath, "skill-request-id", "skill-request-review-id", includeSkillInputs);

    private static AgentRequest BuildRequest(
        string packagePath,
        string requestId,
        string reviewId,
        bool includeSkillInputs = true,
        bool includeReviewJson = true)
    {
        var inputs = new Dictionary<string, string>
        {
            ["package_path"] = packagePath,
            ["project"] = "IronDev",
            ["live_llm"] = "false"
        };

        if (includeSkillInputs)
        {
            inputs["skill_request_package_json"] = JsonSerializer.Serialize(BuildRequestPackage(requestId));
            if (includeReviewJson)
                inputs["skill_request_review_json"] = JsonSerializer.Serialize(BuildReviewPackage(requestId, reviewId));
        }

        return new AgentRequest
        {
            AgentName = "CriticAgent",
            GoalId = "critic-goal",
            DogfoodRunId = "critic-run",
            Inputs = inputs
        };
    }

    private static AgentSkillRequestContext BuildContext(
        string skillId,
        string decision,
        string reviewStatus,
        string recommendedNextAction,
        string riskTier = ProjectApprovalRiskTiers.WorkspaceReporting,
        string category = AgentSkillCategories.WorkspaceContext,
        bool skillKnown = true,
        bool humanApprovalRequired = false,
        bool policyBlocked = false,
        bool dangerousCapability = false) =>
        new()
        {
            ContextId = "skill-request-context-id",
            RequestId = "skill-request-id",
            ReviewId = "skill-request-review-id",
            ProjectId = "IronDev",
            AgentName = "CriticAgent",
            SkillId = skillId,
            Purpose = "Review governed skill request.",
            SkillKnown = skillKnown,
            Decision = decision,
            ReviewStatus = reviewStatus,
            RiskTier = riskTier,
            Category = category,
            HumanReviewRequired = true,
            HumanApprovalRequired = humanApprovalRequired,
            PolicyAllowed = string.Equals(decision, ProjectApprovalDecisions.AllowedByPolicy, StringComparison.Ordinal),
            PolicyBlocked = policyBlocked,
            DangerousCapability = dangerousCapability,
            ExecutionCanStartFromContext = false,
            ApprovalCanBeGrantedByContext = false,
            SourceMutationAllowed = false,
            WorkspaceMutationAllowed = false,
            ExternalSystemAllowed = false,
            CreatesTicketAllowed = false,
            WritesMemoryAllowed = false,
            RecommendedNextAction = recommendedNextAction,
            EvidencePaths = ["evidence.json"],
            ParametersSummary = ["runId=run-1"],
            ReviewChecklist = ["Confirm context is not execution authority."],
            Blockers = policyBlocked ? ["Blocked by project policy."] : [],
            Warnings = [],
            Interpretation = ["Context is advisory only."]
        };

    private static AgentSkillRequestPackage BuildRequestPackage(string requestId) =>
        new()
        {
            RequestId = requestId,
            ProjectId = "IronDev",
            AgentName = "CriticAgent",
            SkillId = AgentSkillIds.WorkspaceReadApplyContext,
            Purpose = "Review governed skill request.",
            SkillKnown = true,
            Decision = ProjectApprovalDecisions.AllowedByPolicy,
            Reason = "Allowed by policy.",
            RiskTier = ProjectApprovalRiskTiers.WorkspaceReporting,
            Category = AgentSkillCategories.WorkspaceContext,
            HumanApprovalRequired = false,
            AutomaticExecutionAllowedByPolicy = true,
            SkillExecutionAllowedByPolicy = true,
            ExecutionCanStartFromRequest = false,
            ApprovalCanBeGrantedByRequest = false,
            SourceMutationAllowed = false,
            WorkspaceMutationAllowed = false,
            ExternalSystemAllowed = false,
            CreatesTicketAllowed = false,
            WritesMemoryAllowed = false,
            EvidencePaths = ["evidence.json"],
            ParametersSummary = ["runId=run-1"]
        };

    private static AgentSkillRequestReview BuildReviewPackage(string requestId, string reviewId) =>
        new()
        {
            ReviewId = reviewId,
            RequestId = requestId,
            ProjectId = "IronDev",
            AgentName = "CriticAgent",
            SkillId = AgentSkillIds.WorkspaceReadApplyContext,
            Purpose = "Review governed skill request.",
            ReviewStatus = AgentSkillRequestReviewStatuses.ReadyForHumanReview,
            Summary = "Ready for human review.",
            Decision = ProjectApprovalDecisions.AllowedByPolicy,
            RiskTier = ProjectApprovalRiskTiers.WorkspaceReporting,
            Category = AgentSkillCategories.WorkspaceContext,
            HumanReviewRequired = true,
            HumanApprovalRequired = false,
            ApprovalCanBeGrantedByReview = false,
            ExecutionCanStartFromReview = false,
            SourceMutationAllowed = false,
            WorkspaceMutationAllowed = false,
            ExternalSystemAllowed = false,
            CreatesTicketAllowed = false,
            WritesMemoryAllowed = false,
            EvidencePaths = ["evidence.json"],
            ParametersSummary = ["runId=run-1"]
        };

    private static void AssertJsonArrayContains(JsonElement array, string expected)
    {
        Assert.IsTrue(
            array.EnumerateArray().Any(item =>
                (item.GetString() ?? string.Empty).Contains(expected, StringComparison.OrdinalIgnoreCase)),
            $"Expected JSON array to contain '{expected}'.");
    }

    private static string ReadCriticAgentSource()
    {
        var repoRoot = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(repoRoot, "IronDev.Infrastructure", "Services", "Agents", "CriticAgent.cs"));
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

    private sealed class FakeAgentSkillRequestContextService : IAgentSkillRequestContextService
    {
        private readonly AgentSkillRequestContext _context;

        public FakeAgentSkillRequestContextService(AgentSkillRequestContext context)
        {
            _context = context;
        }

        public bool ThrowOnCreate { get; init; }

        public int CallCount { get; private set; }

        public AgentSkillRequestContext Create(AgentSkillRequestContextInput input)
        {
            CallCount++;
            if (ThrowOnCreate)
                throw new InvalidOperationException("Synthetic context failure.");

            return _context;
        }
    }

    private sealed class TestFailurePackage : IDisposable
    {
        private TestFailurePackage(string root, string path)
        {
            Root = root;
            Path = path;
        }

        public string Root { get; }
        public string Path { get; }

        public static TestFailurePackage Create()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "IronDev-CriticSkillRequestContextTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var path = System.IO.Path.Combine(root, "failure-package.json");
            var payload = new
            {
                DogfoodRunId = "critic-run",
                ScenarioId = "critic-scenario",
                GoalId = "critic-goal",
                FailureReason = "Validation failed.",
                ExpectedJson = """{"status":"succeeded"}""",
                ActualJson = """{"status":"failed"}""",
                ReproCommand = "irondev workspace validate",
                ValidationCommand = "irondev workspace validate --json",
                EvidencePaths = new[] { System.IO.Path.Combine(root, "evidence.json") },
                LikelyAreas = new[] { "skill request context" },
                SafetyRules = new[] { "do not execute skills" }
            };

            File.WriteAllText(path, JsonSerializer.Serialize(payload));
            return new TestFailurePackage(root, path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
