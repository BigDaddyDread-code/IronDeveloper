using IronDev.Core.Agents;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class WorkspaceApplyActionRequestService : IWorkspaceApplyActionRequestService
{
    public WorkspaceApplyActionRequest Create(WorkspaceApplyActionRequestInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Report);
        ArgumentNullException.ThrowIfNull(input.Recommendation);

        return input.Recommendation.RecommendedAction switch
        {
            WorkspaceApplyRecommendedActions.HumanReviewOrCommit => Build(
                input,
                WorkspaceApplyRequestedActions.HumanReviewSourceChanges,
                "The governed workspace apply spine completed successfully. Human review is required before commit or PR creation.",
                [
                    "source-report succeeded",
                    "apply verification succeeded",
                    "post-apply validation succeeded",
                    "human reviews changed files"
                ],
                warnings: ["Commit/PR creation is outside the current governed workspace apply spine."]),

            WorkspaceApplyRecommendedActions.DoNotRetryUntilSourceReviewed => Build(
                input,
                WorkspaceApplyRequestedActions.ReviewSourceBeforeRetry,
                "Source repository may have been mutated, but apply verification did not complete.",
                [
                    "human inspects source repo",
                    "human inspects apply-copy evidence",
                    "human decides whether recovery is required"
                ]),

            WorkspaceApplyRecommendedActions.FixValidationFailure => Build(
                input,
                WorkspaceApplyRequestedActions.FixValidationFailure,
                "Source mutation was verified, but post-apply validation did not succeed.",
                [
                    "human reviews post-apply-validation evidence",
                    "human determines validation failure cause",
                    "future work must start from a new plan or explicit fix request"
                ]),

            WorkspaceApplyRecommendedActions.RetryAfterFixingBlockers => Build(
                input,
                WorkspaceApplyRequestedActions.RetryGovernedSpineAfterFixingBlockers,
                "No source mutation was reported. Fix blockers before retrying the governed spine.",
                [
                    "blockers are resolved",
                    "new or existing run context is reviewed",
                    "human explicitly chooses to retry"
                ]),

            WorkspaceApplyRecommendedActions.InspectFailurePackage => Build(
                input,
                WorkspaceApplyRequestedActions.InspectFailureEvidence,
                "The workspace apply evidence requires human inspection before any further action.",
                [
                    "human inspects failure-package.json",
                    "human inspects source-report if present",
                    "no automatic retry"
                ]),

            WorkspaceApplyRecommendedActions.CollectMissingEvidence => Build(
                input,
                WorkspaceApplyRequestedActions.CollectMissingEvidence,
                "Workspace apply evidence is incomplete or inconsistent.",
                [
                    "identify missing evidence",
                    "rerun only the appropriate governed command manually",
                    "do not infer success"
                ]),

            WorkspaceApplyRecommendedActions.NoWorkspaceApplyReport => Build(
                input,
                WorkspaceApplyRequestedActions.NoActionAvailable,
                "No usable workspace apply report was available.",
                [
                    "provide source-report or failure-package evidence",
                    "do not proceed from missing evidence"
                ]),

            _ => Build(
                input,
                WorkspaceApplyRequestedActions.CollectMissingEvidence,
                "Workspace apply evidence is incomplete or inconsistent.",
                [
                    "identify missing evidence",
                    "rerun only the appropriate governed command manually",
                    "do not infer success"
                ],
                warnings: [$"Unknown workspace apply recommendation '{input.Recommendation.RecommendedAction}'."])
        };
    }

    private static WorkspaceApplyActionRequest Build(
        WorkspaceApplyActionRequestInput input,
        string requestedAction,
        string reason,
        IReadOnlyList<string> preconditions,
        IReadOnlyList<string>? warnings = null)
    {
        if (!WorkspaceApplyRequestedActions.All.Contains(requestedAction))
            throw new InvalidOperationException($"Unknown workspace apply requested action '{requestedAction}'.");

        var evidencePaths = input.Report.EvidencePaths
            .Concat(input.Recommendation.EvidencePaths)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var riskNotes = input.Report.RiskNotes
            .Concat(input.Recommendation.RiskNotes)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var mergedWarnings = input.Report.Warnings
            .Concat(input.Recommendation.Warnings)
            .Concat(warnings ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new WorkspaceApplyActionRequest
        {
            RequestedAction = requestedAction,
            Reason = reason,
            HumanApprovalRequired = true,
            AutomaticExecutionAllowed = false,
            MutatesSourceRepo = false,
            RequiresFreshHumanDecision = true,
            SuggestedCommand = null,
            SuggestedCommandArguments = [],
            Preconditions = preconditions,
            EvidencePaths = evidencePaths,
            RiskNotes = riskNotes,
            Warnings = mergedWarnings
        };
    }
}
