using IronDev.Core.Agents;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class WorkspaceApplyActionReviewService : IWorkspaceApplyActionReviewService
{
    public WorkspaceApplyActionReview Create(WorkspaceApplyActionReviewInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Report);
        ArgumentNullException.ThrowIfNull(input.Recommendation);
        ArgumentNullException.ThrowIfNull(input.ActionRequest);

        return input.ActionRequest.RequestedAction switch
        {
            WorkspaceApplyRequestedActions.HumanReviewSourceChanges => Build(
                input,
                WorkspaceApplyActionReviewStatuses.ReadyForHumanReview,
                "The governed workspace apply spine completed successfully. Review the changed source files before any commit or PR step.",
                sourceRepoMayBeMutated: true,
                [
                    "Review source-report.json.",
                    "Review changed files listed in source report.",
                    "Confirm apply verification succeeded.",
                    "Confirm post-apply validation succeeded.",
                    "Decide manually whether commit/PR preparation should happen in a later flow."
                ],
                warnings:
                [
                    "This review package does not create a commit or PR.",
                    "This review package does not approve any future execution."
                ]),

            WorkspaceApplyRequestedActions.ReviewSourceBeforeRetry => Build(
                input,
                WorkspaceApplyActionReviewStatuses.BlockedForSourceReview,
                "The source repository may have been mutated before verification completed. Human source review is required before any retry.",
                sourceRepoMayBeMutated: true,
                [
                    "Inspect failure-package.json.",
                    "Inspect apply-copy evidence if present.",
                    "Inspect the source repository manually.",
                    "Determine whether recovery or rollback is needed outside this flow.",
                    "Do not retry apply-copy blindly."
                ]),

            WorkspaceApplyRequestedActions.FixValidationFailure => Build(
                input,
                WorkspaceApplyActionReviewStatuses.BlockedForValidationFailure,
                "The source mutation was verified, but post-apply validation failed. The validation failure must be reviewed before further action.",
                sourceRepoMayBeMutated: true,
                [
                    "Review post-apply-validation.json.",
                    "Review validation command evidence.",
                    "Inspect source-report or apply-verify evidence.",
                    "Create a new explicit fix plan if code changes are needed.",
                    "Do not continue automatically."
                ]),

            WorkspaceApplyRequestedActions.RetryGovernedSpineAfterFixingBlockers => Build(
                input,
                WorkspaceApplyActionReviewStatuses.ReadyForHumanReview,
                "No source mutation was reported. Blockers must be fixed and a human must explicitly decide whether to retry the governed spine.",
                sourceRepoMayBeMutated: false,
                [
                    "Review failure-package.json.",
                    "Fix listed blockers.",
                    "Confirm no source mutation occurred.",
                    "Decide manually whether to retry the governed spine."
                ]),

            WorkspaceApplyRequestedActions.InspectFailureEvidence => Build(
                input,
                WorkspaceApplyActionReviewStatuses.BlockedForEvidence,
                "Workspace apply evidence requires inspection before any further action.",
                sourceRepoMayBeMutated: input.Report.SourceRepoMutated,
                [
                    "Review failure-package.json.",
                    "Review source-report.json if present.",
                    "Confirm whether source mutation occurred.",
                    "Do not infer success."
                ]),

            WorkspaceApplyRequestedActions.CollectMissingEvidence => Build(
                input,
                WorkspaceApplyActionReviewStatuses.BlockedForEvidence,
                "Workspace apply evidence is missing or inconsistent. More evidence is required before deciding next action.",
                sourceRepoMayBeMutated: input.Report.SourceRepoMutated,
                [
                    "Identify missing evidence.",
                    "Do not infer success.",
                    "Rerun only the appropriate governed command manually if needed."
                ]),

            WorkspaceApplyRequestedActions.NoActionAvailable => Build(
                input,
                WorkspaceApplyActionReviewStatuses.BlockedForEvidence,
                "No usable workspace apply report was available. No action can be reviewed yet.",
                sourceRepoMayBeMutated: false,
                [
                    "Provide source-report.json or failure-package.json.",
                    "Do not proceed from missing evidence."
                ]),

            _ => Build(
                input,
                WorkspaceApplyActionReviewStatuses.BlockedForEvidence,
                "Workspace apply evidence is missing or inconsistent. More evidence is required before deciding next action.",
                sourceRepoMayBeMutated: input.Report.SourceRepoMutated,
                [
                    "Identify missing evidence.",
                    "Do not infer success.",
                    "Rerun only the appropriate governed command manually if needed."
                ],
                blockers: [$"Unknown workspace apply requested action '{input.ActionRequest.RequestedAction}'."])
        };
    }

    private static WorkspaceApplyActionReview Build(
        WorkspaceApplyActionReviewInput input,
        string reviewStatus,
        string summary,
        bool sourceRepoMayBeMutated,
        IReadOnlyList<string> reviewChecklist,
        IReadOnlyList<string>? blockers = null,
        IReadOnlyList<string>? warnings = null)
    {
        if (!WorkspaceApplyActionReviewStatuses.All.Contains(reviewStatus))
            throw new InvalidOperationException($"Unknown workspace apply review status '{reviewStatus}'.");

        var evidencePaths = input.Report.EvidencePaths
            .Concat(input.Recommendation.EvidencePaths)
            .Concat(input.ActionRequest.EvidencePaths)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var riskNotes = input.Report.RiskNotes
            .Concat(input.Recommendation.RiskNotes)
            .Concat(input.ActionRequest.RiskNotes)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var mergedWarnings = input.Report.Warnings
            .Concat(input.Recommendation.Warnings)
            .Concat(input.ActionRequest.Warnings)
            .Concat(warnings ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var mergedBlockers = (blockers ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new WorkspaceApplyActionReview
        {
            ReviewStatus = reviewStatus,
            Summary = summary,
            HumanReviewRequired = true,
            ApprovalCanBeGrantedByThisPackage = false,
            ExecutionCanStartFromThisPackage = false,
            SourceRepoMayBeMutated = sourceRepoMayBeMutated,
            RequestedAction = input.ActionRequest.RequestedAction,
            RecommendedAction = input.Recommendation.RecommendedAction,
            ReviewChecklist = reviewChecklist,
            EvidencePaths = evidencePaths,
            RiskNotes = riskNotes,
            Blockers = mergedBlockers,
            Warnings = mergedWarnings
        };
    }
}
