namespace IronDev.Core.Workflow;

public interface IBoxedLangGraphRoutingAdapter
{
    BoxedLangGraphRouteSuggestion SuggestRoute(BoxedLangGraphRoutingRequest? request);
}

public sealed class BoxedLangGraphRoutingAdapter : IBoxedLangGraphRoutingAdapter
{
    private static readonly string[] UnsafeMarkers =
    [
        "private reasoning",
        "hidden reasoning",
        "chainofthought",
        "chain of thought",
        "chain-of-thought",
        "scratchpad",
        "rawprompt",
        "raw prompt",
        "rawcompletion",
        "raw completion",
        "rawtooloutput",
        "raw tool output",
        "wholepatch",
        "whole patch",
        "entirepatch",
        "entire patch",
        "patchpayload",
        "patch payload",
        "approval granted",
        "approval satisfied",
        "execution allowed",
        "workflow transitioned",
        "agent dispatched",
        "tool invoked",
        "source mutated",
        "policy satisfied",
        "memory promoted",
        "retrieval activated"
    ];

    public BoxedLangGraphRouteSuggestion SuggestRoute(BoxedLangGraphRoutingRequest? request)
    {
        if (request is null)
            return Suggestion(string.Empty, string.Empty, BoxedLangGraphRouteLabel.InvalidRoutingSnapshot, [BoxedLangGraphRouteReason.InvalidInput], [], null, false);

        var workflowRunId = NormalizeId(request.WorkflowRunId);
        if (workflowRunId is null)
            return Suggestion(string.Empty, NormalizeId(request.WorkflowStepId) ?? string.Empty, BoxedLangGraphRouteLabel.InvalidRoutingSnapshot, [BoxedLangGraphRouteReason.InvalidInput], [], null, false);

        var workflowStepId = NormalizeId(request.WorkflowStepId);
        if (workflowStepId is null)
            return Suggestion(workflowRunId, string.Empty, BoxedLangGraphRouteLabel.InvalidRoutingSnapshot, [BoxedLangGraphRouteReason.InvalidInput], [], null, false);

        if (ContainsUnsafeMarker(request.SafeSummary))
            return Suggestion(workflowRunId, workflowStepId, BoxedLangGraphRouteLabel.InvalidRoutingSnapshot, [BoxedLangGraphRouteReason.UnsafeInput], [], null, false);

        if (request.DryRunResult is not null)
            return FromDryRun(workflowRunId, workflowStepId, request.DryRunResult, request.SafeSummary);

        if (request.StepEvaluation is not null)
            return FromStepEvaluation(workflowRunId, workflowStepId, request.StepEvaluation, request.SafeSummary);

        return Suggestion(
            workflowRunId,
            workflowStepId,
            BoxedLangGraphRouteLabel.NoRouteSuggested,
            [BoxedLangGraphRouteReason.RunnerSkeletonRemainsEvaluationOnly],
            [],
            request.SafeSummary,
            false);
    }

    private static BoxedLangGraphRouteSuggestion FromStepEvaluation(
        string workflowRunId,
        string workflowStepId,
        WorkflowStepRunnerEvaluation evaluation,
        string? safeSummary)
    {
        var source = new[] { $"StepEligibility:{evaluation.Eligibility}" };

        return evaluation.Eligibility switch
        {
            WorkflowStepRunnerEligibility.InvalidContract => Suggestion(
                workflowRunId,
                workflowStepId,
                BoxedLangGraphRouteLabel.BlockedInvalidStep,
                [BoxedLangGraphRouteReason.StepEvaluationBlocked],
                source,
                safeSummary,
                false),

            WorkflowStepRunnerEligibility.BlockedMissingEvidence => Suggestion(
                workflowRunId,
                workflowStepId,
                BoxedLangGraphRouteLabel.BlockedMissingEvidence,
                [BoxedLangGraphRouteReason.StepEvaluationBlocked, BoxedLangGraphRouteReason.EvidenceIsNotApproval],
                source,
                safeSummary,
                false),

            WorkflowStepRunnerEligibility.BlockedApprovalRequired => Suggestion(
                workflowRunId,
                workflowStepId,
                BoxedLangGraphRouteLabel.BlockedApprovalRequired,
                [BoxedLangGraphRouteReason.ApprovalHaltStillHalts, BoxedLangGraphRouteReason.EvidenceIsNotApproval],
                source,
                safeSummary,
                false),

            WorkflowStepRunnerEligibility.BlockedByBoundary => BoundarySuggestionFor(workflowRunId, workflowStepId, evaluation, source, safeSummary),

            WorkflowStepRunnerEligibility.EligibleForFutureExecution => Suggestion(
                workflowRunId,
                workflowStepId,
                BoxedLangGraphRouteLabel.EligibleForDryRun,
                [BoxedLangGraphRouteReason.RunnerSkeletonRemainsEvaluationOnly, BoxedLangGraphRouteReason.ThoughtLedgerIsNotAuthority],
                source,
                safeSummary,
                false),

            _ => Suggestion(
                workflowRunId,
                workflowStepId,
                BoxedLangGraphRouteLabel.NoRouteSuggested,
                [BoxedLangGraphRouteReason.StepEvaluationBlocked],
                source,
                safeSummary,
                false)
        };
    }

    private static BoxedLangGraphRouteSuggestion BoundarySuggestionFor(
        string workflowRunId,
        string workflowStepId,
        WorkflowStepRunnerEvaluation evaluation,
        IReadOnlyList<string> source,
        string? safeSummary)
    {
        if (IsPolicyBlocked(evaluation))
        {
            return Suggestion(
                workflowRunId,
                workflowStepId,
                BoxedLangGraphRouteLabel.BlockedPolicyPreflight,
                [BoxedLangGraphRouteReason.StepEvaluationBlocked, BoxedLangGraphRouteReason.EvidenceIsNotApproval],
                source,
                safeSummary,
                false);
        }

        if (IsA2aBlocked(evaluation))
        {
            return Suggestion(
                workflowRunId,
                workflowStepId,
                BoxedLangGraphRouteLabel.BlockedA2aValidation,
                [BoxedLangGraphRouteReason.StepEvaluationBlocked, BoxedLangGraphRouteReason.A2aValidationIsNotDispatch],
                source,
                safeSummary,
                false);
        }

        if (evaluation.ApprovalHaltStatus == WorkflowApprovalHaltStatus.ApprovalRequiredHalt ||
            evaluation.MissingApprovalRequirements.Count > 0)
        {
            return Suggestion(
                workflowRunId,
                workflowStepId,
                BoxedLangGraphRouteLabel.BlockedApprovalRequired,
                [BoxedLangGraphRouteReason.ApprovalHaltStillHalts, BoxedLangGraphRouteReason.EvidenceIsNotApproval],
                source,
                safeSummary,
                false);
        }

        return Suggestion(
            workflowRunId,
            workflowStepId,
            BoxedLangGraphRouteLabel.NoRouteSuggested,
            [BoxedLangGraphRouteReason.StepEvaluationBlocked],
            source,
            safeSummary,
            false);
    }

    private static BoxedLangGraphRouteSuggestion FromDryRun(
        string workflowRunId,
        string workflowStepId,
        WorkflowDryRunResult dryRun,
        string? safeSummary)
    {
        var source = new[] { $"DryRunStatus:{dryRun.Status}" };

        return dryRun.Status switch
        {
            WorkflowDryRunStatus.InvalidRequest => Suggestion(
                workflowRunId,
                workflowStepId,
                BoxedLangGraphRouteLabel.InvalidRoutingSnapshot,
                [BoxedLangGraphRouteReason.InvalidInput],
                source,
                safeSummary,
                true),

            WorkflowDryRunStatus.BlockedByStepValidation => Suggestion(
                workflowRunId,
                workflowStepId,
                BoxedLangGraphRouteLabel.BlockedInvalidStep,
                [BoxedLangGraphRouteReason.DryRunBlocked],
                source,
                safeSummary,
                true),

            WorkflowDryRunStatus.BlockedByMissingEvidence => Suggestion(
                workflowRunId,
                workflowStepId,
                BoxedLangGraphRouteLabel.BlockedMissingEvidence,
                [BoxedLangGraphRouteReason.DryRunBlocked, BoxedLangGraphRouteReason.EvidenceIsNotApproval],
                source,
                safeSummary,
                true),

            WorkflowDryRunStatus.BlockedByPolicyPreflight => Suggestion(
                workflowRunId,
                workflowStepId,
                BoxedLangGraphRouteLabel.BlockedPolicyPreflight,
                [BoxedLangGraphRouteReason.DryRunBlocked, BoxedLangGraphRouteReason.EvidenceIsNotApproval],
                source,
                safeSummary,
                true),

            WorkflowDryRunStatus.BlockedByA2aValidation => Suggestion(
                workflowRunId,
                workflowStepId,
                BoxedLangGraphRouteLabel.BlockedA2aValidation,
                [BoxedLangGraphRouteReason.DryRunBlocked, BoxedLangGraphRouteReason.A2aValidationIsNotDispatch],
                source,
                safeSummary,
                true),

            WorkflowDryRunStatus.BlockedByApprovalRequiredHalt => Suggestion(
                workflowRunId,
                workflowStepId,
                BoxedLangGraphRouteLabel.BlockedApprovalRequired,
                [BoxedLangGraphRouteReason.DryRunBlocked, BoxedLangGraphRouteReason.ApprovalHaltStillHalts],
                source,
                safeSummary,
                true),

            WorkflowDryRunStatus.DryRunCompleted => Suggestion(
                workflowRunId,
                workflowStepId,
                BoxedLangGraphRouteLabel.DryRunReviewMaterialAvailable,
                [BoxedLangGraphRouteReason.RunnerSkeletonRemainsEvaluationOnly],
                source,
                safeSummary,
                true),

            _ => Suggestion(
                workflowRunId,
                workflowStepId,
                BoxedLangGraphRouteLabel.InvalidRoutingSnapshot,
                [BoxedLangGraphRouteReason.InvalidInput],
                source,
                safeSummary,
                true)
        };
    }

    private static BoxedLangGraphRouteSuggestion Suggestion(
        string workflowRunId,
        string workflowStepId,
        BoxedLangGraphRouteLabel label,
        IReadOnlyList<BoxedLangGraphRouteReason> reasons,
        IReadOnlyList<string> sourceStatusReferences,
        string? safeSummary,
        bool dryRunBased)
    {
        var allReasons = new List<BoxedLangGraphRouteReason>
        {
            BoxedLangGraphRouteReason.AdvisoryOnly,
            BoxedLangGraphRouteReason.AdapterCannotOwnDecisions,
            BoxedLangGraphRouteReason.NoMutationPerformed
        };

        if (dryRunBased)
            allReasons.Add(BoxedLangGraphRouteReason.DryRunResultIsReviewMaterialOnly);

        allReasons.AddRange(reasons);

        return new BoxedLangGraphRouteSuggestion
        {
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            Label = label,
            Reasons = allReasons.Distinct().OrderBy(reason => reason).ToArray(),
            SourceStatusReferences = sourceStatusReferences
                .Where(reference => !ContainsUnsafeMarker(reference))
                .OrderBy(reference => reference, StringComparer.Ordinal)
                .ToArray(),
            SafeReportLines = ReportLines(label, safeSummary),
            IsAdvisoryOnly = true,
            WorkflowDecisionAuthority = false,
            WorkflowStateChangeAllowed = false,
            StepWorkAllowed = false,
            AgentSendAllowed = false,
            A2aSendAllowed = false,
            ToolUseAllowed = false,
            ApprovalChangeAllowed = false,
            PolicySatisfactionAllowed = false,
            SourceChangeAllowed = false,
            MemoryPromotionAllowed = false,
            RetrievalActivationAllowed = false,
            IsApprovalEvidence = false,
            IsPolicyEvidence = false,
            IsWorkflowTransitionEvidence = false,
            IsDryRunEvidence = false,
            IsA2aValidationEvidence = false,
            IsMemoryPromotionEvidence = false,
            IsRetrievalApprovalEvidence = false
        };
    }

    private static IReadOnlyList<string> ReportLines(BoxedLangGraphRouteLabel label, string? safeSummary)
    {
        var lines = new List<string>
        {
            $"Advisory route label: {label}.",
            "Adapter output is labels and reasons only.",
            "Adapter does not own workflow decisions."
        };

        if (!string.IsNullOrWhiteSpace(safeSummary) && !ContainsUnsafeMarker(safeSummary))
            lines.Add($"Safe summary: {safeSummary.Trim()}");

        return lines;
    }

    private static bool IsPolicyBlocked(WorkflowStepRunnerEvaluation evaluation) =>
        evaluation.PolicyPreflightStatus is WorkflowStepPolicyPreflightStatus.InvalidPolicyRequest or
            WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence ||
        evaluation.MissingPolicyRequirements.Count > 0 ||
        evaluation.BlockReasons.Any(reason => reason is WorkflowRunnerBlockReason.PolicyPreflightInvalid or
            WorkflowRunnerBlockReason.PolicyPreflightMissingEvidence);

    private static bool IsA2aBlocked(WorkflowStepRunnerEvaluation evaluation) =>
        evaluation.A2aHandoffValidationStatus is WorkflowA2aHandoffValidationStatus.InvalidRequest or
            WorkflowA2aHandoffValidationStatus.InvalidStepContract or
            WorkflowA2aHandoffValidationStatus.InvalidHandoffReference or
            WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence ||
        evaluation.MissingA2aHandoffEvidence.Count > 0 ||
        evaluation.BlockReasons.Any(reason => reason is WorkflowRunnerBlockReason.A2aHandoffValidationInvalid or
            WorkflowRunnerBlockReason.A2aHandoffValidationMissingEvidence);

    private static string? NormalizeId(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMarker(value) ? null : value.Trim();

    private static bool ContainsUnsafeMarker(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        UnsafeMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}

public sealed record BoxedLangGraphRoutingRequest
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public WorkflowStepRunnerEvaluation? StepEvaluation { get; init; }
    public WorkflowDryRunResult? DryRunResult { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed record BoxedLangGraphRouteSuggestion
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required BoxedLangGraphRouteLabel Label { get; init; }
    public required IReadOnlyList<BoxedLangGraphRouteReason> Reasons { get; init; }
    public required IReadOnlyList<string> SourceStatusReferences { get; init; }
    public required IReadOnlyList<string> SafeReportLines { get; init; }
    public required bool IsAdvisoryOnly { get; init; }
    public required bool WorkflowDecisionAuthority { get; init; }
    public required bool WorkflowStateChangeAllowed { get; init; }
    public required bool StepWorkAllowed { get; init; }
    public required bool AgentSendAllowed { get; init; }
    public required bool A2aSendAllowed { get; init; }
    public required bool ToolUseAllowed { get; init; }
    public required bool ApprovalChangeAllowed { get; init; }
    public required bool PolicySatisfactionAllowed { get; init; }
    public required bool SourceChangeAllowed { get; init; }
    public required bool MemoryPromotionAllowed { get; init; }
    public required bool RetrievalActivationAllowed { get; init; }
    public required bool IsApprovalEvidence { get; init; }
    public required bool IsPolicyEvidence { get; init; }
    public required bool IsWorkflowTransitionEvidence { get; init; }
    public required bool IsDryRunEvidence { get; init; }
    public required bool IsA2aValidationEvidence { get; init; }
    public required bool IsMemoryPromotionEvidence { get; init; }
    public required bool IsRetrievalApprovalEvidence { get; init; }
}

public enum BoxedLangGraphRouteLabel
{
    Unknown = 0,
    InvalidRoutingSnapshot = 1,
    NoRouteSuggested = 2,
    BlockedInvalidStep = 3,
    BlockedMissingEvidence = 4,
    BlockedPolicyPreflight = 5,
    BlockedA2aValidation = 6,
    BlockedApprovalRequired = 7,
    EligibleForDryRun = 8,
    DryRunReviewMaterialAvailable = 9
}

public enum BoxedLangGraphRouteReason
{
    Unknown = 0,
    AdvisoryOnly = 1,
    RunnerSkeletonRemainsEvaluationOnly = 2,
    DryRunResultIsReviewMaterialOnly = 3,
    EvidenceIsNotApproval = 4,
    ThoughtLedgerIsNotAuthority = 5,
    A2aValidationIsNotDispatch = 6,
    ApprovalHaltStillHalts = 7,
    InvalidInput = 8,
    UnsafeInput = 9,
    StepEvaluationBlocked = 10,
    DryRunBlocked = 11,
    NoMutationPerformed = 12,
    AdapterCannotOwnDecisions = 13
}
