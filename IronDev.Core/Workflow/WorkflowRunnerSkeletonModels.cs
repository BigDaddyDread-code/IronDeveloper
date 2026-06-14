namespace IronDev.Core.Workflow;

public interface IWorkflowRunnerSkeleton
{
    WorkflowRunnerEvaluation Evaluate(WorkflowRunnerEvaluationRequest? request);
}

public sealed class WorkflowRunnerSkeleton : IWorkflowRunnerSkeleton
{
    private readonly WorkflowStepContractValidator _contractValidator;
    private readonly IWorkflowStepPolicyPreflightChecker _policyPreflightChecker;

    public WorkflowRunnerSkeleton()
        : this(new WorkflowStepContractValidator(), new WorkflowStepPolicyPreflightChecker())
    {
    }

    internal WorkflowRunnerSkeleton(WorkflowStepContractValidator contractValidator)
        : this(contractValidator, new WorkflowStepPolicyPreflightChecker(contractValidator))
    {
    }

    internal WorkflowRunnerSkeleton(
        WorkflowStepContractValidator contractValidator,
        IWorkflowStepPolicyPreflightChecker policyPreflightChecker)
    {
        _contractValidator = contractValidator;
        _policyPreflightChecker = policyPreflightChecker;
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

        var policyPreflightRequests = (request.PolicyPreflightRequests ?? [])
            .Where(policyRequest => policyRequest.StepContract is not null &&
                                    !string.IsNullOrWhiteSpace(policyRequest.StepContract.StepContractId))
            .GroupBy(policyRequest => policyRequest.StepContract!.StepContractId.Trim(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var stepEvaluations = request.StepContracts
            .Select(step => EvaluateStep(
                request.WorkflowRunId,
                step,
                availableEvidence,
                policyPreflightRequests))
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

    private WorkflowStepRunnerEvaluation EvaluateStep(
        string workflowRunId,
        WorkflowStepContract step,
        HashSet<string> availableEvidence,
        IReadOnlyDictionary<string, WorkflowStepPolicyPreflightRequest> policyPreflightRequests)
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
                BlockReasons = [.. InvalidContractReasons(validation), .. boundaryReasons],
                MissingEvidenceRequirements = [],
                ThoughtLedgerReference = step.ThoughtLedgerReference,
                PolicyPreflightStatus = null,
                PolicyBlockReasons = [],
                MissingPolicyRequirements = [],
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
                ThoughtLedgerReference = step.ThoughtLedgerReference,
                PolicyPreflightStatus = null,
                PolicyBlockReasons = [],
                MissingPolicyRequirements = [],
                NextRecordableTransition = FirstTransition(step)
            };
        }

        var policyPreflightRequest = TryGetPolicyPreflightRequest(step.StepContractId, policyPreflightRequests);
        var policyPreflight = policyPreflightRequest is null
            ? null
            : _policyPreflightChecker.Check(policyPreflightRequest with { StepContract = step });

        if (policyPreflight?.Status == WorkflowStepPolicyPreflightStatus.InvalidPolicyRequest)
        {
            return new WorkflowStepRunnerEvaluation
            {
                StepId = step.StepContractId,
                Eligibility = WorkflowStepRunnerEligibility.BlockedByBoundary,
                BlockReasons = [WorkflowRunnerBlockReason.PolicyPreflightInvalid, .. boundaryReasons],
                MissingEvidenceRequirements = [],
                ThoughtLedgerReference = step.ThoughtLedgerReference,
                PolicyPreflightStatus = policyPreflight.Status,
                PolicyBlockReasons = policyPreflight.BlockReasons,
                MissingPolicyRequirements = policyPreflight.MissingPolicyRequirements,
                NextRecordableTransition = FirstTransition(step)
            };
        }

        if (policyPreflight?.Status == WorkflowStepPolicyPreflightStatus.BlockedMissingPolicyEvidence)
        {
            return new WorkflowStepRunnerEvaluation
            {
                StepId = step.StepContractId,
                Eligibility = WorkflowStepRunnerEligibility.BlockedByBoundary,
                BlockReasons = [WorkflowRunnerBlockReason.PolicyPreflightMissingEvidence, .. boundaryReasons],
                MissingEvidenceRequirements = [],
                ThoughtLedgerReference = step.ThoughtLedgerReference,
                PolicyPreflightStatus = policyPreflight.Status,
                PolicyBlockReasons = policyPreflight.BlockReasons,
                MissingPolicyRequirements = policyPreflight.MissingPolicyRequirements,
                NextRecordableTransition = FirstTransition(step)
            };
        }

        return new WorkflowStepRunnerEvaluation
        {
            StepId = step.StepContractId,
            Eligibility = WorkflowStepRunnerEligibility.EligibleForFutureExecution,
            BlockReasons = boundaryReasons,
            MissingEvidenceRequirements = [],
            ThoughtLedgerReference = step.ThoughtLedgerReference,
            PolicyPreflightStatus = policyPreflight?.Status,
            PolicyBlockReasons = policyPreflight?.BlockReasons ?? [],
            MissingPolicyRequirements = policyPreflight?.MissingPolicyRequirements ?? [],
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

    private static IReadOnlyList<WorkflowRunnerBlockReason> InvalidContractReasons(WorkflowRunValidationResult validation)
    {
        var reasons = new List<WorkflowRunnerBlockReason> { WorkflowRunnerBlockReason.InvalidStepContract };

        if (validation.Issues.Any(issue => string.Equals(issue.Code, "WORKFLOW_STEP_CONTRACT_THOUGHT_LEDGER_REFERENCE_REQUIRED", StringComparison.Ordinal)))
            reasons.Add(WorkflowRunnerBlockReason.MissingThoughtLedgerReference);

        if (validation.Issues.Any(issue =>
                string.Equals(issue.Code, "WORKFLOW_STEP_CONTRACT_THOUGHT_LEDGER_ENTRY_ID_REQUIRED", StringComparison.Ordinal) ||
                (string.Equals(issue.Code, "WORKFLOW_STEP_CONTRACT_TEXT_UNSAFE", StringComparison.Ordinal) &&
                 issue.Field.StartsWith("thoughtLedgerReference", StringComparison.Ordinal))))
            reasons.Add(WorkflowRunnerBlockReason.InvalidThoughtLedgerReference);

        return reasons.Distinct().OrderBy(reason => reason).ToArray();
    }

    private static WorkflowStepPolicyPreflightRequest? TryGetPolicyPreflightRequest(
        string? stepContractId,
        IReadOnlyDictionary<string, WorkflowStepPolicyPreflightRequest> policyPreflightRequests)
    {
        if (string.IsNullOrWhiteSpace(stepContractId))
            return null;

        return policyPreflightRequests.TryGetValue(stepContractId.Trim(), out var policyRequest)
            ? policyRequest
            : null;
    }

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
    public IReadOnlyList<WorkflowStepPolicyPreflightRequest> PolicyPreflightRequests { get; init; } = [];
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
    public required WorkflowStepThoughtLedgerReference? ThoughtLedgerReference { get; init; }
    public WorkflowStepPolicyPreflightStatus? PolicyPreflightStatus { get; init; }
    public IReadOnlyList<WorkflowStepPolicyBlockReason> PolicyBlockReasons { get; init; } = [];
    public IReadOnlyList<WorkflowStepPolicyRequirement> MissingPolicyRequirements { get; init; } = [];
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
    RetrievalBoundaryPreventsActivation = 11,
    PolicyPreflightMissingEvidence = 12,
    PolicyPreflightInvalid = 13,
    MissingThoughtLedgerReference = 14,
    InvalidThoughtLedgerReference = 15
}
