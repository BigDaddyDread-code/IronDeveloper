namespace IronDev.Core.Workflow;

public enum WorkflowStepContractIntent
{
    Unknown = 0,
    AnalyzeEvidence = 1,
    PrepareReviewMaterial = 2,
    ReviewMaterial = 3,
    RecordDecisionSupport = 4,
    RecordReceipt = 5,
    RecordHandoffContext = 6,
    RecordApprovalRequirement = 7
}

public enum WorkflowStepContractActorKind
{
    Unknown = 0,
    HumanReviewer = 1,
    AgentExpected = 2,
    ToolExpected = 3,
    SystemRecorder = 4
}

public enum WorkflowStepContractReferenceKind
{
    Unknown = 0,
    WorkflowRunRecord = 1,
    WorkflowStepRecord = 2,
    WorkflowCheckpointRecord = 3,
    GovernanceEventRecord = 4,
    ApprovalPolicyRecord = 5,
    HandoffRecord = 6,
    MemoryProposalRecord = 7,
    ReviewMaterial = 8,
    EvidencePackage = 9,
    Receipt = 10
}

public enum WorkflowStepContractTransitionKind
{
    Unknown = 0,
    DraftToReadyForReview = 1,
    ReadyForReviewToChangesRequested = 2,
    ReadyForReviewToReceiptRecorded = 3,
    ChangesRequestedToReadyForReview = 4,
    AnyToCancelled = 5
}

public enum WorkflowStepContractEvidenceRequirementKind
{
    Unknown = 0,
    GovernanceEventReference = 1,
    HandoffRecordReference = 2,
    ApprovalPolicyReference = 3,
    ReviewMaterialReference = 4,
    ValidationCommandReference = 5,
    ReceiptReference = 6
}

public sealed record WorkflowStepContractReference
{
    public required WorkflowStepContractReferenceKind Kind { get; init; }
    public required string ReferenceId { get; init; }
    public string SafeSummary { get; init; } = string.Empty;
    public bool HydratesContent { get; init; }
    public bool ActivatesRetrieval { get; init; }
    public bool GrantsApproval { get; init; }
    public bool AllowsExecution { get; init; }
    public bool MutatesSource { get; init; }
    public bool PromotesMemory { get; init; }
}

public sealed record WorkflowStepContractTransitionRule
{
    public required WorkflowStepContractTransitionKind Kind { get; init; }
    public string SafeLabel { get; init; } = string.Empty;
    public bool StartsWorkflow { get; init; }
    public bool ContinuesWorkflow { get; init; }
    public bool DispatchesAgent { get; init; }
    public bool InvokesTool { get; init; }
    public bool IndicatesExecutionSuccess { get; init; }
}

public sealed record WorkflowStepContractEvidenceRequirement
{
    public required WorkflowStepContractEvidenceRequirementKind Kind { get; init; }
    public required string RequirementId { get; init; }
    public string SafeSummary { get; init; } = string.Empty;
    public bool IsApproval { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool AllowsExecution { get; init; }
    public bool PromotesMemory { get; init; }
    public bool RequiresHydratedContent { get; init; }
}

public sealed record WorkflowStepContractBoundary
{
    public bool AllowsExecution { get; init; }
    public bool AllowsAgentDispatch { get; init; }
    public bool AllowsToolInvocation { get; init; }
    public bool AllowsSourceMutation { get; init; }
    public bool AllowsApprovalMutation { get; init; }
    public bool AllowsMemoryPromotion { get; init; }
    public bool AllowsRetrievalActivation { get; init; }
    public bool AllowsWorkflowContinuation { get; init; }
}

public sealed record WorkflowStepContract
{
    public required string StepContractId { get; init; }
    public required string WorkflowRunId { get; init; }
    public required WorkflowStepContractIntent Intent { get; init; }
    public required WorkflowStepContractReference InputReference { get; init; }
    public required WorkflowStepContractReference ExpectedOutputReference { get; init; }
    public required WorkflowStepContractActorKind ExpectedActorKind { get; init; }
    public IReadOnlyList<WorkflowStepContractTransitionRule> AllowedTransitions { get; init; } = [];
    public IReadOnlyList<WorkflowStepContractEvidenceRequirement> EvidenceRequirements { get; init; } = [];
    public required WorkflowStepContractBoundary Boundary { get; init; }
    public string SafeSummary { get; init; } = string.Empty;
}

public sealed class WorkflowStepContractValidator
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

    public WorkflowRunValidationResult Validate(WorkflowStepContract? contract)
    {
        var issues = new List<WorkflowRunValidationIssue>();

        if (contract is null)
        {
            AddError(issues, "WORKFLOW_STEP_CONTRACT_REQUIRED", "Workflow step contract is required.", "contract");
            return Result(issues);
        }

        Require(contract.StepContractId, "WORKFLOW_STEP_CONTRACT_STEP_ID_REQUIRED", "stepContractId", issues);
        Require(contract.WorkflowRunId, "WORKFLOW_STEP_CONTRACT_RUN_ID_REQUIRED", "workflowRunId", issues);
        ValidateEnum(contract.Intent, "WORKFLOW_STEP_CONTRACT_INTENT_INVALID", "intent", issues);
        ValidateEnum(contract.ExpectedActorKind, "WORKFLOW_STEP_CONTRACT_ACTOR_KIND_INVALID", "expectedActorKind", issues);
        ValidateTextSafety(contract.StepContractId, "stepContractId", issues);
        ValidateTextSafety(contract.WorkflowRunId, "workflowRunId", issues);
        ValidateTextSafety(contract.SafeSummary, "safeSummary", issues);

        ValidateReference(contract.InputReference, "inputReference", issues);
        ValidateReference(contract.ExpectedOutputReference, "expectedOutputReference", issues);
        ValidateTransitions(contract.AllowedTransitions, issues);
        ValidateEvidenceRequirements(contract.EvidenceRequirements, issues);
        ValidateBoundary(contract.Boundary, issues);

        return Result(issues);
    }

    public WorkflowStepContract Normalize(WorkflowStepContract contract) =>
        contract with
        {
            StepContractId = contract.StepContractId.Trim(),
            WorkflowRunId = contract.WorkflowRunId.Trim(),
            SafeSummary = contract.SafeSummary.Trim(),
            InputReference = NormalizeReference(contract.InputReference),
            ExpectedOutputReference = NormalizeReference(contract.ExpectedOutputReference),
            AllowedTransitions = contract.AllowedTransitions.Select(NormalizeTransition).ToArray(),
            EvidenceRequirements = contract.EvidenceRequirements.Select(NormalizeEvidenceRequirement).ToArray()
        };

    private static WorkflowStepContractReference NormalizeReference(WorkflowStepContractReference reference) =>
        reference with
        {
            ReferenceId = reference.ReferenceId.Trim(),
            SafeSummary = reference.SafeSummary.Trim()
        };

    private static WorkflowStepContractTransitionRule NormalizeTransition(WorkflowStepContractTransitionRule transition) =>
        transition with { SafeLabel = transition.SafeLabel.Trim() };

    private static WorkflowStepContractEvidenceRequirement NormalizeEvidenceRequirement(WorkflowStepContractEvidenceRequirement requirement) =>
        requirement with
        {
            RequirementId = requirement.RequirementId.Trim(),
            SafeSummary = requirement.SafeSummary.Trim()
        };

    private static void ValidateReference(WorkflowStepContractReference? reference, string field, List<WorkflowRunValidationIssue> issues)
    {
        if (reference is null)
        {
            AddError(issues, "WORKFLOW_STEP_CONTRACT_REFERENCE_REQUIRED", "Workflow step contract reference is required.", field);
            return;
        }

        ValidateEnum(reference.Kind, "WORKFLOW_STEP_CONTRACT_REFERENCE_KIND_INVALID", $"{field}.kind", issues);
        Require(reference.ReferenceId, "WORKFLOW_STEP_CONTRACT_REFERENCE_ID_REQUIRED", $"{field}.referenceId", issues);
        ValidateTextSafety(reference.ReferenceId, $"{field}.referenceId", issues);
        ValidateTextSafety(reference.SafeSummary, $"{field}.safeSummary", issues);
        RejectAuthorityFlag(reference.HydratesContent, $"{field}.hydratesContent", issues);
        RejectAuthorityFlag(reference.ActivatesRetrieval, $"{field}.activatesRetrieval", issues);
        RejectAuthorityFlag(reference.GrantsApproval, $"{field}.grantsApproval", issues);
        RejectAuthorityFlag(reference.AllowsExecution, $"{field}.allowsExecution", issues);
        RejectAuthorityFlag(reference.MutatesSource, $"{field}.mutatesSource", issues);
        RejectAuthorityFlag(reference.PromotesMemory, $"{field}.promotesMemory", issues);
    }

    private static void ValidateTransitions(IReadOnlyList<WorkflowStepContractTransitionRule>? transitions, List<WorkflowRunValidationIssue> issues)
    {
        if (transitions is null || transitions.Count == 0)
        {
            AddError(issues, "WORKFLOW_STEP_CONTRACT_TRANSITION_REQUIRED", "At least one allowed transition is required.", "allowedTransitions");
            return;
        }

        for (var i = 0; i < transitions.Count; i++)
        {
            var transition = transitions[i];
            var field = $"allowedTransitions[{i}]";
            ValidateEnum(transition.Kind, "WORKFLOW_STEP_CONTRACT_TRANSITION_KIND_INVALID", $"{field}.kind", issues);
            ValidateTextSafety(transition.SafeLabel, $"{field}.safeLabel", issues);
            RejectAuthorityFlag(transition.StartsWorkflow, $"{field}.startsWorkflow", issues);
            RejectAuthorityFlag(transition.ContinuesWorkflow, $"{field}.continuesWorkflow", issues);
            RejectAuthorityFlag(transition.DispatchesAgent, $"{field}.dispatchesAgent", issues);
            RejectAuthorityFlag(transition.InvokesTool, $"{field}.invokesTool", issues);
            RejectAuthorityFlag(transition.IndicatesExecutionSuccess, $"{field}.indicatesExecutionSuccess", issues);
        }
    }

    private static void ValidateEvidenceRequirements(IReadOnlyList<WorkflowStepContractEvidenceRequirement>? requirements, List<WorkflowRunValidationIssue> issues)
    {
        if (requirements is null || requirements.Count == 0)
        {
            AddError(issues, "WORKFLOW_STEP_CONTRACT_EVIDENCE_REQUIRED", "At least one evidence requirement is required.", "evidenceRequirements");
            return;
        }

        for (var i = 0; i < requirements.Count; i++)
        {
            var requirement = requirements[i];
            var field = $"evidenceRequirements[{i}]";
            ValidateEnum(requirement.Kind, "WORKFLOW_STEP_CONTRACT_EVIDENCE_KIND_INVALID", $"{field}.kind", issues);
            Require(requirement.RequirementId, "WORKFLOW_STEP_CONTRACT_EVIDENCE_ID_REQUIRED", $"{field}.requirementId", issues);
            ValidateTextSafety(requirement.RequirementId, $"{field}.requirementId", issues);
            ValidateTextSafety(requirement.SafeSummary, $"{field}.safeSummary", issues);
            RejectAuthorityFlag(requirement.IsApproval, $"{field}.isApproval", issues);
            RejectAuthorityFlag(requirement.SatisfiesPolicy, $"{field}.satisfiesPolicy", issues);
            RejectAuthorityFlag(requirement.AllowsExecution, $"{field}.allowsExecution", issues);
            RejectAuthorityFlag(requirement.PromotesMemory, $"{field}.promotesMemory", issues);
            RejectAuthorityFlag(requirement.RequiresHydratedContent, $"{field}.requiresHydratedContent", issues);
        }
    }

    private static void ValidateBoundary(WorkflowStepContractBoundary? boundary, List<WorkflowRunValidationIssue> issues)
    {
        if (boundary is null)
        {
            AddError(issues, "WORKFLOW_STEP_CONTRACT_BOUNDARY_REQUIRED", "Workflow step contract boundary is required.", "boundary");
            return;
        }

        RejectAuthorityFlag(boundary.AllowsExecution, "boundary.allowsExecution", issues);
        RejectAuthorityFlag(boundary.AllowsAgentDispatch, "boundary.allowsAgentDispatch", issues);
        RejectAuthorityFlag(boundary.AllowsToolInvocation, "boundary.allowsToolInvocation", issues);
        RejectAuthorityFlag(boundary.AllowsSourceMutation, "boundary.allowsSourceMutation", issues);
        RejectAuthorityFlag(boundary.AllowsApprovalMutation, "boundary.allowsApprovalMutation", issues);
        RejectAuthorityFlag(boundary.AllowsMemoryPromotion, "boundary.allowsMemoryPromotion", issues);
        RejectAuthorityFlag(boundary.AllowsRetrievalActivation, "boundary.allowsRetrievalActivation", issues);
        RejectAuthorityFlag(boundary.AllowsWorkflowContinuation, "boundary.allowsWorkflowContinuation", issues);
    }

    private static void ValidateEnum<TEnum>(TEnum value, string code, string field, List<WorkflowRunValidationIssue> issues)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value) || Convert.ToInt32(value) == 0)
            AddError(issues, code, $"{field} is invalid.", field);
    }

    private static void Require(string? value, string code, string field, List<WorkflowRunValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            AddError(issues, code, $"{field} is required.", field);
    }

    private static void ValidateTextSafety(string? value, string field, List<WorkflowRunValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (var marker in PrivateReasoningMarkers.Concat(AuthorityMarkers))
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                AddError(issues, "WORKFLOW_STEP_CONTRACT_TEXT_UNSAFE", $"{field} contains unsafe workflow step contract text.", field);
        }
    }

    private static void RejectAuthorityFlag(bool value, string field, List<WorkflowRunValidationIssue> issues)
    {
        if (value)
            AddError(issues, "WORKFLOW_STEP_CONTRACT_BOUNDARY_AUTHORITY_BLOCKED", $"{field} cannot grant execution, authority, mutation, retrieval activation, or memory promotion.", field);
    }

    private static WorkflowRunValidationResult Result(List<WorkflowRunValidationIssue> issues) =>
        new()
        {
            IsValid = issues.Count == 0,
            Issues = issues
        };

    private static void AddError(List<WorkflowRunValidationIssue> issues, string code, string message, string field) =>
        issues.Add(new WorkflowRunValidationIssue
        {
            Code = code,
            Severity = "Error",
            Message = message,
            Field = field
        });
}
