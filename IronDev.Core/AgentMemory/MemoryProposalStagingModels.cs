using System.Text.Json;

namespace IronDev.Core.AgentMemory;

public enum MemoryProposalType
{
    ProjectFactCandidate,
    ProjectDecisionCandidate,
    ProjectCorrectionCandidate,
    ProjectRiskCandidate,
    ProjectConstraintCandidate,
    ProjectConventionCandidate,
    AgentLocalMemoryCandidate,
    EngineeringPatternCandidate,
    FailureModeCandidate,
    DebuggingLessonCandidate,
    PortableEngineeringMemoryCandidate,
    DeprecationCandidate,
    DuplicateCandidate,
    ClarificationNeededCandidate
}

public enum MemoryProposalTargetScope
{
    ProjectLocalCandidate,
    AgentLocalCandidate,
    PortableEngineeringMemoryCandidate,
    RequiresTriage
}

public enum MemoryProposalStatus
{
    Staged,
    ReadyForReview,
    NeedsEvidence,
    NeedsClarification,
    Quarantined,
    DuplicateCandidate,
    Superseded,
    Withdrawn
}

public enum MemoryProposalConfidentialityLabel
{
    ProjectConfidential,
    AgentLocal,
    ContainsSensitiveProjectDetail,
    ContainsExternalConfidentialDetail,
    PortableCandidateRequiresSanitization,
    SanitizedCandidateForReview,
    UnknownRequiresReview
}

public enum MemoryProposalSanitizationStatus
{
    NotApplicable,
    RequiresReview,
    RequiresSanitization,
    SanitizedCandidate,
    Quarantined
}

public enum MemoryProposalEvidenceType
{
    GovernanceEvent,
    ToolRequest,
    ToolGateDecision,
    ApprovalDecision,
    PolicyDecisionEvent,
    DogfoodReceipt,
    WorkflowRun,
    WorkflowRunStep,
    WorkflowCheckpoint,
    Handoff,
    ThoughtLedgerReference,
    GroundingReference,
    CriticReview,
    ValidationOutput,
    HumanNote,
    RunReport,
    TestFailure,
    BuildFailure,
    SourceReport,
    FailurePackage
}

public enum MemoryProposalEvidenceAllowedUse
{
    Context,
    Review,
    Debugging,
    Validation,
    Traceability,
    HumanDecisionSupport,
    AuditReference,
    PolicyInput,
    HandoffExplanation,
    RequirementEvaluation,
    Grounding,
    MemoryProposalReview
}

public enum MemoryProposalGroundingClaimType
{
    EvidenceSupport,
    RequirementTrace,
    DecisionTrace,
    HandoffTrace,
    PolicyTrace,
    ValidationTrace,
    WorkflowTrace,
    MemoryProposalTrace
}

public enum MemoryProposalWorkflowReferenceType
{
    Origin,
    RelatedRun,
    RelatedStep,
    RelatedCheckpoint,
    GeneratedFrom,
    SupportsReview,
    Traceability
}

public sealed class MemoryProposalEvidenceReferenceCreateRequest
{
    public Guid? MemoryProposalEvidenceReferenceId { get; init; }
    public MemoryProposalEvidenceType EvidenceType { get; init; }
    public string EvidenceId { get; init; } = string.Empty;
    public string? EvidenceLabel { get; init; }
    public string? SafeSummary { get; init; }
    public MemoryProposalEvidenceAllowedUse? AllowedUse { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public Guid? WorkflowRunEvidenceReferenceId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public Guid? HandoffId { get; init; }
    public Guid? ThoughtLedgerEntryId { get; init; }
}

public sealed class MemoryProposalEvidenceReference
{
    public Guid MemoryProposalEvidenceReferenceId { get; init; }
    public Guid MemoryProposalId { get; init; }
    public Guid ProjectId { get; init; }
    public MemoryProposalEvidenceType EvidenceType { get; init; }
    public string EvidenceId { get; init; } = string.Empty;
    public string? EvidenceLabel { get; init; }
    public string? SafeSummary { get; init; }
    public MemoryProposalEvidenceAllowedUse? AllowedUse { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public Guid? WorkflowRunEvidenceReferenceId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public Guid? HandoffId { get; init; }
    public Guid? ThoughtLedgerEntryId { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed class MemoryProposalGroundingReferenceCreateRequest
{
    public Guid? MemoryProposalGroundingReferenceId { get; init; }
    public Guid GroundingReferenceId { get; init; }
    public MemoryProposalGroundingClaimType ClaimType { get; init; }
    public string ClaimId { get; init; } = string.Empty;
    public string? SafeSummary { get; init; }
}

public sealed class MemoryProposalGroundingReference
{
    public Guid MemoryProposalGroundingReferenceId { get; init; }
    public Guid MemoryProposalId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid GroundingReferenceId { get; init; }
    public MemoryProposalGroundingClaimType ClaimType { get; init; }
    public string ClaimId { get; init; } = string.Empty;
    public string? SafeSummary { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed class MemoryProposalWorkflowReferenceCreateRequest
{
    public Guid? MemoryProposalWorkflowReferenceId { get; init; }
    public Guid? WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public MemoryProposalWorkflowReferenceType ReferenceType { get; init; }
    public string? SafeSummary { get; init; }
}

public sealed class MemoryProposalWorkflowReference
{
    public Guid MemoryProposalWorkflowReferenceId { get; init; }
    public Guid MemoryProposalId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid? WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public MemoryProposalWorkflowReferenceType ReferenceType { get; init; }
    public string? SafeSummary { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed class MemoryProposalCreateRequest
{
    public Guid? MemoryProposalId { get; init; }
    public Guid? TenantId { get; init; }
    public Guid ProjectId { get; init; }
    public string ProposalKey { get; init; } = string.Empty;
    public MemoryProposalType ProposalType { get; init; }
    public MemoryProposalTargetScope TargetMemoryScope { get; init; }
    public MemoryProposalStatus ProposalStatus { get; init; } = MemoryProposalStatus.Staged;
    public string SourceType { get; init; } = string.Empty;
    public string? SourceId { get; init; }
    public string? SourceAgentRole { get; init; }
    public string? SourceAgentId { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string SafeProposedMemory { get; init; } = string.Empty;
    public string? SafeRationaleSummary { get; init; }
    public string? SafeRiskSummary { get; init; }
    public string? ConfidenceLabel { get; init; }
    public MemoryProposalConfidentialityLabel ConfidentialityLabel { get; init; } = MemoryProposalConfidentialityLabel.UnknownRequiresReview;
    public MemoryProposalSanitizationStatus SanitizationStatus { get; init; } = MemoryProposalSanitizationStatus.RequiresReview;
    public Guid? WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public string CreatedByActorType { get; init; } = string.Empty;
    public string CreatedByActorId { get; init; } = string.Empty;
    public int MetadataVersion { get; init; } = 1;
    public string MetadataJson { get; init; } = "{}";
    public bool IsAcceptedMemory { get; init; }
    public bool CreatesAcceptedMemory { get; init; }
    public bool PromotesMemory { get; init; }
    public bool WritesCollectiveMemory { get; init; }
    public bool WritesAgentMemory { get; init; }
    public bool WritesVectorIndex { get; init; }
    public bool IsRetrievalAuthority { get; init; }
    public bool IsPolicy { get; init; }
    public bool IsApproval { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool GrantsApproval { get; init; }
    public bool GrantsExecution { get; init; }
    public bool StartsWorkflow { get; init; }
    public bool ContinuesWorkflow { get; init; }
    public bool MutatesSource { get; init; }
    public bool ApprovesRelease { get; init; }
    public IReadOnlyList<MemoryProposalEvidenceReferenceCreateRequest> EvidenceReferences { get; init; } = Array.Empty<MemoryProposalEvidenceReferenceCreateRequest>();
    public IReadOnlyList<MemoryProposalGroundingReferenceCreateRequest> GroundingReferences { get; init; } = Array.Empty<MemoryProposalGroundingReferenceCreateRequest>();
    public IReadOnlyList<MemoryProposalWorkflowReferenceCreateRequest> WorkflowReferences { get; init; } = Array.Empty<MemoryProposalWorkflowReferenceCreateRequest>();
}

public sealed class MemoryProposal
{
    public Guid MemoryProposalId { get; init; }
    public Guid? TenantId { get; init; }
    public Guid ProjectId { get; init; }
    public string ProposalKey { get; init; } = string.Empty;
    public MemoryProposalType ProposalType { get; init; }
    public MemoryProposalTargetScope TargetMemoryScope { get; init; }
    public MemoryProposalStatus ProposalStatus { get; init; }
    public string SourceType { get; init; } = string.Empty;
    public string? SourceId { get; init; }
    public string? SourceAgentRole { get; init; }
    public string? SourceAgentId { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string SafeProposedMemory { get; init; } = string.Empty;
    public string? SafeRationaleSummary { get; init; }
    public string? SafeRiskSummary { get; init; }
    public string? ConfidenceLabel { get; init; }
    public MemoryProposalConfidentialityLabel ConfidentialityLabel { get; init; }
    public MemoryProposalSanitizationStatus SanitizationStatus { get; init; }
    public Guid? WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public string CreatedByActorType { get; init; } = string.Empty;
    public string CreatedByActorId { get; init; } = string.Empty;
    public int MetadataVersion { get; init; }
    public string MetadataJson { get; init; } = "{}";
    public bool IsAcceptedMemory { get; init; }
    public bool CreatesAcceptedMemory { get; init; }
    public bool PromotesMemory { get; init; }
    public bool WritesCollectiveMemory { get; init; }
    public bool WritesAgentMemory { get; init; }
    public bool WritesVectorIndex { get; init; }
    public bool IsRetrievalAuthority { get; init; }
    public bool IsPolicy { get; init; }
    public bool IsApproval { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool GrantsApproval { get; init; }
    public bool GrantsExecution { get; init; }
    public bool StartsWorkflow { get; init; }
    public bool ContinuesWorkflow { get; init; }
    public bool MutatesSource { get; init; }
    public bool ApprovesRelease { get; init; }
    public IReadOnlyList<MemoryProposalEvidenceReference> EvidenceReferences { get; init; } = Array.Empty<MemoryProposalEvidenceReference>();
    public IReadOnlyList<MemoryProposalGroundingReference> GroundingReferences { get; init; } = Array.Empty<MemoryProposalGroundingReference>();
    public IReadOnlyList<MemoryProposalWorkflowReference> WorkflowReferences { get; init; } = Array.Empty<MemoryProposalWorkflowReference>();
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed class MemoryProposalSummary
{
    public Guid MemoryProposalId { get; init; }
    public Guid ProjectId { get; init; }
    public string ProposalKey { get; init; } = string.Empty;
    public MemoryProposalType ProposalType { get; init; }
    public MemoryProposalTargetScope TargetMemoryScope { get; init; }
    public MemoryProposalStatus ProposalStatus { get; init; }
    public string SourceType { get; init; } = string.Empty;
    public string? SourceId { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string SafeProposedMemory { get; init; } = string.Empty;
    public Guid? WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public Guid? CorrelationId { get; init; }
    public int EvidenceReferenceCount { get; init; }
    public int GroundingReferenceCount { get; init; }
    public int WorkflowReferenceCount { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed record MemoryProposalValidationIssue(string Code, string Message);

public sealed class MemoryProposalValidationResult
{
    public IReadOnlyList<MemoryProposalValidationIssue> Issues { get; init; } = Array.Empty<MemoryProposalValidationIssue>();
    public bool IsValid => Issues.Count == 0;
}

public interface IMemoryProposalStagingStore
{
    Task<MemoryProposal> CreateAsync(MemoryProposalCreateRequest request, CancellationToken cancellationToken = default);
    Task<MemoryProposal?> GetAsync(Guid projectId, Guid memoryProposalId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryProposalSummary>> ListByProjectAsync(Guid projectId, int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryProposalSummary>> ListByStatusAsync(Guid projectId, MemoryProposalStatus status, int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryProposalSummary>> ListByWorkflowRunAsync(Guid projectId, Guid workflowRunId, int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MemoryProposalSummary>> ListBySourceAsync(Guid projectId, string sourceType, string sourceId, int take, CancellationToken cancellationToken = default);
}

public sealed class MemoryProposalValidator
{
    private static readonly string[] UnsafeMarkers =
    {
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
        "entirepatch",
        "entire patch",
        "approval granted",
        "approved for execution",
        "execution permission",
        "execution allowed",
        "can execute",
        "authorize execution",
        "policy satisfied",
        "satisfy policy",
        "accepted memory",
        "memory accepted",
        "memory promoted",
        "promote memory",
        "collective memory created",
        "write collective memory",
        "write vector index",
        "retrieval authority",
        "source applied",
        "apply source",
        "apply patch",
        "release approved",
        "approve release",
        "ready to ship",
        "can ship",
        "workflow continued",
        "continue workflow",
        "workflow started",
        "start workflow",
        "authority transferred",
        "transfer authority"
    };

    public MemoryProposalValidationResult ValidateCreate(MemoryProposalCreateRequest request)
    {
        var issues = new List<MemoryProposalValidationIssue>();

        if (request.ProjectId == Guid.Empty) Add(issues, "project_id_required", "ProjectId is required.");
        if (request.MemoryProposalId == Guid.Empty) Add(issues, "memory_proposal_id_empty", "MemoryProposalId cannot be empty when supplied.");
        if (string.IsNullOrWhiteSpace(request.ProposalKey)) Add(issues, "proposal_key_required", "ProposalKey is required.");
        if (string.IsNullOrWhiteSpace(request.SourceType)) Add(issues, "source_type_required", "SourceType is required.");
        if (string.IsNullOrWhiteSpace(request.SafeProposedMemory)) Add(issues, "safe_proposed_memory_required", "SafeProposedMemory is required.");
        if (string.IsNullOrWhiteSpace(request.CreatedByActorType)) Add(issues, "created_by_actor_type_required", "CreatedByActorType is required.");
        if (string.IsNullOrWhiteSpace(request.CreatedByActorId)) Add(issues, "created_by_actor_id_required", "CreatedByActorId is required.");
        if (request.MetadataVersion <= 0) Add(issues, "metadata_version_invalid", "MetadataVersion must be positive.");
        if (!IsJson(request.MetadataJson)) Add(issues, "metadata_json_invalid", "MetadataJson must be valid JSON.");

        ValidateEnum(request.ProposalType, nameof(request.ProposalType), issues);
        ValidateEnum(request.TargetMemoryScope, nameof(request.TargetMemoryScope), issues);
        ValidateEnum(request.ProposalStatus, nameof(request.ProposalStatus), issues);
        ValidateEnum(request.ConfidentialityLabel, nameof(request.ConfidentialityLabel), issues);
        ValidateEnum(request.SanitizationStatus, nameof(request.SanitizationStatus), issues);

        if (request.ProposalStatus is not (MemoryProposalStatus.Staged or MemoryProposalStatus.ReadyForReview or MemoryProposalStatus.NeedsEvidence or MemoryProposalStatus.NeedsClarification or MemoryProposalStatus.Quarantined or MemoryProposalStatus.DuplicateCandidate or MemoryProposalStatus.Superseded or MemoryProposalStatus.Withdrawn))
        {
            Add(issues, "proposal_status_not_stageable", "ProposalStatus must remain a staging/review state.");
        }

        if (request.ProposalType == MemoryProposalType.PortableEngineeringMemoryCandidate &&
            request.ConfidentialityLabel != MemoryProposalConfidentialityLabel.PortableCandidateRequiresSanitization &&
            request.SanitizationStatus != MemoryProposalSanitizationStatus.RequiresReview &&
            request.SanitizationStatus != MemoryProposalSanitizationStatus.RequiresSanitization &&
            request.SanitizationStatus != MemoryProposalSanitizationStatus.SanitizedCandidate)
        {
            Add(issues, "portable_candidate_requires_sanitization_review", "Portable engineering memory candidates require sanitization review metadata.");
        }

        if (HasAuthorityFlag(request))
        {
            Add(issues, "authority_flags_forbidden", "Memory proposal staging cannot grant approval, execution, policy satisfaction, workflow progress, source mutation, retrieval authority, accepted memory, vector indexing, or memory promotion.");
        }

        ScanText(issues, "proposal_text", request.ProposalKey, request.SourceType, request.SourceId, request.SourceAgentRole, request.SourceAgentId, request.SubjectType, request.SubjectId, request.SafeProposedMemory, request.SafeRationaleSummary, request.SafeRiskSummary, request.ConfidenceLabel, request.CreatedByActorType, request.CreatedByActorId, request.MetadataJson);

        foreach (var evidence in request.EvidenceReferences ?? Array.Empty<MemoryProposalEvidenceReferenceCreateRequest>())
        {
            if (evidence.MemoryProposalEvidenceReferenceId == Guid.Empty) Add(issues, "evidence_reference_id_empty", "Evidence reference id cannot be empty when supplied.");
            if (string.IsNullOrWhiteSpace(evidence.EvidenceId)) Add(issues, "evidence_id_required", "EvidenceId is required.");
            ValidateEnum(evidence.EvidenceType, nameof(evidence.EvidenceType), issues);
            if (evidence.AllowedUse is not null) ValidateEnum(evidence.AllowedUse.Value, nameof(evidence.AllowedUse), issues);
            ScanText(issues, "evidence_reference_text", evidence.EvidenceId, evidence.EvidenceLabel, evidence.SafeSummary);
        }

        foreach (var grounding in request.GroundingReferences ?? Array.Empty<MemoryProposalGroundingReferenceCreateRequest>())
        {
            if (grounding.MemoryProposalGroundingReferenceId == Guid.Empty) Add(issues, "grounding_reference_id_empty", "Grounding reference id cannot be empty when supplied.");
            if (grounding.GroundingReferenceId == Guid.Empty) Add(issues, "grounding_reference_id_required", "GroundingReferenceId is required.");
            if (string.IsNullOrWhiteSpace(grounding.ClaimId)) Add(issues, "grounding_claim_id_required", "Grounding ClaimId is required.");
            ValidateEnum(grounding.ClaimType, nameof(grounding.ClaimType), issues);
            ScanText(issues, "grounding_reference_text", grounding.ClaimId, grounding.SafeSummary);
        }

        foreach (var workflow in request.WorkflowReferences ?? Array.Empty<MemoryProposalWorkflowReferenceCreateRequest>())
        {
            if (workflow.MemoryProposalWorkflowReferenceId == Guid.Empty) Add(issues, "workflow_reference_id_empty", "Workflow reference id cannot be empty when supplied.");
            if (workflow.WorkflowRunId is null && workflow.WorkflowRunStepId is null && workflow.WorkflowCheckpointId is null) Add(issues, "workflow_reference_target_required", "Workflow reference must point to a run, step, or checkpoint.");
            ValidateEnum(workflow.ReferenceType, nameof(workflow.ReferenceType), issues);
            ScanText(issues, "workflow_reference_text", workflow.SafeSummary);
        }

        return new MemoryProposalValidationResult { Issues = issues };
    }

    public MemoryProposalCreateRequest Normalize(MemoryProposalCreateRequest request) => new()
    {
        MemoryProposalId = request.MemoryProposalId,
        TenantId = request.TenantId,
        ProjectId = request.ProjectId,
        ProposalKey = NormalizeRequired(request.ProposalKey),
        ProposalType = request.ProposalType,
        TargetMemoryScope = request.TargetMemoryScope,
        ProposalStatus = request.ProposalStatus,
        SourceType = NormalizeRequired(request.SourceType),
        SourceId = NormalizeOptional(request.SourceId),
        SourceAgentRole = NormalizeOptional(request.SourceAgentRole),
        SourceAgentId = NormalizeOptional(request.SourceAgentId),
        SubjectType = NormalizeOptional(request.SubjectType),
        SubjectId = NormalizeOptional(request.SubjectId),
        SafeProposedMemory = NormalizeRequired(request.SafeProposedMemory),
        SafeRationaleSummary = NormalizeOptional(request.SafeRationaleSummary),
        SafeRiskSummary = NormalizeOptional(request.SafeRiskSummary),
        ConfidenceLabel = NormalizeOptional(request.ConfidenceLabel),
        ConfidentialityLabel = request.ConfidentialityLabel,
        SanitizationStatus = request.SanitizationStatus,
        WorkflowRunId = request.WorkflowRunId,
        WorkflowRunStepId = request.WorkflowRunStepId,
        WorkflowCheckpointId = request.WorkflowCheckpointId,
        CorrelationId = request.CorrelationId,
        CausationId = request.CausationId,
        CreatedByActorType = NormalizeRequired(request.CreatedByActorType),
        CreatedByActorId = NormalizeRequired(request.CreatedByActorId),
        MetadataVersion = request.MetadataVersion,
        MetadataJson = string.IsNullOrWhiteSpace(request.MetadataJson) ? "{}" : request.MetadataJson.Trim(),
        IsAcceptedMemory = request.IsAcceptedMemory,
        CreatesAcceptedMemory = request.CreatesAcceptedMemory,
        PromotesMemory = request.PromotesMemory,
        WritesCollectiveMemory = request.WritesCollectiveMemory,
        WritesAgentMemory = request.WritesAgentMemory,
        WritesVectorIndex = request.WritesVectorIndex,
        IsRetrievalAuthority = request.IsRetrievalAuthority,
        IsPolicy = request.IsPolicy,
        IsApproval = request.IsApproval,
        SatisfiesPolicy = request.SatisfiesPolicy,
        GrantsApproval = request.GrantsApproval,
        GrantsExecution = request.GrantsExecution,
        StartsWorkflow = request.StartsWorkflow,
        ContinuesWorkflow = request.ContinuesWorkflow,
        MutatesSource = request.MutatesSource,
        ApprovesRelease = request.ApprovesRelease,
        EvidenceReferences = (request.EvidenceReferences ?? Array.Empty<MemoryProposalEvidenceReferenceCreateRequest>()).Select(e => new MemoryProposalEvidenceReferenceCreateRequest
        {
            MemoryProposalEvidenceReferenceId = e.MemoryProposalEvidenceReferenceId,
            EvidenceType = e.EvidenceType,
            EvidenceId = NormalizeRequired(e.EvidenceId),
            EvidenceLabel = NormalizeOptional(e.EvidenceLabel),
            SafeSummary = NormalizeOptional(e.SafeSummary),
            AllowedUse = e.AllowedUse,
            GovernanceEventId = e.GovernanceEventId,
            WorkflowRunEvidenceReferenceId = e.WorkflowRunEvidenceReferenceId,
            WorkflowRunStepId = e.WorkflowRunStepId,
            WorkflowCheckpointId = e.WorkflowCheckpointId,
            HandoffId = e.HandoffId,
            ThoughtLedgerEntryId = e.ThoughtLedgerEntryId
        }).ToList(),
        GroundingReferences = (request.GroundingReferences ?? Array.Empty<MemoryProposalGroundingReferenceCreateRequest>()).Select(g => new MemoryProposalGroundingReferenceCreateRequest
        {
            MemoryProposalGroundingReferenceId = g.MemoryProposalGroundingReferenceId,
            GroundingReferenceId = g.GroundingReferenceId,
            ClaimType = g.ClaimType,
            ClaimId = NormalizeRequired(g.ClaimId),
            SafeSummary = NormalizeOptional(g.SafeSummary)
        }).ToList(),
        WorkflowReferences = (request.WorkflowReferences ?? Array.Empty<MemoryProposalWorkflowReferenceCreateRequest>()).Select(w => new MemoryProposalWorkflowReferenceCreateRequest
        {
            MemoryProposalWorkflowReferenceId = w.MemoryProposalWorkflowReferenceId,
            WorkflowRunId = w.WorkflowRunId,
            WorkflowRunStepId = w.WorkflowRunStepId,
            WorkflowCheckpointId = w.WorkflowCheckpointId,
            ReferenceType = w.ReferenceType,
            SafeSummary = NormalizeOptional(w.SafeSummary)
        }).ToList()
    };

    public static int NormalizeTake(int take) => Math.Clamp(take, 1, 500);

    private static bool HasAuthorityFlag(MemoryProposalCreateRequest request) =>
        request.IsAcceptedMemory ||
        request.CreatesAcceptedMemory ||
        request.PromotesMemory ||
        request.WritesCollectiveMemory ||
        request.WritesAgentMemory ||
        request.WritesVectorIndex ||
        request.IsRetrievalAuthority ||
        request.IsPolicy ||
        request.IsApproval ||
        request.SatisfiesPolicy ||
        request.GrantsApproval ||
        request.GrantsExecution ||
        request.StartsWorkflow ||
        request.ContinuesWorkflow ||
        request.MutatesSource ||
        request.ApprovesRelease;

    private static void ValidateEnum<T>(T value, string name, ICollection<MemoryProposalValidationIssue> issues)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value)) Add(issues, "invalid_" + ToSnakeCase(name), $"{name} is invalid.");
    }

    private static void ScanText(ICollection<MemoryProposalValidationIssue> issues, string code, params string?[] values)
    {
        var text = string.Join(' ', values.Where(v => !string.IsNullOrWhiteSpace(v))).ToLowerInvariant();
        if (UnsafeMarkers.Any(text.Contains)) Add(issues, code + "_unsafe", "Memory proposal staging text contains raw/private reasoning or authority language.");
    }

    private static bool IsJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string NormalizeRequired(string value) => value.Trim();
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ToSnakeCase(string value) => string.Concat(value.Select((ch, index) => index > 0 && char.IsUpper(ch) ? "_" + char.ToLowerInvariant(ch) : char.ToLowerInvariant(ch).ToString()));
    private static void Add(ICollection<MemoryProposalValidationIssue> issues, string code, string message) => issues.Add(new MemoryProposalValidationIssue(code, message));
}
