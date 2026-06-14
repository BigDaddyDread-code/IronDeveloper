namespace IronDev.Core.Workflow;

public interface IWorkflowApprovalHaltEvaluator
{
    WorkflowApprovalHaltState Evaluate(WorkflowApprovalHaltEvaluationRequest? request);
}

public sealed class WorkflowApprovalHaltEvaluator : IWorkflowApprovalHaltEvaluator
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
        "workflow started"
    ];

    public WorkflowApprovalHaltState Evaluate(WorkflowApprovalHaltEvaluationRequest? request)
    {
        if (request is null)
            return Result(string.Empty, WorkflowApprovalHaltStatus.InvalidApprovalRequirement, [WorkflowApprovalHaltReason.InvalidApprovalRequirement]);

        var reasons = new List<WorkflowApprovalHaltReason>();
        Require(request.WorkflowStepId, WorkflowApprovalHaltReason.InvalidApprovalRequirement, reasons);
        ValidateTextSafety(request.WorkflowStepId, reasons);
        ValidateRequirements(request.RequiredApprovals, reasons);
        ValidateEvidence(request.AvailableApprovalEvidence, reasons);

        if (reasons.Count > 0)
            return Result(SafeId(request.WorkflowStepId), WorkflowApprovalHaltStatus.InvalidApprovalRequirement, reasons);

        if (request.RequiredApprovals.Count == 0)
        {
            return Result(
                request.WorkflowStepId.Trim(),
                WorkflowApprovalHaltStatus.NotApprovalRequired,
                BoundaryReasons());
        }

        var availableEvidence = request.AvailableApprovalEvidence ?? [];
        var available = availableEvidence
            .Select(evidence => EvidenceKey(evidence.Kind, evidence.ReferenceId))
            .ToHashSet(StringComparer.Ordinal);

        var missing = request.RequiredApprovals
            .Where(requirement => !available.Contains(EvidenceKey(requirement.Kind, requirement.RequirementId)))
            .Select(NormalizeRequirement)
            .ToArray();

        if (missing.Length > 0)
        {
            return new WorkflowApprovalHaltState
            {
                WorkflowStepId = request.WorkflowStepId.Trim(),
                Status = WorkflowApprovalHaltStatus.ApprovalRequiredHalt,
                Reasons = [WorkflowApprovalHaltReason.MissingApprovalEvidence, .. BoundaryReasons()],
                MissingApprovalRequirements = missing
            };
        }

        return Result(
            request.WorkflowStepId.Trim(),
            WorkflowApprovalHaltStatus.ApprovalEvidencePresentForFutureExecution,
            BoundaryReasons());
    }

    private static void ValidateRequirements(
        IReadOnlyList<WorkflowApprovalHaltRequirement>? requirements,
        List<WorkflowApprovalHaltReason> reasons)
    {
        if (requirements is null)
        {
            reasons.Add(WorkflowApprovalHaltReason.InvalidApprovalRequirement);
            return;
        }

        foreach (var requirement in requirements)
        {
            ValidateEnum(requirement.Kind, WorkflowApprovalHaltReason.InvalidApprovalRequirement, reasons);
            Require(requirement.RequirementId, WorkflowApprovalHaltReason.InvalidApprovalRequirement, reasons);
            ValidateTextSafety(requirement.RequirementId, reasons);
            ValidateTextSafety(requirement.SafeSummary, reasons);
            RejectAuthorityFlag(requirement.GrantsApproval, reasons);
            RejectAuthorityFlag(requirement.SatisfiesPolicy, reasons);
            RejectAuthorityFlag(requirement.AllowsExecution, reasons);
            RejectAuthorityFlag(requirement.MutatesApprovalState, reasons);
            RejectAuthorityFlag(requirement.TransitionsWorkflow, reasons);
        }
    }

    private static void ValidateEvidence(
        IReadOnlyList<WorkflowApprovalEvidenceReference>? evidenceReferences,
        List<WorkflowApprovalHaltReason> reasons)
    {
        if (evidenceReferences is null)
            return;

        foreach (var evidence in evidenceReferences)
        {
            ValidateEnum(evidence.Kind, WorkflowApprovalHaltReason.InvalidApprovalRequirement, reasons);
            Require(evidence.ReferenceId, WorkflowApprovalHaltReason.InvalidApprovalRequirement, reasons);
            ValidateTextSafety(evidence.ReferenceId, reasons);
            ValidateTextSafety(evidence.CorrelationId, reasons);
            ValidateTextSafety(evidence.SafeSummary, reasons);
            RejectAuthorityFlag(evidence.GrantsApproval, reasons);
            RejectAuthorityFlag(evidence.SatisfiesPolicy, reasons);
            RejectAuthorityFlag(evidence.AllowsExecution, reasons);
            RejectAuthorityFlag(evidence.MutatesApprovalState, reasons);
            RejectAuthorityFlag(evidence.TransitionsWorkflow, reasons);
        }
    }

    private static void ValidateEnum<TEnum>(TEnum value, WorkflowApprovalHaltReason reason, List<WorkflowApprovalHaltReason> reasons)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value) || Convert.ToInt32(value) == 0)
            reasons.Add(reason);
    }

    private static void Require(string? value, WorkflowApprovalHaltReason reason, List<WorkflowApprovalHaltReason> reasons)
    {
        if (string.IsNullOrWhiteSpace(value))
            reasons.Add(reason);
    }

    private static void ValidateTextSafety(string? value, List<WorkflowApprovalHaltReason> reasons)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (PrivateReasoningMarkers.Concat(AuthorityMarkers).Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            reasons.Add(WorkflowApprovalHaltReason.UnsafeApprovalReference);
    }

    private static void RejectAuthorityFlag(bool value, List<WorkflowApprovalHaltReason> reasons)
    {
        if (value)
            reasons.Add(WorkflowApprovalHaltReason.InvalidApprovalRequirement);
    }

    private static WorkflowApprovalHaltRequirement NormalizeRequirement(WorkflowApprovalHaltRequirement requirement) =>
        requirement with
        {
            RequirementId = requirement.RequirementId.Trim(),
            SafeSummary = string.IsNullOrWhiteSpace(requirement.SafeSummary)
                ? string.Empty
                : requirement.SafeSummary.Trim()
        };

    private static WorkflowApprovalHaltReason[] BoundaryReasons() =>
    [
        WorkflowApprovalHaltReason.ApprovalHaltIsNotApproval,
        WorkflowApprovalHaltReason.ApprovalEvidenceIsNotApprovalMutation,
        WorkflowApprovalHaltReason.ApprovalHaltCannotSatisfyPolicy,
        WorkflowApprovalHaltReason.ApprovalHaltCannotExecute,
        WorkflowApprovalHaltReason.ApprovalHaltCannotTransitionWorkflow
    ];

    private static WorkflowApprovalHaltState Result(
        string workflowStepId,
        WorkflowApprovalHaltStatus status,
        IReadOnlyList<WorkflowApprovalHaltReason> reasons) =>
        new()
        {
            WorkflowStepId = workflowStepId,
            Status = status,
            Reasons = reasons.Distinct().OrderBy(reason => reason).ToArray(),
            MissingApprovalRequirements = []
        };

    private static string SafeId(string? value) =>
        string.IsNullOrWhiteSpace(value) || ContainsUnsafeMarker(value) ? string.Empty : value.Trim();

    private static bool ContainsUnsafeMarker(string value) =>
        PrivateReasoningMarkers.Concat(AuthorityMarkers).Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string EvidenceKey(WorkflowApprovalRequirementKind kind, string referenceId) =>
        $"{(int)kind}:{referenceId.Trim()}";
}

public sealed record WorkflowApprovalHaltEvaluationRequest
{
    public required string WorkflowStepId { get; init; }
    public required IReadOnlyList<WorkflowApprovalHaltRequirement> RequiredApprovals { get; init; }
    public required IReadOnlyList<WorkflowApprovalEvidenceReference> AvailableApprovalEvidence { get; init; }
}

public sealed record WorkflowApprovalHaltRequirement
{
    public required WorkflowApprovalRequirementKind Kind { get; init; }
    public required string RequirementId { get; init; }
    public string SafeSummary { get; init; } = string.Empty;
    public bool GrantsApproval { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool AllowsExecution { get; init; }
    public bool MutatesApprovalState { get; init; }
    public bool TransitionsWorkflow { get; init; }
}

public enum WorkflowApprovalRequirementKind
{
    Unknown = 0,
    HumanApprovalReference = 1,
    ApprovalDecisionReference = 2,
    ApprovalPackageReference = 3,
    GovernanceEventReference = 4,
    PolicyDecisionReference = 5
}

public sealed record WorkflowApprovalEvidenceReference
{
    public required WorkflowApprovalRequirementKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? CorrelationId { get; init; }
    public string SafeSummary { get; init; } = string.Empty;
    public bool GrantsApproval { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool AllowsExecution { get; init; }
    public bool MutatesApprovalState { get; init; }
    public bool TransitionsWorkflow { get; init; }
}

public sealed record WorkflowApprovalHaltState
{
    public required string WorkflowStepId { get; init; }
    public required WorkflowApprovalHaltStatus Status { get; init; }
    public required IReadOnlyList<WorkflowApprovalHaltReason> Reasons { get; init; }
    public required IReadOnlyList<WorkflowApprovalHaltRequirement> MissingApprovalRequirements { get; init; }
}

public enum WorkflowApprovalHaltStatus
{
    Unknown = 0,
    NotApprovalRequired = 1,
    ApprovalRequiredHalt = 2,
    ApprovalEvidencePresentForFutureExecution = 3,
    InvalidApprovalRequirement = 4
}

public enum WorkflowApprovalHaltReason
{
    Unknown = 0,
    InvalidApprovalRequirement = 1,
    MissingApprovalEvidence = 2,
    UnsafeApprovalReference = 3,
    ApprovalHaltIsNotApproval = 4,
    ApprovalEvidenceIsNotApprovalMutation = 5,
    ApprovalHaltCannotSatisfyPolicy = 6,
    ApprovalHaltCannotExecute = 7,
    ApprovalHaltCannotTransitionWorkflow = 8
}
