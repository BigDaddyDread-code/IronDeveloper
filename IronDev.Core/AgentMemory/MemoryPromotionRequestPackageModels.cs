using System.Text.Json;

namespace IronDev.Core.AgentMemory;

public enum MemoryPromotionRequestPackageStatus
{
    Assembled,
    ReadyForReview,
    NeedsEvidence,
    NeedsHumanReview,
    RequiresApprovalReview,
    RequiresSanitizationReview,
    ContainsDuplicateRisk,
    ContainsStaleRisk,
    ContainsConflictRisk,
    Quarantined,
    Superseded,
    Withdrawn
}

public enum MemoryPromotionRequestPurpose
{
    HumanPromotionReview,
    GovernedPromotionReview,
    ProjectMemoryReview,
    AgentLocalMemoryReview,
    PortableEngineeringMemoryReview,
    SanitizationReview,
    RiskReview,
    EvidenceCompletenessReview
}

public enum MemoryPromotionRequestedTargetMemoryScope
{
    ProjectLocalCandidateForPromotion,
    AgentLocalCandidateForPromotion,
    PortableEngineeringMemoryCandidateForPromotion,
    RequiresTriage
}

public sealed class MemoryPromotionRequestEvidenceReference
{
    public string EvidenceType { get; init; } = string.Empty;
    public string EvidenceId { get; init; } = string.Empty;
    public string? EvidenceLabel { get; init; }
    public string? SafeSummary { get; init; }
    public string? AllowedUse { get; init; }
    public Guid? MemoryProposalId { get; init; }
    public Guid? MemoryProposalEvidencePackageId { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public Guid? WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public bool EvidenceIsDecision { get; init; }
    public bool EvidenceGrantsApproval { get; init; }
    public bool EvidenceSatisfiesPolicy { get; init; }
    public bool EvidenceAcceptsMemory { get; init; }
    public bool EvidencePromotesMemory { get; init; }
}

public sealed class MemoryPromotionRequestGroundingReference
{
    public Guid GroundingEvidenceReferenceId { get; init; }
    public string ClaimType { get; init; } = string.Empty;
    public string ClaimId { get; init; } = string.Empty;
    public string? SafeSummary { get; init; }
    public bool GroundingIsAuthority { get; init; }
    public bool GroundingAcceptsMemory { get; init; }
    public bool GroundingPromotesMemory { get; init; }
}

public sealed class MemoryPromotionRequestSignalReference
{
    public string SignalType { get; init; } = string.Empty;
    public Guid SignalId { get; init; }
    public string SafeSummary { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public bool SignalIsDecision { get; init; }
    public bool SignalBlocksPromotion { get; init; }
    public bool SignalAllowsPromotion { get; init; }
    public bool SignalAcceptsMemory { get; init; }
    public bool SignalPromotesMemory { get; init; }
}

public sealed class MemoryPromotionApprovalRequirementReference
{
    public string RequirementType { get; init; } = string.Empty;
    public string RequirementId { get; init; } = string.Empty;
    public string SafeSummary { get; init; } = string.Empty;
    public bool RequirementIsApproval { get; init; }
    public bool RequirementSatisfiesPolicy { get; init; }
    public bool RequirementAllowsPromotion { get; init; }
}

public sealed class MemoryPromotionRequestReviewNote
{
    public string NoteType { get; init; } = string.Empty;
    public string SafeSummary { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public bool NoteIsDecision { get; init; }
    public bool NoteGrantsApproval { get; init; }
    public bool NoteAcceptsMemory { get; init; }
    public bool NoteRejectsMemory { get; init; }
    public bool NotePromotesMemory { get; init; }
}

public sealed class MemoryPromotionRequestPackageCreateRequest
{
    public Guid MemoryPromotionRequestPackageId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid MemoryProposalId { get; init; }
    public string PromotionRequestKey { get; init; } = string.Empty;
    public MemoryPromotionRequestPackageStatus Status { get; init; } = MemoryPromotionRequestPackageStatus.Assembled;
    public MemoryPromotionRequestPurpose Purpose { get; init; } = MemoryPromotionRequestPurpose.HumanPromotionReview;
    public string ProposalType { get; init; } = string.Empty;
    public string CurrentProposalStatus { get; init; } = string.Empty;
    public MemoryPromotionRequestedTargetMemoryScope RequestedTargetMemoryScope { get; init; } = MemoryPromotionRequestedTargetMemoryScope.RequiresTriage;
    public string SafeProposedMemory { get; init; } = string.Empty;
    public string? SafePromotionRationale { get; init; }
    public string? SafeRiskSummary { get; init; }
    public string? SafeSanitizationSummary { get; init; }
    public string? SafeReviewerInstructions { get; init; }
    public string ConfidentialityLabel { get; init; } = string.Empty;
    public string SanitizationStatus { get; init; } = string.Empty;
    public IReadOnlyList<MemoryPromotionRequestEvidenceReference> EvidenceReferences { get; init; } = Array.Empty<MemoryPromotionRequestEvidenceReference>();
    public IReadOnlyList<MemoryPromotionRequestGroundingReference> GroundingReferences { get; init; } = Array.Empty<MemoryPromotionRequestGroundingReference>();
    public IReadOnlyList<MemoryPromotionRequestSignalReference> SignalReferences { get; init; } = Array.Empty<MemoryPromotionRequestSignalReference>();
    public IReadOnlyList<MemoryPromotionApprovalRequirementReference> ApprovalRequirementReferences { get; init; } = Array.Empty<MemoryPromotionApprovalRequirementReference>();
    public IReadOnlyList<MemoryPromotionRequestReviewNote> ReviewNotes { get; init; } = Array.Empty<MemoryPromotionRequestReviewNote>();
    public Guid? WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public string CreatedByActorType { get; init; } = string.Empty;
    public string CreatedByActorId { get; init; } = string.Empty;
    public int MetadataVersion { get; init; } = 1;
    public string MetadataJson { get; init; } = "{}";
    public bool IsDecision { get; init; }
    public bool GrantsApproval { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool AcceptsMemory { get; init; }
    public bool RejectsMemory { get; init; }
    public bool PromotesMemory { get; init; }
    public bool CreatesAcceptedMemory { get; init; }
    public bool CreatesPortableMemory { get; init; }
    public bool ActivatesRetrieval { get; init; }
    public bool CreatesEmbedding { get; init; }
    public bool WritesVectorStore { get; init; }
    public bool TransfersAuthority { get; init; }
    public bool MutatesSource { get; init; }
    public bool ApprovesRelease { get; init; }
}

public sealed class MemoryPromotionRequestPackage
{
    public Guid MemoryPromotionRequestPackageId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid MemoryProposalId { get; init; }
    public string PromotionRequestKey { get; init; } = string.Empty;
    public MemoryPromotionRequestPackageStatus Status { get; init; }
    public MemoryPromotionRequestPurpose Purpose { get; init; }
    public string ProposalType { get; init; } = string.Empty;
    public string CurrentProposalStatus { get; init; } = string.Empty;
    public MemoryPromotionRequestedTargetMemoryScope RequestedTargetMemoryScope { get; init; }
    public string SafeProposedMemory { get; init; } = string.Empty;
    public string? SafePromotionRationale { get; init; }
    public string? SafeRiskSummary { get; init; }
    public string? SafeSanitizationSummary { get; init; }
    public string? SafeReviewerInstructions { get; init; }
    public string ConfidentialityLabel { get; init; } = string.Empty;
    public string SanitizationStatus { get; init; } = string.Empty;
    public IReadOnlyList<MemoryPromotionRequestEvidenceReference> EvidenceReferences { get; init; } = Array.Empty<MemoryPromotionRequestEvidenceReference>();
    public IReadOnlyList<MemoryPromotionRequestGroundingReference> GroundingReferences { get; init; } = Array.Empty<MemoryPromotionRequestGroundingReference>();
    public IReadOnlyList<MemoryPromotionRequestSignalReference> SignalReferences { get; init; } = Array.Empty<MemoryPromotionRequestSignalReference>();
    public IReadOnlyList<MemoryPromotionApprovalRequirementReference> ApprovalRequirementReferences { get; init; } = Array.Empty<MemoryPromotionApprovalRequirementReference>();
    public IReadOnlyList<MemoryPromotionRequestReviewNote> ReviewNotes { get; init; } = Array.Empty<MemoryPromotionRequestReviewNote>();
    public Guid? WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public string CreatedByActorType { get; init; } = string.Empty;
    public string CreatedByActorId { get; init; } = string.Empty;
    public int MetadataVersion { get; init; }
    public string MetadataJson { get; init; } = "{}";
    public bool IsDecision { get; init; }
    public bool GrantsApproval { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool AcceptsMemory { get; init; }
    public bool RejectsMemory { get; init; }
    public bool PromotesMemory { get; init; }
    public bool CreatesAcceptedMemory { get; init; }
    public bool CreatesPortableMemory { get; init; }
    public bool ActivatesRetrieval { get; init; }
    public bool CreatesEmbedding { get; init; }
    public bool WritesVectorStore { get; init; }
    public bool TransfersAuthority { get; init; }
    public bool MutatesSource { get; init; }
    public bool ApprovesRelease { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed class MemoryPromotionRequestPackageSummary
{
    public Guid MemoryPromotionRequestPackageId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid MemoryProposalId { get; init; }
    public string PromotionRequestKey { get; init; } = string.Empty;
    public MemoryPromotionRequestPackageStatus Status { get; init; }
    public MemoryPromotionRequestPurpose Purpose { get; init; }
    public MemoryPromotionRequestedTargetMemoryScope RequestedTargetMemoryScope { get; init; }
    public int EvidenceReferenceCount { get; init; }
    public int GroundingReferenceCount { get; init; }
    public int SignalReferenceCount { get; init; }
    public int ApprovalRequirementCount { get; init; }
    public int ReviewNoteCount { get; init; }
}

public sealed class MemoryPromotionRequestPackageValidator
{
    private static readonly HashSet<string> AllowedSignalTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DuplicateCandidate",
        "StaleCandidate",
        "ConflictCandidate",
        "CrossRunPatternCandidate",
        "EvidenceGap",
        "SanitizationRisk",
        "ConfidentialityRisk"
    };

    private static readonly HashSet<string> AllowedReviewNoteTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PromotionRationale",
        "EvidenceSummary",
        "GroundingSummary",
        "DuplicateRisk",
        "StaleRisk",
        "ConflictRisk",
        "PatternSignal",
        "ConfidentialityRisk",
        "SanitizationNeeded",
        "HumanReviewNeeded",
        "GovernedApprovalNeeded",
        "MoreEvidenceNeeded"
    };

    private static readonly string[] UnsafeMarkers =
    {
        "hiddenreasoning",
        "hidden reasoning",
        "chainofthought",
        "chain of thought",
        "chain-of-thought",
        "private reasoning",
        "scratchpad",
        "rawprompt",
        "raw prompt",
        "rawcompletion",
        "raw completion",
        "rawtooloutput",
        "raw tool output",
        "entirepatch",
        "entire patch",
        "accepted memory",
        "approved memory",
        "promoted memory",
        "active memory",
        "memory accepted",
        "memory promoted",
        "promotion approved",
        "promotion allowed",
        "retrieval active",
        "indexed for retrieval",
        "embedding created",
        "vector write",
        "weaviate write",
        "portable memory approved",
        "portable engineering memory accepted",
        "approval granted",
        "approval satisfied",
        "policy satisfied",
        "truth confirmed",
        "global memory",
        "cross-project approved",
        "authority transferred",
        "source applied",
        "release approved"
    };

    public MemoryProposalValidationResult Validate(MemoryPromotionRequestPackageCreateRequest? request)
    {
        var issues = new List<MemoryProposalValidationIssue>();
        if (request is null)
        {
            Add(issues, "request_required", "Memory promotion request package request is required.");
            return new MemoryProposalValidationResult { Issues = issues };
        }

        if (request.MemoryPromotionRequestPackageId == Guid.Empty) Add(issues, "memory_promotion_request_package_id_required", "MemoryPromotionRequestPackageId is required.");
        if (request.ProjectId == Guid.Empty) Add(issues, "project_id_required", "ProjectId is required.");
        if (request.MemoryProposalId == Guid.Empty) Add(issues, "memory_proposal_id_required", "MemoryProposalId is required.");
        if (string.IsNullOrWhiteSpace(request.PromotionRequestKey)) Add(issues, "promotion_request_key_required", "PromotionRequestKey is required.");
        if (string.IsNullOrWhiteSpace(request.SafeProposedMemory)) Add(issues, "safe_proposed_memory_required", "SafeProposedMemory is required.");
        if (string.IsNullOrWhiteSpace(request.CreatedByActorType)) Add(issues, "created_by_actor_type_required", "CreatedByActorType is required.");
        if (string.IsNullOrWhiteSpace(request.CreatedByActorId)) Add(issues, "created_by_actor_id_required", "CreatedByActorId is required.");
        if (request.MetadataVersion <= 0) Add(issues, "metadata_version_invalid", "MetadataVersion must be positive.");
        if (!IsJson(request.MetadataJson)) Add(issues, "metadata_json_invalid", "MetadataJson must be valid JSON.");

        ValidateEnum(request.Status, "status_forbidden", issues);
        ValidateEnum(request.Purpose, "purpose_forbidden", issues);
        ValidateEnum(request.RequestedTargetMemoryScope, "target_scope_forbidden", issues);

        if (HasAuthorityFlag(request))
        {
            Add(issues, "authority_flags_forbidden", "Memory promotion request packages cannot decide, approve, satisfy policy, accept, reject, promote, create memory, activate retrieval, create embeddings, write vector storage, transfer authority, mutate source, or approve release.");
        }

        ScanText(issues, "promotion_request_package_text_unsafe", request.PromotionRequestKey, request.ProposalType, request.CurrentProposalStatus, request.RequestedTargetMemoryScope.ToString(), request.SafeProposedMemory, request.SafePromotionRationale, request.SafeRiskSummary, request.SafeSanitizationSummary, request.SafeReviewerInstructions, request.ConfidentialityLabel, request.SanitizationStatus, request.CreatedByActorType, request.CreatedByActorId, request.MetadataJson);

        foreach (var evidence in request.EvidenceReferences ?? Array.Empty<MemoryPromotionRequestEvidenceReference>())
        {
            if (string.IsNullOrWhiteSpace(evidence.EvidenceType)) Add(issues, "evidence_type_required", "EvidenceType is required.");
            if (string.IsNullOrWhiteSpace(evidence.EvidenceId)) Add(issues, "evidence_id_required", "EvidenceId is required.");
            if (evidence.EvidenceIsDecision || evidence.EvidenceGrantsApproval || evidence.EvidenceSatisfiesPolicy || evidence.EvidenceAcceptsMemory || evidence.EvidencePromotesMemory)
            {
                Add(issues, "evidence_authority_forbidden", "Evidence references cannot be decisions, approval, policy satisfaction, memory acceptance, or memory promotion.");
            }

            ScanText(issues, "evidence_reference_text_unsafe", evidence.EvidenceType, evidence.EvidenceId, evidence.EvidenceLabel, evidence.SafeSummary, evidence.AllowedUse);
        }

        foreach (var grounding in request.GroundingReferences ?? Array.Empty<MemoryPromotionRequestGroundingReference>())
        {
            if (grounding.GroundingEvidenceReferenceId == Guid.Empty) Add(issues, "grounding_reference_id_required", "GroundingEvidenceReferenceId is required.");
            if (string.IsNullOrWhiteSpace(grounding.ClaimType)) Add(issues, "grounding_claim_type_required", "Grounding ClaimType is required.");
            if (string.IsNullOrWhiteSpace(grounding.ClaimId)) Add(issues, "grounding_claim_id_required", "Grounding ClaimId is required.");
            if (grounding.GroundingIsAuthority || grounding.GroundingAcceptsMemory || grounding.GroundingPromotesMemory)
            {
                Add(issues, "grounding_authority_forbidden", "Grounding references cannot be authority, memory acceptance, or memory promotion.");
            }

            ScanText(issues, "grounding_reference_text_unsafe", grounding.ClaimType, grounding.ClaimId, grounding.SafeSummary);
        }

        foreach (var signal in request.SignalReferences ?? Array.Empty<MemoryPromotionRequestSignalReference>())
        {
            if (!AllowedSignalTypes.Contains(signal.SignalType)) Add(issues, "signal_type_forbidden", "SignalType must be a bounded review signal type.");
            if (signal.SignalId == Guid.Empty) Add(issues, "signal_id_required", "SignalId is required.");
            if (string.IsNullOrWhiteSpace(signal.SafeSummary)) Add(issues, "signal_summary_required", "Signal SafeSummary is required.");
            if (string.IsNullOrWhiteSpace(signal.Severity)) Add(issues, "signal_severity_required", "Signal Severity is required.");
            if (signal.SignalIsDecision || signal.SignalBlocksPromotion || signal.SignalAllowsPromotion || signal.SignalAcceptsMemory || signal.SignalPromotesMemory)
            {
                Add(issues, "signal_authority_forbidden", "Signal references cannot decide, block, allow, accept, or promote memory.");
            }

            ScanText(issues, "signal_reference_text_unsafe", signal.SignalType, signal.SafeSummary, signal.Severity);
        }

        foreach (var requirement in request.ApprovalRequirementReferences ?? Array.Empty<MemoryPromotionApprovalRequirementReference>())
        {
            if (string.IsNullOrWhiteSpace(requirement.RequirementType)) Add(issues, "approval_requirement_type_required", "Approval requirement type is required.");
            if (string.IsNullOrWhiteSpace(requirement.RequirementId)) Add(issues, "approval_requirement_id_required", "Approval requirement id is required.");
            if (string.IsNullOrWhiteSpace(requirement.SafeSummary)) Add(issues, "approval_requirement_summary_required", "Approval requirement SafeSummary is required.");
            if (requirement.RequirementIsApproval || requirement.RequirementSatisfiesPolicy || requirement.RequirementAllowsPromotion)
            {
                Add(issues, "approval_requirement_authority_forbidden", "Approval requirement references cannot be approval, policy satisfaction, or promotion permission.");
            }

            ScanText(issues, "approval_requirement_text_unsafe", requirement.RequirementType, requirement.RequirementId, requirement.SafeSummary);
        }

        foreach (var note in request.ReviewNotes ?? Array.Empty<MemoryPromotionRequestReviewNote>())
        {
            if (!AllowedReviewNoteTypes.Contains(note.NoteType)) Add(issues, "review_note_type_forbidden", "Review note type must be bounded to promotion rationale, evidence, grounding, risk, signal, sanitization, human review, governed approval, or evidence gap notes.");
            if (string.IsNullOrWhiteSpace(note.SafeSummary)) Add(issues, "review_note_summary_required", "Review note SafeSummary is required.");
            if (string.IsNullOrWhiteSpace(note.Severity)) Add(issues, "review_note_severity_required", "Review note Severity is required.");
            if (note.NoteIsDecision || note.NoteGrantsApproval || note.NoteAcceptsMemory || note.NoteRejectsMemory || note.NotePromotesMemory)
            {
                Add(issues, "review_note_authority_forbidden", "Review notes cannot decide, grant approval, accept, reject, or promote memory.");
            }

            ScanText(issues, "review_note_text_unsafe", note.NoteType, note.SafeSummary, note.Severity);
        }

        return new MemoryProposalValidationResult { Issues = issues };
    }

    public MemoryPromotionRequestPackage Normalize(MemoryPromotionRequestPackageCreateRequest request)
    {
        var result = Validate(request);
        if (!result.IsValid)
        {
            throw new InvalidOperationException("Memory promotion request package is invalid: " + string.Join("; ", result.Issues.Select(issue => issue.Code)));
        }

        return new MemoryPromotionRequestPackage
        {
            MemoryPromotionRequestPackageId = request.MemoryPromotionRequestPackageId,
            ProjectId = request.ProjectId,
            MemoryProposalId = request.MemoryProposalId,
            PromotionRequestKey = NormalizeRequired(request.PromotionRequestKey),
            Status = request.Status,
            Purpose = request.Purpose,
            ProposalType = NormalizeRequired(request.ProposalType),
            CurrentProposalStatus = NormalizeRequired(request.CurrentProposalStatus),
            RequestedTargetMemoryScope = request.RequestedTargetMemoryScope,
            SafeProposedMemory = NormalizeRequired(request.SafeProposedMemory),
            SafePromotionRationale = NormalizeOptional(request.SafePromotionRationale),
            SafeRiskSummary = NormalizeOptional(request.SafeRiskSummary),
            SafeSanitizationSummary = NormalizeOptional(request.SafeSanitizationSummary),
            SafeReviewerInstructions = NormalizeOptional(request.SafeReviewerInstructions),
            ConfidentialityLabel = NormalizeRequired(request.ConfidentialityLabel),
            SanitizationStatus = NormalizeRequired(request.SanitizationStatus),
            EvidenceReferences = (request.EvidenceReferences ?? Array.Empty<MemoryPromotionRequestEvidenceReference>()).Select(NormalizeEvidence).ToList(),
            GroundingReferences = (request.GroundingReferences ?? Array.Empty<MemoryPromotionRequestGroundingReference>()).Select(NormalizeGrounding).ToList(),
            SignalReferences = (request.SignalReferences ?? Array.Empty<MemoryPromotionRequestSignalReference>()).Select(NormalizeSignal).ToList(),
            ApprovalRequirementReferences = (request.ApprovalRequirementReferences ?? Array.Empty<MemoryPromotionApprovalRequirementReference>()).Select(NormalizeRequirement).ToList(),
            ReviewNotes = (request.ReviewNotes ?? Array.Empty<MemoryPromotionRequestReviewNote>()).Select(NormalizeNote).ToList(),
            WorkflowRunId = request.WorkflowRunId,
            WorkflowRunStepId = request.WorkflowRunStepId,
            WorkflowCheckpointId = request.WorkflowCheckpointId,
            CorrelationId = request.CorrelationId,
            CausationId = request.CausationId,
            CreatedByActorType = NormalizeRequired(request.CreatedByActorType),
            CreatedByActorId = NormalizeRequired(request.CreatedByActorId),
            MetadataVersion = request.MetadataVersion,
            MetadataJson = string.IsNullOrWhiteSpace(request.MetadataJson) ? "{}" : request.MetadataJson.Trim(),
            CreatedUtc = DateTimeOffset.UtcNow
        };
    }

    private static MemoryPromotionRequestEvidenceReference NormalizeEvidence(MemoryPromotionRequestEvidenceReference evidence) => new()
    {
        EvidenceType = NormalizeRequired(evidence.EvidenceType),
        EvidenceId = NormalizeRequired(evidence.EvidenceId),
        EvidenceLabel = NormalizeOptional(evidence.EvidenceLabel),
        SafeSummary = NormalizeOptional(evidence.SafeSummary),
        AllowedUse = NormalizeOptional(evidence.AllowedUse),
        MemoryProposalId = evidence.MemoryProposalId,
        MemoryProposalEvidencePackageId = evidence.MemoryProposalEvidencePackageId,
        GovernanceEventId = evidence.GovernanceEventId,
        WorkflowRunId = evidence.WorkflowRunId,
        WorkflowRunStepId = evidence.WorkflowRunStepId,
        WorkflowCheckpointId = evidence.WorkflowCheckpointId
    };

    private static MemoryPromotionRequestGroundingReference NormalizeGrounding(MemoryPromotionRequestGroundingReference grounding) => new()
    {
        GroundingEvidenceReferenceId = grounding.GroundingEvidenceReferenceId,
        ClaimType = NormalizeRequired(grounding.ClaimType),
        ClaimId = NormalizeRequired(grounding.ClaimId),
        SafeSummary = NormalizeOptional(grounding.SafeSummary)
    };

    private static MemoryPromotionRequestSignalReference NormalizeSignal(MemoryPromotionRequestSignalReference signal) => new()
    {
        SignalType = NormalizeRequired(signal.SignalType),
        SignalId = signal.SignalId,
        SafeSummary = NormalizeRequired(signal.SafeSummary),
        Severity = NormalizeRequired(signal.Severity)
    };

    private static MemoryPromotionApprovalRequirementReference NormalizeRequirement(MemoryPromotionApprovalRequirementReference requirement) => new()
    {
        RequirementType = NormalizeRequired(requirement.RequirementType),
        RequirementId = NormalizeRequired(requirement.RequirementId),
        SafeSummary = NormalizeRequired(requirement.SafeSummary)
    };

    private static MemoryPromotionRequestReviewNote NormalizeNote(MemoryPromotionRequestReviewNote note) => new()
    {
        NoteType = NormalizeRequired(note.NoteType),
        SafeSummary = NormalizeRequired(note.SafeSummary),
        Severity = NormalizeRequired(note.Severity)
    };

    private static bool HasAuthorityFlag(MemoryPromotionRequestPackageCreateRequest request) =>
        request.IsDecision ||
        request.GrantsApproval ||
        request.SatisfiesPolicy ||
        request.AcceptsMemory ||
        request.RejectsMemory ||
        request.PromotesMemory ||
        request.CreatesAcceptedMemory ||
        request.CreatesPortableMemory ||
        request.ActivatesRetrieval ||
        request.CreatesEmbedding ||
        request.WritesVectorStore ||
        request.TransfersAuthority ||
        request.MutatesSource ||
        request.ApprovesRelease;

    private static void ValidateEnum<T>(T value, string code, ICollection<MemoryProposalValidationIssue> issues) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) Add(issues, code, $"{typeof(T).Name} has an unsupported value.");
    }

    private static void ScanText(ICollection<MemoryProposalValidationIssue> issues, string code, params string?[] values)
    {
        var text = string.Join(' ', values.Where(value => !string.IsNullOrWhiteSpace(value))).ToLowerInvariant();
        if (UnsafeMarkers.Any(text.Contains)) Add(issues, code, "Text contains hidden reasoning, raw material, memory authority, approval, policy, retrieval, vector, portable-approval, truth, authority-transfer, source, or release language.");
    }

    private static bool IsJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { using var _ = JsonDocument.Parse(value); return true; } catch (JsonException) { return false; }
    }

    private static string NormalizeRequired(string value) => value.Trim();
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void Add(ICollection<MemoryProposalValidationIssue> issues, string code, string message) => issues.Add(new MemoryProposalValidationIssue(code, message));
}

public sealed class MemoryPromotionRequestPackageAssembler
{
    public MemoryPromotionRequestPackageCreateRequest Assemble(
        MemoryProposal stagedProposal,
        MemoryProposalEvidencePackage evidencePackage,
        IReadOnlyList<MemoryProposalDuplicateCandidate>? duplicateCandidates = null,
        IReadOnlyList<MemoryProposalStaleCandidate>? staleCandidates = null,
        IReadOnlyList<MemoryProposalConflictCandidate>? conflictCandidates = null,
        IReadOnlyList<CrossRunMemoryPatternCandidate>? patternCandidates = null)
    {
        ArgumentNullException.ThrowIfNull(stagedProposal);
        ArgumentNullException.ThrowIfNull(evidencePackage);

        var duplicates = duplicateCandidates ?? Array.Empty<MemoryProposalDuplicateCandidate>();
        var stale = staleCandidates ?? Array.Empty<MemoryProposalStaleCandidate>();
        var conflicts = conflictCandidates ?? Array.Empty<MemoryProposalConflictCandidate>();
        var patterns = patternCandidates ?? Array.Empty<CrossRunMemoryPatternCandidate>();
        var signals = BuildSignals(duplicates, stale, conflicts, patterns);

        return new MemoryPromotionRequestPackageCreateRequest
        {
            MemoryPromotionRequestPackageId = DeterministicGuid(stagedProposal.ProjectId, stagedProposal.MemoryProposalId, evidencePackage.MemoryProposalEvidencePackageId),
            ProjectId = stagedProposal.ProjectId,
            MemoryProposalId = stagedProposal.MemoryProposalId,
            PromotionRequestKey = $"{stagedProposal.ProposalKey}.promotion-review",
            Status = DetermineStatus(stagedProposal, evidencePackage, duplicates, stale, conflicts),
            Purpose = DeterminePurpose(stagedProposal),
            ProposalType = stagedProposal.ProposalType.ToString(),
            CurrentProposalStatus = stagedProposal.ProposalStatus.ToString(),
            RequestedTargetMemoryScope = DetermineTargetScope(stagedProposal.TargetMemoryScope),
            SafeProposedMemory = stagedProposal.SafeProposedMemory,
            SafePromotionRationale = "Package gathers staged proposal material for governed promotion review only.",
            SafeRiskSummary = BuildRiskSummary(evidencePackage, duplicates, stale, conflicts, patterns),
            SafeSanitizationSummary = BuildSanitizationSummary(stagedProposal),
            SafeReviewerInstructions = "Review evidence, grounding, risk signals, and approval requirements before any later memory action.",
            ConfidentialityLabel = stagedProposal.ConfidentialityLabel.ToString(),
            SanitizationStatus = stagedProposal.SanitizationStatus.ToString(),
            EvidenceReferences = BuildEvidenceReferences(stagedProposal, evidencePackage),
            GroundingReferences = BuildGroundingReferences(evidencePackage),
            SignalReferences = signals,
            ApprovalRequirementReferences = BuildApprovalRequirements(stagedProposal),
            ReviewNotes = BuildReviewNotes(stagedProposal, evidencePackage, signals),
            WorkflowRunId = stagedProposal.WorkflowRunId,
            WorkflowRunStepId = stagedProposal.WorkflowRunStepId,
            WorkflowCheckpointId = stagedProposal.WorkflowCheckpointId,
            CorrelationId = stagedProposal.CorrelationId,
            CausationId = stagedProposal.CausationId,
            CreatedByActorType = "system",
            CreatedByActorId = "memory-promotion-request-package-assembler",
            MetadataVersion = 1,
            MetadataJson = JsonSerializer.Serialize(new { source = "memory-promotion-request-package-assembler", signalCount = signals.Count })
        };
    }

    private static MemoryPromotionRequestPackageStatus DetermineStatus(MemoryProposal stagedProposal, MemoryProposalEvidencePackage evidencePackage, IReadOnlyCollection<MemoryProposalDuplicateCandidate> duplicates, IReadOnlyCollection<MemoryProposalStaleCandidate> stale, IReadOnlyCollection<MemoryProposalConflictCandidate> conflicts)
    {
        if (conflicts.Count > 0) return MemoryPromotionRequestPackageStatus.ContainsConflictRisk;
        if (duplicates.Count > 0) return MemoryPromotionRequestPackageStatus.ContainsDuplicateRisk;
        if (stale.Count > 0) return MemoryPromotionRequestPackageStatus.ContainsStaleRisk;
        if (evidencePackage.EvidenceReferences.Count == 0) return MemoryPromotionRequestPackageStatus.NeedsEvidence;
        if (stagedProposal.TargetMemoryScope == MemoryProposalTargetScope.PortableEngineeringMemoryCandidate || stagedProposal.SanitizationStatus is MemoryProposalSanitizationStatus.RequiresReview or MemoryProposalSanitizationStatus.RequiresSanitization) return MemoryPromotionRequestPackageStatus.RequiresSanitizationReview;
        return MemoryPromotionRequestPackageStatus.ReadyForReview;
    }

    private static MemoryPromotionRequestPurpose DeterminePurpose(MemoryProposal stagedProposal) => stagedProposal.TargetMemoryScope switch
    {
        MemoryProposalTargetScope.AgentLocalCandidate => MemoryPromotionRequestPurpose.AgentLocalMemoryReview,
        MemoryProposalTargetScope.PortableEngineeringMemoryCandidate => MemoryPromotionRequestPurpose.PortableEngineeringMemoryReview,
        MemoryProposalTargetScope.RequiresTriage => MemoryPromotionRequestPurpose.RiskReview,
        _ => MemoryPromotionRequestPurpose.ProjectMemoryReview
    };

    private static MemoryPromotionRequestedTargetMemoryScope DetermineTargetScope(MemoryProposalTargetScope scope) => scope switch
    {
        MemoryProposalTargetScope.AgentLocalCandidate => MemoryPromotionRequestedTargetMemoryScope.AgentLocalCandidateForPromotion,
        MemoryProposalTargetScope.PortableEngineeringMemoryCandidate => MemoryPromotionRequestedTargetMemoryScope.PortableEngineeringMemoryCandidateForPromotion,
        MemoryProposalTargetScope.ProjectLocalCandidate => MemoryPromotionRequestedTargetMemoryScope.ProjectLocalCandidateForPromotion,
        _ => MemoryPromotionRequestedTargetMemoryScope.RequiresTriage
    };

    private static string BuildRiskSummary(MemoryProposalEvidencePackage evidencePackage, IReadOnlyCollection<MemoryProposalDuplicateCandidate> duplicates, IReadOnlyCollection<MemoryProposalStaleCandidate> stale, IReadOnlyCollection<MemoryProposalConflictCandidate> conflicts, IReadOnlyCollection<CrossRunMemoryPatternCandidate> patterns)
    {
        var risks = new List<string>();
        if (duplicates.Count > 0) risks.Add($"{duplicates.Count} duplicate signal(s)");
        if (stale.Count > 0) risks.Add($"{stale.Count} stale signal(s)");
        if (conflicts.Count > 0) risks.Add($"{conflicts.Count} conflict signal(s)");
        if (patterns.Count > 0) risks.Add($"{patterns.Count} cross-run pattern signal(s)");
        if (evidencePackage.EvidenceReferences.Count == 0) risks.Add("evidence gap");
        return risks.Count == 0 ? "No advisory risk signal was attached. Review is still required." : "Advisory review signals: " + string.Join(", ", risks) + ".";
    }

    private static string BuildSanitizationSummary(MemoryProposal stagedProposal) =>
        stagedProposal.TargetMemoryScope == MemoryProposalTargetScope.PortableEngineeringMemoryCandidate
            ? "Portable candidate review requires separate sanitization review before any later memory action."
            : stagedProposal.SanitizationStatus.ToString();

    private static IReadOnlyList<MemoryPromotionRequestEvidenceReference> BuildEvidenceReferences(MemoryProposal stagedProposal, MemoryProposalEvidencePackage evidencePackage)
    {
        var references = new List<MemoryPromotionRequestEvidenceReference>
        {
            new()
            {
                EvidenceType = "MemoryProposal",
                EvidenceId = stagedProposal.MemoryProposalId.ToString(),
                EvidenceLabel = "Staged memory proposal",
                SafeSummary = "Staged proposal is the subject of this promotion review package.",
                AllowedUse = "PromotionReview",
                MemoryProposalId = stagedProposal.MemoryProposalId,
                WorkflowRunId = stagedProposal.WorkflowRunId,
                WorkflowRunStepId = stagedProposal.WorkflowRunStepId,
                WorkflowCheckpointId = stagedProposal.WorkflowCheckpointId
            },
            new()
            {
                EvidenceType = "MemoryProposalEvidencePackage",
                EvidenceId = evidencePackage.MemoryProposalEvidencePackageId.ToString(),
                EvidenceLabel = "Memory proposal evidence package",
                SafeSummary = "Evidence package is included for review context only.",
                AllowedUse = "PromotionReview",
                MemoryProposalId = evidencePackage.MemoryProposalId,
                MemoryProposalEvidencePackageId = evidencePackage.MemoryProposalEvidencePackageId,
                WorkflowRunId = evidencePackage.WorkflowRunId,
                WorkflowRunStepId = evidencePackage.WorkflowRunStepId,
                WorkflowCheckpointId = evidencePackage.WorkflowCheckpointId
            }
        };

        references.AddRange(evidencePackage.EvidenceReferences.Select(evidence => new MemoryPromotionRequestEvidenceReference
        {
            EvidenceType = evidence.EvidenceType,
            EvidenceId = evidence.EvidenceId,
            EvidenceLabel = evidence.EvidenceLabel,
            SafeSummary = evidence.SafeSummary,
            AllowedUse = "PromotionReview",
            MemoryProposalId = evidencePackage.MemoryProposalId,
            MemoryProposalEvidencePackageId = evidencePackage.MemoryProposalEvidencePackageId,
            GovernanceEventId = evidence.GovernanceEventId,
            WorkflowRunStepId = evidence.WorkflowRunStepId,
            WorkflowCheckpointId = evidence.WorkflowCheckpointId
        }));

        return references;
    }

    private static IReadOnlyList<MemoryPromotionRequestGroundingReference> BuildGroundingReferences(MemoryProposalEvidencePackage evidencePackage) =>
        evidencePackage.GroundingReferences.Select(grounding => new MemoryPromotionRequestGroundingReference
        {
            GroundingEvidenceReferenceId = grounding.GroundingEvidenceReferenceId,
            ClaimType = grounding.ClaimType,
            ClaimId = grounding.ClaimId,
            SafeSummary = grounding.SafeSummary
        }).ToList();

    private static IReadOnlyList<MemoryPromotionRequestSignalReference> BuildSignals(
        IReadOnlyCollection<MemoryProposalDuplicateCandidate> duplicates,
        IReadOnlyCollection<MemoryProposalStaleCandidate> stale,
        IReadOnlyCollection<MemoryProposalConflictCandidate> conflicts,
        IReadOnlyCollection<CrossRunMemoryPatternCandidate> patterns)
    {
        var signals = new List<MemoryPromotionRequestSignalReference>();
        signals.AddRange(duplicates.Select(signal => Signal("DuplicateCandidate", signal.MemoryProposalDuplicateCandidateId, signal.SafeReasonSummary ?? "Duplicate signal needs review.", "warning")));
        signals.AddRange(stale.Select(signal => Signal("StaleCandidate", signal.MemoryProposalStaleCandidateId, signal.SafeReasonSummary ?? "Stale signal needs review.", "warning")));
        signals.AddRange(conflicts.Select(signal => Signal("ConflictCandidate", signal.MemoryProposalConflictCandidateId, signal.SafeConflictSummary ?? "Conflict signal needs review.", "error")));
        signals.AddRange(patterns.Select(signal => Signal("CrossRunPatternCandidate", signal.CrossRunMemoryPatternCandidateId, signal.SafePatternSummary, "info")));
        return signals;
    }

    private static MemoryPromotionRequestSignalReference Signal(string signalType, Guid signalId, string safeSummary, string severity) => new()
    {
        SignalType = signalType,
        SignalId = signalId,
        SafeSummary = safeSummary,
        Severity = severity
    };

    private static IReadOnlyList<MemoryPromotionApprovalRequirementReference> BuildApprovalRequirements(MemoryProposal stagedProposal)
    {
        var requirements = new List<MemoryPromotionApprovalRequirementReference>
        {
            new()
            {
                RequirementType = "HumanReview",
                RequirementId = $"{stagedProposal.MemoryProposalId:N}:human-review",
                SafeSummary = "Human review is required before any later memory action."
            },
            new()
            {
                RequirementType = "GovernedApproval",
                RequirementId = $"{stagedProposal.MemoryProposalId:N}:governed-approval",
                SafeSummary = "Governed approval review is required before any later memory action."
            }
        };

        if (stagedProposal.TargetMemoryScope == MemoryProposalTargetScope.PortableEngineeringMemoryCandidate)
        {
            requirements.Add(new MemoryPromotionApprovalRequirementReference
            {
                RequirementType = "SanitizationReview",
                RequirementId = $"{stagedProposal.MemoryProposalId:N}:sanitization-review",
                SafeSummary = "Sanitization review is required for portable candidate review."
            });
        }

        return requirements;
    }

    private static IReadOnlyList<MemoryPromotionRequestReviewNote> BuildReviewNotes(MemoryProposal stagedProposal, MemoryProposalEvidencePackage evidencePackage, IReadOnlyCollection<MemoryPromotionRequestSignalReference> signals)
    {
        var notes = new List<MemoryPromotionRequestReviewNote>
        {
            new() { NoteType = "PromotionRationale", SafeSummary = "Package gathers staged proposal material for promotion review only.", Severity = "info" },
            new() { NoteType = "EvidenceSummary", SafeSummary = $"Evidence package contributes {evidencePackage.EvidenceReferences.Count} evidence reference(s).", Severity = evidencePackage.EvidenceReferences.Count == 0 ? "warning" : "info" },
            new() { NoteType = "GroundingSummary", SafeSummary = $"Evidence package contributes {evidencePackage.GroundingReferences.Count} grounding reference(s).", Severity = "info" },
            new() { NoteType = "HumanReviewNeeded", SafeSummary = "Human review remains required before any later memory action.", Severity = "warning" },
            new() { NoteType = "GovernedApprovalNeeded", SafeSummary = "Governed approval review remains required before any later memory action.", Severity = "warning" }
        };

        if (signals.Any(signal => signal.SignalType == "DuplicateCandidate")) notes.Add(new MemoryPromotionRequestReviewNote { NoteType = "DuplicateRisk", SafeSummary = "Duplicate signal requires review.", Severity = "warning" });
        if (signals.Any(signal => signal.SignalType == "StaleCandidate")) notes.Add(new MemoryPromotionRequestReviewNote { NoteType = "StaleRisk", SafeSummary = "Stale signal requires review.", Severity = "warning" });
        if (signals.Any(signal => signal.SignalType == "ConflictCandidate")) notes.Add(new MemoryPromotionRequestReviewNote { NoteType = "ConflictRisk", SafeSummary = "Conflict signal requires review.", Severity = "error" });
        if (signals.Any(signal => signal.SignalType == "CrossRunPatternCandidate")) notes.Add(new MemoryPromotionRequestReviewNote { NoteType = "PatternSignal", SafeSummary = "Cross-run pattern signal requires review.", Severity = "info" });
        if (stagedProposal.TargetMemoryScope == MemoryProposalTargetScope.PortableEngineeringMemoryCandidate || stagedProposal.SanitizationStatus is MemoryProposalSanitizationStatus.RequiresReview or MemoryProposalSanitizationStatus.RequiresSanitization) notes.Add(new MemoryPromotionRequestReviewNote { NoteType = "SanitizationNeeded", SafeSummary = "Sanitization review remains required.", Severity = "warning" });

        return notes;
    }

    private static Guid DeterministicGuid(Guid projectId, Guid proposalId, Guid packageId)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{projectId:N}:{proposalId:N}:{packageId:N}:promotion-request"));
        var guidBytes = bytes.Take(16).ToArray();
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x40);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }
}
