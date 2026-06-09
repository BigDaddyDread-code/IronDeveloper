using IronDev.Core.Agents;
using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;
using IronDev.Core.Agents.WorkspaceApply;
using IronDev.Infrastructure.Services.Agents.Skills;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentReportingIntentSkillExecutionServiceTests
{
    [TestMethod]
    public async Task AgentReportingIntentSkillExecution_RecommendApplyAction_ExecutesReadOnlyPayload()
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest(AgentSkillIds.WorkspaceRecommendApplyAction));

        AssertSucceededReadOnly(result);
        Assert.IsInstanceOfType(result.Payload, typeof(AgentSkillWorkspaceApplyRecommendationExecutionPayload));
        var payload = (AgentSkillWorkspaceApplyRecommendationExecutionPayload)result.Payload!;
        Assert.IsTrue(payload.RecommendationAvailable);
        Assert.AreEqual(WorkspaceApplyRecommendedActions.HumanReviewOrCommit, payload.RecommendedAction);
        CollectionAssert.Contains(payload.Rationale.ToArray(), "Source changes were applied and verified.");
        CollectionAssert.Contains(payload.EvidencePaths.ToArray(), "source-report.json");
        CollectionAssert.Contains(payload.RiskNotes.ToArray(), "Human review required.");
        Assert.AreEqual(1, fake.CallCount);
    }

    [TestMethod]
    public async Task AgentReportingIntentSkillExecution_RequestApplyAction_ExecutesReadOnlyPayload()
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest(AgentSkillIds.WorkspaceCreateActionRequest));

        AssertSucceededReadOnly(result);
        Assert.IsInstanceOfType(result.Payload, typeof(AgentSkillWorkspaceApplyActionRequestExecutionPayload));
        var payload = (AgentSkillWorkspaceApplyActionRequestExecutionPayload)result.Payload!;
        Assert.IsTrue(payload.ActionRequestAvailable);
        Assert.AreEqual(WorkspaceApplyRequestedActions.HumanReviewSourceChanges, payload.RequestedAction);
        Assert.AreEqual("CriticAgent", payload.RequestedByAgent);
        Assert.IsFalse(result.ApprovalGranted);
        Assert.IsFalse(result.SourceMutated);
        Assert.IsFalse(result.WorkspaceMutated);
        Assert.AreEqual(1, fake.CallCount);
    }

    [TestMethod]
    public async Task AgentReportingIntentSkillExecution_ReviewApplyAction_ExecutesReadOnlyPayload()
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest(AgentSkillIds.WorkspaceCreateActionReview));

        AssertSucceededReadOnly(result);
        Assert.IsInstanceOfType(result.Payload, typeof(AgentSkillWorkspaceApplyActionReviewExecutionPayload));
        var payload = (AgentSkillWorkspaceApplyActionReviewExecutionPayload)result.Payload!;
        Assert.IsTrue(payload.ActionReviewAvailable);
        Assert.AreEqual(WorkspaceApplyActionReviewStatuses.ReadyForHumanReview, payload.ReviewStatus);
        Assert.IsTrue(payload.SourceRepoMayBeMutated);
        Assert.IsFalse(result.ApprovalGranted);
        Assert.IsFalse(result.SourceMutated);
        Assert.IsFalse(result.WorkspaceMutated);
        Assert.AreEqual(1, fake.CallCount);
    }

    [DataTestMethod]
    [DataRow(AgentSkillIds.WorkspaceApplyCopy)]
    public async Task AgentReportingIntentSkillExecution_UnsupportedWorkspaceSkillsRemainBlocked(string skillId)
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest(skillId));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedUnsupportedSkill, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, fake.CallCount);
    }

    [TestMethod]
    public async Task AgentReportingIntentSkillExecution_UnknownSkillRemainsBlocked()
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext("missing.skill") with { SkillKnown = false }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedUnknownSkill, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.AreEqual(0, fake.CallCount);
    }

    [TestMethod]
    public async Task AgentReportingIntentSkillExecution_PolicyBlockedRemainsBlocked()
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext(AgentSkillIds.WorkspaceRecommendApplyAction) with
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
    public async Task AgentReportingIntentSkillExecution_DangerousContextRemainsBlocked()
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext(AgentSkillIds.WorkspaceRecommendApplyAction) with
            {
                DangerousCapability = true,
                RiskTier = ProjectApprovalRiskTiers.SourceMutation
            }));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedDangerousCapability, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.AreEqual(0, fake.CallCount);
    }

    [TestMethod]
    public async Task AgentReportingIntentSkillExecution_ApprovalRequiredRemainsBlocked()
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest(
            BuildAllowedContext(AgentSkillIds.WorkspaceRecommendApplyAction) with
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

    [DataTestMethod]
    [DataRow("review")]
    [DataRow("action")]
    public async Task AgentReportingIntentSkillExecution_ContextNotReadyRemainsBlocked(string badContext)
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var service = AgentSkillExecutionTestServices.Create(fake);
        var context = BuildAllowedContext(AgentSkillIds.WorkspaceRecommendApplyAction);
        context = badContext switch
        {
            "review" => context with { ReviewStatus = AgentSkillRequestReviewStatuses.ApprovalRequired },
            "action" => context with { RecommendedNextAction = AgentSkillRequestContextRecommendedActions.CollectMissingEvidence },
            _ => context
        };

        var result = await service.ExecuteAsync(BuildExecutionRequest(context));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.AreEqual(0, fake.CallCount);
    }

    [DataTestMethod]
    [DataRow(AgentSkillIds.WorkspaceRecommendApplyAction, "source")]
    [DataRow(AgentSkillIds.WorkspaceRecommendApplyAction, "workspace")]
    [DataRow(AgentSkillIds.WorkspaceRecommendApplyAction, "external")]
    [DataRow(AgentSkillIds.WorkspaceRecommendApplyAction, "ticket")]
    [DataRow(AgentSkillIds.WorkspaceRecommendApplyAction, "memory")]
    [DataRow(AgentSkillIds.WorkspaceRecommendApplyAction, "approval")]
    [DataRow(AgentSkillIds.WorkspaceRecommendApplyAction, "execution")]
    [DataRow(AgentSkillIds.WorkspaceCreateActionRequest, "source")]
    [DataRow(AgentSkillIds.WorkspaceCreateActionRequest, "workspace")]
    [DataRow(AgentSkillIds.WorkspaceCreateActionRequest, "external")]
    [DataRow(AgentSkillIds.WorkspaceCreateActionRequest, "ticket")]
    [DataRow(AgentSkillIds.WorkspaceCreateActionRequest, "memory")]
    [DataRow(AgentSkillIds.WorkspaceCreateActionRequest, "approval")]
    [DataRow(AgentSkillIds.WorkspaceCreateActionRequest, "execution")]
    [DataRow(AgentSkillIds.WorkspaceCreateActionReview, "source")]
    [DataRow(AgentSkillIds.WorkspaceCreateActionReview, "workspace")]
    [DataRow(AgentSkillIds.WorkspaceCreateActionReview, "external")]
    [DataRow(AgentSkillIds.WorkspaceCreateActionReview, "ticket")]
    [DataRow(AgentSkillIds.WorkspaceCreateActionReview, "memory")]
    [DataRow(AgentSkillIds.WorkspaceCreateActionReview, "approval")]
    [DataRow(AgentSkillIds.WorkspaceCreateActionReview, "execution")]
    public async Task AgentReportingIntentSkillExecution_AuthorityFlagsBlockSupportedReportingIntentSkills(
        string skillId,
        string flag)
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest(BuildContextWithFlag(skillId, flag)));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        AssertNoAuthorityFlags(result);
        Assert.AreEqual(0, fake.CallCount);
    }

    [TestMethod]
    public async Task AgentReportingIntentSkillExecution_MissingRecommendation_BlocksRecommendSkill()
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext() with
        {
            WorkspaceApplyRecommendation = null
        });
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest(AgentSkillIds.WorkspaceRecommendApplyAction));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.IsTrue(result.Blockers.Contains("Workspace apply recommendation was not available."));
        Assert.AreEqual(1, fake.CallCount);
    }

    [TestMethod]
    public async Task AgentReportingIntentSkillExecution_MissingActionRequest_BlocksRequestSkill()
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext() with
        {
            WorkspaceApplyActionRequest = null
        });
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest(AgentSkillIds.WorkspaceCreateActionRequest));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.IsTrue(result.Blockers.Contains("Workspace apply action request was not available."));
        Assert.AreEqual(1, fake.CallCount);
    }

    [TestMethod]
    public async Task AgentReportingIntentSkillExecution_MissingActionReview_BlocksReviewSkill()
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext() with
        {
            WorkspaceApplyActionReview = null
        });
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest(AgentSkillIds.WorkspaceCreateActionReview));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.IsTrue(result.Blockers.Contains("Workspace apply action review was not available."));
        Assert.AreEqual(1, fake.CallCount);
    }

    [TestMethod]
    public async Task AgentReportingIntentSkillExecution_ReadFailure_ReturnsFailedWithoutMutation()
    {
        var fake = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext())
        {
            ThrowOnCreate = true
        };
        var service = AgentSkillExecutionTestServices.Create(fake);

        var result = await service.ExecuteAsync(BuildExecutionRequest(AgentSkillIds.WorkspaceRecommendApplyAction));

        Assert.AreEqual(AgentSkillExecutionStatuses.Failed, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.IsTrue(result.ReadOnlyExecution);
        AssertNoAuthorityFlags(result);
        Assert.IsTrue(result.Warnings.Any(item => item.Contains("Synthetic workspace context failure.", StringComparison.Ordinal)));
        Assert.AreEqual(1, fake.CallCount);
    }

    [TestMethod]
    public void AgentReportingIntentSkillExecutionService_HasNoMutationExternalOrProcessDependencies()
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
        Assert.IsFalse(source.Contains("IMemory", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("MemoryService", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AgentReportingIntentSkillExecutionService_IsNotWiredIntoAgents()
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

    private static AgentSkillExecutionRequest BuildExecutionRequest(string skillId) =>
        BuildExecutionRequest(BuildAllowedContext(skillId));

    private static AgentSkillExecutionRequest BuildExecutionRequest(AgentSkillRequestContext context) =>
        new()
        {
            SkillRequestContext = context,
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

    private static AgentSkillRequestContext BuildContextWithFlag(string skillId, string flag)
    {
        var context = BuildAllowedContext(skillId);
        return flag switch
        {
            "source" => context with { SourceMutationAllowed = true },
            "workspace" => context with { WorkspaceMutationAllowed = true },
            "external" => context with { ExternalSystemAllowed = true },
            "ticket" => context with { CreatesTicketAllowed = true },
            "memory" => context with { WritesMemoryAllowed = true },
            "approval" => context with { ApprovalCanBeGrantedByContext = true },
            "execution" => context with { ExecutionCanStartFromContext = true },
            _ => throw new ArgumentOutOfRangeException(nameof(flag), flag, "Unknown test flag.")
        };
    }

    private static AgentSkillRequestContext BuildAllowedContext(string skillId) =>
        new()
        {
            ContextId = $"skill-context-{skillId.Replace('.', '-')}",
            RequestId = "skill-request-1",
            ReviewId = "skill-review-1",
            ProjectId = "IronDev",
            AgentName = "CriticAgent",
            SkillId = skillId,
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

    private static void AssertSucceededReadOnly(AgentSkillExecutionResult result)
    {
        Assert.AreEqual(AgentSkillExecutionStatuses.Succeeded, result.Status);
        Assert.IsTrue(result.Executed);
        Assert.IsTrue(result.ReadOnlyExecution);
        AssertNoAuthorityFlags(result);
    }

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

        public Task<AgentWorkspaceApplyContext> CreateAsync(
            AgentWorkspaceApplyContextRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (ThrowOnCreate)
                throw new InvalidOperationException("Synthetic workspace context failure.");

            return Task.FromResult(_context);
        }
    }
}
