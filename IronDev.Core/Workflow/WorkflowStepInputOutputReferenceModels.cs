using System.Text.Json;

namespace IronDev.Core.Workflow;

public enum WorkflowStepInputReferenceType
{
    WorkflowRunFact = 1,
    WorkflowStepFact = 2,
    WorkflowCheckpointFact = 3,
    EvidenceReference = 4,
    GroundingReference = 5,
    ThoughtLedgerReference = 6,
    HumanNote = 7,
    ValidationFinding = 8,
    ReviewFinding = 9,
    DebugFinding = 10,
    PolicyEvaluationInput = 11,
    ApprovalRequirementInput = 12,
    HandoffContext = 13,
    SourceFileRangeReference = 14,
    MemoryCandidateReference = 15
}

public enum WorkflowStepOutputReferenceType
{
    WorkflowStepFact = 1,
    WorkflowCheckpointFact = 2,
    EvidenceReference = 3,
    GroundingReference = 4,
    ThoughtLedgerReference = 5,
    ValidationFinding = 6,
    ReviewFinding = 7,
    DebugFinding = 8,
    PolicyEvaluationFinding = 9,
    ApprovalRequirementFinding = 10,
    HumanDecisionSupportOutput = 11,
    HandoffSummary = 12,
    MemoryCandidateOutput = 13,
    SourceApplyCandidateOutput = 14,
    Receipt = 15
}

public enum WorkflowStepReferenceStatus
{
    Recorded = 1,
    Captured = 2,
    ReadyForReview = 3,
    Superseded = 4,
    Cancelled = 5,
    Rejected = 6
}

public static class WorkflowStepReferenceAllowedUses
{
    public const string Context = "Context";
    public const string Review = "Review";
    public const string Debugging = "Debugging";
    public const string Validation = "Validation";
    public const string Traceability = "Traceability";
    public const string HumanDecisionSupport = "HumanDecisionSupport";
    public const string AuditReference = "AuditReference";
    public const string PolicyInput = "PolicyInput";
    public const string RequirementEvaluation = "RequirementEvaluation";
    public const string ClaimSupport = "ClaimSupport";
    public const string CheckpointExplanation = "CheckpointExplanation";
    public const string StepExplanation = "StepExplanation";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Context,
        Review,
        Debugging,
        Validation,
        Traceability,
        HumanDecisionSupport,
        AuditReference,
        PolicyInput,
        RequirementEvaluation,
        ClaimSupport,
        CheckpointExplanation,
        StepExplanation
    };
}

public sealed record WorkflowStepInputReference
{
    public required Guid WorkflowStepInputReferenceId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public required Guid WorkflowRunStepId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string InputKey { get; init; }
    public required WorkflowStepInputReferenceType InputType { get; init; }
    public required WorkflowStepReferenceStatus Status { get; init; }
    public string? SourceType { get; init; }
    public string? SourceId { get; init; }
    public string? SafeSummary { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public Guid? WorkflowRunEvidenceReferenceId { get; init; }
    public Guid? WorkflowRunGroundingReferenceId { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public required IReadOnlyList<string> AllowedUses { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required string CreatedByActorType { get; init; }
    public required string CreatedByActorId { get; init; }
    public required int MetadataVersion { get; init; }
    public required string MetadataJson { get; init; }
    public required bool GrantsApproval { get; init; }
    public required bool GrantsExecution { get; init; }
    public required bool MutatesSource { get; init; }
    public required bool PromotesMemory { get; init; }
    public required bool StartsWorkflow { get; init; }
    public required bool ContinuesWorkflow { get; init; }
    public required bool ResumesWorkflow { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool TransfersAuthority { get; init; }
    public required bool ApprovesRelease { get; init; }
    public required bool CreatesAcceptedMemory { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowStepInputReferenceCreateRequest
{
    public Guid? WorkflowStepInputReferenceId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public required Guid WorkflowRunStepId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string InputKey { get; init; }
    public required WorkflowStepInputReferenceType InputType { get; init; }
    public required WorkflowStepReferenceStatus Status { get; init; }
    public string? SourceType { get; init; }
    public string? SourceId { get; init; }
    public string? SafeSummary { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public Guid? WorkflowRunEvidenceReferenceId { get; init; }
    public Guid? WorkflowRunGroundingReferenceId { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public IReadOnlyList<string> AllowedUses { get; init; } = [];
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required string CreatedByActorType { get; init; }
    public required string CreatedByActorId { get; init; }
    public required int MetadataVersion { get; init; }
    public required string MetadataJson { get; init; }
    public bool GrantsApproval { get; init; }
    public bool GrantsExecution { get; init; }
    public bool MutatesSource { get; init; }
    public bool PromotesMemory { get; init; }
    public bool StartsWorkflow { get; init; }
    public bool ContinuesWorkflow { get; init; }
    public bool ResumesWorkflow { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool TransfersAuthority { get; init; }
    public bool ApprovesRelease { get; init; }
    public bool CreatesAcceptedMemory { get; init; }
}

public sealed record WorkflowStepInputReferenceSummary
{
    public required Guid WorkflowStepInputReferenceId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public required Guid WorkflowRunStepId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string InputKey { get; init; }
    public required WorkflowStepInputReferenceType InputType { get; init; }
    public required WorkflowStepReferenceStatus Status { get; init; }
    public string? SourceType { get; init; }
    public string? SourceId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public Guid? WorkflowRunEvidenceReferenceId { get; init; }
    public Guid? WorkflowRunGroundingReferenceId { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowStepOutputReference
{
    public required Guid WorkflowStepOutputReferenceId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public required Guid WorkflowRunStepId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string OutputKey { get; init; }
    public required WorkflowStepOutputReferenceType OutputType { get; init; }
    public required WorkflowStepReferenceStatus Status { get; init; }
    public string? TargetType { get; init; }
    public string? TargetId { get; init; }
    public string? SafeSummary { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public Guid? WorkflowRunEvidenceReferenceId { get; init; }
    public Guid? WorkflowRunGroundingReferenceId { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public required IReadOnlyList<string> AllowedUses { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required string CreatedByActorType { get; init; }
    public required string CreatedByActorId { get; init; }
    public required int MetadataVersion { get; init; }
    public required string MetadataJson { get; init; }
    public required bool GrantsApproval { get; init; }
    public required bool GrantsExecution { get; init; }
    public required bool MutatesSource { get; init; }
    public required bool PromotesMemory { get; init; }
    public required bool StartsWorkflow { get; init; }
    public required bool ContinuesWorkflow { get; init; }
    public required bool ResumesWorkflow { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool TransfersAuthority { get; init; }
    public required bool ApprovesRelease { get; init; }
    public required bool CreatesAcceptedMemory { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowStepOutputReferenceCreateRequest
{
    public Guid? WorkflowStepOutputReferenceId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public required Guid WorkflowRunStepId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string OutputKey { get; init; }
    public required WorkflowStepOutputReferenceType OutputType { get; init; }
    public required WorkflowStepReferenceStatus Status { get; init; }
    public string? TargetType { get; init; }
    public string? TargetId { get; init; }
    public string? SafeSummary { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public Guid? WorkflowRunEvidenceReferenceId { get; init; }
    public Guid? WorkflowRunGroundingReferenceId { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public IReadOnlyList<string> AllowedUses { get; init; } = [];
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required string CreatedByActorType { get; init; }
    public required string CreatedByActorId { get; init; }
    public required int MetadataVersion { get; init; }
    public required string MetadataJson { get; init; }
    public bool GrantsApproval { get; init; }
    public bool GrantsExecution { get; init; }
    public bool MutatesSource { get; init; }
    public bool PromotesMemory { get; init; }
    public bool StartsWorkflow { get; init; }
    public bool ContinuesWorkflow { get; init; }
    public bool ResumesWorkflow { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool TransfersAuthority { get; init; }
    public bool ApprovesRelease { get; init; }
    public bool CreatesAcceptedMemory { get; init; }
}

public sealed record WorkflowStepOutputReferenceSummary
{
    public required Guid WorkflowStepOutputReferenceId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public required Guid WorkflowRunStepId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string OutputKey { get; init; }
    public required WorkflowStepOutputReferenceType OutputType { get; init; }
    public required WorkflowStepReferenceStatus Status { get; init; }
    public string? TargetType { get; init; }
    public string? TargetId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public Guid? WorkflowRunEvidenceReferenceId { get; init; }
    public Guid? WorkflowRunGroundingReferenceId { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed class WorkflowStepInputOutputReferenceValidator
{
    private const int MaxJsonLength = 64_000;

    private static readonly string[] PrivateReasoningMarkers =
    [
        "hiddenReasoning",
        "hidden reasoning",
        "chainOfThought",
        "chain of thought",
        "chain-of-thought",
        "private reasoning",
        "scratchpad",
        "rawPrompt",
        "raw prompt",
        "rawCompletion",
        "raw completion",
        "rawToolOutput",
        "raw tool output",
        "entirePatch",
        "entire patch"
    ];

    private static readonly string[] AuthorityAndRuntimeMarkers =
    [
        "approval granted",
        "approved for execution",
        "execution permission",
        "execution allowed",
        "can execute",
        "authorize execution",
        "policy satisfied",
        "satisfy policy",
        "source applied",
        "apply source",
        "apply patch",
        "memory promoted",
        "promote memory",
        "accepted memory",
        "release approved",
        "approve release",
        "ready to ship",
        "can ship",
        "authority transferred",
        "transfer authority",
        "workflow continued",
        "continue workflow",
        "workflow started",
        "start workflow",
        "resume workflow",
        "workflow resumed",
        "resume allowed",
        "restorable",
        "restore workflow",
        Phrase("dispatch", " agent"),
        Phrase("agent", " dispatched"),
        "tool executed",
        "tool ran",
        "model output",
        "raw model output",
        Phrase("runtime", " frame"),
        Phrase("Lang", "Graph state")
    ];

    public WorkflowRunValidationResult ValidateInput(WorkflowStepInputReferenceCreateRequest? request)
    {
        var issues = new List<WorkflowRunValidationIssue>();
        if (request is null)
        {
            AddError(issues, "WORKFLOW_STEP_INPUT_REFERENCE_REQUIRED", "Workflow step input reference is required.", nameof(WorkflowStepInputReferenceCreateRequest));
            return Result(issues);
        }

        ValidateCommon(
            request.WorkflowRunId,
            request.WorkflowRunStepId,
            request.ProjectId,
            request.InputKey,
            request.Status,
            request.SourceType,
            request.SourceId,
            request.SafeSummary,
            request.AllowedUses,
            request.CreatedByActorType,
            request.CreatedByActorId,
            request.MetadataVersion,
            request.MetadataJson,
            request.GrantsApproval,
            request.GrantsExecution,
            request.MutatesSource,
            request.PromotesMemory,
            request.StartsWorkflow,
            request.ContinuesWorkflow,
            request.ResumesWorkflow,
            request.SatisfiesPolicy,
            request.TransfersAuthority,
            request.ApprovesRelease,
            request.CreatesAcceptedMemory,
            nameof(WorkflowStepInputReferenceCreateRequest.InputKey),
            "INPUT",
            issues);

        if (request.WorkflowStepInputReferenceId == Guid.Empty)
            AddError(issues, "WORKFLOW_STEP_INPUT_REFERENCE_ID_INVALID", "WorkflowStepInputReferenceId cannot be empty when supplied.", nameof(WorkflowStepInputReferenceCreateRequest.WorkflowStepInputReferenceId));
        if (!Enum.IsDefined(request.InputType))
            AddError(issues, "WORKFLOW_STEP_INPUT_REFERENCE_TYPE_INVALID", "InputType is invalid.", nameof(WorkflowStepInputReferenceCreateRequest.InputType));

        return Result(issues);
    }

    public WorkflowRunValidationResult ValidateOutput(WorkflowStepOutputReferenceCreateRequest? request)
    {
        var issues = new List<WorkflowRunValidationIssue>();
        if (request is null)
        {
            AddError(issues, "WORKFLOW_STEP_OUTPUT_REFERENCE_REQUIRED", "Workflow step output reference is required.", nameof(WorkflowStepOutputReferenceCreateRequest));
            return Result(issues);
        }

        ValidateCommon(
            request.WorkflowRunId,
            request.WorkflowRunStepId,
            request.ProjectId,
            request.OutputKey,
            request.Status,
            request.TargetType,
            request.TargetId,
            request.SafeSummary,
            request.AllowedUses,
            request.CreatedByActorType,
            request.CreatedByActorId,
            request.MetadataVersion,
            request.MetadataJson,
            request.GrantsApproval,
            request.GrantsExecution,
            request.MutatesSource,
            request.PromotesMemory,
            request.StartsWorkflow,
            request.ContinuesWorkflow,
            request.ResumesWorkflow,
            request.SatisfiesPolicy,
            request.TransfersAuthority,
            request.ApprovesRelease,
            request.CreatesAcceptedMemory,
            nameof(WorkflowStepOutputReferenceCreateRequest.OutputKey),
            "OUTPUT",
            issues);

        if (request.WorkflowStepOutputReferenceId == Guid.Empty)
            AddError(issues, "WORKFLOW_STEP_OUTPUT_REFERENCE_ID_INVALID", "WorkflowStepOutputReferenceId cannot be empty when supplied.", nameof(WorkflowStepOutputReferenceCreateRequest.WorkflowStepOutputReferenceId));
        if (!Enum.IsDefined(request.OutputType))
            AddError(issues, "WORKFLOW_STEP_OUTPUT_REFERENCE_TYPE_INVALID", "OutputType is invalid.", nameof(WorkflowStepOutputReferenceCreateRequest.OutputType));

        return Result(issues);
    }

    public WorkflowStepInputReferenceCreateRequest NormalizeInput(WorkflowStepInputReferenceCreateRequest request) =>
        request with
        {
            InputKey = request.InputKey.Trim(),
            SourceType = NormalizeOptional(request.SourceType),
            SourceId = NormalizeOptional(request.SourceId),
            SafeSummary = NormalizeOptional(request.SafeSummary),
            AllowedUses = request.AllowedUses.Select(value => value.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray(),
            CreatedByActorType = request.CreatedByActorType.Trim(),
            CreatedByActorId = request.CreatedByActorId.Trim()
        };

    public WorkflowStepOutputReferenceCreateRequest NormalizeOutput(WorkflowStepOutputReferenceCreateRequest request) =>
        request with
        {
            OutputKey = request.OutputKey.Trim(),
            TargetType = NormalizeOptional(request.TargetType),
            TargetId = NormalizeOptional(request.TargetId),
            SafeSummary = NormalizeOptional(request.SafeSummary),
            AllowedUses = request.AllowedUses.Select(value => value.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray(),
            CreatedByActorType = request.CreatedByActorType.Trim(),
            CreatedByActorId = request.CreatedByActorId.Trim()
        };

    private static void ValidateCommon(
        Guid workflowRunId,
        Guid workflowRunStepId,
        Guid projectId,
        string key,
        WorkflowStepReferenceStatus status,
        string? referenceType,
        string? referenceId,
        string? safeSummary,
        IReadOnlyList<string> allowedUses,
        string createdByActorType,
        string createdByActorId,
        int metadataVersion,
        string metadataJson,
        bool grantsApproval,
        bool grantsExecution,
        bool mutatesSource,
        bool promotesMemory,
        bool startsWorkflow,
        bool continuesWorkflow,
        bool resumesWorkflow,
        bool satisfiesPolicy,
        bool transfersAuthority,
        bool approvesRelease,
        bool createsAcceptedMemory,
        string keyField,
        string codePrefix,
        List<WorkflowRunValidationIssue> issues)
    {
        if (workflowRunId == Guid.Empty)
            AddError(issues, $"WORKFLOW_STEP_{codePrefix}_REFERENCE_RUN_ID_REQUIRED", "WorkflowRunId is required.", "WorkflowRunId");
        if (workflowRunStepId == Guid.Empty)
            AddError(issues, $"WORKFLOW_STEP_{codePrefix}_REFERENCE_STEP_ID_REQUIRED", "WorkflowRunStepId is required.", "WorkflowRunStepId");
        if (projectId == Guid.Empty)
            AddError(issues, $"WORKFLOW_STEP_{codePrefix}_REFERENCE_PROJECT_ID_REQUIRED", "ProjectId is required.", "ProjectId");
        if (!Enum.IsDefined(status))
            AddError(issues, $"WORKFLOW_STEP_{codePrefix}_REFERENCE_STATUS_INVALID", "Reference status is invalid.", "Status");
        if (metadataVersion <= 0)
            AddError(issues, $"WORKFLOW_STEP_{codePrefix}_REFERENCE_METADATA_VERSION_INVALID", "MetadataVersion must be positive.", "MetadataVersion");

        Require(key, $"WORKFLOW_STEP_{codePrefix}_REFERENCE_KEY_REQUIRED", keyField, issues);
        Require(createdByActorType, $"WORKFLOW_STEP_{codePrefix}_REFERENCE_ACTOR_TYPE_REQUIRED", "CreatedByActorType", issues);
        Require(createdByActorId, $"WORKFLOW_STEP_{codePrefix}_REFERENCE_ACTOR_ID_REQUIRED", "CreatedByActorId", issues);

        ValidateAllowedUses(allowedUses, codePrefix, issues);
        ValidateJson(metadataJson, $"WORKFLOW_STEP_{codePrefix}_REFERENCE_METADATA_JSON", "MetadataJson", issues);
        ValidateTextSafety(key, keyField, issues);
        ValidateTextSafety(referenceType, "ReferenceType", issues);
        ValidateTextSafety(referenceId, "ReferenceId", issues);
        ValidateTextSafety(safeSummary, "SafeSummary", issues);
        ValidateTextSafety(createdByActorType, "CreatedByActorType", issues);
        ValidateTextSafety(createdByActorId, "CreatedByActorId", issues);

        RejectAuthorityFlag(grantsApproval, "GrantsApproval", issues);
        RejectAuthorityFlag(grantsExecution, "GrantsExecution", issues);
        RejectAuthorityFlag(mutatesSource, "MutatesSource", issues);
        RejectAuthorityFlag(promotesMemory, "PromotesMemory", issues);
        RejectAuthorityFlag(startsWorkflow, "StartsWorkflow", issues);
        RejectAuthorityFlag(continuesWorkflow, "ContinuesWorkflow", issues);
        RejectAuthorityFlag(resumesWorkflow, "ResumesWorkflow", issues);
        RejectAuthorityFlag(satisfiesPolicy, "SatisfiesPolicy", issues);
        RejectAuthorityFlag(transfersAuthority, "TransfersAuthority", issues);
        RejectAuthorityFlag(approvesRelease, "ApprovesRelease", issues);
        RejectAuthorityFlag(createsAcceptedMemory, "CreatesAcceptedMemory", issues);
    }

    private static void ValidateAllowedUses(IReadOnlyList<string>? allowedUses, string codePrefix, List<WorkflowRunValidationIssue> issues)
    {
        if (allowedUses is null || allowedUses.Count == 0)
        {
            AddError(issues, $"WORKFLOW_STEP_{codePrefix}_REFERENCE_ALLOWED_USE_REQUIRED", "At least one allowed use is required.", "AllowedUses");
            return;
        }

        foreach (var allowedUse in allowedUses)
        {
            if (string.IsNullOrWhiteSpace(allowedUse))
            {
                AddError(issues, $"WORKFLOW_STEP_{codePrefix}_REFERENCE_ALLOWED_USE_BLANK", "Allowed use cannot be blank.", "AllowedUses");
                continue;
            }

            ValidateTextSafety(allowedUse, "AllowedUses", issues);
            if (!WorkflowStepReferenceAllowedUses.All.Contains(allowedUse.Trim()))
                AddError(issues, $"WORKFLOW_STEP_{codePrefix}_REFERENCE_ALLOWED_USE_INVALID", "Allowed use is not in the bounded vocabulary.", "AllowedUses");
        }
    }

    private static void ValidateJson(string? value, string codePrefix, string field, List<WorkflowRunValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(issues, codePrefix + "_REQUIRED", "JSON content is required.", field);
            return;
        }

        if (value.Length > MaxJsonLength)
            AddError(issues, codePrefix + "_TOO_LARGE", "JSON content is too large.", field);

        ValidateTextSafety(value, field, issues);

        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                AddError(issues, codePrefix + "_OBJECT_REQUIRED", "JSON content must be an object.", field);
            ValidateJsonElement(document.RootElement, field, issues);
        }
        catch (JsonException)
        {
            AddError(issues, codePrefix + "_INVALID", "JSON content must be valid JSON.", field);
        }
    }

    private static void ValidateJsonElement(JsonElement element, string field, List<WorkflowRunValidationIssue> issues)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    ValidateTextSafety(property.Name, field, issues);
                    if (IsAuthorityOrRuntimeProperty(property.Name) && IsTruthy(property.Value))
                        AddError(issues, "WORKFLOW_STEP_REFERENCE_AUTHORITY_METADATA_BLOCKED", $"Metadata property cannot grant authority, action, runtime, or continuation semantics: {property.Name}.", field);
                    ValidateJsonElement(property.Value, field, issues);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    ValidateJsonElement(item, field, issues);
                break;
            case JsonValueKind.String:
                ValidateTextSafety(element.GetString(), field, issues);
                break;
        }
    }

    private static bool IsAuthorityOrRuntimeProperty(string propertyName) =>
        propertyName.Contains("grants", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("execution", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("execute", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("mutatesSource", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("sourceApply", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("promotesMemory", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("memoryPromotion", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("acceptedMemory", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("startsWorkflow", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("workflowStarted", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("continuesWorkflow", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("workflowContinued", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("resume", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("workflowResumed", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("resumable", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("restore", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("restorable", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("dispatch", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("satisfiesPolicy", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("policySatisfied", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("transfersAuthority", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("approvesRelease", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("releaseApproved", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("runtime", StringComparison.OrdinalIgnoreCase);

    private static bool IsTruthy(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => false,
            JsonValueKind.String => !string.Equals(value.GetString(), "false", StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(value.GetString(), "no", StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(value.GetString(), "0", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number => value.TryGetInt32(out var number) && number != 0,
            _ => true
        };

    private static void Require(string? value, string code, string field, List<WorkflowRunValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            AddError(issues, code, "Required text is blank.", field);
    }

    private static void ValidateTextSafety(string? value, string field, List<WorkflowRunValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (ContainsAny(value, PrivateReasoningMarkers))
            AddError(issues, "WORKFLOW_STEP_REFERENCE_PRIVATE_REASONING_BLOCKED", "Workflow step reference text must not contain hidden/private reasoning or raw dump markers.", field);

        if (ContainsAny(value, AuthorityAndRuntimeMarkers))
            AddError(issues, "WORKFLOW_STEP_REFERENCE_AUTHORITY_OR_RUNTIME_LANGUAGE_BLOCKED", "Workflow step reference text must not claim approval, execution, policy satisfaction, source apply, memory promotion, release approval, workflow continuation/resume, dispatch, runtime, or authority transfer.", field);
    }

    private static void RejectAuthorityFlag(bool value, string field, List<WorkflowRunValidationIssue> issues)
    {
        if (value)
            AddError(issues, "WORKFLOW_STEP_REFERENCE_AUTHORITY_FLAG_BLOCKED", "Workflow step reference authority/action flags must be false.", field);
    }

    private static bool ContainsAny(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string Phrase(string left, string right) => left + right;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static WorkflowRunValidationResult Result(List<WorkflowRunValidationIssue> issues) =>
        new()
        {
            IsValid = issues.All(issue => !string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)),
            Issues = issues
        };

    private static void AddError(List<WorkflowRunValidationIssue> issues, string code, string message, string field) =>
        issues.Add(new WorkflowRunValidationIssue
        {
            Code = code,
            Severity = "error",
            Message = message,
            Field = field
        });
}
