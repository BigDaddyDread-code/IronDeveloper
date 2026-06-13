using System.Text.Json;

namespace IronDev.Core.Workflow;

public enum WorkflowCheckpointType
{
    RunCreated = 1,
    StepRecorded = 2,
    EvidenceCollected = 3,
    GroundingRecorded = 4,
    ReviewSnapshot = 5,
    ValidationSnapshot = 6,
    HumanDecisionSupport = 7,
    Receipt = 8,
    FailureSnapshot = 9,
    BlockedSnapshot = 10,
    CancelledSnapshot = 11
}

public enum WorkflowCheckpointStatus
{
    Created = 1,
    Captured = 2,
    ReadyForReview = 3,
    Blocked = 4,
    Completed = 5,
    Failed = 6,
    Cancelled = 7,
    Superseded = 8
}

public sealed record WorkflowCheckpoint
{
    public required Guid WorkflowCheckpointId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string CheckpointKey { get; init; }
    public required string CheckpointName { get; init; }
    public required WorkflowCheckpointType CheckpointType { get; init; }
    public required WorkflowCheckpointStatus Status { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string? SafeSummary { get; init; }
    public required int StateVersion { get; init; }
    public required string StateJson { get; init; }
    public string? StateHashSha256 { get; init; }
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
    public required IReadOnlyList<WorkflowCheckpointEvidenceReference> EvidenceReferences { get; init; }
    public required IReadOnlyList<WorkflowCheckpointGroundingReference> GroundingReferences { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowCheckpointCreateRequest
{
    public Guid? WorkflowCheckpointId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string CheckpointKey { get; init; }
    public required string CheckpointName { get; init; }
    public required WorkflowCheckpointType CheckpointType { get; init; }
    public required WorkflowCheckpointStatus Status { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string? SafeSummary { get; init; }
    public required int StateVersion { get; init; }
    public required string StateJson { get; init; }
    public string? StateHashSha256 { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required string CreatedByActorType { get; init; }
    public required string CreatedByActorId { get; init; }
    public required int MetadataVersion { get; init; }
    public required string MetadataJson { get; init; }
    public IReadOnlyList<WorkflowCheckpointEvidenceReferenceCreateRequest> EvidenceReferences { get; init; } = [];
    public IReadOnlyList<WorkflowCheckpointGroundingReferenceCreateRequest> GroundingReferences { get; init; } = [];
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

public sealed record WorkflowCheckpointSummary
{
    public required Guid WorkflowCheckpointId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string CheckpointKey { get; init; }
    public required string CheckpointName { get; init; }
    public required WorkflowCheckpointType CheckpointType { get; init; }
    public required WorkflowCheckpointStatus Status { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string? StateHashSha256 { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required int EvidenceReferenceCount { get; init; }
    public required int GroundingReferenceCount { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowCheckpointEvidenceReference
{
    public required Guid WorkflowCheckpointEvidenceReferenceId { get; init; }
    public required Guid WorkflowCheckpointId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public required Guid ProjectId { get; init; }
    public required WorkflowRunEvidenceType EvidenceType { get; init; }
    public required string EvidenceId { get; init; }
    public string? EvidenceLabel { get; init; }
    public string? SafeSummary { get; init; }
    public WorkflowRunEvidenceAllowedUse? AllowedUse { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public Guid? HandoffRecordId { get; init; }
    public Guid? ThoughtLedgerEntryId { get; init; }
    public Guid? GroundingReferenceId { get; init; }
    public Guid? WorkflowRunEvidenceReferenceId { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowCheckpointEvidenceReferenceCreateRequest
{
    public required WorkflowRunEvidenceType EvidenceType { get; init; }
    public required string EvidenceId { get; init; }
    public string? EvidenceLabel { get; init; }
    public string? SafeSummary { get; init; }
    public WorkflowRunEvidenceAllowedUse? AllowedUse { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public Guid? HandoffRecordId { get; init; }
    public Guid? ThoughtLedgerEntryId { get; init; }
    public Guid? GroundingReferenceId { get; init; }
    public Guid? WorkflowRunEvidenceReferenceId { get; init; }
}

public sealed record WorkflowCheckpointGroundingReference
{
    public required Guid WorkflowCheckpointGroundingReferenceId { get; init; }
    public required Guid WorkflowCheckpointId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid GroundingReferenceId { get; init; }
    public required WorkflowRunGroundingClaimType ClaimType { get; init; }
    public required string ClaimId { get; init; }
    public string? SafeSummary { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowCheckpointGroundingReferenceCreateRequest
{
    public required Guid GroundingReferenceId { get; init; }
    public required WorkflowRunGroundingClaimType ClaimType { get; init; }
    public required string ClaimId { get; init; }
    public string? SafeSummary { get; init; }
}

public interface IWorkflowCheckpointStore
{
    Task<WorkflowCheckpoint> CreateAsync(WorkflowCheckpointCreateRequest request, CancellationToken cancellationToken = default);

    Task<WorkflowCheckpoint?> GetAsync(Guid projectId, Guid workflowRunId, Guid workflowCheckpointId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowCheckpointSummary>> ListByRunAsync(Guid projectId, Guid workflowRunId, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowCheckpointSummary>> ListByStepAsync(Guid projectId, Guid workflowRunId, Guid workflowRunStepId, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowCheckpointSummary>> ListByCorrelationAsync(Guid projectId, Guid correlationId, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowCheckpointSummary>> ListBySubjectAsync(Guid projectId, string subjectType, string subjectId, int take, CancellationToken cancellationToken = default);
}

public sealed class WorkflowCheckpointValidator
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;
    private const int MaxJsonLength = 64_000;
    private const int MaxStateHashLength = 128;

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

    private static readonly string[] AuthorityAndResumeMarkers =
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
        "dispatch agent",
        "tool executed",
        "tool ran"
    ];

    public WorkflowRunValidationResult ValidateCreate(WorkflowCheckpointCreateRequest? request)
    {
        var issues = new List<WorkflowRunValidationIssue>();
        if (request is null)
        {
            AddError(issues, "WORKFLOW_CHECKPOINT_REQUIRED", "Workflow checkpoint create request is required.", nameof(WorkflowCheckpointCreateRequest));
            return Result(issues);
        }

        if (request.WorkflowCheckpointId == Guid.Empty)
            AddError(issues, "WORKFLOW_CHECKPOINT_ID_INVALID", "WorkflowCheckpointId cannot be empty when supplied.", nameof(WorkflowCheckpointCreateRequest.WorkflowCheckpointId));
        if (request.WorkflowRunId == Guid.Empty)
            AddError(issues, "WORKFLOW_CHECKPOINT_RUN_ID_REQUIRED", "WorkflowRunId is required.", nameof(WorkflowCheckpointCreateRequest.WorkflowRunId));
        if (request.WorkflowRunStepId == Guid.Empty)
            AddError(issues, "WORKFLOW_CHECKPOINT_STEP_ID_INVALID", "WorkflowRunStepId cannot be empty when supplied.", nameof(WorkflowCheckpointCreateRequest.WorkflowRunStepId));
        if (request.ProjectId == Guid.Empty)
            AddError(issues, "WORKFLOW_CHECKPOINT_PROJECT_ID_REQUIRED", "ProjectId is required.", nameof(WorkflowCheckpointCreateRequest.ProjectId));
        if (!Enum.IsDefined(request.CheckpointType))
            AddError(issues, "WORKFLOW_CHECKPOINT_TYPE_INVALID", "CheckpointType is invalid.", nameof(WorkflowCheckpointCreateRequest.CheckpointType));
        if (!Enum.IsDefined(request.Status))
            AddError(issues, "WORKFLOW_CHECKPOINT_STATUS_INVALID", "Checkpoint status is invalid.", nameof(WorkflowCheckpointCreateRequest.Status));
        if (request.StateVersion <= 0)
            AddError(issues, "WORKFLOW_CHECKPOINT_STATE_VERSION_INVALID", "StateVersion must be positive.", nameof(WorkflowCheckpointCreateRequest.StateVersion));
        if (request.MetadataVersion <= 0)
            AddError(issues, "WORKFLOW_CHECKPOINT_METADATA_VERSION_INVALID", "MetadataVersion must be positive.", nameof(WorkflowCheckpointCreateRequest.MetadataVersion));
        if (request.StateHashSha256 is { Length: > MaxStateHashLength })
            AddError(issues, "WORKFLOW_CHECKPOINT_STATE_HASH_TOO_LONG", "StateHashSha256 is too long.", nameof(WorkflowCheckpointCreateRequest.StateHashSha256));

        Require(request.CheckpointKey, "WORKFLOW_CHECKPOINT_KEY_REQUIRED", nameof(WorkflowCheckpointCreateRequest.CheckpointKey), issues);
        Require(request.CheckpointName, "WORKFLOW_CHECKPOINT_NAME_REQUIRED", nameof(WorkflowCheckpointCreateRequest.CheckpointName), issues);
        Require(request.CreatedByActorType, "WORKFLOW_CHECKPOINT_ACTOR_TYPE_REQUIRED", nameof(WorkflowCheckpointCreateRequest.CreatedByActorType), issues);
        Require(request.CreatedByActorId, "WORKFLOW_CHECKPOINT_ACTOR_ID_REQUIRED", nameof(WorkflowCheckpointCreateRequest.CreatedByActorId), issues);

        ValidateJson(request.StateJson, "WORKFLOW_CHECKPOINT_STATE_JSON", nameof(WorkflowCheckpointCreateRequest.StateJson), issues);
        ValidateJson(request.MetadataJson, "WORKFLOW_CHECKPOINT_METADATA_JSON", nameof(WorkflowCheckpointCreateRequest.MetadataJson), issues);
        ValidateTextSafety(request.CheckpointKey, nameof(WorkflowCheckpointCreateRequest.CheckpointKey), issues);
        ValidateTextSafety(request.CheckpointName, nameof(WorkflowCheckpointCreateRequest.CheckpointName), issues);
        ValidateTextSafety(request.SubjectType, nameof(WorkflowCheckpointCreateRequest.SubjectType), issues);
        ValidateTextSafety(request.SubjectId, nameof(WorkflowCheckpointCreateRequest.SubjectId), issues);
        ValidateTextSafety(request.SafeSummary, nameof(WorkflowCheckpointCreateRequest.SafeSummary), issues);
        ValidateTextSafety(request.StateHashSha256, nameof(WorkflowCheckpointCreateRequest.StateHashSha256), issues);
        ValidateTextSafety(request.CreatedByActorType, nameof(WorkflowCheckpointCreateRequest.CreatedByActorType), issues);
        ValidateTextSafety(request.CreatedByActorId, nameof(WorkflowCheckpointCreateRequest.CreatedByActorId), issues);

        RejectAuthorityFlag(request.GrantsApproval, nameof(WorkflowCheckpointCreateRequest.GrantsApproval), issues);
        RejectAuthorityFlag(request.GrantsExecution, nameof(WorkflowCheckpointCreateRequest.GrantsExecution), issues);
        RejectAuthorityFlag(request.MutatesSource, nameof(WorkflowCheckpointCreateRequest.MutatesSource), issues);
        RejectAuthorityFlag(request.PromotesMemory, nameof(WorkflowCheckpointCreateRequest.PromotesMemory), issues);
        RejectAuthorityFlag(request.StartsWorkflow, nameof(WorkflowCheckpointCreateRequest.StartsWorkflow), issues);
        RejectAuthorityFlag(request.ContinuesWorkflow, nameof(WorkflowCheckpointCreateRequest.ContinuesWorkflow), issues);
        RejectAuthorityFlag(request.ResumesWorkflow, nameof(WorkflowCheckpointCreateRequest.ResumesWorkflow), issues);
        RejectAuthorityFlag(request.SatisfiesPolicy, nameof(WorkflowCheckpointCreateRequest.SatisfiesPolicy), issues);
        RejectAuthorityFlag(request.TransfersAuthority, nameof(WorkflowCheckpointCreateRequest.TransfersAuthority), issues);
        RejectAuthorityFlag(request.ApprovesRelease, nameof(WorkflowCheckpointCreateRequest.ApprovesRelease), issues);
        RejectAuthorityFlag(request.CreatesAcceptedMemory, nameof(WorkflowCheckpointCreateRequest.CreatesAcceptedMemory), issues);

        ValidateEvidence(request.EvidenceReferences, issues);
        ValidateGrounding(request.GroundingReferences, issues);
        return Result(issues);
    }

    public WorkflowRunValidationResult ValidateMaterialized(WorkflowCheckpoint checkpoint)
    {
        var issues = new List<WorkflowRunValidationIssue>();
        if (checkpoint.WorkflowCheckpointId == Guid.Empty)
            AddError(issues, "WORKFLOW_CHECKPOINT_ID_REQUIRED", "WorkflowCheckpointId is required.", nameof(WorkflowCheckpoint.WorkflowCheckpointId));
        if (checkpoint.WorkflowRunId == Guid.Empty)
            AddError(issues, "WORKFLOW_CHECKPOINT_RUN_ID_REQUIRED", "WorkflowRunId is required.", nameof(WorkflowCheckpoint.WorkflowRunId));
        if (checkpoint.WorkflowRunStepId == Guid.Empty)
            AddError(issues, "WORKFLOW_CHECKPOINT_STEP_ID_INVALID", "WorkflowRunStepId cannot be empty when supplied.", nameof(WorkflowCheckpoint.WorkflowRunStepId));
        if (checkpoint.ProjectId == Guid.Empty)
            AddError(issues, "WORKFLOW_CHECKPOINT_PROJECT_ID_REQUIRED", "ProjectId is required.", nameof(WorkflowCheckpoint.ProjectId));
        if (checkpoint.StateVersion <= 0)
            AddError(issues, "WORKFLOW_CHECKPOINT_STATE_VERSION_INVALID", "StateVersion must be positive.", nameof(WorkflowCheckpoint.StateVersion));
        RejectAuthorityFlag(checkpoint.GrantsApproval, nameof(WorkflowCheckpoint.GrantsApproval), issues);
        RejectAuthorityFlag(checkpoint.GrantsExecution, nameof(WorkflowCheckpoint.GrantsExecution), issues);
        RejectAuthorityFlag(checkpoint.MutatesSource, nameof(WorkflowCheckpoint.MutatesSource), issues);
        RejectAuthorityFlag(checkpoint.PromotesMemory, nameof(WorkflowCheckpoint.PromotesMemory), issues);
        RejectAuthorityFlag(checkpoint.StartsWorkflow, nameof(WorkflowCheckpoint.StartsWorkflow), issues);
        RejectAuthorityFlag(checkpoint.ContinuesWorkflow, nameof(WorkflowCheckpoint.ContinuesWorkflow), issues);
        RejectAuthorityFlag(checkpoint.ResumesWorkflow, nameof(WorkflowCheckpoint.ResumesWorkflow), issues);
        RejectAuthorityFlag(checkpoint.SatisfiesPolicy, nameof(WorkflowCheckpoint.SatisfiesPolicy), issues);
        RejectAuthorityFlag(checkpoint.TransfersAuthority, nameof(WorkflowCheckpoint.TransfersAuthority), issues);
        RejectAuthorityFlag(checkpoint.ApprovesRelease, nameof(WorkflowCheckpoint.ApprovesRelease), issues);
        RejectAuthorityFlag(checkpoint.CreatesAcceptedMemory, nameof(WorkflowCheckpoint.CreatesAcceptedMemory), issues);
        return Result(issues);
    }

    public WorkflowCheckpointCreateRequest Normalize(WorkflowCheckpointCreateRequest request) =>
        request with
        {
            CheckpointKey = request.CheckpointKey.Trim(),
            CheckpointName = request.CheckpointName.Trim(),
            SubjectType = NormalizeOptional(request.SubjectType),
            SubjectId = NormalizeOptional(request.SubjectId),
            SafeSummary = NormalizeOptional(request.SafeSummary),
            StateHashSha256 = NormalizeOptional(request.StateHashSha256),
            CreatedByActorType = request.CreatedByActorType.Trim(),
            CreatedByActorId = request.CreatedByActorId.Trim(),
            EvidenceReferences = request.EvidenceReferences.Select(NormalizeEvidence).ToArray(),
            GroundingReferences = request.GroundingReferences.Select(NormalizeGrounding).ToArray()
        };

    public static int NormalizeTake(int take) => Math.Clamp(take <= 0 ? DefaultTake : take, 1, MaxTake);

    private static WorkflowCheckpointEvidenceReferenceCreateRequest NormalizeEvidence(WorkflowCheckpointEvidenceReferenceCreateRequest evidence) =>
        evidence with
        {
            EvidenceId = evidence.EvidenceId.Trim(),
            EvidenceLabel = NormalizeOptional(evidence.EvidenceLabel),
            SafeSummary = NormalizeOptional(evidence.SafeSummary)
        };

    private static WorkflowCheckpointGroundingReferenceCreateRequest NormalizeGrounding(WorkflowCheckpointGroundingReferenceCreateRequest grounding) =>
        grounding with
        {
            ClaimId = grounding.ClaimId.Trim(),
            SafeSummary = NormalizeOptional(grounding.SafeSummary)
        };

    private static void ValidateEvidence(IReadOnlyList<WorkflowCheckpointEvidenceReferenceCreateRequest> evidenceReferences, List<WorkflowRunValidationIssue> issues)
    {
        foreach (var evidence in evidenceReferences ?? [])
        {
            if (evidence is null)
            {
                AddError(issues, "WORKFLOW_CHECKPOINT_EVIDENCE_INVALID", "Evidence reference cannot be null.", nameof(WorkflowCheckpointCreateRequest.EvidenceReferences));
                continue;
            }

            if (!Enum.IsDefined(evidence.EvidenceType))
                AddError(issues, "WORKFLOW_CHECKPOINT_EVIDENCE_TYPE_INVALID", "EvidenceType is invalid.", nameof(WorkflowCheckpointEvidenceReferenceCreateRequest.EvidenceType));
            Require(evidence.EvidenceId, "WORKFLOW_CHECKPOINT_EVIDENCE_ID_REQUIRED", nameof(WorkflowCheckpointEvidenceReferenceCreateRequest.EvidenceId), issues);
            if (evidence.AllowedUse.HasValue && !Enum.IsDefined(evidence.AllowedUse.Value))
                AddError(issues, "WORKFLOW_CHECKPOINT_EVIDENCE_ALLOWED_USE_INVALID", "AllowedUse is invalid.", nameof(WorkflowCheckpointEvidenceReferenceCreateRequest.AllowedUse));
            ValidateTextSafety(evidence.EvidenceId, nameof(WorkflowCheckpointEvidenceReferenceCreateRequest.EvidenceId), issues);
            ValidateTextSafety(evidence.EvidenceLabel, nameof(WorkflowCheckpointEvidenceReferenceCreateRequest.EvidenceLabel), issues);
            ValidateTextSafety(evidence.SafeSummary, nameof(WorkflowCheckpointEvidenceReferenceCreateRequest.SafeSummary), issues);
        }
    }

    private static void ValidateGrounding(IReadOnlyList<WorkflowCheckpointGroundingReferenceCreateRequest> groundingReferences, List<WorkflowRunValidationIssue> issues)
    {
        foreach (var grounding in groundingReferences ?? [])
        {
            if (grounding is null)
            {
                AddError(issues, "WORKFLOW_CHECKPOINT_GROUNDING_INVALID", "Grounding reference cannot be null.", nameof(WorkflowCheckpointCreateRequest.GroundingReferences));
                continue;
            }

            if (grounding.GroundingReferenceId == Guid.Empty)
                AddError(issues, "WORKFLOW_CHECKPOINT_GROUNDING_ID_REQUIRED", "GroundingReferenceId is required.", nameof(WorkflowCheckpointGroundingReferenceCreateRequest.GroundingReferenceId));
            if (!Enum.IsDefined(grounding.ClaimType))
                AddError(issues, "WORKFLOW_CHECKPOINT_GROUNDING_CLAIM_TYPE_INVALID", "ClaimType is invalid.", nameof(WorkflowCheckpointGroundingReferenceCreateRequest.ClaimType));
            Require(grounding.ClaimId, "WORKFLOW_CHECKPOINT_GROUNDING_CLAIM_ID_REQUIRED", nameof(WorkflowCheckpointGroundingReferenceCreateRequest.ClaimId), issues);
            ValidateTextSafety(grounding.ClaimId, nameof(WorkflowCheckpointGroundingReferenceCreateRequest.ClaimId), issues);
            ValidateTextSafety(grounding.SafeSummary, nameof(WorkflowCheckpointGroundingReferenceCreateRequest.SafeSummary), issues);
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
                    if (IsAuthorityOrResumeProperty(property.Name) && IsTruthy(property.Value))
                        AddError(issues, "WORKFLOW_CHECKPOINT_AUTHORITY_METADATA_BLOCKED", $"State or metadata property cannot grant authority, action, or resumability: {property.Name}.", field);
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

    private static bool IsAuthorityOrResumeProperty(string propertyName) =>
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
        propertyName.Contains("continuesWorkflow", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("resume", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("resumable", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("restore", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("restorable", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("dispatch", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("satisfiesPolicy", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("policySatisfied", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("transfersAuthority", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("approvesRelease", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("releaseApproved", StringComparison.OrdinalIgnoreCase);

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
            AddError(issues, "WORKFLOW_CHECKPOINT_PRIVATE_REASONING_BLOCKED", "Workflow checkpoint text must not contain hidden/private reasoning or raw dump markers.", field);

        if (ContainsAny(value, AuthorityAndResumeMarkers))
            AddError(issues, "WORKFLOW_CHECKPOINT_AUTHORITY_OR_RESUME_LANGUAGE_BLOCKED", "Workflow checkpoint text must not claim approval, execution, policy satisfaction, source apply, memory promotion, release approval, workflow continuation/resume, dispatch, or authority transfer.", field);
    }

    private static void RejectAuthorityFlag(bool value, string field, List<WorkflowRunValidationIssue> issues)
    {
        if (value)
            AddError(issues, "WORKFLOW_CHECKPOINT_AUTHORITY_FLAG_BLOCKED", "Workflow checkpoint authority/action flags must be false.", field);
    }

    private static bool ContainsAny(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

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