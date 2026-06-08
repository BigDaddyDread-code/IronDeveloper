using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.WorkspaceApply;
using IronDev.Core.Interfaces;
using IronDev.Infrastructure.Services.Agents;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class CriticWorkspaceApplyContextTests
{
    [TestMethod]
    public async Task CriticAgent_ConsumesSuccessWorkspaceContext()
    {
        using var package = TestFailurePackage.Create();
        var fakeContextService = new FakeAgentWorkspaceApplyContextService(BuildSuccessContext());
        var agent = BuildCriticAgent(fakeContextService);

        var result = await agent.RunAsync(BuildRequest(package.Path, includeWorkspaceInput: true));

        using var document = JsonDocument.Parse(result.OutputJson);
        var context = document.RootElement.GetProperty("workspaceApplyContext");
        Assert.IsTrue(context.GetProperty("available").GetBoolean());
        Assert.AreEqual("success", context.GetProperty("outcome").GetString());
        Assert.AreEqual(WorkspaceApplyRecommendedActions.HumanReviewOrCommit, context.GetProperty("recommendedAction").GetString());
        Assert.AreEqual(WorkspaceApplyRequestedActions.HumanReviewSourceChanges, context.GetProperty("requestedAction").GetString());
        Assert.AreEqual(WorkspaceApplyActionReviewStatuses.ReadyForHumanReview, context.GetProperty("reviewStatus").GetString());
        Assert.AreEqual(ProjectApprovalDecisions.AllowedByPolicy, context.GetProperty("policyDecision").GetString());
        Assert.IsFalse(context.GetProperty("executionAllowedByThisAgent").GetBoolean());
        Assert.IsFalse(context.GetProperty("approvalAllowedByThisAgent").GetBoolean());
        Assert.IsFalse(context.GetProperty("sourceMutationAllowedByThisAgent").GetBoolean());
        Assert.AreEqual(1, fakeContextService.CallCount);
    }

    [TestMethod]
    public async Task CriticAgent_ConsumesFailureWorkspaceContext()
    {
        using var package = TestFailurePackage.Create();
        var fakeContextService = new FakeAgentWorkspaceApplyContextService(BuildFailureContext());
        var agent = BuildCriticAgent(fakeContextService);

        var result = await agent.RunAsync(BuildRequest(package.Path, includeWorkspaceInput: true));

        using var document = JsonDocument.Parse(result.OutputJson);
        var context = document.RootElement.GetProperty("workspaceApplyContext");
        Assert.IsTrue(context.GetProperty("available").GetBoolean());
        Assert.AreEqual("failure", context.GetProperty("outcome").GetString());
        Assert.AreEqual(WorkspaceApplyRecommendedActions.DoNotRetryUntilSourceReviewed, context.GetProperty("recommendedAction").GetString());
        Assert.AreEqual(WorkspaceApplyRequestedActions.ReviewSourceBeforeRetry, context.GetProperty("requestedAction").GetString());
        Assert.AreEqual(WorkspaceApplyActionReviewStatuses.BlockedForSourceReview, context.GetProperty("reviewStatus").GetString());
        Assert.IsTrue(context.GetProperty("sourceRepoMayBeMutated").GetBoolean());
        Assert.IsFalse(context.GetProperty("executionAllowedByThisAgent").GetBoolean());
        Assert.AreEqual(1, fakeContextService.CallCount);
    }

    [TestMethod]
    public async Task CriticAgent_HandlesUnavailableWorkspaceContext()
    {
        using var package = TestFailurePackage.Create();
        var fakeContextService = new FakeAgentWorkspaceApplyContextService(BuildUnavailableContext());
        var agent = BuildCriticAgent(fakeContextService);

        var result = await agent.RunAsync(BuildRequest(package.Path, includeWorkspaceInput: true));

        using var document = JsonDocument.Parse(result.OutputJson);
        var context = document.RootElement.GetProperty("workspaceApplyContext");
        Assert.IsFalse(context.GetProperty("available").GetBoolean());
        Assert.AreEqual("unavailable", context.GetProperty("outcome").GetString());
        Assert.IsFalse(context.GetProperty("executionAllowedByThisAgent").GetBoolean());
        Assert.IsTrue(
            context.GetProperty("warnings").EnumerateArray().Any(item =>
                (item.GetString() ?? string.Empty).Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
                (item.GetString() ?? string.Empty).Contains("no usable", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(1, fakeContextService.CallCount);
    }

    [TestMethod]
    public async Task CriticAgent_WithoutWorkspaceInput_LeavesContextNull()
    {
        using var package = TestFailurePackage.Create();
        var fakeContextService = new FakeAgentWorkspaceApplyContextService(BuildSuccessContext());
        var agent = BuildCriticAgent(fakeContextService);

        var result = await agent.RunAsync(BuildRequest(package.Path, includeWorkspaceInput: false));

        using var document = JsonDocument.Parse(result.OutputJson);
        Assert.AreEqual(JsonValueKind.Null, document.RootElement.GetProperty("workspaceApplyContext").ValueKind);
        Assert.AreEqual(0, fakeContextService.CallCount);
    }

    [TestMethod]
    public void CriticAgentBoundary_UsesSharedWorkspaceApplyContextServiceOnly()
    {
        var criticSource = ReadCriticAgentSource();

        Assert.IsTrue(criticSource.Contains("IAgentWorkspaceApplyContextService", StringComparison.Ordinal));
        Assert.IsFalse(criticSource.Contains("IWorkspaceApplyReportReader", StringComparison.Ordinal));
        Assert.IsFalse(criticSource.Contains("IWorkspaceApplyRecommendationService", StringComparison.Ordinal));
        Assert.IsFalse(criticSource.Contains("IWorkspaceApplyActionRequestService", StringComparison.Ordinal));
        Assert.IsFalse(criticSource.Contains("IWorkspaceApplyActionReviewService", StringComparison.Ordinal));
        Assert.IsFalse(criticSource.Contains("IWorkspaceApplyPolicyContextService", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CriticAgentBoundary_HasNoWorkspaceMutationOrExecutionDependencies()
    {
        var criticSource = ReadCriticAgentSource();

        Assert.IsFalse(criticSource.Contains("File.Copy", StringComparison.Ordinal));
        Assert.IsFalse(criticSource.Contains("File.Delete", StringComparison.Ordinal));
        Assert.IsFalse(criticSource.Contains("Process.Start", StringComparison.Ordinal));
        Assert.IsFalse(criticSource.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(criticSource.Contains("IDisposableWorkspaceApplyCopyService", StringComparison.Ordinal));
        Assert.IsFalse(criticSource.Contains("IDisposableWorkspacePrepareService", StringComparison.Ordinal));
        Assert.IsFalse(criticSource.Contains("IDisposableWorkspaceCommandService", StringComparison.Ordinal));
        Assert.IsFalse(criticSource.Contains("IDisposableWorkspaceValidationService", StringComparison.Ordinal));
        Assert.IsFalse(criticSource.Contains("IDisposableWorkspacePromotionApprovalService", StringComparison.Ordinal));
        Assert.IsFalse(criticSource.Contains("IDisposableWorkspaceApplyPreflightService", StringComparison.Ordinal));
        Assert.IsFalse(criticSource.Contains("IDisposableWorkspaceApplyDryRunService", StringComparison.Ordinal));
        Assert.IsFalse(criticSource.Contains("IDisposableWorkspaceApplyVerifyService", StringComparison.Ordinal));
    }

    private static CriticAgent BuildCriticAgent(IAgentWorkspaceApplyContextService? contextService) =>
        new(
            new AgentDefinition
            {
                Name = "CriticAgent",
                Purpose = "Review evidence without mutation.",
                DefaultModelProfile = "strong-reviewer"
            },
            new AgentModelResolver(),
            workspaceApplyContextService: contextService);

    private static AgentRequest BuildRequest(string packagePath, bool includeWorkspaceInput)
    {
        var inputs = new Dictionary<string, string>
        {
            ["package_path"] = packagePath,
            ["project"] = "IronDev",
            ["live_llm"] = "false"
        };

        if (includeWorkspaceInput)
        {
            inputs["workspace_apply_run_id"] = "apply-run";
            inputs["workspace_apply_workspace_path"] = "C:\\workspaces\\apply-run";
        }

        return new AgentRequest
        {
            AgentName = "CriticAgent",
            GoalId = "critic-goal",
            DogfoodRunId = "critic-run",
            Inputs = inputs
        };
    }

    private static AgentWorkspaceApplyContext BuildSuccessContext()
    {
        var report = BuildReport("success", sourceRepoMutated: true, applyVerified: true, postApplyValidationSucceeded: true);
        var recommendation = new WorkspaceApplyRecommendation
        {
            RecommendedAction = WorkspaceApplyRecommendedActions.HumanReviewOrCommit,
            Reason = "Human review is required before commit.",
            HumanReviewRequired = true,
            SafeToRetry = false,
            SafeToCommitAfterReview = true,
            SourceReviewRequiredBeforeRetry = false,
            BlocksAutomaticExecution = true,
            EvidencePaths = report.EvidencePaths,
            RiskNotes = report.RiskNotes
        };
        var actionRequest = new WorkspaceApplyActionRequest
        {
            RequestedAction = WorkspaceApplyRequestedActions.HumanReviewSourceChanges,
            Reason = "Review source changes.",
            HumanApprovalRequired = true,
            AutomaticExecutionAllowed = false,
            MutatesSourceRepo = false,
            RequiresFreshHumanDecision = true,
            EvidencePaths = report.EvidencePaths,
            RiskNotes = report.RiskNotes
        };
        var actionReview = new WorkspaceApplyActionReview
        {
            ReviewStatus = WorkspaceApplyActionReviewStatuses.ReadyForHumanReview,
            Summary = "Ready for human review.",
            HumanReviewRequired = true,
            ApprovalCanBeGrantedByThisPackage = false,
            ExecutionCanStartFromThisPackage = false,
            SourceRepoMayBeMutated = true,
            RequestedAction = actionRequest.RequestedAction,
            RecommendedAction = recommendation.RecommendedAction,
            EvidencePaths = report.EvidencePaths,
            RiskNotes = report.RiskNotes
        };
        var policyContext = BuildPolicyContext(ProjectApprovalDecisions.AllowedByPolicy, actionRequest.RequestedAction, report.EvidencePaths, report.RiskNotes);

        return BuildContext(report, recommendation, actionRequest, actionReview, policyContext, available: true);
    }

    private static AgentWorkspaceApplyContext BuildFailureContext()
    {
        var report = BuildReport("failure", sourceRepoMutated: true, applyVerified: false, postApplyValidationSucceeded: false) with
        {
            FailedStage = "apply-copy",
            FailureSeverity = "critical",
            RecommendedNextAction = "do_not_retry_until_source_reviewed"
        };
        var recommendation = new WorkspaceApplyRecommendation
        {
            RecommendedAction = WorkspaceApplyRecommendedActions.DoNotRetryUntilSourceReviewed,
            Reason = "Source may have been mutated without verification.",
            HumanReviewRequired = true,
            SafeToRetry = false,
            SafeToCommitAfterReview = false,
            SourceReviewRequiredBeforeRetry = true,
            BlocksAutomaticExecution = true,
            EvidencePaths = report.EvidencePaths,
            RiskNotes = report.RiskNotes
        };
        var actionRequest = new WorkspaceApplyActionRequest
        {
            RequestedAction = WorkspaceApplyRequestedActions.ReviewSourceBeforeRetry,
            Reason = "Review source before retry.",
            HumanApprovalRequired = true,
            AutomaticExecutionAllowed = false,
            MutatesSourceRepo = false,
            RequiresFreshHumanDecision = true,
            EvidencePaths = report.EvidencePaths,
            RiskNotes = report.RiskNotes
        };
        var actionReview = new WorkspaceApplyActionReview
        {
            ReviewStatus = WorkspaceApplyActionReviewStatuses.BlockedForSourceReview,
            Summary = "Source review is required before retry.",
            HumanReviewRequired = true,
            ApprovalCanBeGrantedByThisPackage = false,
            ExecutionCanStartFromThisPackage = false,
            SourceRepoMayBeMutated = true,
            RequestedAction = actionRequest.RequestedAction,
            RecommendedAction = recommendation.RecommendedAction,
            EvidencePaths = report.EvidencePaths,
            RiskNotes = report.RiskNotes
        };
        var policyContext = BuildPolicyContext(ProjectApprovalDecisions.AllowedByPolicy, actionRequest.RequestedAction, report.EvidencePaths, report.RiskNotes);

        return BuildContext(report, recommendation, actionRequest, actionReview, policyContext, available: true);
    }

    private static AgentWorkspaceApplyContext BuildUnavailableContext()
    {
        var report = BuildReport("unavailable", sourceRepoMutated: false, applyVerified: false, postApplyValidationSucceeded: false) with
        {
            Warnings = ["No usable source-report or failure-package was found."]
        };
        var recommendation = new WorkspaceApplyRecommendation
        {
            RecommendedAction = WorkspaceApplyRecommendedActions.NoWorkspaceApplyReport,
            Reason = "No workspace apply report is available.",
            HumanReviewRequired = true,
            SafeToRetry = false,
            SafeToCommitAfterReview = false,
            SourceReviewRequiredBeforeRetry = false,
            BlocksAutomaticExecution = true,
            Warnings = ["Workspace apply context unavailable."]
        };
        var actionRequest = new WorkspaceApplyActionRequest
        {
            RequestedAction = WorkspaceApplyRequestedActions.NoActionAvailable,
            Reason = "No action is available without evidence.",
            HumanApprovalRequired = true,
            AutomaticExecutionAllowed = false,
            MutatesSourceRepo = false,
            RequiresFreshHumanDecision = true,
            Warnings = ["Collect missing evidence."]
        };
        var actionReview = new WorkspaceApplyActionReview
        {
            ReviewStatus = WorkspaceApplyActionReviewStatuses.BlockedForEvidence,
            Summary = "Blocked for evidence.",
            HumanReviewRequired = true,
            ApprovalCanBeGrantedByThisPackage = false,
            ExecutionCanStartFromThisPackage = false,
            SourceRepoMayBeMutated = false,
            RequestedAction = actionRequest.RequestedAction,
            RecommendedAction = recommendation.RecommendedAction,
            Warnings = ["No usable workspace apply context."]
        };
        var policyContext = BuildPolicyContext(ProjectApprovalDecisions.AllowedByPolicy, actionRequest.RequestedAction, [], []);

        return BuildContext(report, recommendation, actionRequest, actionReview, policyContext, available: false) with
        {
            Warnings = ["Workspace apply context unavailable."]
        };
    }

    private static WorkspaceApplyReportSummary BuildReport(
        string outcome,
        bool sourceRepoMutated,
        bool applyVerified,
        bool postApplyValidationSucceeded) =>
        new()
        {
            RunId = "apply-run",
            WorkspacePath = "C:\\workspaces\\apply-run",
            SourceRepo = "C:\\repo\\IronDeveloper",
            Outcome = outcome,
            SourceRepoMutated = sourceRepoMutated,
            ApplyVerified = applyVerified,
            SourceMatchesWorkspace = applyVerified,
            PostApplyValidationSucceeded = postApplyValidationSucceeded,
            AddCount = outcome == "success" ? 1 : 0,
            ModifyCount = outcome == "success" ? 1 : 0,
            DeleteCount = 0,
            EvidencePaths = ["C:\\workspaces\\apply-run\\.irondev\\runs\\apply-run\\source-report.json"],
            RiskNotes = ["Human should review changed files before commit/PR."]
        };

    private static WorkspaceApplyPolicyContext BuildPolicyContext(
        string decision,
        string requestedAction,
        IReadOnlyList<string> evidencePaths,
        IReadOnlyList<string> riskNotes) =>
        new()
        {
            Decision = decision,
            Reason = "Allowed by project policy.",
            RiskTier = ProjectApprovalRiskTiers.WorkspaceIntent,
            ActionType = WorkspaceApplyPolicyActionTypes.WorkspaceApplyActionReview,
            RequestedAction = requestedAction,
            HumanApprovalRequired = false,
            AutomaticExecutionAllowed = true,
            SourceMutationAllowed = false,
            ExecutionCanStartFromPolicyContext = false,
            ApprovalCanBeGrantedByPolicyContext = false,
            EvidencePaths = evidencePaths,
            RiskNotes = riskNotes
        };

    private static AgentWorkspaceApplyContext BuildContext(
        WorkspaceApplyReportSummary report,
        WorkspaceApplyRecommendation recommendation,
        WorkspaceApplyActionRequest actionRequest,
        WorkspaceApplyActionReview actionReview,
        WorkspaceApplyPolicyContext policyContext,
        bool available) =>
        new()
        {
            ProjectId = "IronDev",
            RunId = report.RunId,
            WorkspacePath = report.WorkspacePath,
            WorkspaceApply = report,
            WorkspaceApplyRecommendation = recommendation,
            WorkspaceApplyActionRequest = actionRequest,
            WorkspaceApplyActionReview = actionReview,
            WorkspaceApplyPolicyContext = policyContext,
            ContextAvailable = available,
            EvidencePaths = report.EvidencePaths,
            Warnings = report.Warnings
        };

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

    private sealed class FakeAgentWorkspaceApplyContextService : IAgentWorkspaceApplyContextService
    {
        private readonly AgentWorkspaceApplyContext _context;

        public FakeAgentWorkspaceApplyContextService(AgentWorkspaceApplyContext context)
        {
            _context = context;
        }

        public int CallCount { get; private set; }

        public Task<AgentWorkspaceApplyContext> CreateAsync(
            AgentWorkspaceApplyContextRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(_context);
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
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "IronDev-CriticWorkspaceApplyContextTests", Guid.NewGuid().ToString("N"));
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
                LikelyAreas = new[] { "workspace apply spine" },
                SafetyRules = new[] { "do not patch automatically" }
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
