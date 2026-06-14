namespace IronDev.Core.Workflow;

public interface IWorkflowA2aHandoffValidator
{
    WorkflowA2aHandoffValidationResult Validate(WorkflowA2aHandoffValidationRequest? request);
}

public sealed class WorkflowA2aHandoffValidator : IWorkflowA2aHandoffValidator
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
        "receiver may act"
    ];

    private readonly WorkflowStepContractValidator _stepContractValidator;

    public WorkflowA2aHandoffValidator()
        : this(new WorkflowStepContractValidator())
    {
    }

    internal WorkflowA2aHandoffValidator(WorkflowStepContractValidator stepContractValidator)
    {
        _stepContractValidator = stepContractValidator;
    }

    public WorkflowA2aHandoffValidationResult Validate(WorkflowA2aHandoffValidationRequest? request)
    {
        if (request is null)
            return Result(string.Empty, string.Empty, WorkflowA2aHandoffValidationStatus.InvalidRequest, [WorkflowA2aHandoffBlockReason.MissingHandoffReference]);

        if (request.StepContract is null)
            return Result(string.Empty, string.Empty, WorkflowA2aHandoffValidationStatus.InvalidRequest, [WorkflowA2aHandoffBlockReason.InvalidStepContract]);

        var step = request.StepContract;
        var stepValidation = _stepContractValidator.Validate(step);
        if (!stepValidation.IsValid)
            return Result(step.WorkflowRunId, step.StepContractId, WorkflowA2aHandoffValidationStatus.InvalidStepContract, StepContractReasons(stepValidation));

        if (request.HandoffReference is null)
            return Result(step.WorkflowRunId, step.StepContractId, WorkflowA2aHandoffValidationStatus.InvalidHandoffReference, [WorkflowA2aHandoffBlockReason.MissingHandoffReference]);

        var handoff = request.HandoffReference;
        var blockReasons = new List<WorkflowA2aHandoffBlockReason>();

        Require(handoff.HandoffReferenceId, WorkflowA2aHandoffBlockReason.MissingHandoffReferenceId, blockReasons);
        ValidateTextSafety(handoff.HandoffReferenceId, blockReasons);
        ValidateTextSafety(handoff.WorkflowRunId, blockReasons);
        ValidateTextSafety(handoff.WorkflowStepId, blockReasons);
        ValidateTextSafety(handoff.CorrelationId, blockReasons);
        if (!string.Equals(handoff.WorkflowRunId?.Trim(), step.WorkflowRunId.Trim(), StringComparison.Ordinal))
            blockReasons.Add(WorkflowA2aHandoffBlockReason.WorkflowRunMismatch);

        if (!string.Equals(handoff.WorkflowStepId?.Trim(), step.StepContractId.Trim(), StringComparison.Ordinal))
            blockReasons.Add(WorkflowA2aHandoffBlockReason.WorkflowStepMismatch);

        ValidateParticipant(handoff.Sender, WorkflowA2aHandoffBlockReason.MissingSender, blockReasons);
        ValidateParticipant(handoff.Receiver, WorkflowA2aHandoffBlockReason.MissingReceiver, blockReasons);
        ValidateTextSafety(handoff.SafeSummary, blockReasons);
        ValidateThoughtLedgerReference(step.ThoughtLedgerReference, handoff.ThoughtLedgerReference, blockReasons);

        if (blockReasons.Count > 0)
            return Result(step.WorkflowRunId, step.StepContractId, WorkflowA2aHandoffValidationStatus.InvalidHandoffReference, blockReasons);

        var available = (request.AvailableEvidence ?? [])
            .Where(evidence => evidence.Kind != WorkflowA2aHandoffEvidenceKind.Unknown && !string.IsNullOrWhiteSpace(evidence.ReferenceId))
            .Select(evidence => EvidenceKey(evidence.Kind, evidence.ReferenceId))
            .ToHashSet(StringComparer.Ordinal);

        var requiredEvidence = RequiredEvidenceFor(step, handoff, blockReasons);
        if (blockReasons.Count > 0)
            return Result(step.WorkflowRunId, step.StepContractId, WorkflowA2aHandoffValidationStatus.InvalidHandoffReference, blockReasons);

        var missingEvidence = requiredEvidence
            .Where(evidence => !available.Contains(EvidenceKey(evidence.Kind, evidence.ReferenceId)))
            .ToArray();

        if (missingEvidence.Length > 0)
            return Result(
                step.WorkflowRunId,
                step.StepContractId,
                WorkflowA2aHandoffValidationStatus.BlockedMissingEvidence,
                MissingEvidenceReasons(missingEvidence),
                missingEvidence);

        return Result(step.WorkflowRunId, step.StepContractId, WorkflowA2aHandoffValidationStatus.ValidForFutureHandoff, []);
    }

    private static WorkflowA2aHandoffEvidenceReference[] RequiredEvidenceFor(
        WorkflowStepContract step,
        WorkflowA2aHandoffReference handoff,
        List<WorkflowA2aHandoffBlockReason> blockReasons)
    {
        var governanceEventId = step.ThoughtLedgerReference?.GovernanceEventId ?? handoff.ThoughtLedgerReference?.GovernanceEventId;
        if (string.IsNullOrWhiteSpace(governanceEventId))
            blockReasons.Add(WorkflowA2aHandoffBlockReason.MissingGovernanceEvidence);

        var required = new List<WorkflowA2aHandoffEvidenceReference>
        {
            new()
            {
                Kind = WorkflowA2aHandoffEvidenceKind.GovernanceEventReference,
                ReferenceId = governanceEventId ?? string.Empty,
                CorrelationId = handoff.CorrelationId
            },
            new()
            {
                Kind = WorkflowA2aHandoffEvidenceKind.HandoffContractReference,
                ReferenceId = handoff.HandoffReferenceId,
                CorrelationId = handoff.CorrelationId
            },
            new()
            {
                Kind = WorkflowA2aHandoffEvidenceKind.HandoffValidationReference,
                ReferenceId = handoff.HandoffReferenceId,
                CorrelationId = handoff.CorrelationId
            }
        };

        foreach (var evidence in required)
        {
            ValidateTextSafety(evidence.ReferenceId, blockReasons);
            ValidateTextSafety(evidence.CorrelationId, blockReasons);
        }

        if (IsA2aSensitive(step))
        {
            var policyReference = new WorkflowA2aHandoffEvidenceReference
            {
                Kind = WorkflowA2aHandoffEvidenceKind.PolicyPreflightReference,
                ReferenceId = handoff.CorrelationId ?? step.StepContractId,
                CorrelationId = handoff.CorrelationId
            };
            ValidateTextSafety(policyReference.ReferenceId, blockReasons);
            ValidateTextSafety(policyReference.CorrelationId, blockReasons);
            required.Add(policyReference);
        }

        return required.ToArray();
    }

    private static bool IsA2aSensitive(WorkflowStepContract step) =>
        step.Intent == WorkflowStepContractIntent.RecordHandoffContext ||
        step.InputReference.Kind == WorkflowStepContractReferenceKind.HandoffRecord ||
        step.ExpectedOutputReference.Kind == WorkflowStepContractReferenceKind.HandoffRecord ||
        step.EvidenceRequirements.Any(requirement => requirement.Kind == WorkflowStepContractEvidenceRequirementKind.HandoffRecordReference);

    private static IReadOnlyList<WorkflowA2aHandoffBlockReason> StepContractReasons(WorkflowRunValidationResult validation)
    {
        var reasons = new List<WorkflowA2aHandoffBlockReason> { WorkflowA2aHandoffBlockReason.InvalidStepContract };

        if (validation.Issues.Any(issue => string.Equals(issue.Code, "WORKFLOW_STEP_CONTRACT_THOUGHT_LEDGER_REFERENCE_REQUIRED", StringComparison.Ordinal)))
            reasons.Add(WorkflowA2aHandoffBlockReason.MissingThoughtLedgerReference);

        if (validation.Issues.Any(issue =>
                string.Equals(issue.Code, "WORKFLOW_STEP_CONTRACT_THOUGHT_LEDGER_ENTRY_ID_REQUIRED", StringComparison.Ordinal) ||
                (string.Equals(issue.Code, "WORKFLOW_STEP_CONTRACT_TEXT_UNSAFE", StringComparison.Ordinal) &&
                 issue.Field.StartsWith("thoughtLedgerReference", StringComparison.Ordinal))))
            reasons.Add(WorkflowA2aHandoffBlockReason.InvalidThoughtLedgerReference);

        return reasons.Distinct().OrderBy(reason => reason).ToArray();
    }

    private static WorkflowA2aHandoffBlockReason[] MissingEvidenceReasons(IReadOnlyList<WorkflowA2aHandoffEvidenceReference> missingEvidence)
    {
        var reasons = new List<WorkflowA2aHandoffBlockReason>();
        if (missingEvidence.Any(evidence => evidence.Kind == WorkflowA2aHandoffEvidenceKind.GovernanceEventReference))
            reasons.Add(WorkflowA2aHandoffBlockReason.MissingGovernanceEvidence);

        if (missingEvidence.Any(evidence => evidence.Kind is WorkflowA2aHandoffEvidenceKind.HandoffContractReference or WorkflowA2aHandoffEvidenceKind.HandoffValidationReference))
            reasons.Add(WorkflowA2aHandoffBlockReason.MissingHandoffContractEvidence);

        if (missingEvidence.Any(evidence => evidence.Kind == WorkflowA2aHandoffEvidenceKind.PolicyPreflightReference))
            reasons.Add(WorkflowA2aHandoffBlockReason.MissingPolicyPreflightEvidence);

        return reasons.Distinct().OrderBy(reason => reason).ToArray();
    }

    private static void ValidateParticipant(
        WorkflowA2aParticipantReference? participant,
        WorkflowA2aHandoffBlockReason missingReason,
        List<WorkflowA2aHandoffBlockReason> blockReasons)
    {
        if (participant is null)
        {
            blockReasons.Add(missingReason);
            return;
        }

        if (!Enum.IsDefined(participant.Kind) || participant.Kind == WorkflowA2aParticipantKind.Unknown)
            blockReasons.Add(WorkflowA2aHandoffBlockReason.UnknownParticipantKind);

        if (string.IsNullOrWhiteSpace(participant.ReferenceId))
            blockReasons.Add(WorkflowA2aHandoffBlockReason.MissingParticipantReference);

        ValidateTextSafety(participant.ReferenceId, blockReasons);
        ValidateTextSafety(participant.SafeLabel, blockReasons);
    }

    private static void ValidateThoughtLedgerReference(
        WorkflowStepThoughtLedgerReference? stepReference,
        WorkflowStepThoughtLedgerReference? handoffReference,
        List<WorkflowA2aHandoffBlockReason> blockReasons)
    {
        if (handoffReference is null)
        {
            blockReasons.Add(WorkflowA2aHandoffBlockReason.MissingThoughtLedgerReference);
            return;
        }

        if (string.IsNullOrWhiteSpace(handoffReference.ThoughtLedgerEntryId))
            blockReasons.Add(WorkflowA2aHandoffBlockReason.InvalidThoughtLedgerReference);

        if (stepReference is null ||
            !string.Equals(stepReference.ThoughtLedgerEntryId?.Trim(), handoffReference.ThoughtLedgerEntryId?.Trim(), StringComparison.Ordinal))
            blockReasons.Add(WorkflowA2aHandoffBlockReason.InvalidThoughtLedgerReference);

        ValidateTextSafety(handoffReference.ThoughtLedgerEntryId, blockReasons);
        ValidateTextSafety(handoffReference.TraceId, blockReasons);
        ValidateTextSafety(handoffReference.GovernanceEventId, blockReasons);
        ValidateTextSafety(handoffReference.CorrelationId, blockReasons);
        ValidateTextSafety(handoffReference.SafeSummary, blockReasons);
    }

    private static void Require(string? value, WorkflowA2aHandoffBlockReason reason, List<WorkflowA2aHandoffBlockReason> blockReasons)
    {
        if (string.IsNullOrWhiteSpace(value))
            blockReasons.Add(reason);
    }

    private static void ValidateTextSafety(string? value, List<WorkflowA2aHandoffBlockReason> blockReasons)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (PrivateReasoningMarkers.Concat(AuthorityMarkers).Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            blockReasons.Add(WorkflowA2aHandoffBlockReason.InvalidThoughtLedgerReference);
    }

    private static WorkflowA2aHandoffValidationResult Result(
        string? workflowRunId,
        string? workflowStepId,
        WorkflowA2aHandoffValidationStatus status,
        IReadOnlyList<WorkflowA2aHandoffBlockReason> blockReasons,
        IReadOnlyList<WorkflowA2aHandoffEvidenceReference>? missingEvidence = null) =>
        new()
        {
            WorkflowRunId = workflowRunId?.Trim() ?? string.Empty,
            WorkflowStepId = workflowStepId?.Trim() ?? string.Empty,
            Status = status,
            BlockReasons = blockReasons.Distinct().OrderBy(reason => reason).ToArray(),
            MissingEvidence = missingEvidence?.ToArray() ?? []
        };

    private static string EvidenceKey(WorkflowA2aHandoffEvidenceKind kind, string referenceId) =>
        $"{(int)kind}:{referenceId.Trim()}";
}

public sealed record WorkflowA2aHandoffValidationRequest
{
    public required WorkflowStepContract? StepContract { get; init; }
    public required WorkflowA2aHandoffReference? HandoffReference { get; init; }
    public required IReadOnlyList<WorkflowA2aHandoffEvidenceReference> AvailableEvidence { get; init; }
}

public sealed record WorkflowA2aHandoffReference
{
    public required string HandoffReferenceId { get; init; }
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required WorkflowA2aParticipantReference? Sender { get; init; }
    public required WorkflowA2aParticipantReference? Receiver { get; init; }
    public required WorkflowStepThoughtLedgerReference? ThoughtLedgerReference { get; init; }
    public string? CorrelationId { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed record WorkflowA2aParticipantReference
{
    public required WorkflowA2aParticipantKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? SafeLabel { get; init; }
}

public enum WorkflowA2aParticipantKind
{
    Unknown = 0,
    Human = 1,
    Agent = 2,
    SystemRecorder = 3,
    ToolGateway = 4
}

public sealed record WorkflowA2aHandoffEvidenceReference
{
    public required WorkflowA2aHandoffEvidenceKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string? CorrelationId { get; init; }
}

public enum WorkflowA2aHandoffEvidenceKind
{
    Unknown = 0,
    GovernanceEventReference = 1,
    ThoughtLedgerReference = 2,
    PolicyPreflightReference = 3,
    HandoffContractReference = 4,
    HandoffValidationReference = 5,
    ReviewMaterialReference = 6
}

public sealed record WorkflowA2aHandoffValidationResult
{
    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required WorkflowA2aHandoffValidationStatus Status { get; init; }
    public required IReadOnlyList<WorkflowA2aHandoffBlockReason> BlockReasons { get; init; }
    public required IReadOnlyList<WorkflowA2aHandoffEvidenceReference> MissingEvidence { get; init; }
}

public enum WorkflowA2aHandoffValidationStatus
{
    Unknown = 0,
    InvalidRequest = 1,
    InvalidStepContract = 2,
    InvalidHandoffReference = 3,
    BlockedMissingEvidence = 4,
    ValidForFutureHandoff = 5
}

public enum WorkflowA2aHandoffBlockReason
{
    Unknown = 0,
    InvalidStepContract = 1,
    MissingHandoffReference = 2,
    MissingHandoffReferenceId = 3,
    WorkflowRunMismatch = 4,
    WorkflowStepMismatch = 5,
    MissingSender = 6,
    MissingReceiver = 7,
    UnknownParticipantKind = 8,
    MissingParticipantReference = 9,
    MissingThoughtLedgerReference = 10,
    InvalidThoughtLedgerReference = 11,
    MissingGovernanceEvidence = 12,
    MissingHandoffContractEvidence = 13,
    MissingPolicyPreflightEvidence = 14,
    HandoffValidationCannotDispatch = 15,
    HandoffValidationCannotGrantAuthority = 16,
    HandoffValidationCannotExecute = 17,
    HandoffValidationCannotApprove = 18
}
