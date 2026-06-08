using IronDev.Core.Agents;
using IronDev.Core.Agents.ApprovalPolicy;
using IronDev.Core.Agents.WorkspaceApply;
using IronDev.Infrastructure.Services.Agents.ApprovalPolicy;

namespace IronDev.Infrastructure.Services.Agents.WorkspaceApply;

public sealed class AgentWorkspaceApplyContextService : IAgentWorkspaceApplyContextService
{
    private readonly IWorkspaceApplyReportReader _reportReader;
    private readonly IWorkspaceApplyRecommendationService _recommendationService;
    private readonly IWorkspaceApplyActionRequestService _actionRequestService;
    private readonly IWorkspaceApplyActionReviewService _actionReviewService;
    private readonly IWorkspaceApplyPolicyContextService _policyContextService;

    public AgentWorkspaceApplyContextService()
        : this(
            new WorkspaceApplyReportReader(),
            new WorkspaceApplyRecommendationService(),
            new WorkspaceApplyActionRequestService(),
            new WorkspaceApplyActionReviewService(),
            new WorkspaceApplyPolicyContextService(new ProjectApprovalPolicyEvaluator()))
    {
    }

    public AgentWorkspaceApplyContextService(
        IWorkspaceApplyReportReader reportReader,
        IWorkspaceApplyRecommendationService recommendationService,
        IWorkspaceApplyActionRequestService actionRequestService,
        IWorkspaceApplyActionReviewService actionReviewService,
        IWorkspaceApplyPolicyContextService policyContextService)
    {
        _reportReader = reportReader ?? throw new ArgumentNullException(nameof(reportReader));
        _recommendationService = recommendationService ?? throw new ArgumentNullException(nameof(recommendationService));
        _actionRequestService = actionRequestService ?? throw new ArgumentNullException(nameof(actionRequestService));
        _actionReviewService = actionReviewService ?? throw new ArgumentNullException(nameof(actionReviewService));
        _policyContextService = policyContextService ?? throw new ArgumentNullException(nameof(policyContextService));
    }

    public async Task<AgentWorkspaceApplyContext> CreateAsync(
        AgentWorkspaceApplyContextRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var warnings = new List<string>();
        var report = await ReadReportAsync(request, warnings, cancellationToken).ConfigureAwait(false);
        var recommendation = BuildRecommendation(report);
        var actionRequest = BuildActionRequest(report, recommendation);
        var actionReview = BuildActionReview(report, recommendation, actionRequest);
        var policyContext = BuildPolicyContext(request, report, recommendation, actionRequest, actionReview);
        var evidencePaths = MergeEvidencePaths(report, recommendation, actionRequest, actionReview, policyContext);

        return new AgentWorkspaceApplyContext
        {
            ProjectId = request.ProjectId,
            RunId = report.RunId,
            WorkspacePath = report.WorkspacePath,
            WorkspaceApply = report,
            WorkspaceApplyRecommendation = recommendation,
            WorkspaceApplyActionRequest = actionRequest,
            WorkspaceApplyActionReview = actionReview,
            WorkspaceApplyPolicyContext = policyContext,
            ContextAvailable = !string.Equals(report.Outcome, "unavailable", StringComparison.OrdinalIgnoreCase),
            Warnings = warnings
                .Concat(report.Warnings)
                .Concat(recommendation.Warnings)
                .Concat(actionRequest.Warnings)
                .Concat(actionReview.Warnings)
                .Concat(policyContext.Warnings)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            EvidencePaths = evidencePaths
        };
    }

    private async Task<WorkspaceApplyReportSummary> ReadReportAsync(
        AgentWorkspaceApplyContextRequest request,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _reportReader.ReadAsync(
                new WorkspaceApplyReportRequest
                {
                    RunId = request.RunId,
                    WorkspacePath = request.WorkspacePath
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            warnings.Add($"Workspace apply report could not be read: {exception.Message}");
            return new WorkspaceApplyReportSummary
            {
                RunId = request.RunId,
                WorkspacePath = request.WorkspacePath,
                Outcome = "unavailable",
                Warnings = [$"Workspace apply report could not be read: {exception.Message}"]
            };
        }
    }

    private WorkspaceApplyRecommendation BuildRecommendation(WorkspaceApplyReportSummary report)
    {
        try
        {
            return _recommendationService.Recommend(new WorkspaceApplyRecommendationRequest
            {
                Report = report
            });
        }
        catch (Exception exception)
        {
            return new WorkspaceApplyRecommendation
            {
                RecommendedAction = WorkspaceApplyRecommendedActions.CollectMissingEvidence,
                Reason = "Workspace apply evidence is incomplete or inconsistent.",
                HumanReviewRequired = true,
                SafeToRetry = false,
                SafeToCommitAfterReview = false,
                SourceReviewRequiredBeforeRetry = true,
                BlocksAutomaticExecution = true,
                EvidencePaths = report.EvidencePaths,
                RiskNotes = report.RiskNotes,
                Warnings = [$"Workspace apply recommendation could not be produced: {exception.Message}"]
            };
        }
    }

    private WorkspaceApplyActionRequest BuildActionRequest(
        WorkspaceApplyReportSummary report,
        WorkspaceApplyRecommendation recommendation)
    {
        try
        {
            return _actionRequestService.Create(new WorkspaceApplyActionRequestInput
            {
                Report = report,
                Recommendation = recommendation
            });
        }
        catch (Exception exception)
        {
            return new WorkspaceApplyActionRequest
            {
                RequestedAction = string.Equals(recommendation.RecommendedAction, WorkspaceApplyRecommendedActions.NoWorkspaceApplyReport, StringComparison.Ordinal)
                    ? WorkspaceApplyRequestedActions.NoActionAvailable
                    : WorkspaceApplyRequestedActions.CollectMissingEvidence,
                Reason = "Workspace apply evidence is incomplete or inconsistent.",
                HumanApprovalRequired = true,
                AutomaticExecutionAllowed = false,
                MutatesSourceRepo = false,
                RequiresFreshHumanDecision = true,
                SuggestedCommand = null,
                SuggestedCommandArguments = [],
                Preconditions =
                [
                    "identify missing evidence",
                    "rerun only the appropriate governed command manually",
                    "do not infer success"
                ],
                EvidencePaths = report.EvidencePaths,
                RiskNotes = report.RiskNotes,
                Warnings = [$"Workspace apply action request could not be produced: {exception.Message}"]
            };
        }
    }

    private WorkspaceApplyActionReview BuildActionReview(
        WorkspaceApplyReportSummary report,
        WorkspaceApplyRecommendation recommendation,
        WorkspaceApplyActionRequest actionRequest)
    {
        try
        {
            return _actionReviewService.Create(new WorkspaceApplyActionReviewInput
            {
                Report = report,
                Recommendation = recommendation,
                ActionRequest = actionRequest
            });
        }
        catch (Exception exception)
        {
            return new WorkspaceApplyActionReview
            {
                ReviewStatus = WorkspaceApplyActionReviewStatuses.BlockedForEvidence,
                Summary = "Workspace apply evidence is missing or inconsistent. More evidence is required before deciding next action.",
                HumanReviewRequired = true,
                ApprovalCanBeGrantedByThisPackage = false,
                ExecutionCanStartFromThisPackage = false,
                SourceRepoMayBeMutated = report.SourceRepoMutated,
                RequestedAction = actionRequest.RequestedAction,
                RecommendedAction = recommendation.RecommendedAction,
                ReviewChecklist =
                [
                    "Identify missing evidence.",
                    "Do not infer success.",
                    "Rerun only the appropriate governed command manually if needed."
                ],
                EvidencePaths = actionRequest.EvidencePaths,
                RiskNotes = actionRequest.RiskNotes,
                Blockers = [$"Workspace apply action review could not be produced: {exception.Message}"],
                Warnings = actionRequest.Warnings
            };
        }
    }

    private WorkspaceApplyPolicyContext BuildPolicyContext(
        AgentWorkspaceApplyContextRequest request,
        WorkspaceApplyReportSummary report,
        WorkspaceApplyRecommendation recommendation,
        WorkspaceApplyActionRequest actionRequest,
        WorkspaceApplyActionReview actionReview)
    {
        try
        {
            return _policyContextService.Create(new WorkspaceApplyPolicyContextInput
            {
                ProjectId = request.ProjectId,
                Report = report,
                Recommendation = recommendation,
                ActionRequest = actionRequest,
                ActionReview = actionReview,
                Policy = request.Policy ?? ProjectApprovalPolicy.CreateDefault(request.ProjectId)
            });
        }
        catch (Exception exception)
        {
            return new WorkspaceApplyPolicyContext
            {
                Decision = ProjectApprovalDecisions.ApprovalRequired,
                Reason = "Workspace apply policy context could not be produced.",
                RiskTier = ProjectApprovalRiskTiers.WorkspaceReporting,
                ActionType = WorkspaceApplyPolicyActionTypes.WorkspaceApplyActionReview,
                RequestedAction = actionRequest.RequestedAction,
                HumanApprovalRequired = true,
                AutomaticExecutionAllowed = false,
                SourceMutationAllowed = false,
                ExecutionCanStartFromPolicyContext = false,
                ApprovalCanBeGrantedByPolicyContext = false,
                MatchedRuleDescription = null,
                EvidencePaths = actionReview.EvidencePaths,
                RiskNotes = actionReview.RiskNotes,
                Warnings = [$"Workspace apply policy context could not be produced: {exception.Message}"]
            };
        }
    }

    private static IReadOnlyList<string> MergeEvidencePaths(
        WorkspaceApplyReportSummary report,
        WorkspaceApplyRecommendation recommendation,
        WorkspaceApplyActionRequest actionRequest,
        WorkspaceApplyActionReview actionReview,
        WorkspaceApplyPolicyContext policyContext) =>
        report.EvidencePaths
            .Concat(recommendation.EvidencePaths)
            .Concat(actionRequest.EvidencePaths)
            .Concat(actionReview.EvidencePaths)
            .Concat(policyContext.EvidencePaths)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
