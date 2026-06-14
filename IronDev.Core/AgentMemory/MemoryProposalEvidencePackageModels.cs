using System.Text.Json;

namespace IronDev.Core.AgentMemory;

public enum MemoryProposalEvidencePackageStatus
{
    Assembled,
    ReadyForReview,
    NeedsEvidence,
    NeedsClarification,
    ContainsRisk,
    RequiresSanitization,
    Quarantined,
    Superseded,
    Withdrawn
}

public enum MemoryProposalEvidencePackagePurpose
{
    HumanReview,
    GovernedReview,
    SanitizationReview,
    DuplicateReview,
    RiskReview,
    EvidenceCompletenessReview,
    PortableCandidateReview,
    ProjectMemoryCandidateReview,
    AgentMemoryCandidateReview
}

public sealed class MemoryProposalEvidencePackageCreateRequest
{
    public Guid MemoryProposalEvidencePackageId { get; init; }
    public Guid MemoryProposalId { get; init; }
    public Guid ProjectId { get; init; }
    public string PackageKey { get; init; } = string.Empty;
    public MemoryProposalEvidencePackageStatus Status { get; init; } = MemoryProposalEvidencePackageStatus.Assembled;
    public MemoryProposalEvidencePackagePurpose Purpose { get; init; } = MemoryProposalEvidencePackagePurpose.HumanReview;
    public string ProposalType { get; init; } = string.Empty;
    public string TargetMemoryScope { get; init; } = string.Empty;
    public string ProposalStatus { get; init; } = string.Empty;
    public string SafeProposedMemory { get; init; } = string.Empty;
    public string? SafeRationaleSummary { get; init; }
    public string? SafeRiskSummary { get; init; }
    public string ConfidentialityLabel { get; init; } = string.Empty;
    public string SanitizationStatus { get; init; } = string.Empty;
    public IReadOnlyList<MemoryProposalPackageEvidenceReference> EvidenceReferences { get; init; } = Array.Empty<MemoryProposalPackageEvidenceReference>();
    public IReadOnlyList<MemoryProposalPackageGroundingReference> GroundingReferences { get; init; } = Array.Empty<MemoryProposalPackageGroundingReference>();
    public IReadOnlyList<MemoryProposalPackageWorkflowReference> WorkflowReferences { get; init; } = Array.Empty<MemoryProposalPackageWorkflowReference>();
    public IReadOnlyList<MemoryProposalPackageReviewNote> ReviewNotes { get; init; } = Array.Empty<MemoryProposalPackageReviewNote>();
    public Guid? WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public string CreatedByActorType { get; init; } = string.Empty;
    public string CreatedByActorId { get; init; } = string.Empty;
    public int MetadataVersion { get; init; } = 1;
    public string MetadataJson { get; init; } = "{}";
    public bool GrantsApproval { get; init; }
    public bool GrantsExecution { get; init; }
    public bool AcceptsMemory { get; init; }
    public bool RejectsMemory { get; init; }
    public bool PromotesMemory { get; init; }
    public bool CreatesAcceptedMemory { get; init; }
    public bool CreatesPortableMemory { get; init; }
    public bool ActivatesRetrieval { get; init; }
    public bool CreatesEmbedding { get; init; }
    public bool WritesVectorStore { get; init; }
    public bool MutatesSource { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool TransfersAuthority { get; init; }
    public bool ApprovesRelease { get; init; }
}

public sealed class MemoryProposalEvidencePackage
{
    public Guid MemoryProposalEvidencePackageId { get; init; }
    public Guid MemoryProposalId { get; init; }
    public Guid ProjectId { get; init; }
    public string PackageKey { get; init; } = string.Empty;
    public MemoryProposalEvidencePackageStatus Status { get; init; }
    public MemoryProposalEvidencePackagePurpose Purpose { get; init; }
    public string ProposalType { get; init; } = string.Empty;
    public string TargetMemoryScope { get; init; } = string.Empty;
    public string ProposalStatus { get; init; } = string.Empty;
    public string SafeProposedMemory { get; init; } = string.Empty;
    public string? SafeRationaleSummary { get; init; }
    public string? SafeRiskSummary { get; init; }
    public string ConfidentialityLabel { get; init; } = string.Empty;
    public string SanitizationStatus { get; init; } = string.Empty;
    public IReadOnlyList<MemoryProposalPackageEvidenceReference> EvidenceReferences { get; init; } = Array.Empty<MemoryProposalPackageEvidenceReference>();
    public IReadOnlyList<MemoryProposalPackageGroundingReference> GroundingReferences { get; init; } = Array.Empty<MemoryProposalPackageGroundingReference>();
    public IReadOnlyList<MemoryProposalPackageWorkflowReference> WorkflowReferences { get; init; } = Array.Empty<MemoryProposalPackageWorkflowReference>();
    public IReadOnlyList<MemoryProposalPackageReviewNote> ReviewNotes { get; init; } = Array.Empty<MemoryProposalPackageReviewNote>();
    public Guid? WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? CausationId { get; init; }
    public string CreatedByActorType { get; init; } = string.Empty;
    public string CreatedByActorId { get; init; } = string.Empty;
    public int MetadataVersion { get; init; }
    public string MetadataJson { get; init; } = "{}";
    public bool GrantsApproval { get; init; }
    public bool GrantsExecution { get; init; }
    public bool AcceptsMemory { get; init; }
    public bool RejectsMemory { get; init; }
    public bool PromotesMemory { get; init; }
    public bool CreatesAcceptedMemory { get; init; }
    public bool CreatesPortableMemory { get; init; }
    public bool ActivatesRetrieval { get; init; }
    public bool CreatesEmbedding { get; init; }
    public bool WritesVectorStore { get; init; }
    public bool MutatesSource { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool TransfersAuthority { get; init; }
    public bool ApprovesRelease { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed class MemoryProposalEvidencePackageSummary
{
    public Guid MemoryProposalEvidencePackageId { get; init; }
    public Guid MemoryProposalId { get; init; }
    public Guid ProjectId { get; init; }
    public string PackageKey { get; init; } = string.Empty;
    public MemoryProposalEvidencePackageStatus Status { get; init; }
    public MemoryProposalEvidencePackagePurpose Purpose { get; init; }
    public int EvidenceReferenceCount { get; init; }
    public int GroundingReferenceCount { get; init; }
    public int WorkflowReferenceCount { get; init; }
    public int ReviewNoteCount { get; init; }
    public bool ReadyForReview => Status == MemoryProposalEvidencePackageStatus.ReadyForReview;
}

public sealed class MemoryProposalPackageEvidenceReference
{
    public string EvidenceType { get; init; } = string.Empty;
    public string EvidenceId { get; init; } = string.Empty;
    public string? EvidenceLabel { get; init; }
    public string? SafeSummary { get; init; }
    public string? AllowedUse { get; init; }
    public Guid? GovernanceEventId { get; init; }
    public Guid? WorkflowRunEvidenceReferenceId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public Guid? HandoffId { get; init; }
    public Guid? ThoughtLedgerEntryId { get; init; }
    public bool EvidenceIsApproval { get; init; }
    public bool EvidenceIsPermission { get; init; }
    public bool EvidenceAcceptsMemory { get; init; }
}

public sealed class MemoryProposalPackageGroundingReference
{
    public Guid GroundingEvidenceReferenceId { get; init; }
    public string ClaimType { get; init; } = string.Empty;
    public string ClaimId { get; init; } = string.Empty;
    public string? SafeSummary { get; init; }
    public bool GroundingIsAuthority { get; init; }
    public bool GroundingAcceptsMemory { get; init; }
}

public sealed class MemoryProposalPackageWorkflowReference
{
    public Guid? WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public string ReferenceType { get; init; } = string.Empty;
    public string? SafeSummary { get; init; }
    public bool WorkflowReferenceAcceptsMemory { get; init; }
    public bool WorkflowReferencePromotesMemory { get; init; }
}

public sealed class MemoryProposalPackageReviewNote
{
    public string NoteType { get; init; } = string.Empty;
    public string SafeSummary { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public bool NoteAcceptsMemory { get; init; }
    public bool NoteRejectsMemory { get; init; }
    public bool NotePromotesMemory { get; init; }
}

public sealed class MemoryProposalEvidencePackageValidator
{
    private static readonly HashSet<string> AllowedReviewNoteTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "EvidenceSummary",
        "GroundingSummary",
        "WorkflowContextSummary",
        "RiskSummary",
        "ConfidentialitySummary",
        "SanitizationSummary",
        "DuplicateRisk",
        "ContradictionRisk",
        "StalenessRisk",
        "HumanReviewNeeded",
        "MoreEvidenceNeeded"
    };

    private static readonly HashSet<string> AllowedUses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Review",
        "Traceability",
        "HumanDecisionSupport",
        "GovernedDecisionSupport",
        "SanitizationReview",
        "RiskReview",
        "EvidenceReview",
        "DuplicateReview",
        "AuditReference",
        "ClaimSupport"
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
        "portable memory approved",
        "portable engineering memory accepted",
        "retrieval active",
        "indexed for retrieval",
        "embedding created",
        "vector write",
        "weaviate write",
        "memory promotion allowed",
        "accepted memory created",
        "cross-project approved",
        "global memory",
        "authority transferred",
        "policy satisfied",
        "approval granted",
        "source applied",
        "release approved"
    };

    public MemoryProposalValidationResult Validate(MemoryProposalEvidencePackageCreateRequest? request)
    {
        var issues = new List<MemoryProposalValidationIssue>();
        if (request is null)
        {
            Add(issues, "request_required", "Memory proposal evidence package request is required.");
            return new MemoryProposalValidationResult { Issues = issues };
        }

        if (request.MemoryProposalEvidencePackageId == Guid.Empty) Add(issues, "memory_proposal_evidence_package_id_required", "MemoryProposalEvidencePackageId is required.");
        if (request.MemoryProposalId == Guid.Empty) Add(issues, "memory_proposal_id_required", "MemoryProposalId is required.");
        if (request.ProjectId == Guid.Empty) Add(issues, "project_id_required", "ProjectId is required.");
        if (string.IsNullOrWhiteSpace(request.PackageKey)) Add(issues, "package_key_required", "PackageKey is required.");
        if (string.IsNullOrWhiteSpace(request.SafeProposedMemory)) Add(issues, "safe_proposed_memory_required", "SafeProposedMemory is required.");
        if (string.IsNullOrWhiteSpace(request.CreatedByActorType)) Add(issues, "created_by_actor_type_required", "CreatedByActorType is required.");
        if (string.IsNullOrWhiteSpace(request.CreatedByActorId)) Add(issues, "created_by_actor_id_required", "CreatedByActorId is required.");
        if (request.MetadataVersion <= 0) Add(issues, "metadata_version_invalid", "MetadataVersion must be positive.");
        if (!IsJson(request.MetadataJson)) Add(issues, "metadata_json_invalid", "MetadataJson must be valid JSON.");

        ValidateEnum(request.Status, nameof(request.Status), issues);
        ValidateEnum(request.Purpose, nameof(request.Purpose), issues);

        if (HasAuthorityFlag(request))
        {
            Add(issues, "authority_flags_forbidden", "Memory proposal evidence packages cannot approve, execute, accept, reject, promote, retrieve, embed, write vector storage, satisfy policy, transfer authority, mutate source, or approve release.");
        }

        ScanText(issues, "package_text", request.PackageKey, request.ProposalType, request.TargetMemoryScope, request.ProposalStatus, request.SafeProposedMemory, request.SafeRationaleSummary, request.SafeRiskSummary, request.ConfidentialityLabel, request.SanitizationStatus, request.CreatedByActorType, request.CreatedByActorId, request.MetadataJson);

        foreach (var evidence in request.EvidenceReferences ?? Array.Empty<MemoryProposalPackageEvidenceReference>())
        {
            if (string.IsNullOrWhiteSpace(evidence.EvidenceType)) Add(issues, "evidence_type_required", "EvidenceType is required.");
            if (string.IsNullOrWhiteSpace(evidence.EvidenceId)) Add(issues, "evidence_id_required", "EvidenceId is required.");
            if (!string.IsNullOrWhiteSpace(evidence.AllowedUse) && !AllowedUses.Contains(evidence.AllowedUse)) Add(issues, "evidence_allowed_use_forbidden", "Evidence AllowedUse must remain review, traceability, support, or audit reference only.");
            if (evidence.EvidenceIsApproval || evidence.EvidenceIsPermission || evidence.EvidenceAcceptsMemory) Add(issues, "evidence_authority_forbidden", "Evidence references cannot be approval, permission, or memory acceptance.");
            ScanText(issues, "evidence_reference_text", evidence.EvidenceType, evidence.EvidenceId, evidence.EvidenceLabel, evidence.SafeSummary, evidence.AllowedUse);
        }

        foreach (var grounding in request.GroundingReferences ?? Array.Empty<MemoryProposalPackageGroundingReference>())
        {
            if (grounding.GroundingEvidenceReferenceId == Guid.Empty) Add(issues, "grounding_reference_id_required", "GroundingEvidenceReferenceId is required.");
            if (string.IsNullOrWhiteSpace(grounding.ClaimType)) Add(issues, "grounding_claim_type_required", "ClaimType is required.");
            if (string.IsNullOrWhiteSpace(grounding.ClaimId)) Add(issues, "grounding_claim_id_required", "ClaimId is required.");
            if (grounding.GroundingIsAuthority || grounding.GroundingAcceptsMemory) Add(issues, "grounding_authority_forbidden", "Grounding references cannot be authority or memory acceptance.");
            ScanText(issues, "grounding_reference_text", grounding.ClaimType, grounding.ClaimId, grounding.SafeSummary);
        }

        foreach (var workflow in request.WorkflowReferences ?? Array.Empty<MemoryProposalPackageWorkflowReference>())
        {
            if (workflow.WorkflowRunId is null && workflow.WorkflowRunStepId is null && workflow.WorkflowCheckpointId is null) Add(issues, "workflow_reference_target_required", "Workflow reference must point to a run, step, or checkpoint.");
            if (string.IsNullOrWhiteSpace(workflow.ReferenceType)) Add(issues, "workflow_reference_type_required", "Workflow ReferenceType is required.");
            if (workflow.WorkflowReferenceAcceptsMemory || workflow.WorkflowReferencePromotesMemory) Add(issues, "workflow_reference_authority_forbidden", "Workflow references cannot accept or promote memory.");
            ScanText(issues, "workflow_reference_text", workflow.ReferenceType, workflow.SafeSummary);
        }

        foreach (var note in request.ReviewNotes ?? Array.Empty<MemoryProposalPackageReviewNote>())
        {
            if (string.IsNullOrWhiteSpace(note.NoteType)) Add(issues, "review_note_type_required", "Review note type is required.");
            if (!AllowedReviewNoteTypes.Contains(note.NoteType)) Add(issues, "review_note_type_forbidden", "Review note type must be bounded to evidence, grounding, workflow, risk, confidentiality, sanitization, duplicate, contradiction, staleness, or human review notes.");
            if (string.IsNullOrWhiteSpace(note.SafeSummary)) Add(issues, "review_note_summary_required", "Review note SafeSummary is required.");
            if (string.IsNullOrWhiteSpace(note.Severity)) Add(issues, "review_note_severity_required", "Review note Severity is required.");
            if (note.NoteAcceptsMemory || note.NoteRejectsMemory || note.NotePromotesMemory) Add(issues, "review_note_authority_forbidden", "Review notes cannot accept, reject, or promote memory.");
            ScanText(issues, "review_note_text", note.NoteType, note.SafeSummary, note.Severity);
        }

        return new MemoryProposalValidationResult { Issues = issues };
    }

    public MemoryProposalEvidencePackage Normalize(MemoryProposalEvidencePackageCreateRequest request)
    {
        var result = Validate(request);
        if (!result.IsValid)
        {
            throw new InvalidOperationException("Memory proposal evidence package request is invalid: " + string.Join("; ", result.Issues.Select(issue => issue.Code)));
        }

        return new MemoryProposalEvidencePackage
        {
            MemoryProposalEvidencePackageId = request.MemoryProposalEvidencePackageId,
            MemoryProposalId = request.MemoryProposalId,
            ProjectId = request.ProjectId,
            PackageKey = NormalizeRequired(request.PackageKey),
            Status = request.Status,
            Purpose = request.Purpose,
            ProposalType = NormalizeRequired(request.ProposalType),
            TargetMemoryScope = NormalizeRequired(request.TargetMemoryScope),
            ProposalStatus = NormalizeRequired(request.ProposalStatus),
            SafeProposedMemory = NormalizeRequired(request.SafeProposedMemory),
            SafeRationaleSummary = NormalizeOptional(request.SafeRationaleSummary),
            SafeRiskSummary = NormalizeOptional(request.SafeRiskSummary),
            ConfidentialityLabel = NormalizeRequired(request.ConfidentialityLabel),
            SanitizationStatus = NormalizeRequired(request.SanitizationStatus),
            EvidenceReferences = (request.EvidenceReferences ?? Array.Empty<MemoryProposalPackageEvidenceReference>()).Select(NormalizeEvidenceReference).ToList(),
            GroundingReferences = (request.GroundingReferences ?? Array.Empty<MemoryProposalPackageGroundingReference>()).Select(NormalizeGroundingReference).ToList(),
            WorkflowReferences = (request.WorkflowReferences ?? Array.Empty<MemoryProposalPackageWorkflowReference>()).Select(NormalizeWorkflowReference).ToList(),
            ReviewNotes = (request.ReviewNotes ?? Array.Empty<MemoryProposalPackageReviewNote>()).Select(NormalizeReviewNote).ToList(),
            WorkflowRunId = request.WorkflowRunId,
            WorkflowRunStepId = request.WorkflowRunStepId,
            WorkflowCheckpointId = request.WorkflowCheckpointId,
            CorrelationId = request.CorrelationId,
            CausationId = request.CausationId,
            CreatedByActorType = NormalizeRequired(request.CreatedByActorType),
            CreatedByActorId = NormalizeRequired(request.CreatedByActorId),
            MetadataVersion = request.MetadataVersion,
            MetadataJson = string.IsNullOrWhiteSpace(request.MetadataJson) ? "{}" : request.MetadataJson.Trim(),
            GrantsApproval = false,
            GrantsExecution = false,
            AcceptsMemory = false,
            RejectsMemory = false,
            PromotesMemory = false,
            CreatesAcceptedMemory = false,
            CreatesPortableMemory = false,
            ActivatesRetrieval = false,
            CreatesEmbedding = false,
            WritesVectorStore = false,
            MutatesSource = false,
            SatisfiesPolicy = false,
            TransfersAuthority = false,
            ApprovesRelease = false,
            CreatedUtc = DateTimeOffset.UtcNow
        };
    }

    private static MemoryProposalPackageEvidenceReference NormalizeEvidenceReference(MemoryProposalPackageEvidenceReference reference) => new()
    {
        EvidenceType = NormalizeRequired(reference.EvidenceType),
        EvidenceId = NormalizeRequired(reference.EvidenceId),
        EvidenceLabel = NormalizeOptional(reference.EvidenceLabel),
        SafeSummary = NormalizeOptional(reference.SafeSummary),
        AllowedUse = NormalizeOptional(reference.AllowedUse),
        GovernanceEventId = reference.GovernanceEventId,
        WorkflowRunEvidenceReferenceId = reference.WorkflowRunEvidenceReferenceId,
        WorkflowRunStepId = reference.WorkflowRunStepId,
        WorkflowCheckpointId = reference.WorkflowCheckpointId,
        HandoffId = reference.HandoffId,
        ThoughtLedgerEntryId = reference.ThoughtLedgerEntryId,
        EvidenceIsApproval = false,
        EvidenceIsPermission = false,
        EvidenceAcceptsMemory = false
    };

    private static MemoryProposalPackageGroundingReference NormalizeGroundingReference(MemoryProposalPackageGroundingReference reference) => new()
    {
        GroundingEvidenceReferenceId = reference.GroundingEvidenceReferenceId,
        ClaimType = NormalizeRequired(reference.ClaimType),
        ClaimId = NormalizeRequired(reference.ClaimId),
        SafeSummary = NormalizeOptional(reference.SafeSummary),
        GroundingIsAuthority = false,
        GroundingAcceptsMemory = false
    };

    private static MemoryProposalPackageWorkflowReference NormalizeWorkflowReference(MemoryProposalPackageWorkflowReference reference) => new()
    {
        WorkflowRunId = reference.WorkflowRunId,
        WorkflowRunStepId = reference.WorkflowRunStepId,
        WorkflowCheckpointId = reference.WorkflowCheckpointId,
        ReferenceType = NormalizeRequired(reference.ReferenceType),
        SafeSummary = NormalizeOptional(reference.SafeSummary),
        WorkflowReferenceAcceptsMemory = false,
        WorkflowReferencePromotesMemory = false
    };

    private static MemoryProposalPackageReviewNote NormalizeReviewNote(MemoryProposalPackageReviewNote note) => new()
    {
        NoteType = NormalizeRequired(note.NoteType),
        SafeSummary = NormalizeRequired(note.SafeSummary),
        Severity = NormalizeRequired(note.Severity),
        NoteAcceptsMemory = false,
        NoteRejectsMemory = false,
        NotePromotesMemory = false
    };

    private static bool HasAuthorityFlag(MemoryProposalEvidencePackageCreateRequest request) =>
        request.GrantsApproval ||
        request.GrantsExecution ||
        request.AcceptsMemory ||
        request.RejectsMemory ||
        request.PromotesMemory ||
        request.CreatesAcceptedMemory ||
        request.CreatesPortableMemory ||
        request.ActivatesRetrieval ||
        request.CreatesEmbedding ||
        request.WritesVectorStore ||
        request.MutatesSource ||
        request.SatisfiesPolicy ||
        request.TransfersAuthority ||
        request.ApprovesRelease;

    private static void ValidateEnum<T>(T value, string name, ICollection<MemoryProposalValidationIssue> issues)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value)) Add(issues, "invalid_" + ToSnakeCase(name), name + " is invalid.");
    }

    private static void ScanText(ICollection<MemoryProposalValidationIssue> issues, string code, params string?[] values)
    {
        var text = string.Join(' ', values.Where(value => !string.IsNullOrWhiteSpace(value))).ToLowerInvariant();
        if (UnsafeMarkers.Any(text.Contains)) Add(issues, code + "_unsafe", "Memory proposal evidence package text contains raw/private reasoning or authority language.");
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

public sealed class MemoryProposalEvidencePackageAssembler
{
    public MemoryProposalEvidencePackageCreateRequest Assemble(MemoryProposal stagedProposal)
    {
        ArgumentNullException.ThrowIfNull(stagedProposal);

        return new MemoryProposalEvidencePackageCreateRequest
        {
            MemoryProposalEvidencePackageId = Guid.NewGuid(),
            MemoryProposalId = stagedProposal.MemoryProposalId,
            ProjectId = stagedProposal.ProjectId,
            PackageKey = stagedProposal.ProposalKey + ".evidence-package",
            Status = stagedProposal.EvidenceReferences.Count == 0 ? MemoryProposalEvidencePackageStatus.NeedsEvidence : MemoryProposalEvidencePackageStatus.ReadyForReview,
            Purpose = stagedProposal.TargetMemoryScope == MemoryProposalTargetScope.PortableEngineeringMemoryCandidate
                ? MemoryProposalEvidencePackagePurpose.PortableCandidateReview
                : MemoryProposalEvidencePackagePurpose.HumanReview,
            ProposalType = stagedProposal.ProposalType.ToString(),
            TargetMemoryScope = stagedProposal.TargetMemoryScope.ToString(),
            ProposalStatus = stagedProposal.ProposalStatus.ToString(),
            SafeProposedMemory = stagedProposal.SafeProposedMemory,
            SafeRationaleSummary = stagedProposal.SafeRationaleSummary,
            SafeRiskSummary = stagedProposal.SafeRiskSummary,
            ConfidentialityLabel = stagedProposal.ConfidentialityLabel.ToString(),
            SanitizationStatus = stagedProposal.SanitizationStatus.ToString(),
            EvidenceReferences = stagedProposal.EvidenceReferences.Select(evidence => new MemoryProposalPackageEvidenceReference
            {
                EvidenceType = evidence.EvidenceType.ToString(),
                EvidenceId = evidence.EvidenceId,
                EvidenceLabel = evidence.EvidenceLabel,
                SafeSummary = evidence.SafeSummary,
                AllowedUse = "Review",
                GovernanceEventId = evidence.GovernanceEventId,
                WorkflowRunEvidenceReferenceId = evidence.WorkflowRunEvidenceReferenceId,
                WorkflowRunStepId = evidence.WorkflowRunStepId,
                WorkflowCheckpointId = evidence.WorkflowCheckpointId,
                ThoughtLedgerEntryId = evidence.ThoughtLedgerEntryId,
                EvidenceIsApproval = false,
                EvidenceIsPermission = false,
                EvidenceAcceptsMemory = false
            }).ToList(),
            GroundingReferences = stagedProposal.GroundingReferences.Select(grounding => new MemoryProposalPackageGroundingReference
            {
                GroundingEvidenceReferenceId = grounding.GroundingReferenceId,
                ClaimType = grounding.ClaimType.ToString(),
                ClaimId = grounding.ClaimId,
                SafeSummary = grounding.SafeSummary,
                GroundingIsAuthority = false,
                GroundingAcceptsMemory = false
            }).ToList(),
            WorkflowReferences = stagedProposal.WorkflowReferences.Select(workflow => new MemoryProposalPackageWorkflowReference
            {
                WorkflowRunId = workflow.WorkflowRunId,
                WorkflowRunStepId = workflow.WorkflowRunStepId,
                WorkflowCheckpointId = workflow.WorkflowCheckpointId,
                ReferenceType = workflow.ReferenceType.ToString(),
                SafeSummary = workflow.SafeSummary,
                WorkflowReferenceAcceptsMemory = false,
                WorkflowReferencePromotesMemory = false
            }).ToList(),
            ReviewNotes = BuildReviewNotes(stagedProposal),
            WorkflowRunId = stagedProposal.WorkflowRunId,
            WorkflowRunStepId = stagedProposal.WorkflowRunStepId,
            WorkflowCheckpointId = stagedProposal.WorkflowCheckpointId,
            CorrelationId = stagedProposal.CorrelationId,
            CausationId = stagedProposal.CausationId,
            CreatedByActorType = "system",
            CreatedByActorId = "memory-proposal-evidence-package-assembler",
            MetadataVersion = 1,
            MetadataJson = "{\"source\":\"memory-proposal-evidence-package-assembler\"}"
        };
    }

    private static IReadOnlyList<MemoryProposalPackageReviewNote> BuildReviewNotes(MemoryProposal proposal)
    {
        var notes = new List<MemoryProposalPackageReviewNote>
        {
            new()
            {
                NoteType = "EvidenceSummary",
                SafeSummary = $"Package contains {proposal.EvidenceReferences.Count} evidence reference(s) for review.",
                Severity = proposal.EvidenceReferences.Count == 0 ? "warning" : "info"
            },
            new()
            {
                NoteType = "GroundingSummary",
                SafeSummary = $"Package contains {proposal.GroundingReferences.Count} grounding reference(s) for traceability.",
                Severity = "info"
            },
            new()
            {
                NoteType = "WorkflowContextSummary",
                SafeSummary = $"Package contains {proposal.WorkflowReferences.Count} workflow reference(s) for context.",
                Severity = "info"
            },
            new()
            {
                NoteType = "HumanReviewNeeded",
                SafeSummary = "Human or governed review remains required before memory can be accepted or promoted.",
                Severity = "warning"
            }
        };

        if (!string.IsNullOrWhiteSpace(proposal.SafeRiskSummary))
        {
            notes.Add(new MemoryProposalPackageReviewNote
            {
                NoteType = "RiskSummary",
                SafeSummary = proposal.SafeRiskSummary,
                Severity = "warning"
            });
        }

        if (proposal.SanitizationStatus is MemoryProposalSanitizationStatus.RequiresReview or MemoryProposalSanitizationStatus.RequiresSanitization)
        {
            notes.Add(new MemoryProposalPackageReviewNote
            {
                NoteType = "SanitizationSummary",
                SafeSummary = "Sanitization review remains required before any portable or accepted-memory decision.",
                Severity = "warning"
            });
        }

        return notes;
    }
}



