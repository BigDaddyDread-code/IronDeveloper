namespace IronDev.Core.Workflow;

public interface IWorkflowStepPolicyPreflightChecker
{
    WorkflowStepPolicyPreflightResult Check(WorkflowStepPolicyPreflightRequest? request);
}

public sealed class WorkflowStepPolicyPreflightChecker : IWorkflowStepPolicyPreflightChecker
{
    private static readonly WorkflowStepPolicyBlockReason[] BoundaryReasons =
    [
        WorkflowStepPolicyBlockReason.ApprovalCannotBeInferredFromEvidence,
        WorkflowStepPolicyBlockReason.WorkflowCannotGrantAuthority,
        WorkflowStepPolicyBlockReason.PolicyPreflightCannotMutateApproval,
        WorkflowStepPolicyBlockReason.PolicyPreflightCannotExecute
    ];

    private readonly WorkflowStepContractValidator _contractValidator;

    public WorkflowStepPolicyPreflightChecker()
        : this(new WorkflowStepContractValidator())
    {
    }

    internal WorkflowStepPolicyPreflightChecker(WorkflowStepContractValidator contractValidator)
    {
        _contractValidator = contractValidator;
    }

    public WorkflowStepPolicyPreflightResult Check(WorkflowStepPolicyPreflightRequest? request)
    {
        if (request?.StepContract is null)
        {
            return BuildResult(
                string.Empty,
                WorkflowStepSensitivityKind.Unknown,
                WorkflowStepPolicyPreflightStatus.InvalidPolicyRequest,
                [WorkflowStepPolicyBlockReason.InvalidStepContract]);
        }

        var step = request.StepContract;
        var validation = _contractValidator.Validate(step);
        if (!validation.IsValid)
        {
            return BuildResult(
                step.StepContractId,
                request.SensitivityKind,
                WorkflowStepPolicyPreflightStatus.InvalidPolicyRequest,
                [WorkflowStepPolicyBlockReason.InvalidStepContract]);
        }

        if (!Enum.IsDefined(request.SensitivityKind) || request.SensitivityKind == WorkflowStepSensitivityKind.Unknown)
        {
            return BuildResult(
                step.StepContractId,
                request.SensitivityKind,
                WorkflowStepPolicyPreflightStatus.InvalidPolicyRequest,
                [WorkflowStepPolicyBlockReason.UnknownSensitivity]);
        }

        var requirements = NormalizeRequirements(request.RequiredPolicyReferences);
        if (request.SensitivityKind == WorkflowStepSensitivityKind.None)
        {
            return BuildResult(
                step.StepContractId,
                request.SensitivityKind,
                WorkflowStepPolicyPreflightStatus.NotSensitive,
                []);
        }

        if (requirements.Length == 0 ||
            requirements.Any(requirement => requirement.Kind == WorkflowStepPolicyRequirementKind.Unknown ||
                                            string.IsNullOrWhiteSpace(requirement.ReferenceId)))
        {
            return BuildResult(
                step.StepContractId,
                request.SensitivityKind,
                WorkflowStepPolicyPreflightStatus.InvalidPolicyRequest,
                [WorkflowStepPolicyBlockReason.MissingRequiredPolicyReference]);
        }

        if (!HasRequiredPolicyKind(request.SensitivityKind, requirements))
        {
            return BuildResult(
                step.StepContractId,
                request.SensitivityKind,
                WorkflowStepPolicyPreflightStatus.InvalidPolicyRequest,
                [WorkflowStepPolicyBlockReason.MissingRequiredPolicyReference]);
        }

        var available = NormalizeEvidence(request.AvailablePolicyEvidence)
            .Select(evidence => EvidenceKey(evidence.Kind, evidence.ReferenceId))
            .ToHashSet(StringComparer.Ordinal);

        var missing = requirements
            .Where(requirement => !available.Contains(EvidenceKey(requirement.Kind, requirement.ReferenceId)))
            .ToArray();

        if (missing.Length > 0)
        {
            return BuildResult(
                step.StepContractId,
                request.SensitivityKind,
                WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence,
                [WorkflowStepPolicyBlockReason.MissingPolicyEvidence],
                missing);
        }

        return BuildResult(
            step.StepContractId,
            request.SensitivityKind,
            WorkflowStepPolicyPreflightStatus.PolicyEvidencePresentForFutureExecution,
            []);
    }

    private static WorkflowStepPolicyRequirement[] NormalizeRequirements(IReadOnlyList<WorkflowStepPolicyRequirement>? requirements) =>
        (requirements ?? [])
            .Select(requirement => requirement with
            {
                ReferenceId = requirement.ReferenceId?.Trim() ?? string.Empty,
                ProjectId = string.IsNullOrWhiteSpace(requirement.ProjectId) ? null : requirement.ProjectId.Trim(),
                CorrelationId = string.IsNullOrWhiteSpace(requirement.CorrelationId) ? null : requirement.CorrelationId.Trim()
            })
            .ToArray();

    private static WorkflowStepPolicyEvidenceReference[] NormalizeEvidence(IReadOnlyList<WorkflowStepPolicyEvidenceReference>? evidence) =>
        (evidence ?? [])
            .Where(item => item.Kind != WorkflowStepPolicyRequirementKind.Unknown && !string.IsNullOrWhiteSpace(item.ReferenceId))
            .Select(item => item with
            {
                ReferenceId = item.ReferenceId.Trim(),
                ProjectId = string.IsNullOrWhiteSpace(item.ProjectId) ? null : item.ProjectId.Trim(),
                CorrelationId = string.IsNullOrWhiteSpace(item.CorrelationId) ? null : item.CorrelationId.Trim()
            })
            .ToArray();

    private static bool HasRequiredPolicyKind(WorkflowStepSensitivityKind sensitivity, IReadOnlyList<WorkflowStepPolicyRequirement> requirements)
    {
        var kinds = RequiredPolicyKindsFor(sensitivity);
        return kinds.Length == 0 || requirements.Any(requirement => kinds.Contains(requirement.Kind));
    }

    private static WorkflowStepPolicyRequirementKind[] RequiredPolicyKindsFor(WorkflowStepSensitivityKind sensitivity) =>
        sensitivity switch
        {
            WorkflowStepSensitivityKind.SourceMutation => [WorkflowStepPolicyRequirementKind.SourceMutationApprovalReference],
            WorkflowStepSensitivityKind.ToolInvocation => [WorkflowStepPolicyRequirementKind.ToolGateReference],
            WorkflowStepSensitivityKind.AgentDispatch => [WorkflowStepPolicyRequirementKind.GovernanceEventReference, WorkflowStepPolicyRequirementKind.A2aHandoffValidationReference],
            WorkflowStepSensitivityKind.ApprovalRequiredAction => [WorkflowStepPolicyRequirementKind.HumanApprovalReference],
            WorkflowStepSensitivityKind.MemoryPromotion => [WorkflowStepPolicyRequirementKind.MemoryPromotionApprovalReference],
            WorkflowStepSensitivityKind.RetrievalActivation => [WorkflowStepPolicyRequirementKind.RetrievalApprovalReference],
            WorkflowStepSensitivityKind.A2aHandoff => [WorkflowStepPolicyRequirementKind.A2aHandoffValidationReference],
            WorkflowStepSensitivityKind.ModelCall => [WorkflowStepPolicyRequirementKind.GovernanceEventReference, WorkflowStepPolicyRequirementKind.ApprovalPolicyReference],
            WorkflowStepSensitivityKind.PatchProposal => [WorkflowStepPolicyRequirementKind.GovernanceEventReference, WorkflowStepPolicyRequirementKind.ApprovalPolicyReference],
            WorkflowStepSensitivityKind.PatchApply => [WorkflowStepPolicyRequirementKind.SourceMutationApprovalReference],
            _ => []
        };

    private static WorkflowStepPolicyPreflightResult BuildResult(
        string? stepId,
        WorkflowStepSensitivityKind sensitivity,
        WorkflowStepPolicyPreflightStatus status,
        IReadOnlyList<WorkflowStepPolicyBlockReason> reasons,
        IReadOnlyList<WorkflowStepPolicyRequirement>? missing = null) =>
        new()
        {
            StepId = stepId?.Trim() ?? string.Empty,
            SensitivityKind = sensitivity,
            Status = status,
            BlockReasons = reasons
                .Concat(BoundaryReasons)
                .Distinct()
                .OrderBy(reason => reason)
                .ToArray(),
            MissingPolicyRequirements = missing?.ToArray() ?? []
        };

    private static string EvidenceKey(WorkflowStepPolicyRequirementKind kind, string referenceId) =>
        $"{(int)kind}:{referenceId.Trim()}";
}

public enum WorkflowStepSensitivityKind
{
    Unknown = 0,
    None = 1,
    SourceMutation = 2,
    ToolInvocation = 3,
    AgentDispatch = 4,
    ApprovalRequiredAction = 5,
    MemoryPromotion = 6,
    RetrievalActivation = 7,
    A2aHandoff = 8,
    ModelCall = 9,
    PatchProposal = 10,
    PatchApply = 11
}

public enum WorkflowStepPolicyRequirementKind
{
    Unknown = 0,
    ApprovalPolicyReference = 1,
    HumanApprovalReference = 2,
    GovernanceEventReference = 3,
    A2aHandoffValidationReference = 4,
    ToolGateReference = 5,
    SourceMutationApprovalReference = 6,
    MemoryPromotionApprovalReference = 7,
    RetrievalApprovalReference = 8
}

public sealed record WorkflowStepPolicyRequirement
{
    public required WorkflowStepPolicyRequirementKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? ProjectId { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record WorkflowStepPolicyEvidenceReference
{
    public required WorkflowStepPolicyRequirementKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? ProjectId { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record WorkflowStepPolicyPreflightRequest
{
    public required WorkflowStepContract? StepContract { get; init; }
    public required WorkflowStepSensitivityKind SensitivityKind { get; init; }
    public required IReadOnlyList<WorkflowStepPolicyRequirement> RequiredPolicyReferences { get; init; }
    public required IReadOnlyList<WorkflowStepPolicyEvidenceReference> AvailablePolicyEvidence { get; init; }
}

public sealed record WorkflowStepPolicyPreflightResult
{
    public required string StepId { get; init; }
    public required WorkflowStepSensitivityKind SensitivityKind { get; init; }
    public required WorkflowStepPolicyPreflightStatus Status { get; init; }
    public required IReadOnlyList<WorkflowStepPolicyBlockReason> BlockReasons { get; init; }
    public required IReadOnlyList<WorkflowStepPolicyRequirement> MissingPolicyRequirements { get; init; }
}

public enum WorkflowStepPolicyPreflightStatus
{
    Unknown = 0,
    NotSensitive = 1,
    BlockedMissingPolicyEvidence = 2,
    PolicyEvidencePresentForFutureExecution = 3,
    InvalidPolicyRequest = 4
}

public enum WorkflowStepPolicyBlockReason
{
    Unknown = 0,
    InvalidStepContract = 1,
    UnknownSensitivity = 2,
    MissingRequiredPolicyReference = 3,
    MissingPolicyEvidence = 4,
    ApprovalCannotBeInferredFromEvidence = 5,
    WorkflowCannotGrantAuthority = 6,
    PolicyPreflightCannotMutateApproval = 7,
    PolicyPreflightCannotExecute = 8
}
