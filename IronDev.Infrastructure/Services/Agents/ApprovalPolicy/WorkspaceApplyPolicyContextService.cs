using IronDev.Core.Agents;
using IronDev.Core.Agents.ApprovalPolicy;

namespace IronDev.Infrastructure.Services.Agents.ApprovalPolicy;

public sealed class WorkspaceApplyPolicyContextService : IWorkspaceApplyPolicyContextService
{
    private readonly IProjectApprovalPolicyEvaluator _policyEvaluator;

    public WorkspaceApplyPolicyContextService(IProjectApprovalPolicyEvaluator policyEvaluator)
    {
        _policyEvaluator = policyEvaluator ?? throw new ArgumentNullException(nameof(policyEvaluator));
    }

    public WorkspaceApplyPolicyContext Create(WorkspaceApplyPolicyContextInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Report);
        ArgumentNullException.ThrowIfNull(input.Recommendation);
        ArgumentNullException.ThrowIfNull(input.ActionRequest);
        ArgumentNullException.ThrowIfNull(input.ActionReview);
        ArgumentNullException.ThrowIfNull(input.Policy);

        var warnings = new List<string>();
        var riskTier = MapRiskTier(input.ActionRequest.RequestedAction, warnings);
        var evaluation = _policyEvaluator.Evaluate(new ProjectApprovalEvaluationRequest
        {
            ProjectId = input.ProjectId,
            RiskTier = riskTier,
            ActionType = WorkspaceApplyPolicyActionTypes.WorkspaceApplyActionReview,
            RequestedAction = input.ActionRequest.RequestedAction,
            EvidenceHash = null,
            RunId = input.Report.RunId,
            WorkspacePath = input.Report.WorkspacePath,
            SourceRepo = input.Report.SourceRepo,
            Policy = input.Policy
        });

        warnings.AddRange(evaluation.Warnings);

        var riskNotes = input.Report.RiskNotes
            .Concat(input.Recommendation.RiskNotes)
            .Concat(input.ActionRequest.RiskNotes)
            .Concat(input.ActionReview.RiskNotes)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (input.ActionReview.SourceRepoMayBeMutated)
            riskNotes.Add("Workspace apply review concerns a source repository that may already have been mutated; policy context is advisory and cannot authorize further mutation.");

        return new WorkspaceApplyPolicyContext
        {
            Decision = evaluation.Decision,
            Reason = evaluation.Reason,
            RiskTier = riskTier,
            ActionType = WorkspaceApplyPolicyActionTypes.WorkspaceApplyActionReview,
            RequestedAction = input.ActionRequest.RequestedAction,
            HumanApprovalRequired = evaluation.HumanApprovalRequired,
            AutomaticExecutionAllowed = evaluation.AutomaticExecutionAllowed,
            SourceMutationAllowed = false,
            ExecutionCanStartFromPolicyContext = false,
            ApprovalCanBeGrantedByPolicyContext = false,
            MatchedRuleDescription = evaluation.MatchedRuleDescription,
            EvidencePaths = MergeEvidencePaths(input),
            RiskNotes = riskNotes.Distinct(StringComparer.Ordinal).ToArray(),
            Warnings = warnings
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static string MapRiskTier(string requestedAction, List<string> warnings)
    {
        switch (requestedAction)
        {
            case WorkspaceApplyRequestedActions.HumanReviewSourceChanges:
            case WorkspaceApplyRequestedActions.ReviewSourceBeforeRetry:
            case WorkspaceApplyRequestedActions.FixValidationFailure:
            case WorkspaceApplyRequestedActions.RetryGovernedSpineAfterFixingBlockers:
                return ProjectApprovalRiskTiers.WorkspaceIntent;
            case WorkspaceApplyRequestedActions.InspectFailureEvidence:
            case WorkspaceApplyRequestedActions.CollectMissingEvidence:
            case WorkspaceApplyRequestedActions.NoActionAvailable:
                return ProjectApprovalRiskTiers.WorkspaceReporting;
            default:
                warnings.Add($"Unknown workspace apply requested action '{requestedAction}'. Treating policy context as workspace reporting only.");
                return ProjectApprovalRiskTiers.WorkspaceReporting;
        }
    }

    private static IReadOnlyList<string> MergeEvidencePaths(WorkspaceApplyPolicyContextInput input) =>
        input.Report.EvidencePaths
            .Concat(input.Recommendation.EvidencePaths)
            .Concat(input.ActionRequest.EvidencePaths)
            .Concat(input.ActionReview.EvidencePaths)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
