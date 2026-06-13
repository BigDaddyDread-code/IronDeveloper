using System.Text.Json;

namespace IronDev.Core.Workflow;

public sealed record WorkflowStep
{
    public required Guid WorkflowRunStepId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string StepKey { get; init; }
    public required string StepName { get; init; }
    public required WorkflowRunStepType StepType { get; init; }
    public required WorkflowRunStatus Status { get; init; }
    public string? AgentRole { get; init; }
    public string? AgentId { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string? SafeSummary { get; init; }
    public required int SequenceNumber { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required int MetadataVersion { get; init; }
    public required string MetadataJson { get; init; }
    public required bool GrantsApproval { get; init; }
    public required bool GrantsExecution { get; init; }
    public required bool MutatesSource { get; init; }
    public required bool PromotesMemory { get; init; }
    public required bool StartsWorkflow { get; init; }
    public required bool ContinuesWorkflow { get; init; }
    public required bool SatisfiesPolicy { get; init; }
    public required bool TransfersAuthority { get; init; }
    public required bool ApprovesRelease { get; init; }
    public required bool CreatesAcceptedMemory { get; init; }
    public required IReadOnlyList<WorkflowRunEvidenceReference> EvidenceReferences { get; init; }
    public required IReadOnlyList<WorkflowRunGroundingReference> GroundingReferences { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public sealed record WorkflowStepCreateRequest
{
    public Guid? WorkflowRunStepId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string StepKey { get; init; }
    public required string StepName { get; init; }
    public required WorkflowRunStepType StepType { get; init; }
    public required WorkflowRunStatus Status { get; init; }
    public string? AgentRole { get; init; }
    public string? AgentId { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string? SafeSummary { get; init; }
    public required int SequenceNumber { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required int MetadataVersion { get; init; }
    public required string MetadataJson { get; init; }
    public IReadOnlyList<WorkflowRunEvidenceReferenceCreateRequest> EvidenceReferences { get; init; } = [];
    public IReadOnlyList<WorkflowRunGroundingReferenceCreateRequest> GroundingReferences { get; init; } = [];
    public bool GrantsApproval { get; init; }
    public bool GrantsExecution { get; init; }
    public bool MutatesSource { get; init; }
    public bool PromotesMemory { get; init; }
    public bool StartsWorkflow { get; init; }
    public bool ContinuesWorkflow { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool TransfersAuthority { get; init; }
    public bool ApprovesRelease { get; init; }
    public bool CreatesAcceptedMemory { get; init; }
}

public sealed record WorkflowStepSummary
{
    public required Guid WorkflowRunStepId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string StepKey { get; init; }
    public required string StepName { get; init; }
    public required WorkflowRunStepType StepType { get; init; }
    public required WorkflowRunStatus Status { get; init; }
    public string? AgentRole { get; init; }
    public string? AgentId { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public required int SequenceNumber { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public required int EvidenceReferenceCount { get; init; }
    public required int GroundingReferenceCount { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
}

public interface IWorkflowStepStore
{
    Task<WorkflowStep> CreateAsync(WorkflowStepCreateRequest request, CancellationToken cancellationToken = default);

    Task<WorkflowStep?> GetAsync(Guid projectId, Guid workflowRunId, Guid workflowRunStepId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowStepSummary>> ListByRunAsync(Guid projectId, Guid workflowRunId, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowStepSummary>> ListByCorrelationAsync(Guid projectId, Guid correlationId, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowStepSummary>> ListBySubjectAsync(Guid projectId, string subjectType, string subjectId, int take, CancellationToken cancellationToken = default);
}

public sealed class WorkflowStepValidator
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;
    private const int MaxMetadataJsonLength = 32_000;

    private static readonly string[] PrivateReasoningMarkers =
    [
        "hiddenReasoning",
        "chainOfThought",
        "chain-of-thought",
        "chain of thought",
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

    private static readonly string[] AuthorityMarkers =
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
        "dispatch agent",
        "tool executed",
        "tool ran"
    ];

    public WorkflowRunValidationResult ValidateCreate(WorkflowStepCreateRequest? request)
    {
        var issues = new List<WorkflowRunValidationIssue>();
        if (request is null)
        {
            AddError(issues, "WORKFLOW_STEP_REQUIRED", "Workflow step create request is required.", nameof(WorkflowStepCreateRequest));
            return Result(issues);
        }

        if (request.WorkflowRunStepId == Guid.Empty)
            AddError(issues, "WORKFLOW_STEP_ID_INVALID", "WorkflowRunStepId cannot be empty when supplied.", nameof(WorkflowStepCreateRequest.WorkflowRunStepId));
        if (request.WorkflowRunId == Guid.Empty)
            AddError(issues, "WORKFLOW_STEP_RUN_ID_REQUIRED", "WorkflowRunId is required.", nameof(WorkflowStepCreateRequest.WorkflowRunId));
        if (request.ProjectId == Guid.Empty)
            AddError(issues, "WORKFLOW_STEP_PROJECT_ID_REQUIRED", "ProjectId is required.", nameof(WorkflowStepCreateRequest.ProjectId));
        if (request.SequenceNumber <= 0)
            AddError(issues, "WORKFLOW_STEP_SEQUENCE_INVALID", "SequenceNumber must be positive.", nameof(WorkflowStepCreateRequest.SequenceNumber));
        if (!Enum.IsDefined(request.StepType))
            AddError(issues, "WORKFLOW_STEP_TYPE_INVALID", "StepType is invalid.", nameof(WorkflowStepCreateRequest.StepType));
        if (!Enum.IsDefined(request.Status))
            AddError(issues, "WORKFLOW_STEP_STATUS_INVALID", "Step status is invalid.", nameof(WorkflowStepCreateRequest.Status));
        if (request.MetadataVersion <= 0)
            AddError(issues, "WORKFLOW_STEP_METADATA_VERSION_INVALID", "MetadataVersion must be positive.", nameof(WorkflowStepCreateRequest.MetadataVersion));

        Require(request.StepKey, "WORKFLOW_STEP_KEY_REQUIRED", nameof(WorkflowStepCreateRequest.StepKey), issues);
        Require(request.StepName, "WORKFLOW_STEP_NAME_REQUIRED", nameof(WorkflowStepCreateRequest.StepName), issues);
        ValidateJson(request.MetadataJson, "WORKFLOW_STEP_METADATA_JSON", nameof(WorkflowStepCreateRequest.MetadataJson), issues);
        ValidateTextSafety(request.StepKey, nameof(WorkflowStepCreateRequest.StepKey), issues);
        ValidateTextSafety(request.StepName, nameof(WorkflowStepCreateRequest.StepName), issues);
        ValidateTextSafety(request.AgentRole, nameof(WorkflowStepCreateRequest.AgentRole), issues);
        ValidateTextSafety(request.AgentId, nameof(WorkflowStepCreateRequest.AgentId), issues);
        ValidateTextSafety(request.SubjectType, nameof(WorkflowStepCreateRequest.SubjectType), issues);
        ValidateTextSafety(request.SubjectId, nameof(WorkflowStepCreateRequest.SubjectId), issues);
        ValidateTextSafety(request.SafeSummary, nameof(WorkflowStepCreateRequest.SafeSummary), issues);

        RejectAuthorityFlag(request.GrantsApproval, nameof(WorkflowStepCreateRequest.GrantsApproval), issues);
        RejectAuthorityFlag(request.GrantsExecution, nameof(WorkflowStepCreateRequest.GrantsExecution), issues);
        RejectAuthorityFlag(request.MutatesSource, nameof(WorkflowStepCreateRequest.MutatesSource), issues);
        RejectAuthorityFlag(request.PromotesMemory, nameof(WorkflowStepCreateRequest.PromotesMemory), issues);
        RejectAuthorityFlag(request.StartsWorkflow, nameof(WorkflowStepCreateRequest.StartsWorkflow), issues);
        RejectAuthorityFlag(request.ContinuesWorkflow, nameof(WorkflowStepCreateRequest.ContinuesWorkflow), issues);
        RejectAuthorityFlag(request.SatisfiesPolicy, nameof(WorkflowStepCreateRequest.SatisfiesPolicy), issues);
        RejectAuthorityFlag(request.TransfersAuthority, nameof(WorkflowStepCreateRequest.TransfersAuthority), issues);
        RejectAuthorityFlag(request.ApprovesRelease, nameof(WorkflowStepCreateRequest.ApprovesRelease), issues);
        RejectAuthorityFlag(request.CreatesAcceptedMemory, nameof(WorkflowStepCreateRequest.CreatesAcceptedMemory), issues);

        ValidateEvidence(request.EvidenceReferences, issues);
        ValidateGrounding(request.GroundingReferences, issues);
        return Result(issues);
    }

    public WorkflowRunValidationResult ValidateMaterialized(WorkflowStep step)
    {
        var issues = new List<WorkflowRunValidationIssue>();
        if (step.WorkflowRunStepId == Guid.Empty)
            AddError(issues, "WORKFLOW_STEP_ID_REQUIRED", "WorkflowRunStepId is required.", nameof(WorkflowStep.WorkflowRunStepId));
        if (step.WorkflowRunId == Guid.Empty)
            AddError(issues, "WORKFLOW_STEP_RUN_ID_REQUIRED", "WorkflowRunId is required.", nameof(WorkflowStep.WorkflowRunId));
        if (step.ProjectId == Guid.Empty)
            AddError(issues, "WORKFLOW_STEP_PROJECT_ID_REQUIRED", "ProjectId is required.", nameof(WorkflowStep.ProjectId));
        if (step.SequenceNumber <= 0)
            AddError(issues, "WORKFLOW_STEP_SEQUENCE_INVALID", "SequenceNumber must be positive.", nameof(WorkflowStep.SequenceNumber));
        RejectAuthorityFlag(step.GrantsApproval, nameof(WorkflowStep.GrantsApproval), issues);
        RejectAuthorityFlag(step.GrantsExecution, nameof(WorkflowStep.GrantsExecution), issues);
        RejectAuthorityFlag(step.MutatesSource, nameof(WorkflowStep.MutatesSource), issues);
        RejectAuthorityFlag(step.PromotesMemory, nameof(WorkflowStep.PromotesMemory), issues);
        RejectAuthorityFlag(step.StartsWorkflow, nameof(WorkflowStep.StartsWorkflow), issues);
        RejectAuthorityFlag(step.ContinuesWorkflow, nameof(WorkflowStep.ContinuesWorkflow), issues);
        RejectAuthorityFlag(step.SatisfiesPolicy, nameof(WorkflowStep.SatisfiesPolicy), issues);
        RejectAuthorityFlag(step.TransfersAuthority, nameof(WorkflowStep.TransfersAuthority), issues);
        RejectAuthorityFlag(step.ApprovesRelease, nameof(WorkflowStep.ApprovesRelease), issues);
        RejectAuthorityFlag(step.CreatesAcceptedMemory, nameof(WorkflowStep.CreatesAcceptedMemory), issues);
        return Result(issues);
    }

    public WorkflowStepCreateRequest Normalize(WorkflowStepCreateRequest request) =>
        request with
        {
            StepKey = request.StepKey.Trim(),
            StepName = request.StepName.Trim(),
            AgentRole = NormalizeOptional(request.AgentRole),
            AgentId = NormalizeOptional(request.AgentId),
            SubjectType = NormalizeOptional(request.SubjectType),
            SubjectId = NormalizeOptional(request.SubjectId),
            SafeSummary = NormalizeOptional(request.SafeSummary),
            EvidenceReferences = request.EvidenceReferences.Select(NormalizeEvidence).ToArray(),
            GroundingReferences = request.GroundingReferences.Select(NormalizeGrounding).ToArray()
        };

    public static int NormalizeTake(int take) => Math.Clamp(take <= 0 ? DefaultTake : take, 1, MaxTake);

    private static WorkflowRunEvidenceReferenceCreateRequest NormalizeEvidence(WorkflowRunEvidenceReferenceCreateRequest evidence) =>
        evidence with
        {
            StepKey = NormalizeOptional(evidence.StepKey),
            EvidenceId = evidence.EvidenceId.Trim(),
            EvidenceLabel = NormalizeOptional(evidence.EvidenceLabel),
            SafeSummary = NormalizeOptional(evidence.SafeSummary)
        };

    private static WorkflowRunGroundingReferenceCreateRequest NormalizeGrounding(WorkflowRunGroundingReferenceCreateRequest grounding) =>
        grounding with
        {
            StepKey = NormalizeOptional(grounding.StepKey),
            ClaimId = grounding.ClaimId.Trim(),
            SafeSummary = NormalizeOptional(grounding.SafeSummary)
        };

    private static void ValidateEvidence(IReadOnlyList<WorkflowRunEvidenceReferenceCreateRequest> evidenceReferences, List<WorkflowRunValidationIssue> issues)
    {
        foreach (var evidence in evidenceReferences ?? [])
        {
            if (evidence is null)
            {
                AddError(issues, "WORKFLOW_STEP_EVIDENCE_INVALID", "Evidence reference cannot be null.", nameof(WorkflowStepCreateRequest.EvidenceReferences));
                continue;
            }

            if (!Enum.IsDefined(evidence.EvidenceType))
                AddError(issues, "WORKFLOW_STEP_EVIDENCE_TYPE_INVALID", "EvidenceType is invalid.", nameof(WorkflowRunEvidenceReferenceCreateRequest.EvidenceType));
            Require(evidence.EvidenceId, "WORKFLOW_STEP_EVIDENCE_ID_REQUIRED", nameof(WorkflowRunEvidenceReferenceCreateRequest.EvidenceId), issues);
            if (evidence.AllowedUse.HasValue && !Enum.IsDefined(evidence.AllowedUse.Value))
                AddError(issues, "WORKFLOW_STEP_EVIDENCE_ALLOWED_USE_INVALID", "AllowedUse is invalid.", nameof(WorkflowRunEvidenceReferenceCreateRequest.AllowedUse));
            ValidateTextSafety(evidence.StepKey, nameof(WorkflowRunEvidenceReferenceCreateRequest.StepKey), issues);
            ValidateTextSafety(evidence.EvidenceId, nameof(WorkflowRunEvidenceReferenceCreateRequest.EvidenceId), issues);
            ValidateTextSafety(evidence.EvidenceLabel, nameof(WorkflowRunEvidenceReferenceCreateRequest.EvidenceLabel), issues);
            ValidateTextSafety(evidence.SafeSummary, nameof(WorkflowRunEvidenceReferenceCreateRequest.SafeSummary), issues);
        }
    }

    private static void ValidateGrounding(IReadOnlyList<WorkflowRunGroundingReferenceCreateRequest> groundingReferences, List<WorkflowRunValidationIssue> issues)
    {
        foreach (var grounding in groundingReferences ?? [])
        {
            if (grounding is null)
            {
                AddError(issues, "WORKFLOW_STEP_GROUNDING_INVALID", "Grounding reference cannot be null.", nameof(WorkflowStepCreateRequest.GroundingReferences));
                continue;
            }

            if (grounding.GroundingEvidenceReferenceId == Guid.Empty)
                AddError(issues, "WORKFLOW_STEP_GROUNDING_ID_REQUIRED", "GroundingEvidenceReferenceId is required.", nameof(WorkflowRunGroundingReferenceCreateRequest.GroundingEvidenceReferenceId));
            if (!Enum.IsDefined(grounding.ClaimType))
                AddError(issues, "WORKFLOW_STEP_GROUNDING_CLAIM_TYPE_INVALID", "ClaimType is invalid.", nameof(WorkflowRunGroundingReferenceCreateRequest.ClaimType));
            Require(grounding.ClaimId, "WORKFLOW_STEP_GROUNDING_CLAIM_ID_REQUIRED", nameof(WorkflowRunGroundingReferenceCreateRequest.ClaimId), issues);
            ValidateTextSafety(grounding.StepKey, nameof(WorkflowRunGroundingReferenceCreateRequest.StepKey), issues);
            ValidateTextSafety(grounding.ClaimId, nameof(WorkflowRunGroundingReferenceCreateRequest.ClaimId), issues);
            ValidateTextSafety(grounding.SafeSummary, nameof(WorkflowRunGroundingReferenceCreateRequest.SafeSummary), issues);
        }
    }

    private static void ValidateJson(string? value, string codePrefix, string field, List<WorkflowRunValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(issues, codePrefix + "_REQUIRED", "JSON metadata is required.", field);
            return;
        }

        if (value.Length > MaxMetadataJsonLength)
            AddError(issues, codePrefix + "_TOO_LARGE", "JSON metadata is too large.", field);

        ValidateTextSafety(value, field, issues);

        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                AddError(issues, codePrefix + "_OBJECT_REQUIRED", "JSON metadata must be an object.", field);
            ValidateJsonElement(document.RootElement, field, issues);
        }
        catch (JsonException)
        {
            AddError(issues, codePrefix + "_INVALID", "JSON metadata must be valid JSON.", field);
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
                    if (IsAuthorityProperty(property.Name) && IsTruthy(property.Value))
                        AddError(issues, "WORKFLOW_STEP_AUTHORITY_METADATA_BLOCKED", $"Metadata property cannot grant authority: {property.Name}.", field);
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

    private static bool IsAuthorityProperty(string propertyName) =>
        propertyName.Contains("grants", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("approval", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("execution", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("mutatesSource", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("promotesMemory", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("startsWorkflow", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("continuesWorkflow", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("satisfiesPolicy", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("transfersAuthority", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("approvesRelease", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("createsAcceptedMemory", StringComparison.OrdinalIgnoreCase);

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
            AddError(issues, "WORKFLOW_STEP_PRIVATE_REASONING_BLOCKED", "Workflow step text must not contain hidden/private reasoning or raw dump markers.", field);

        if (ContainsAny(value, AuthorityMarkers))
            AddError(issues, "WORKFLOW_STEP_AUTHORITY_LANGUAGE_BLOCKED", "Workflow step text must not claim approval, execution, policy satisfaction, source apply, memory promotion, release approval, workflow continuation, or authority transfer.", field);
    }

    private static void RejectAuthorityFlag(bool value, string field, List<WorkflowRunValidationIssue> issues)
    {
        if (value)
            AddError(issues, "WORKFLOW_STEP_AUTHORITY_FLAG_BLOCKED", "Workflow step authority/action flags must be false.", field);
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
