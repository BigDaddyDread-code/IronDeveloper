using IronDev.Core.AgentMemory;
using IronDev.Core.AgentMemory.Execution;
using IronDev.Core.Agents;
using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.Skills;
using IronDev.Core.Agents.WorkspaceApply;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests;

[TestClass]
public sealed class AgentSkillMemoryExecutionGateIntegrationTests
{
    [TestMethod]
    public async Task AgentSkillExecution_MemoryBackedRequestWithoutGate_BlocksBeforeExecution()
    {
        var workspaceContext = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var service = AgentSkillExecutionTestServices.Create(workspaceContext);

        var result = await service.ExecuteAsync(BuildExecutionRequest(memoryContext: BuildMemoryContext()));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByMemory, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.AreEqual(0, workspaceContext.CallCount);
        Assert.IsNotNull(result.MemoryEvidence);
        Assert.AreEqual(MemoryExecutionGateDecision.Blocked, result.MemoryEvidence!.GateDecision);
        CollectionAssert.Contains(result.MemoryEvidence.IssueCodes.ToArray(), MemoryGovernanceIssueCode.GovernanceResultMismatch);
    }

    [TestMethod]
    public async Task AgentSkillExecution_MemoryGateBlock_DoesNotExecuteSkill()
    {
        var workspaceContext = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var gate = new FakeMemoryExecutionGate(BuildGateResult(MemoryExecutionGateDecision.Blocked, mayProceed: false));
        var service = AgentSkillExecutionTestServices.Create(workspaceContext, memoryExecutionGate: gate);

        var result = await service.ExecuteAsync(BuildExecutionRequest(memoryContext: BuildMemoryContext()));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByMemory, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.AreEqual(1, gate.CallCount);
        Assert.AreEqual(0, workspaceContext.CallCount);
        Assert.AreEqual(MemoryExecutionGateDecision.Blocked, result.MemoryEvidence!.GateDecision);
    }

    [TestMethod]
    public async Task AgentSkillExecution_MemoryWarnStillRequiresNormalApproval()
    {
        var workspaceContext = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var gate = new FakeMemoryExecutionGate(BuildGateResult(MemoryExecutionGateDecision.WarningRequiresOuterApproval));
        var service = AgentSkillExecutionTestServices.Create(workspaceContext, memoryExecutionGate: gate);
        var context = BuildAllowedContext() with
        {
            HumanApprovalRequired = true,
            ReviewStatus = AgentSkillRequestReviewStatuses.ApprovalRequired,
            RecommendedNextAction = AgentSkillRequestContextRecommendedActions.RequestSeparateApproval
        };

        var result = await service.ExecuteAsync(BuildExecutionRequest(context, BuildMemoryContext()));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.IsTrue(result.Blockers.Any(item => item.Contains("approval", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(0, workspaceContext.CallCount);
        Assert.AreEqual(MemoryExecutionGateDecision.WarningRequiresOuterApproval, result.MemoryEvidence!.GateDecision);
    }

    [TestMethod]
    public async Task AgentSkillExecution_MemoryAllowStillRequiresNormalApproval()
    {
        var workspaceContext = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var gate = new FakeMemoryExecutionGate(BuildGateResult(MemoryExecutionGateDecision.Allowed));
        var service = AgentSkillExecutionTestServices.Create(workspaceContext, memoryExecutionGate: gate);
        var context = BuildAllowedContext() with
        {
            HumanApprovalRequired = true,
            ReviewStatus = AgentSkillRequestReviewStatuses.ApprovalRequired,
            RecommendedNextAction = AgentSkillRequestContextRecommendedActions.RequestSeparateApproval
        };

        var result = await service.ExecuteAsync(BuildExecutionRequest(context, BuildMemoryContext()));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.AreEqual(0, workspaceContext.CallCount);
        Assert.AreEqual(MemoryExecutionGateDecision.Allowed, result.MemoryEvidence!.GateDecision);
    }

    [TestMethod]
    public async Task AgentSkillExecution_PolicyBlockStillWinsOverMemoryAllow()
    {
        var workspaceContext = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var gate = new FakeMemoryExecutionGate(BuildGateResult(MemoryExecutionGateDecision.Allowed));
        var service = AgentSkillExecutionTestServices.Create(workspaceContext, memoryExecutionGate: gate);
        var context = BuildAllowedContext() with
        {
            Decision = ProjectApprovalDecisions.BlockedByPolicy,
            PolicyAllowed = false,
            PolicyBlocked = true
        };

        var result = await service.ExecuteAsync(BuildExecutionRequest(context, BuildMemoryContext()));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByPolicy, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.AreEqual(1, gate.CallCount);
        Assert.AreEqual(0, workspaceContext.CallCount);
        Assert.AreEqual(MemoryExecutionGateDecision.Allowed, result.MemoryEvidence!.GateDecision);
    }

    [DataTestMethod]
    [DataRow(MemoryExecutionGateDecision.Allowed)]
    [DataRow(MemoryExecutionGateDecision.WarningRequiresOuterApproval)]
    public async Task AgentSkillExecution_MemoryGateDoesNotAuthorizeSourceMutation(MemoryExecutionGateDecision decision)
    {
        var workspaceContext = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var gate = new FakeMemoryExecutionGate(BuildGateResult(decision));
        var service = AgentSkillExecutionTestServices.Create(workspaceContext, memoryExecutionGate: gate);
        var context = BuildAllowedContext() with { SourceMutationAllowed = true };

        var result = await service.ExecuteAsync(BuildExecutionRequest(context, BuildMemoryContext()));

        Assert.AreEqual(AgentSkillExecutionStatuses.BlockedByContext, result.Status);
        Assert.IsFalse(result.Executed);
        Assert.IsFalse(result.SourceMutated);
        Assert.AreEqual(0, workspaceContext.CallCount);
        Assert.AreEqual(decision, result.MemoryEvidence!.GateDecision);
    }

    [TestMethod]
    public async Task AgentSkillExecution_MemoryGateEvidenceAppearsOnSuccessfulResult()
    {
        var workspaceContext = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var gate = new FakeMemoryExecutionGate(BuildGateResult(MemoryExecutionGateDecision.Allowed));
        var service = AgentSkillExecutionTestServices.Create(workspaceContext, memoryExecutionGate: gate);

        var result = await service.ExecuteAsync(BuildExecutionRequest(memoryContext: BuildMemoryContext()));

        Assert.AreEqual(AgentSkillExecutionStatuses.Succeeded, result.Status);
        Assert.IsTrue(result.Executed);
        Assert.AreEqual(1, workspaceContext.CallCount);
        Assert.IsNotNull(result.MemoryEvidence);
        Assert.IsTrue(result.MemoryEvidence!.IsMemoryBacked);
        Assert.AreEqual("memory-governance-test", result.MemoryEvidence.GovernanceCheckId);
        CollectionAssert.Contains(result.MemoryEvidence.MemoryItemIds.ToArray(), "memory-1");
        CollectionAssert.Contains(result.MemoryEvidence.InfluenceIds.ToArray(), "influence-1");
    }

    [TestMethod]
    public async Task AgentSkillExecution_NonMemoryBehaviorUnchanged()
    {
        var workspaceContext = new FakeAgentWorkspaceApplyContextService(BuildWorkspaceApplyContext());
        var gate = new FakeMemoryExecutionGate(BuildGateResult(MemoryExecutionGateDecision.Blocked, mayProceed: false));
        var service = AgentSkillExecutionTestServices.Create(workspaceContext, memoryExecutionGate: gate);

        var result = await service.ExecuteAsync(BuildExecutionRequest());

        Assert.AreEqual(AgentSkillExecutionStatuses.Succeeded, result.Status);
        Assert.IsTrue(result.Executed);
        Assert.IsNull(result.MemoryEvidence);
        Assert.AreEqual(0, gate.CallCount);
        Assert.AreEqual(1, workspaceContext.CallCount);
    }

    private static AgentSkillExecutionRequest BuildExecutionRequest(
        AgentSkillRequestContext? context = null,
        MemoryBackedExecutionContext? memoryContext = null) =>
        new()
        {
            SkillRequestContext = context ?? BuildAllowedContext(),
            RequestedByAgent = "CriticAgent",
            ProjectId = "IronDev",
            RunId = "run-1",
            WorkspacePath = "C:\\workspaces\\run-1",
            SourceRepo = "C:\\repo\\IronDeveloper",
            MemoryExecutionContext = memoryContext,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = "run-1",
                ["workspacePath"] = "C:\\workspaces\\run-1"
            }
        };

    private static AgentSkillRequestContext BuildAllowedContext() =>
        new()
        {
            ContextId = "skill-context-memory-gate",
            RequestId = "skill-request-memory-gate",
            ReviewId = "skill-review-memory-gate",
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

    private static MemoryBackedExecutionContext BuildMemoryContext() =>
        new()
        {
            Scope = new AgentMemoryScope
            {
                TenantId = "tenant-1",
                ProjectId = "project-1",
                CampaignId = "campaign-1",
                RunId = "run-1",
                AgentId = "critic-agent"
            },
            ActionType = MemoryGovernanceActionType.ContextUse,
            DecisionId = "decision-1",
            ReferencedArtifacts =
            [
                new MemoryBackedExecutionReference
                {
                    MemoryItemId = "memory-1",
                    InfluenceId = "influence-1",
                    DecisionId = "decision-1",
                    ThoughtLedgerEntryId = "thought-1"
                }
            ],
            RequestedAt = new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero),
            CorrelationId = "correlation-1",
            InfluenceRecordRequired = false
        };

    private static MemoryExecutionGateResult BuildGateResult(
        MemoryExecutionGateDecision decision,
        bool mayProceed = true)
    {
        var governanceDecision = decision == MemoryExecutionGateDecision.Blocked
            ? MemoryGovernanceDecision.Block
            : decision == MemoryExecutionGateDecision.WarningRequiresOuterApproval
                ? MemoryGovernanceDecision.Warn
                : MemoryGovernanceDecision.Allow;
        return new MemoryExecutionGateResult
        {
            Decision = decision,
            MayProceedToPolicyGate = mayProceed,
            Summary = $"Synthetic memory gate result: {decision}.",
            Issues = decision == MemoryExecutionGateDecision.WarningRequiresOuterApproval
                ?
                [
                    new MemoryGovernanceIssue
                    {
                        Code = MemoryGovernanceIssueCode.LowConfidenceMemoryUse,
                        Severity = MemoryGovernanceIssueSeverity.Warning,
                        Summary = "Synthetic warning."
                    }
                ]
                : [],
            Evidence = new MemoryExecutionEvidence
            {
                IsMemoryBacked = true,
                GovernanceCheckId = "memory-governance-test",
                DecisionId = "decision-1",
                GateDecision = decision,
                GovernanceDecision = governanceDecision,
                IssueCodes = decision == MemoryExecutionGateDecision.WarningRequiresOuterApproval
                    ? [MemoryGovernanceIssueCode.LowConfidenceMemoryUse]
                    : [],
                MemoryItemIds = ["memory-1"],
                InfluenceIds = ["influence-1"],
                HandoffMemorySliceIds = []
            }
        };
    }

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
                EvidencePaths = ["source-report.json"],
                RiskNotes = ["Human should review changed files before commit."]
            },
            EvidencePaths = ["source-report.json"],
            Warnings = []
        };

    private sealed class FakeMemoryExecutionGate : IMemoryExecutionGate
    {
        private readonly MemoryExecutionGateResult _result;

        public FakeMemoryExecutionGate(MemoryExecutionGateResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public Task<MemoryExecutionGateResult> EvaluateAsync(
            MemoryBackedExecutionContext? context,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
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
            CallCount++;
            return Task.FromResult(_context);
        }
    }
}
