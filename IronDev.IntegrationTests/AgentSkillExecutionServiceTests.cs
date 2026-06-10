using IronDev.Core.Agents;
using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;
using IronDev.Core.Agents.WorkspaceApply;
using IronDev.Infrastructure.Services.Agents.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentSkillExecutionServiceTests
{
    [TestMethod]
    public async Task AgentSkillExecution_AllowedReadApplyContext_ExecutesReadOnly()
    {
        var workspaceContext = BuildWorkspaceApplyContext();
        var fake = new FakeAgentWorkspaceApplyContextService(workspaceContext);
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest());

        Assert.AreEqual(AgentSkillExecutionStatuses.Succeeded, result.Status);
        Assert.IsTrue(result.Executed);
        Assert.IsTrue(result.ReadOnlyExecution);
        AssertNoAuthorityFlags(result);
        Assert.IsInstanceOfType(result.Payload, typeof(AgentSkillWorkspaceApplyContextExecutionPayload));
        var payload = (AgentSkillWorkspaceApplyContextExecutionPayload)result.Payload!;
        Assert.IsTrue(payload.WorkspaceApplyContextAvailable);
        Assert.AreEqual("success", payload.Outcome);
        Assert.AreEqual(WorkspaceApplyRecommendedActions.HumanReviewOrCommit, payload.RecommendedAction);
        Assert.AreEqual(WorkspaceApplyRequestedActions.HumanReviewSourceChanges, payload.RequestedAction);
        Assert.AreEqual(WorkspaceApplyActionReviewStatuses.ReadyForHumanReview, payload.ReviewStatus);
        Assert.AreEqual(ProjectApprovalDecisions.ApprovalRequired, payload.PolicyDecision);
        Assert.AreEqual(ProjectApprovalRiskTiers.WorkspaceReporting, payload.RiskTier);
        CollectionAssert.Contains(result.EvidencePaths.ToArray(), "context.json");
        CollectionAssert.Contains(result.EvidencePaths.ToArray(), "source-report.json");
        Assert.AreEqual(1, fake.CallCount);
        Assert.AreEqual("IronDev", fake.LastRequest!.ProjectId);
        Assert.AreEqual("run-1", fake.LastRequest.RunId);
        Assert.AreEqual("C:\\workspaces\\run-1", fake.LastRequest.WorkspacePath);
    }

    [TestMethod]
    public async Task AgentSkillExecution_UnsupportedKnownSkill_BlocksWithoutReadingWorkspaceContext()
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with { SkillId = AgentSkillIds.WorkspaceSourceReport }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedUnsupportedSkill, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.AreEqual(0, fake.CallCount);
    }

    [TestMethod]
    public async Task AgentSkillExecution_UnknownSkill_BlocksUnknownWithoutReadingWorkspaceContext()
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with { SkillKnown = false, SkillId = "missing.skill" }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedUnknownSkill, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.AreEqual(0, fake.CallCount);
    }

    [TestMethod]
    public async Task AgentSkillExecution_PolicyBlocked_BlocksWithoutReadingWorkspaceContext()
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with
            {
                Decision = ProjectApprovalDecisions.BlockedByPolicy,
                PolicyAllowed = false,
                PolicyBlocked = true
            }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByPolicy, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.AreEqual(0, fake.CallCount);
    }

    [TestMethod]
    public async Task AgentSkillExecution_DangerousCapability_BlocksWithoutReadingWorkspaceContext()
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with
            {
                DangerousCapability = true,
                RiskTier = ProjectApprovalRiskTiers.SourceMutation
            }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedDangerousCapability, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.AreEqual(0, fake.CallCount);
    }

    [TestMethod]
    public async Task AgentSkillExecution_ApprovalRequired_BlocksByContext()
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with
            {
                HumanApprovalRequired = true,
                ReviewStatus = AgentSkillRequestReviewStatuses.ApprovalRequired,
                RecommendedNextAction = AgentSkillRequestContextRecommendedActions.RequestSeparateApproval
            }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.IsTrue(result.Blockers.Any(item => item.Contains("approval", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(0, fake.CallCount);
    }

    [TestMethod]
    public async Task AgentSkillExecution_NotReadyForHumanReview_BlocksByContext()
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with { ReviewStatus = AgentSkillRequestReviewStatuses.ApprovalRequired }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.AreEqual(0, fake.CallCount);
    }

    [TestMethod]
    public async Task AgentSkillExecution_WrongRecommendedAction_BlocksByContext()
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with { RecommendedNextAction = AgentSkillRequestContextRecommendedActions.CollectMissingEvidence }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.AreEqual(0, fake.CallCount);
    }

    [DataTestMethod]
    [DataRow("source")]
    [DataRow("workspace")]
    [DataRow("external")]
    [DataRow("ticket")]
    [DataRow("memory")]
    public async Task AgentSkillExecution_MutationWriteOrExternalFlags_BlockByContext(string flag)
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest(BuildContextWithFlag(flag)));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, fake.CallCount);
    }

    [TestMethod]
    public async Task AgentSkillExecution_WorkspaceContextReadFailure_ReturnsFailedWithoutMutation()
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext())
        {
            ThrowOnCreate = true
        };
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest());

        Assert.AreEqual(AgentSkillExecutionStatuses.Failed, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.IsTrue(result.ReadOnlyExecution);
        AssertNoAuthorityFlags(result);
        Assert.IsTrue(result.Warnings.Any(item => item.Contains("Synthetic workspace context failure.", StringComparison.Ordinal)));
        Assert.AreEqual(1, fake.CallCount);
    }

    [TestMethod]
    public async Task AgentSkillExecution_ExecutionIdIsStable()
    {
        var service = AgentSkillExecutionTestServices.Create(new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext()));
        var first = await service.ExecuteAsync(BuildExecutionRequest());
        var second = await service.ExecuteAsync(BuildExecutionRequest());
        var different = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext() with { ContextId = "skill-context-other" }));

        Assert.AreEqual(first.ExecutionId, second.ExecutionId);
        Assert.AreNotEqual(first.ExecutionId, different.ExecutionId);
        Assert.AreEqual("skill-execution-skill-context-1-workspace-read_apply_context", first.ExecutionId);
    }

    [TestMethod]
    public void AgentSkillExecutionService_HasNoMutationExternalOrProcessDependencies()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "IronDev.Infrastructure",
            "Services",
            "Agents",
            "Skills",
            "AgentSkillExecutionService.cs"));

        Assert.IsFalse(source.Contains("File.Copy", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("File.Delete", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("ProcessStartInfo", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("Process.Start", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IDisposableWorkspaceApplyCopyService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IDisposableWorkspaceCommandService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IDisposableWorkspacePrepareService", StringComparison.Ordinal));
                Assert.IsFalse(source.Contains("IGitHub", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("ITicket", StringComparison.Ordinal));
        Assert.IsTrue(source.Contains("IMemoryExecutionGate", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IAgentMemorySilo", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IMemoryIndexingService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("IMemoryImprovementProposalService", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("MemoryService", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AgentSkillExecutionService_IsNotWiredIntoAgents()
    {
        var agentsDirectory = Path.Combine(FindRepositoryRoot(), "IronDev.Infrastructure", "Services", "Agents");
        var wiredAgentFiles = Directory
            .EnumerateFiles(agentsDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path => File.ReadAllText(path).Contains("IAgentSkillExecutionService", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(Array.Empty<string>(), wiredAgentFiles);
    }

    private static AgentSkillExecutionRequest BuildExecutionRequest(AgentSkillRequestContext? context = null) =>
        new()
        {
            SkillRequestContext = context ?? BuildAllowedContext(),
            RequestedByAgent = "CriticAgent",
            ProjectId = "IronDev",
            RunId = "run-1",
            WorkspacePath = "C:\\workspaces\\run-1",
            SourceRepo = "C:\\repo\\IronDeveloper",
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = "run-1",
                ["workspacePath"] = "C:\\workspaces\\run-1"
            }
        };

    private static AgentSkillRequestContext BuildContextWithFlag(string flag)
    {
        var context = BuildAllowedContext();
        return flag switch
        {
            "source" => context with { SourceMutationAllowed = true },
            "workspace" => context with { WorkspaceMutationAllowed = true },
            "external" => context with { ExternalSystemAllowed = true },
            "ticket" => context with { CreatesTicketAllowed = true },
            "memory" => context with { WritesMemoryAllowed = true },
            _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, "Unknown test flag.")
        };
    }

    private static AgentSkillRequestContext BuildAllowedContext() =>
        new()
        {
            ContextId = "skill-context-1",
            RequestId = "skill-request-1",
            ReviewId = "skill-review-1",
            ProjectId = "IronDev",
            AgentName = "CriticAgent",
            SkillId = AgentSkillIds.WorkspaceReadApplyContext,
            Purpose = "Read governed workspace apply context.",
            SkillKnown = true,
            Decision = ProjectApprovalDecisions.AllowedByPolicy,
            ReviewStatus = AgentSkillRequestReviewStatuses.ReadyForHumanReview,
            RiskTier = ProjectApprovalRiskTiers.WorkspaceReporting,
            Category = AgentSkillCategories.WorkspaceContext,
            HumanReviewRequired = true,
            HumanApprovalRequired = false,
            PolicyAllowed = true,
            PolicyBlocked = false,
            DangerousCapability = false,
            ExecutionCanStartFromContext = false,
            ApprovalCanBeGrantedByContext = false,
            SourceMutationAllowed = false,
            WorkspaceMutationAllowed = false,
            ExternalSystemAllowed = false,
            CreatesTicketAllowed = false,
            WritesMemoryAllowed = false,
            RecommendedNextAction = AgentSkillRequestContextRecommendedActions.ReviewRequest,
            EvidencePaths = ["context.json"],
            ParametersSummary = ["runId=run-1", "workspacePath=C:\\workspaces\\run-1"],
            ReviewChecklist = ["Confirm this context is not execution authority."],
            Blockers = [],
            Warnings = [],
            Interpretation = ["Context is reviewable but not execution authority."]
        };

    private static AgentWorkspaceApplyContext BuildWorkspaceApplyContext() =>
        new()
        {
            ProjectId = "IronDev",
            RunId = "run-1",
            WorkspacePath = "C:\\workspaces\\run-1",
            ContextAvailable = true,
            WorkspaceApply = new WorkspaceApplyReportSummary
            {
                RunId = "run-1",
                WorkspacePath = "C:\\workspaces\\run-1",
                SourceRepo = "C:\\repo\\IronDeveloper",
                Outcome = "success",
                Recommendation = "ready_for_human_review",
                SourceRepoMutated = true,
                ApplyVerified = true,
                SourceMatchesWorkspace = true,
                PostApplyValidationSucceeded = true,
                AddCount = 1,
                ModifyCount = 1,
                EvidencePaths = ["source-report.json"],
                RiskNotes = ["Human should review changed files before commit."]
            },
            WorkspaceApplyRecommendation = new WorkspaceApplyRecommendation
            {
                RecommendedAction = WorkspaceApplyRecommendedActions.HumanReviewOrCommit,
                Reason = "Source changes were applied and verified.",
                HumanReviewRequired = true,
                SafeToRetry = false,
                SafeToCommitAfterReview = true,
                SourceReviewRequiredBeforeRetry = false,
                BlocksAutomaticExecution = true,
                EvidencePaths = ["source-report.json"],
                RiskNotes = ["Human review required."]
            },
            WorkspaceApplyActionRequest = new WorkspaceApplyActionRequest
            {
                RequestedAction = WorkspaceApplyRequestedActions.HumanReviewSourceChanges,
                Reason = "Review source changes.",
                HumanApprovalRequired = true,
                AutomaticExecutionAllowed = false,
                MutatesSourceRepo = false,
                RequiresFreshHumanDecision = true,
                EvidencePaths = ["action-request.json"],
                RiskNotes = ["No automatic commit."]
            },
            WorkspaceApplyActionReview = new WorkspaceApplyActionReview
            {
                ReviewStatus = WorkspaceApplyActionReviewStatuses.ReadyForHumanReview,
                Summary = "Ready for human review.",
                HumanReviewRequired = true,
                ApprovalCanBeGrantedByThisPackage = false,
                ExecutionCanStartFromThisPackage = false,
                SourceRepoMayBeMutated = true,
                RequestedAction = WorkspaceApplyRequestedActions.HumanReviewSourceChanges,
                RecommendedAction = WorkspaceApplyRecommendedActions.HumanReviewOrCommit,
                EvidencePaths = ["action-review.json"],
                RiskNotes = ["Review before commit."]
            },
            WorkspaceApplyPolicyContext = new WorkspaceApplyPolicyContext
            {
                Decision = ProjectApprovalDecisions.ApprovalRequired,
                Reason = "Human review remains required.",
                RiskTier = ProjectApprovalRiskTiers.WorkspaceReporting,
                ActionType = WorkspaceApplyPolicyActionTypes.WorkspaceApplyActionReview,
                RequestedAction = WorkspaceApplyRequestedActions.HumanReviewSourceChanges,
                HumanApprovalRequired = true,
                AutomaticExecutionAllowed = false,
                SourceMutationAllowed = false,
                ExecutionCanStartFromPolicyContext = false,
                ApprovalCanBeGrantedByPolicyContext = false,
                EvidencePaths = ["policy-context.json"],
                RiskNotes = ["Policy context is advisory."]
            },
            EvidencePaths = ["source-report.json", "policy-context.json"],
            Warnings = ["Human review is still required."]
        };

    private static void AssertNoAuthorityFlags(AgentSkillExecutionResult result)
    {
        Assert.IsFalse(result.SourceMutated);
        Assert.IsFalse(result.WorkspaceMutated);
        Assert.IsFalse(result.ExternalSystemCalled);
        Assert.IsFalse(result.TicketCreated);
        Assert.IsFalse(result.MemoryWritten);
        Assert.IsFalse(result.ApprovalGranted);
        Assert.IsFalse(result.ShellCommandRun);
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

        public bool ThrowOnCreate { get; init; }

        public AgentWorkspaceApplyContextRequest? LastRequest { get; private set; }

        public Task<AgentWorkspaceApplyContext> CreateAsync(
            AgentWorkspaceApplyContextRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            if (ThrowOnCreate)
                throw new InvalidOperationException("Synthetic workspace context failure.");

            return Task.FromResult(_context);
        }
    }
}
