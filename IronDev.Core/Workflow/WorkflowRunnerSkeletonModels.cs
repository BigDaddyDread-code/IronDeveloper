namespace IronDev.Core.Workflow;

public interface IWorkflowRunnerSkeleton
{
    WorkflowRunnerEvaluation Evaluate(WorkflowRunnerEvaluationRequest? request);
}

public sealed class WorkflowRunnerSkeleton : IWorkflowRunnerSkeleton
{
    private readonly WorkflowStepContractValidator _contractValidator;

    public WorkflowRunnerSkeleton()
        : this(new WorkflowStepContractValidator())
    {
    }

    internal WorkflowRunnerSkeleton(WorkflowStepContractValidator contractValidator)
    {
        _contractValidator = contractValidator;
    }

    public WorkflowRunnerEvaluation Evaluate(WorkflowRunnerEvaluationRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.WorkflowRunId))
            return InvalidRequest(request?.WorkflowRunId ?? string.Empty, WorkflowRunnerBlockReason.MissingWorkflowRunId);

        if (request.StepContracts is null || request.StepContracts.Count == 0)
            return new WorkflowRunnerEvaluation
            {
                WorkflowRunId = request.WorkflowRunId,
                Status = WorkflowRunnerEvaluationStatus.NoSteps,
                StepEvaluations = [],
                BlockReasons = [WorkflowRunnerBlockReason.NoStepContracts]
            };

        var availableEvidence = (request.AvailableEvidence ?? [])
            .Where(evidence => !string.IsNullOrWhiteSpace(evidence.ReferenceId))
            .Select(evidence => EvidenceKey(evidence.Kind, evidence.ReferenceId))
            .ToHashSet(StringComparer.Ordinal);

        var stepEvaluations = request.StepContracts
            .Select(step => EvaluateStep(request.WorkflowRunId, step, availableEvidence))
            .ToArray();

        var aggregateReasons = stepEvaluations
            .SelectMany(step => step.BlockReasons)
            .Distinct()
            .OrderBy(reason => reason)
            .ToArray();

        return new WorkflowRunnerEvaluation
        {
            WorkflowRunId = request.WorkflowRunId,
            Status = stepEvaluations.Any(step => step.Eligibility == WorkflowStepRunnerEligibility.EligibleForFutureExecution)
                ? WorkflowRunnerEvaluationStatus.HasEligibleSteps
                : WorkflowRunnerEvaluationStatus.AllBlocked,
            StepEvaluations = stepEvaluations,
            BlockReasons = aggregateReasons
        };
    }

    private WorkflowStepRunnerEvaluation EvaluateStep(string workflowRunId, WorkflowStepContract step, HashSet<string> availableEvidence)
    {
        var validation = _contractValidator.Validate(step);
        if (!string.Equals(step.WorkflowRunId, workflowRunId, StringComparison.Ordinal))
        {
            validation = validation with
            {
                IsValid = false,
                Issues = validation.Issues.Concat(
                    [
                        new WorkflowRunValidationIssue
                        {
                            Code = "WORKFLOW_RUNNER_STEP_RUN_MISMATCH",
                            Severity = "Error",
                            Message = "Step contract workflow run id must match the evaluated workflow run id.",
                            Field = "workflowRunId"
                        }
                    ]).ToArray()
            };
        }

        var boundaryReasons = BoundaryReasonsFor(step);
        if (!validation.IsValid)
        {
            return new WorkflowStepRunnerEvaluation
            {
                StepId = step.StepContractId,
                Eligibility = WorkflowStepRunnerEligibility.InvalidContract,
                BlockReasons = [WorkflowRunnerBlockReason.InvalidStepContract, .. boundaryReasons],
                MissingEvidenceRequirements = [],
                NextRecordableTransition = null
            };
        }

        var missingEvidence = step.EvidenceRequirements
            .Where(requirement => !availableEvidence.Contains(EvidenceKey(requirement.Kind, requirement.RequirementId)))
            .ToArray();

        if (missingEvidence.Length > 0)
        {
            return new WorkflowStepRunnerEvaluation
            {
                StepId = step.StepContractId,
                Eligibility = WorkflowStepRunnerEligibility.BlockedMissingEvidence,
                BlockReasons = [WorkflowRunnerBlockReason.MissingRequiredEvidence, .. boundaryReasons],
                MissingEvidenceRequirements = missingEvidence,
                NextRecordableTransition = FirstTransition(step)
            };
        }

        return new WorkflowStepRunnerEvaluation
        {
            StepId = step.StepContractId,
            Eligibility = WorkflowStepRunnerEligibility.EligibleForFutureExecution,
            BlockReasons = boundaryReasons,
            MissingEvidenceRequirements = [],
            NextRecordableTransition = FirstTransition(step)
        };
    }

    private static WorkflowRunnerEvaluation InvalidRequest(string workflowRunId, WorkflowRunnerBlockReason reason) =>
        new()
        {
            WorkflowRunId = workflowRunId,
            Status = WorkflowRunnerEvaluationStatus.InvalidRequest,
            StepEvaluations = [],
            BlockReasons = [reason]
        };

    private static WorkflowStepContractTransitionKind? FirstTransition(WorkflowStepContract step) =>
        step.AllowedTransitions.FirstOrDefault()?.Kind;

    private static IReadOnlyList<WorkflowRunnerBlockReason> BoundaryReasonsFor(WorkflowStepContract step)
    {
        var reasons = new List<WorkflowRunnerBlockReason>
        {
            WorkflowRunnerBlockReason.RuntimeBoundaryPreventsExecution,
            WorkflowRunnerBlockReason.RetrievalBoundaryPreventsActivation
        };

        if (step.ExpectedActorKind == WorkflowStepContractActorKind.AgentExpected)
            reasons.Add(WorkflowRunnerBlockReason.DispatchBoundaryPreventsActorResolution);

        if (step.ExpectedActorKind == WorkflowStepContractActorKind.ToolExpected)
            reasons.Add(WorkflowRunnerBlockReason.ToolBoundaryPreventsInvocation);

        if (step.InputReference.Kind == WorkflowStepContractReferenceKind.ApprovalPolicyRecord ||
            step.ExpectedOutputReference.Kind == WorkflowStepContractReferenceKind.ApprovalPolicyRecord ||
            step.EvidenceRequirements.Any(requirement => requirement.Kind == WorkflowStepContractEvidenceRequirementKind.ApprovalPolicyReference))
            reasons.Add(WorkflowRunnerBlockReason.ApprovalBoundaryPreventsMutation);

        if (step.InputReference.Kind == WorkflowStepContractReferenceKind.MemoryProposalRecord ||
            step.ExpectedOutputReference.Kind == WorkflowStepContractReferenceKind.MemoryProposalRecord)
            reasons.Add(WorkflowRunnerBlockReason.MemoryBoundaryPreventsPromotion);

        return reasons.Distinct().OrderBy(reason => reason).ToArray();
    }

    private static string EvidenceKey(WorkflowStepContractEvidenceRequirementKind kind, string referenceId) =>
        $"{(int)kind}:{referenceId.Trim()}";
}

public sealed record WorkflowRunnerEvaluationRequest
{
    public required string WorkflowRunId { get; init; }
    public required IReadOnlyList<WorkflowStepContract> StepContracts { get; init; }
    public required IReadOnlyList<WorkflowEvidenceReference> AvailableEvidence { get; init; }
}

public sealed record WorkflowEvidenceReference
{
    public required WorkflowStepContractEvidenceRequirementKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? CorrelationId { get; init; }
}

public sealed record WorkflowRunnerEvaluation
{
    public required string WorkflowRunId { get; init; }
    public required WorkflowRunnerEvaluationStatus Status { get; init; }
    public required IReadOnlyList<WorkflowStepRunnerEvaluation> StepEvaluations { get; init; }
    public required IReadOnlyList<WorkflowRunnerBlockReason> BlockReasons { get; init; }
}

public enum WorkflowRunnerEvaluationStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    NoSteps = 2,
    AllBlocked = 3,
    HasEligibleSteps = 4
}

public sealed record WorkflowStepRunnerEvaluation
{
    public required string StepId { get; init; }
    public required WorkflowStepRunnerEligibility Eligibility { get; init; }
    public required IReadOnlyList<WorkflowRunnerBlockReason> BlockReasons { get; init; }
    public required IReadOnlyList<WorkflowStepContractEvidenceRequirement> MissingEvidenceRequirements { get; init; }
    public WorkflowStepContractTransitionKind? NextRecordableTransition { get; init; }
}

public enum WorkflowStepRunnerEligibility
{
    Unknown = 0,
    InvalidContract = 1,
    BlockedMissingEvidence = 2,
    BlockedByBoundary = 3,
    EligibleForFutureExecution = 4
}

public enum WorkflowRunnerBlockReason
{
    Unknown = 0,
    MissingWorkflowRunId = 1,
    NoStepContracts = 2,
    InvalidStepContract = 3,
    MissingRequiredEvidence = 4,
    RuntimeBoundaryPreventsExecution = 5,
    DispatchBoundaryPreventsActorResolution = 6,
    ToolBoundaryPreventsInvocation = 7,
    SourceMutationBoundaryPreventsApply = 8,
    ApprovalBoundaryPreventsMutation = 9,
    MemoryBoundaryPreventsPromotion = 10,
    RetrievalBoundaryPreventsActivation = 11
}
