using System.Text.Json;

namespace IronDev.Core.Workflow;

public enum WorkflowFailureType
{
    ValidationFailure = 1,
    PolicyBoundaryFailure = 2,
    MissingEvidence = 3,
    InvalidEvidence = 4,
    ToolGateBlocked = 5,
    ApprovalRequired = 6,
    HumanReviewRequired = 7,
    TimeoutObserved = 8,
    ExternalDependencyUnavailable = 9,
    DataShapeMismatch = 10,
    InvariantViolation = 11,
    ConfigurationMissing = 12,
    StorageFailure = 13,
    BuildFailure = 14,
    TestFailure = 15,
    ReviewFinding = 16,
    UserCancelled = 17,
    UnknownSafeFailure = 18
}

public enum WorkflowFailureSeverity
{
    Info = 1,
    Warning = 2,
    Blocked = 3,
    Failed = 4,
    Critical = 5
}

public enum WorkflowFailureStatus
{
    Recorded = 1,
    ReadyForReview = 2,
    Blocked = 3,
    Superseded = 4,
    Cancelled = 5,
    Rejected = 6
}

public enum WorkflowRetryStatus
{
    Recorded = 1,
    ReadyForReview = 2,
    Blocked = 3,
    Superseded = 4,
    Cancelled = 5,
    Rejected = 6
}

public enum WorkflowRetryDisposition
{
    NoRetryRecommended = 1,
    RetryMayBeReviewed = 2,
    RetryRequiresHumanReview = 3,
    RetryRequiresPolicyEvaluation = 4,
    RetryRequiresMoreEvidence = 5,
    RetryBlockedByPolicy = 6,
    RetryBlockedByMissingApproval = 7,
    RetryBlockedByMissingEvidence = 8,
    ManualInvestigationRequired = 9
}

public enum WorkflowRetryRecommendation
{
    DoNotRetry = 1,
    ReviewBeforeRetry = 2,
    CollectMoreEvidence = 3,
    RequestHumanDecision = 4,
    EvaluatePolicy = 5,
    CreateFollowUpTicket = 6,
    MarkBlockedForReview = 7
}

public static class WorkflowFailureRetryAllowedUses
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
    public const string FailureExplanation = "FailureExplanation";
    public const string RetryExplanation = "RetryExplanation";

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
        FailureExplanation,
        RetryExplanation
    };
}

public sealed record WorkflowFailureState
{
    public required Guid WorkflowFailureStateId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string FailureKey { get; init; }
    public required WorkflowFailureType FailureType { get; init; }
    public required WorkflowFailureSeverity Severity { get; init; }
    public required WorkflowFailureStatus Status { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string? SafeSummary { get; init; }
    public required IReadOnlyList<WorkflowFailureEvidenceReference> EvidenceReferences { get; init; }
    public required IReadOnlyList<WorkflowFailureGroundingReference> GroundingReferences { get; init; }
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
    public required bool RetriesWorkflow { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool TransfersAuthority { get; init; }
    public required bool ApprovesRelease { get; init; }
    public required bool CreatesAcceptedMemory { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowFailureStateCreateRequest
{
    public Guid? WorkflowFailureStateId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string FailureKey { get; init; }
    public required WorkflowFailureType FailureType { get; init; }
    public required WorkflowFailureSeverity Severity { get; init; }
    public required WorkflowFailureStatus Status { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string? SafeSummary { get; init; }
    public IReadOnlyList<WorkflowFailureEvidenceReference> EvidenceReferences { get; init; } = [];
    public IReadOnlyList<WorkflowFailureGroundingReference> GroundingReferences { get; init; } = [];
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
    public bool RetriesWorkflow { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool TransfersAuthority { get; init; }
    public bool ApprovesRelease { get; init; }
    public bool CreatesAcceptedMemory { get; init; }
}

public sealed record WorkflowFailureStateSummary
{
    public required Guid WorkflowFailureStateId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string FailureKey { get; init; }
    public required WorkflowFailureType FailureType { get; init; }
    public required WorkflowFailureSeverity Severity { get; init; }
    public required WorkflowFailureStatus Status { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public required int EvidenceReferenceCount { get; init; }
    public required int GroundingReferenceCount { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowRetryState
{
    public required Guid WorkflowRetryStateId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public Guid? WorkflowFailureStateId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string RetryKey { get; init; }
    public required WorkflowRetryStatus Status { get; init; }
    public required WorkflowRetryDisposition Disposition { get; init; }
    public required WorkflowRetryRecommendation Recommendation { get; init; }
    public required int AttemptNumber { get; init; }
    public int? MaxAttempts { get; init; }
    public DateTimeOffset? EarliestRetryUtc { get; init; }
    public DateTimeOffset? RetryAfterUtc { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string? SafeSummary { get; init; }
    public required IReadOnlyList<WorkflowFailureEvidenceReference> EvidenceReferences { get; init; }
    public required IReadOnlyList<WorkflowFailureGroundingReference> GroundingReferences { get; init; }
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
    public required bool RetriesWorkflow { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool TransfersAuthority { get; init; }
    public required bool ApprovesRelease { get; init; }
    public required bool CreatesAcceptedMemory { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowRetryStateCreateRequest
{
    public Guid? WorkflowRetryStateId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public Guid? WorkflowFailureStateId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string RetryKey { get; init; }
    public required WorkflowRetryStatus Status { get; init; }
    public required WorkflowRetryDisposition Disposition { get; init; }
    public required WorkflowRetryRecommendation Recommendation { get; init; }
    public required int AttemptNumber { get; init; }
    public int? MaxAttempts { get; init; }
    public DateTimeOffset? EarliestRetryUtc { get; init; }
    public DateTimeOffset? RetryAfterUtc { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string? SafeSummary { get; init; }
    public IReadOnlyList<WorkflowFailureEvidenceReference> EvidenceReferences { get; init; } = [];
    public IReadOnlyList<WorkflowFailureGroundingReference> GroundingReferences { get; init; } = [];
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
    public bool RetriesWorkflow { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool TransfersAuthority { get; init; }
    public bool ApprovesRelease { get; init; }
    public bool CreatesAcceptedMemory { get; init; }
}

public sealed record WorkflowRetryStateSummary
{
    public required Guid WorkflowRetryStateId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public Guid? WorkflowFailureStateId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string RetryKey { get; init; }
    public required WorkflowRetryStatus Status { get; init; }
    public required WorkflowRetryDisposition Disposition { get; init; }
    public required WorkflowRetryRecommendation Recommendation { get; init; }
    public required int AttemptNumber { get; init; }
    public int? MaxAttempts { get; init; }
    public required int EvidenceReferenceCount { get; init; }
    public required int GroundingReferenceCount { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowFailureEvidenceReference
{
    public required string EvidenceType { get; init; }
    public required string EvidenceId { get; init; }
    public string? EvidenceLabel { get; init; }
    public string? SafeSummary { get; init; }
    public string? AllowedUse { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public Guid? WorkflowRunEvidenceReferenceId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
}

public sealed record WorkflowFailureGroundingReference
{
    public required Guid GroundingEvidenceReferenceId { get; init; }
    public required string ClaimType { get; init; }
    public required string ClaimId { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed class WorkflowFailureRetryStateValidator
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

    private static readonly string[] AuthorityRetryAndRuntimeMarkers =
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
        "retry now",
        "auto retry",
        "retry allowed",
        "retry queued",
        "retry scheduled",
        "retry dispatched",
        "retry executed",
        Phrase("dispatch", " agent"),
        Phrase("agent", " dispatched"),
        "tool executed",
        "tool ran",
        "model output",
        "raw model output",
        Phrase("runtime", " frame"),
        Phrase("Lang", "Graph state")
    ];

    public WorkflowRunValidationResult ValidateFailure(WorkflowFailureStateCreateRequest? request)
    {
        var issues = new List<WorkflowRunValidationIssue>();
        if (request is null)
        {
            AddError(issues, "WORKFLOW_FAILURE_STATE_REQUIRED", "Workflow failure state is required.", nameof(WorkflowFailureStateCreateRequest));
            return Result(issues);
        }

        ValidateCommon(
            request.WorkflowRunId,
            request.ProjectId,
            request.FailureKey,
            request.SubjectType,
            request.SubjectId,
            request.SafeSummary,
            request.EvidenceReferences,
            request.GroundingReferences,
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
            request.RetriesWorkflow,
            request.SatisfiesPolicy,
            request.TransfersAuthority,
            request.ApprovesRelease,
            request.CreatesAcceptedMemory,
            nameof(WorkflowFailureStateCreateRequest.FailureKey),
            "FAILURE",
            issues);

        if (request.WorkflowFailureStateId == Guid.Empty)
            AddError(issues, "WORKFLOW_FAILURE_STATE_ID_INVALID", "WorkflowFailureStateId cannot be empty when supplied.", nameof(WorkflowFailureStateCreateRequest.WorkflowFailureStateId));
        if (request.WorkflowRunStepId == Guid.Empty)
            AddError(issues, "WORKFLOW_FAILURE_STATE_STEP_ID_INVALID", "WorkflowRunStepId cannot be empty when supplied.", nameof(WorkflowFailureStateCreateRequest.WorkflowRunStepId));
        if (request.WorkflowCheckpointId == Guid.Empty)
            AddError(issues, "WORKFLOW_FAILURE_STATE_CHECKPOINT_ID_INVALID", "WorkflowCheckpointId cannot be empty when supplied.", nameof(WorkflowFailureStateCreateRequest.WorkflowCheckpointId));
        if (!Enum.IsDefined(request.FailureType))
            AddError(issues, "WORKFLOW_FAILURE_TYPE_INVALID", "FailureType is invalid.", nameof(WorkflowFailureStateCreateRequest.FailureType));
        if (!Enum.IsDefined(request.Severity))
            AddError(issues, "WORKFLOW_FAILURE_SEVERITY_INVALID", "Severity is invalid.", nameof(WorkflowFailureStateCreateRequest.Severity));
        if (!Enum.IsDefined(request.Status))
            AddError(issues, "WORKFLOW_FAILURE_STATUS_INVALID", "Failure status is invalid.", nameof(WorkflowFailureStateCreateRequest.Status));

        return Result(issues);
    }

    public WorkflowRunValidationResult ValidateRetry(WorkflowRetryStateCreateRequest? request)
    {
        var issues = new List<WorkflowRunValidationIssue>();
        if (request is null)
        {
            AddError(issues, "WORKFLOW_RETRY_STATE_REQUIRED", "Workflow retry state is required.", nameof(WorkflowRetryStateCreateRequest));
            return Result(issues);
        }

        ValidateCommon(
            request.WorkflowRunId,
            request.ProjectId,
            request.RetryKey,
            request.SubjectType,
            request.SubjectId,
            request.SafeSummary,
            request.EvidenceReferences,
            request.GroundingReferences,
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
            request.RetriesWorkflow,
            request.SatisfiesPolicy,
            request.TransfersAuthority,
            request.ApprovesRelease,
            request.CreatesAcceptedMemory,
            nameof(WorkflowRetryStateCreateRequest.RetryKey),
            "RETRY",
            issues);

        if (request.WorkflowRetryStateId == Guid.Empty)
            AddError(issues, "WORKFLOW_RETRY_STATE_ID_INVALID", "WorkflowRetryStateId cannot be empty when supplied.", nameof(WorkflowRetryStateCreateRequest.WorkflowRetryStateId));
        if (request.WorkflowRunStepId == Guid.Empty)
            AddError(issues, "WORKFLOW_RETRY_STATE_STEP_ID_INVALID", "WorkflowRunStepId cannot be empty when supplied.", nameof(WorkflowRetryStateCreateRequest.WorkflowRunStepId));
        if (request.WorkflowCheckpointId == Guid.Empty)
            AddError(issues, "WORKFLOW_RETRY_STATE_CHECKPOINT_ID_INVALID", "WorkflowCheckpointId cannot be empty when supplied.", nameof(WorkflowRetryStateCreateRequest.WorkflowCheckpointId));
        if (request.WorkflowFailureStateId == Guid.Empty)
            AddError(issues, "WORKFLOW_RETRY_STATE_FAILURE_ID_INVALID", "WorkflowFailureStateId cannot be empty when supplied.", nameof(WorkflowRetryStateCreateRequest.WorkflowFailureStateId));
        if (!Enum.IsDefined(request.Status))
            AddError(issues, "WORKFLOW_RETRY_STATUS_INVALID", "Retry status is invalid.", nameof(WorkflowRetryStateCreateRequest.Status));
        if (!Enum.IsDefined(request.Disposition))
            AddError(issues, "WORKFLOW_RETRY_DISPOSITION_INVALID", "Retry disposition is invalid.", nameof(WorkflowRetryStateCreateRequest.Disposition));
        if (!Enum.IsDefined(request.Recommendation))
            AddError(issues, "WORKFLOW_RETRY_RECOMMENDATION_INVALID", "Retry recommendation is invalid.", nameof(WorkflowRetryStateCreateRequest.Recommendation));
        if (request.AttemptNumber < 0)
            AddError(issues, "WORKFLOW_RETRY_ATTEMPT_INVALID", "AttemptNumber cannot be negative.", nameof(WorkflowRetryStateCreateRequest.AttemptNumber));
        if (request.MaxAttempts.HasValue && request.MaxAttempts.Value < request.AttemptNumber)
            AddError(issues, "WORKFLOW_RETRY_MAX_ATTEMPTS_INVALID", "MaxAttempts cannot be lower than AttemptNumber.", nameof(WorkflowRetryStateCreateRequest.MaxAttempts));
        if (request.EarliestRetryUtc.HasValue && request.RetryAfterUtc.HasValue && request.RetryAfterUtc.Value < request.EarliestRetryUtc.Value)
            AddError(issues, "WORKFLOW_RETRY_TIMESTAMPS_INVALID", "RetryAfterUtc cannot be earlier than EarliestRetryUtc.", nameof(WorkflowRetryStateCreateRequest.RetryAfterUtc));

        return Result(issues);
    }

    public WorkflowFailureStateCreateRequest NormalizeFailure(WorkflowFailureStateCreateRequest request) =>
        request with
        {
            FailureKey = request.FailureKey.Trim(),
            SubjectType = NormalizeOptional(request.SubjectType),
            SubjectId = NormalizeOptional(request.SubjectId),
            SafeSummary = NormalizeOptional(request.SafeSummary),
            EvidenceReferences = request.EvidenceReferences.Select(NormalizeEvidence).ToArray(),
            GroundingReferences = request.GroundingReferences.Select(NormalizeGrounding).ToArray(),
            CreatedByActorType = request.CreatedByActorType.Trim(),
            CreatedByActorId = request.CreatedByActorId.Trim()
        };

    public WorkflowRetryStateCreateRequest NormalizeRetry(WorkflowRetryStateCreateRequest request) =>
        request with
        {
            RetryKey = request.RetryKey.Trim(),
            SubjectType = NormalizeOptional(request.SubjectType),
            SubjectId = NormalizeOptional(request.SubjectId),
            SafeSummary = NormalizeOptional(request.SafeSummary),
            EvidenceReferences = request.EvidenceReferences.Select(NormalizeEvidence).ToArray(),
            GroundingReferences = request.GroundingReferences.Select(NormalizeGrounding).ToArray(),
            CreatedByActorType = request.CreatedByActorType.Trim(),
            CreatedByActorId = request.CreatedByActorId.Trim()
        };

    private static void ValidateCommon(
        Guid workflowRunId,
        Guid projectId,
        string key,
        string? subjectType,
        string? subjectId,
        string? safeSummary,
        IReadOnlyList<WorkflowFailureEvidenceReference> evidenceReferences,
        IReadOnlyList<WorkflowFailureGroundingReference> groundingReferences,
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
        bool retriesWorkflow,
        bool satisfiesPolicy,
        bool transfersAuthority,
        bool approvesRelease,
        bool createsAcceptedMemory,
        string keyField,
        string codePrefix,
        List<WorkflowRunValidationIssue> issues)
    {
        if (workflowRunId == Guid.Empty)
            AddError(issues, $"WORKFLOW_{codePrefix}_STATE_RUN_ID_REQUIRED", "WorkflowRunId is required.", "WorkflowRunId");
        if (projectId == Guid.Empty)
            AddError(issues, $"WORKFLOW_{codePrefix}_STATE_PROJECT_ID_REQUIRED", "ProjectId is required.", "ProjectId");
        if (metadataVersion <= 0)
            AddError(issues, $"WORKFLOW_{codePrefix}_STATE_METADATA_VERSION_INVALID", "MetadataVersion must be positive.", "MetadataVersion");

        Require(key, $"WORKFLOW_{codePrefix}_STATE_KEY_REQUIRED", keyField, issues);
        Require(createdByActorType, $"WORKFLOW_{codePrefix}_STATE_ACTOR_TYPE_REQUIRED", "CreatedByActorType", issues);
        Require(createdByActorId, $"WORKFLOW_{codePrefix}_STATE_ACTOR_ID_REQUIRED", "CreatedByActorId", issues);

        ValidateJson(metadataJson, $"WORKFLOW_{codePrefix}_STATE_METADATA_JSON", "MetadataJson", issues);
        ValidateTextSafety(key, keyField, issues);
        ValidateTextSafety(subjectType, nameof(subjectType), issues);
        ValidateTextSafety(subjectId, nameof(subjectId), issues);
        ValidateTextSafety(safeSummary, nameof(safeSummary), issues);
        ValidateTextSafety(createdByActorType, nameof(createdByActorType), issues);
        ValidateTextSafety(createdByActorId, nameof(createdByActorId), issues);
        ValidateEvidence(evidenceReferences, codePrefix, issues);
        ValidateGrounding(groundingReferences, codePrefix, issues);

        RejectAuthorityFlag(grantsApproval, "GrantsApproval", issues);
        RejectAuthorityFlag(grantsExecution, "GrantsExecution", issues);
        RejectAuthorityFlag(mutatesSource, "MutatesSource", issues);
        RejectAuthorityFlag(promotesMemory, "PromotesMemory", issues);
        RejectAuthorityFlag(startsWorkflow, "StartsWorkflow", issues);
        RejectAuthorityFlag(continuesWorkflow, "ContinuesWorkflow", issues);
        RejectAuthorityFlag(resumesWorkflow, "ResumesWorkflow", issues);
        RejectAuthorityFlag(retriesWorkflow, "RetriesWorkflow", issues);
        RejectAuthorityFlag(satisfiesPolicy, "SatisfiesPolicy", issues);
        RejectAuthorityFlag(transfersAuthority, "TransfersAuthority", issues);
        RejectAuthorityFlag(approvesRelease, "ApprovesRelease", issues);
        RejectAuthorityFlag(createsAcceptedMemory, "CreatesAcceptedMemory", issues);
    }

    private static void ValidateEvidence(IReadOnlyList<WorkflowFailureEvidenceReference>? evidenceReferences, string codePrefix, List<WorkflowRunValidationIssue> issues)
    {
        foreach (var evidence in evidenceReferences ?? [])
        {
            if (evidence is null)
            {
                AddError(issues, $"WORKFLOW_{codePrefix}_EVIDENCE_INVALID", "Evidence reference cannot be null.", "EvidenceReferences");
                continue;
            }

            Require(evidence.EvidenceType, $"WORKFLOW_{codePrefix}_EVIDENCE_TYPE_REQUIRED", nameof(WorkflowFailureEvidenceReference.EvidenceType), issues);
            Require(evidence.EvidenceId, $"WORKFLOW_{codePrefix}_EVIDENCE_ID_REQUIRED", nameof(WorkflowFailureEvidenceReference.EvidenceId), issues);
            ValidateTextSafety(evidence.EvidenceType, nameof(WorkflowFailureEvidenceReference.EvidenceType), issues);
            ValidateTextSafety(evidence.EvidenceId, nameof(WorkflowFailureEvidenceReference.EvidenceId), issues);
            ValidateTextSafety(evidence.EvidenceLabel, nameof(WorkflowFailureEvidenceReference.EvidenceLabel), issues);
            ValidateTextSafety(evidence.SafeSummary, nameof(WorkflowFailureEvidenceReference.SafeSummary), issues);
            ValidateTextSafety(evidence.AllowedUse, nameof(WorkflowFailureEvidenceReference.AllowedUse), issues);
            if (!string.IsNullOrWhiteSpace(evidence.AllowedUse) && !WorkflowFailureRetryAllowedUses.All.Contains(evidence.AllowedUse.Trim()))
                AddError(issues, $"WORKFLOW_{codePrefix}_EVIDENCE_ALLOWED_USE_INVALID", "Evidence allowed use is not in the bounded vocabulary.", nameof(WorkflowFailureEvidenceReference.AllowedUse));
        }
    }

    private static void ValidateGrounding(IReadOnlyList<WorkflowFailureGroundingReference>? groundingReferences, string codePrefix, List<WorkflowRunValidationIssue> issues)
    {
        foreach (var grounding in groundingReferences ?? [])
        {
            if (grounding is null)
            {
                AddError(issues, $"WORKFLOW_{codePrefix}_GROUNDING_INVALID", "Grounding reference cannot be null.", "GroundingReferences");
                continue;
            }

            if (grounding.GroundingEvidenceReferenceId == Guid.Empty)
                AddError(issues, $"WORKFLOW_{codePrefix}_GROUNDING_ID_REQUIRED", "GroundingEvidenceReferenceId is required.", nameof(WorkflowFailureGroundingReference.GroundingEvidenceReferenceId));
            Require(grounding.ClaimType, $"WORKFLOW_{codePrefix}_GROUNDING_CLAIM_TYPE_REQUIRED", nameof(WorkflowFailureGroundingReference.ClaimType), issues);
            Require(grounding.ClaimId, $"WORKFLOW_{codePrefix}_GROUNDING_CLAIM_ID_REQUIRED", nameof(WorkflowFailureGroundingReference.ClaimId), issues);
            ValidateTextSafety(grounding.ClaimType, nameof(WorkflowFailureGroundingReference.ClaimType), issues);
            ValidateTextSafety(grounding.ClaimId, nameof(WorkflowFailureGroundingReference.ClaimId), issues);
            ValidateTextSafety(grounding.SafeSummary, nameof(WorkflowFailureGroundingReference.SafeSummary), issues);
        }
    }

    private static WorkflowFailureEvidenceReference NormalizeEvidence(WorkflowFailureEvidenceReference evidence) =>
        evidence with
        {
            EvidenceType = evidence.EvidenceType.Trim(),
            EvidenceId = evidence.EvidenceId.Trim(),
            EvidenceLabel = NormalizeOptional(evidence.EvidenceLabel),
            SafeSummary = NormalizeOptional(evidence.SafeSummary),
            AllowedUse = NormalizeOptional(evidence.AllowedUse)
        };

    private static WorkflowFailureGroundingReference NormalizeGrounding(WorkflowFailureGroundingReference grounding) =>
        grounding with
        {
            ClaimType = grounding.ClaimType.Trim(),
            ClaimId = grounding.ClaimId.Trim(),
            SafeSummary = NormalizeOptional(grounding.SafeSummary)
        };

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
                    if (IsAuthorityRetryOrRuntimeProperty(property.Name) && IsTruthy(property.Value))
                        AddError(issues, "WORKFLOW_FAILURE_RETRY_AUTHORITY_METADATA_BLOCKED", $"Metadata property cannot grant authority, retry execution, action, runtime, or continuation semantics: {property.Name}.", field);
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

    private static bool IsAuthorityRetryOrRuntimeProperty(string propertyName) =>
        propertyName.Contains("grants", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("execution", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("execute", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("retryNow", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("autoRetry", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("retryAllowed", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("retryQueued", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("retryScheduled", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("retryDispatched", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("retryExecuted", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("retriesWorkflow", StringComparison.OrdinalIgnoreCase) ||
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
            AddError(issues, "WORKFLOW_FAILURE_RETRY_PRIVATE_REASONING_BLOCKED", "Workflow failure/retry text must not contain hidden/private reasoning or raw dump markers.", field);

        if (ContainsAny(value, AuthorityRetryAndRuntimeMarkers))
            AddError(issues, "WORKFLOW_FAILURE_RETRY_AUTHORITY_OR_RUNTIME_LANGUAGE_BLOCKED", "Workflow failure/retry text must not claim retry execution, approval, execution, policy satisfaction, source apply, memory promotion, release approval, workflow continuation/resume, dispatch, runtime, or authority transfer.", field);
    }

    private static void RejectAuthorityFlag(bool value, string field, List<WorkflowRunValidationIssue> issues)
    {
        if (value)
            AddError(issues, "WORKFLOW_FAILURE_RETRY_AUTHORITY_FLAG_BLOCKED", "Workflow failure/retry authority/action flags must be false.", field);
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
