namespace IronDev.Core.Workflow;

public interface IWorkflowDryRunExecutor
{
    WorkflowDryRunResult ExecuteDryRun(WorkflowDryRunRequest? request);
}

public sealed class WorkflowDryRunExecutor : IWorkflowDryRunExecutor
{
    private static readonly string[] PrivateReasoningMarkers =
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
        "patch payload"
    ];

    private static readonly string[] AuthorityMarkers =
    [
        "approval granted",
        "approval satisfied",
        "execution allowed",
        "execution succeeded",
        "execution success",
        "run tool",
        "dispatch agent",
        "invoke tool",
        "tool executed",
        "source mutated",
        "apply patch",
        "patch applied",
        "policy satisfied",
        "promote memory",
        "memory promoted",
        "retrieval activated",
        "activate retrieval",
        "release approved",
        "workflow continued",
        "workflow started",
        "dry-run approved",
        "dry-run completed workflow step"
    ];

    private readonly WorkflowStepContractValidator _contractValidator;

    public WorkflowDryRunExecutor()
        : this(new WorkflowStepContractValidator())
    {
    }

    internal WorkflowDryRunExecutor(WorkflowStepContractValidator contractValidator)
    {
        _contractValidator = contractValidator;
    }

    public WorkflowDryRunResult ExecuteDryRun(WorkflowDryRunRequest? request)
    {
        if (request is null)
            return Result(string.Empty, string.Empty, WorkflowDryRunActionKind.Unknown, WorkflowDryRunStatus.InvalidRequest, [WorkflowDryRunBlockReason.InvalidRequest]);

        var step = request.StepContract;
        var evaluation = request.StepEvaluation;
        var workflowRunId = SafeId(request.WorkflowRunId) ?? SafeId(step?.WorkflowRunId) ?? string.Empty;
        var stepId = SafeId(step?.StepContractId) ?? SafeId(evaluation?.StepId) ?? string.Empty;
        var actionKind = request.ActionKind;

        if (step is null || evaluation is null)
            return Result(workflowRunId, stepId, actionKind, WorkflowDryRunStatus.InvalidRequest, [WorkflowDryRunBlockReason.InvalidRequest]);

        if (!Enum.IsDefined(actionKind) || actionKind == WorkflowDryRunActionKind.Unknown)
            return Result(workflowRunId, stepId, actionKind, WorkflowDryRunStatus.InvalidRequest, [WorkflowDryRunBlockReason.UnknownDryRunAction]);

        if (ContainsUnsafeMarker(request.SafeSummary))
            return Result(workflowRunId, stepId, actionKind, WorkflowDryRunStatus.InvalidRequest, [WorkflowDryRunBlockReason.UnsafeDryRunMaterial]);

        var validation = _contractValidator.Validate(step);
        if (!validation.IsValid)
            return Result(workflowRunId, stepId, actionKind, WorkflowDryRunStatus.BlockedByStepValidation, [WorkflowDryRunBlockReason.InvalidStepContract]);

        var normalizedWorkflowRunId = string.IsNullOrWhiteSpace(request.WorkflowRunId)
            ? step.WorkflowRunId.Trim()
            : request.WorkflowRunId.Trim();

        if (!string.Equals(normalizedWorkflowRunId, step.WorkflowRunId.Trim(), StringComparison.Ordinal))
            return Result(workflowRunId, stepId, actionKind, WorkflowDryRunStatus.BlockedByStepValidation, [WorkflowDryRunBlockReason.WorkflowRunMismatch]);

        if (!string.Equals(evaluation.StepId?.Trim(), step.StepContractId.Trim(), StringComparison.Ordinal))
            return Result(workflowRunId, stepId, actionKind, WorkflowDryRunStatus.BlockedByStepValidation, [WorkflowDryRunBlockReason.StepEvaluationMismatch]);

        var blocked = BlockedResultFor(step, evaluation, actionKind, workflowRunId, stepId);
        if (blocked is not null)
            return blocked;

        return Result(
            workflowRunId,
            stepId,
            actionKind,
            WorkflowDryRunStatus.DryRunCompleted,
            BoundaryReasons(),
            ReportLinesFor(actionKind, request.SafeSummary));
    }

    private static WorkflowDryRunResult? BlockedResultFor(
        WorkflowStepContract step,
        WorkflowStepRunnerEvaluation evaluation,
        WorkflowDryRunActionKind actionKind,
        string workflowRunId,
        string stepId)
    {
        if (evaluation.Eligibility == WorkflowStepRunnerEligibility.InvalidContract)
            return Result(workflowRunId, stepId, actionKind, WorkflowDryRunStatus.BlockedByStepValidation, [WorkflowDryRunBlockReason.InvalidStepContract]);

        if (evaluation.Eligibility == WorkflowStepRunnerEligibility.BlockedMissingEvidence ||
            evaluation.MissingEvidenceRequirements.Count > 0)
            return Result(workflowRunId, stepId, actionKind, WorkflowDryRunStatus.BlockedByMissingEvidence, [WorkflowDryRunBlockReason.MissingRequiredEvidence]);

        if (evaluation.PolicyPreflightStatus is WorkflowStepPolicyPreflightStatus.InvalidPolicyRequest or
            WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence ||
            evaluation.MissingPolicyRequirements.Count > 0)
            return Result(workflowRunId, stepId, actionKind, WorkflowDryRunStatus.BlockedByPolicyPreflight, [WorkflowDryRunBlockReason.PolicyPreflightBlocked]);

        if (evaluation.A2aHandoffValidationStatus is WorkflowA2aHandoffValidationStatus.InvalidRequest or
            WorkflowA2aHandoffValidationStatus.InvalidStepContract or
            WorkflowA2aHandoffValidationStatus.InvalidHandoffReference or
            WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence ||
            evaluation.MissingA2aHandoffEvidence.Count > 0)
            return Result(workflowRunId, stepId, actionKind, WorkflowDryRunStatus.BlockedByA2aValidation, [WorkflowDryRunBlockReason.A2aValidationBlocked]);

        if (evaluation.Eligibility == WorkflowStepRunnerEligibility.BlockedApprovalRequired ||
            evaluation.ApprovalHaltStatus == WorkflowApprovalHaltStatus.ApprovalRequiredHalt ||
            evaluation.MissingApprovalRequirements.Count > 0)
            return Result(workflowRunId, stepId, actionKind, WorkflowDryRunStatus.BlockedByApprovalRequiredHalt, [WorkflowDryRunBlockReason.ApprovalRequiredHalt]);

        if (evaluation.Eligibility != WorkflowStepRunnerEligibility.EligibleForFutureExecution)
            return Result(workflowRunId, stepId, actionKind, WorkflowDryRunStatus.BlockedByStepValidation, [WorkflowDryRunBlockReason.StepNotEligibleForFutureExecution]);

        return null;
    }

    private static IReadOnlyList<string> ReportLinesFor(WorkflowDryRunActionKind actionKind, string? safeSummary)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(safeSummary))
            lines.Add($"Dry-run summary: {safeSummary.Trim()}");

        if (actionKind == WorkflowDryRunActionKind.ReviewMaterialEligibilityPreview)
        {
            lines.Add("Step is eligible to produce review material later.");
            lines.Add("Review material was not created.");
        }
        else
        {
            lines.Add("Step contract was valid.");
            lines.Add("Required evidence references were present.");
            lines.Add("Policy/A2A/approval blockers were not active.");
        }

        lines.Add("No mutation was performed.");
        lines.Add("Dry-run result is safe review material only.");
        return lines;
    }

    private static WorkflowDryRunBlockReason[] BoundaryReasons() =>
    [
        WorkflowDryRunBlockReason.DryRunCannotMutateState,
        WorkflowDryRunBlockReason.DryRunCannotApprove,
        WorkflowDryRunBlockReason.DryRunCannotDispatch,
        WorkflowDryRunBlockReason.DryRunCannotInvokeTools,
        WorkflowDryRunBlockReason.DryRunCannotMutateSource,
        WorkflowDryRunBlockReason.DryRunCannotPromoteMemory,
        WorkflowDryRunBlockReason.DryRunCannotActivateRetrieval,
        WorkflowDryRunBlockReason.DryRunCannotCallModels
    ];

    private static WorkflowDryRunResult Result(
        string workflowRunId,
        string workflowStepId,
        WorkflowDryRunActionKind actionKind,
        WorkflowDryRunStatus status,
        IReadOnlyList<WorkflowDryRunBlockReason> reasons,
        IReadOnlyList<string>? reportLines = null) =>
        new()
        {
            WorkflowRunId = workflowRunId,
            WorkflowStepId = workflowStepId,
            ActionKind = actionKind,
            Status = status,
            BlockReasons = reasons.Distinct().OrderBy(reason => reason).ToArray(),
            SafeReportLines = reportLines?.ToArray() ?? []
        };

    private static string? SafeId(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMarker(value) ? null : value.Trim();

    private static bool ContainsUnsafeMarker(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        PrivateReasoningMarkers.Concat(AuthorityMarkers).Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}

public sealed record WorkflowDryRunRequest
{
    public string? WorkflowRunId { get; init; }
    public required WorkflowStepContract? StepContract { get; init; }
    public required WorkflowStepRunnerEvaluation? StepEvaluation { get; init; }
    public required WorkflowDryRunActionKind ActionKind { get; init; }
    public string? SafeSummary { get; init; }
}

public enum WorkflowDryRunActionKind
{
    Unknown = 0,
    NoOpValidationDryRun = 1,
    ReviewMaterialEligibilityPreview = 2
}

public sealed record WorkflowDryRunResult
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required WorkflowDryRunActionKind ActionKind { get; init; }
    public required WorkflowDryRunStatus Status { get; init; }
    public required IReadOnlyList<WorkflowDryRunBlockReason> BlockReasons { get; init; }
    public required IReadOnlyList<string> SafeReportLines { get; init; }
}

public enum WorkflowDryRunStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    BlockedByStepValidation = 2,
    BlockedByMissingEvidence = 3,
    BlockedByPolicyPreflight = 4,
    BlockedByA2aValidation = 5,
    BlockedByApprovalRequiredHalt = 6,
    DryRunCompleted = 7
}

public enum WorkflowDryRunBlockReason
{
    Unknown = 0,
    InvalidRequest = 1,
    InvalidStepContract = 2,
    StepNotEligibleForFutureExecution = 3,
    MissingRequiredEvidence = 4,
    PolicyPreflightBlocked = 5,
    A2aValidationBlocked = 6,
    ApprovalRequiredHalt = 7,
    UnknownDryRunAction = 8,
    DryRunCannotMutateState = 9,
    DryRunCannotApprove = 10,
    DryRunCannotDispatch = 11,
    DryRunCannotInvokeTools = 12,
    DryRunCannotMutateSource = 13,
    DryRunCannotPromoteMemory = 14,
    DryRunCannotActivateRetrieval = 15,
    StepEvaluationMismatch = 16,
    WorkflowRunMismatch = 17,
    UnsafeDryRunMaterial = 18,
    DryRunCannotCallModels = 19
}
