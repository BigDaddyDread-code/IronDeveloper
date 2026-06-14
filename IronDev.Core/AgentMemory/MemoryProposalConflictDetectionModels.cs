using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IronDev.Core.AgentMemory;

public enum MemoryProposalConflictCandidateStatus
{
    Detected,
    ReadyForReview,
    NeedsEvidence,
    NeedsHumanReview,
    Quarantined,
    Superseded,
    Withdrawn
}

public enum MemoryProposalConflictType
{
    DirectContradictionCandidate,
    NegationCandidate,
    IncompatiblePolicyCandidate,
    IncompatibleScopeCandidate,
    IncompatibleStatusCandidate,
    IncompatibleDecisionCandidate,
    IncompatibleWorkflowStateCandidate,
    IncompatibleMemoryBoundaryCandidate,
    ConflictingEvidenceCandidate,
    ConflictingTerminologyCandidate,
    ConflictingPortableMemoryCandidate,
    NeedsHumanConflictReview
}

public enum MemoryProposalConflictBand
{
    HighConflictRisk,
    MediumConflictRisk,
    LowConflictRisk,
    DirectContradiction,
    ScopeMismatch,
    PolicyMismatch,
    TerminologyMismatch,
    Unknown
}

public sealed class MemoryProposalConflictDetectionOptions
{
    public int MaxCandidateCount { get; init; } = 50;
    public decimal MinimumSharedTokenScore { get; init; } = 0.20m;
    public string CreatedByActorType { get; init; } = "system";
    public string CreatedByActorId { get; init; } = "memory-proposal-conflict-detector";
}

public sealed class MemoryProposalConflictCandidateCreateRequest
{
    public Guid MemoryProposalConflictCandidateId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid PrimaryMemoryProposalId { get; init; }
    public Guid ConflictingMemoryProposalId { get; init; }
    public string ConflictCandidateKey { get; init; } = string.Empty;
    public MemoryProposalConflictCandidateStatus Status { get; init; } = MemoryProposalConflictCandidateStatus.Detected;
    public MemoryProposalConflictType ConflictType { get; init; } = MemoryProposalConflictType.NeedsHumanConflictReview;
    public decimal ConflictScore { get; init; }
    public MemoryProposalConflictBand ConflictBand { get; init; } = MemoryProposalConflictBand.Unknown;
    public string SafePrimarySummary { get; init; } = string.Empty;
    public string SafeConflictingSummary { get; init; } = string.Empty;
    public string? SafeConflictSummary { get; init; }
    public string? SafeReviewRecommendation { get; init; }
    public IReadOnlyList<MemoryProposalConflictEvidenceReference> EvidenceReferences { get; init; } = Array.Empty<MemoryProposalConflictEvidenceReference>();
    public IReadOnlyList<MemoryProposalConflictReviewNote> ReviewNotes { get; init; } = Array.Empty<MemoryProposalConflictReviewNote>();
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
    public bool ChoosesTruth { get; init; }
    public bool RejectsProposal { get; init; }
    public bool DeletesProposal { get; init; }
    public bool CorrectsProposal { get; init; }
    public bool MergesProposal { get; init; }
    public bool AcceptsMemory { get; init; }
    public bool PromotesMemory { get; init; }
    public bool CreatesAcceptedMemory { get; init; }
    public bool ActivatesRetrieval { get; init; }
    public bool CreatesEmbedding { get; init; }
    public bool WritesVectorStore { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool TransfersAuthority { get; init; }
}

public sealed class MemoryProposalConflictCandidate
{
    public Guid MemoryProposalConflictCandidateId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid PrimaryMemoryProposalId { get; init; }
    public Guid ConflictingMemoryProposalId { get; init; }
    public string ConflictCandidateKey { get; init; } = string.Empty;
    public MemoryProposalConflictCandidateStatus Status { get; init; }
    public MemoryProposalConflictType ConflictType { get; init; }
    public decimal ConflictScore { get; init; }
    public MemoryProposalConflictBand ConflictBand { get; init; }
    public string SafePrimarySummary { get; init; } = string.Empty;
    public string SafeConflictingSummary { get; init; } = string.Empty;
    public string? SafeConflictSummary { get; init; }
    public string? SafeReviewRecommendation { get; init; }
    public IReadOnlyList<MemoryProposalConflictEvidenceReference> EvidenceReferences { get; init; } = Array.Empty<MemoryProposalConflictEvidenceReference>();
    public IReadOnlyList<MemoryProposalConflictReviewNote> ReviewNotes { get; init; } = Array.Empty<MemoryProposalConflictReviewNote>();
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
    public bool ChoosesTruth { get; init; }
    public bool RejectsProposal { get; init; }
    public bool DeletesProposal { get; init; }
    public bool CorrectsProposal { get; init; }
    public bool MergesProposal { get; init; }
    public bool AcceptsMemory { get; init; }
    public bool PromotesMemory { get; init; }
    public bool CreatesAcceptedMemory { get; init; }
    public bool ActivatesRetrieval { get; init; }
    public bool CreatesEmbedding { get; init; }
    public bool WritesVectorStore { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool TransfersAuthority { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed class MemoryProposalConflictEvidenceReference
{
    public string EvidenceType { get; init; } = string.Empty;
    public string EvidenceId { get; init; } = string.Empty;
    public string? EvidenceLabel { get; init; }
    public string? SafeSummary { get; init; }
    public string? AllowedUse { get; init; }
    public Guid? PrimaryMemoryProposalId { get; init; }
    public Guid? ConflictingMemoryProposalId { get; init; }
    public Guid? WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public bool EvidenceIsDecision { get; init; }
    public bool EvidenceChoosesTruth { get; init; }
    public bool EvidenceRejectsProposal { get; init; }
    public bool EvidenceDeletesProposal { get; init; }
    public bool EvidenceCorrectsProposal { get; init; }
    public bool EvidenceAcceptsMemory { get; init; }
}

public sealed class MemoryProposalConflictReviewNote
{
    public string NoteType { get; init; } = string.Empty;
    public string SafeSummary { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public bool NoteIsDecision { get; init; }
    public bool NoteChoosesTruth { get; init; }
    public bool NoteRejectsProposal { get; init; }
    public bool NoteDeletesProposal { get; init; }
    public bool NoteCorrectsProposal { get; init; }
    public bool NoteAcceptsMemory { get; init; }
}

public sealed class MemoryProposalConflictCandidateValidator
{
    private static readonly HashSet<MemoryProposalConflictType> AllowedConflictTypes = new()
    {
        MemoryProposalConflictType.DirectContradictionCandidate,
        MemoryProposalConflictType.NegationCandidate,
        MemoryProposalConflictType.IncompatiblePolicyCandidate,
        MemoryProposalConflictType.IncompatibleScopeCandidate,
        MemoryProposalConflictType.IncompatibleStatusCandidate,
        MemoryProposalConflictType.IncompatibleDecisionCandidate,
        MemoryProposalConflictType.IncompatibleWorkflowStateCandidate,
        MemoryProposalConflictType.IncompatibleMemoryBoundaryCandidate,
        MemoryProposalConflictType.ConflictingEvidenceCandidate,
        MemoryProposalConflictType.ConflictingTerminologyCandidate,
        MemoryProposalConflictType.ConflictingPortableMemoryCandidate,
        MemoryProposalConflictType.NeedsHumanConflictReview
    };

    private static readonly HashSet<MemoryProposalConflictCandidateStatus> AllowedStatuses = new()
    {
        MemoryProposalConflictCandidateStatus.Detected,
        MemoryProposalConflictCandidateStatus.ReadyForReview,
        MemoryProposalConflictCandidateStatus.NeedsEvidence,
        MemoryProposalConflictCandidateStatus.NeedsHumanReview,
        MemoryProposalConflictCandidateStatus.Quarantined,
        MemoryProposalConflictCandidateStatus.Superseded,
        MemoryProposalConflictCandidateStatus.Withdrawn
    };

    private static readonly HashSet<MemoryProposalConflictBand> AllowedBands = new()
    {
        MemoryProposalConflictBand.HighConflictRisk,
        MemoryProposalConflictBand.MediumConflictRisk,
        MemoryProposalConflictBand.LowConflictRisk,
        MemoryProposalConflictBand.DirectContradiction,
        MemoryProposalConflictBand.ScopeMismatch,
        MemoryProposalConflictBand.PolicyMismatch,
        MemoryProposalConflictBand.TerminologyMismatch,
        MemoryProposalConflictBand.Unknown
    };

    private static readonly HashSet<string> AllowedReviewNoteTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ContradictionReason",
        "ScopeMismatchReason",
        "PolicyMismatchReason",
        "BoundaryMismatchReason",
        "TerminologyMismatchReason",
        "EvidenceConflictReason",
        "PortableMemoryRisk",
        "NeedsHumanReview",
        "MoreEvidenceNeeded"
    };

    private static readonly string[] UnsafeMarkers =
    {
        "hiddenreasoning", "hidden reasoning", "chainofthought", "chain of thought", "chain-of-thought", "private reasoning", "scratchpad",
        "rawprompt", "raw prompt", "rawcompletion", "raw completion", "rawtooloutput", "raw tool output", "entirepatch", "entire patch",
        "confirmed conflict", "resolved conflict", "truth selected", "truth decision", "primary wins", "candidate invalid",
        "reject proposal", "delete proposal", "correct proposal", "accepted memory", "promoted memory", "active memory", "retrieval active",
        "indexed for retrieval", "embedding created", "vector write", "weaviate write", "policy satisfied", "authority transferred",
        "approval granted", "source applied", "release approved"
    };

    public MemoryProposalValidationResult Validate(MemoryProposalConflictCandidateCreateRequest? request)
    {
        var issues = new List<MemoryProposalValidationIssue>();
        if (request is null)
        {
            Add(issues, "request_required", "Memory proposal conflict candidate request is required.");
            return new MemoryProposalValidationResult { Issues = issues };
        }

        if (request.MemoryProposalConflictCandidateId == Guid.Empty) Add(issues, "conflict_candidate_id_required", "MemoryProposalConflictCandidateId is required.");
        if (request.ProjectId == Guid.Empty) Add(issues, "project_id_required", "ProjectId is required.");
        if (request.PrimaryMemoryProposalId == Guid.Empty) Add(issues, "primary_memory_proposal_id_required", "PrimaryMemoryProposalId is required.");
        if (request.ConflictingMemoryProposalId == Guid.Empty) Add(issues, "conflicting_memory_proposal_id_required", "ConflictingMemoryProposalId is required.");
        if (request.PrimaryMemoryProposalId != Guid.Empty && request.PrimaryMemoryProposalId == request.ConflictingMemoryProposalId) Add(issues, "proposal_ids_must_differ", "Primary and conflicting memory proposal ids must differ.");
        if (string.IsNullOrWhiteSpace(request.ConflictCandidateKey)) Add(issues, "conflict_candidate_key_required", "ConflictCandidateKey is required.");
        if (request.ConflictScore < 0m || request.ConflictScore > 1m) Add(issues, "conflict_score_invalid", "ConflictScore must be between 0 and 1.");
        if (string.IsNullOrWhiteSpace(request.SafePrimarySummary)) Add(issues, "safe_primary_summary_required", "SafePrimarySummary is required.");
        if (string.IsNullOrWhiteSpace(request.SafeConflictingSummary)) Add(issues, "safe_conflicting_summary_required", "SafeConflictingSummary is required.");
        if (string.IsNullOrWhiteSpace(request.CreatedByActorType)) Add(issues, "created_by_actor_type_required", "CreatedByActorType is required.");
        if (string.IsNullOrWhiteSpace(request.CreatedByActorId)) Add(issues, "created_by_actor_id_required", "CreatedByActorId is required.");
        if (request.MetadataVersion <= 0) Add(issues, "metadata_version_invalid", "MetadataVersion must be positive.");
        if (!IsJson(request.MetadataJson)) Add(issues, "metadata_json_invalid", "MetadataJson must be valid JSON.");
        if (!Enum.IsDefined(request.ConflictType) || !AllowedConflictTypes.Contains(request.ConflictType)) Add(issues, "conflict_type_forbidden", "ConflictType must stay within conflict-candidate review vocabulary.");
        if (!Enum.IsDefined(request.Status) || !AllowedStatuses.Contains(request.Status)) Add(issues, "status_forbidden", "Status must stay within conflict-candidate review vocabulary.");
        if (!Enum.IsDefined(request.ConflictBand) || !AllowedBands.Contains(request.ConflictBand)) Add(issues, "conflict_band_forbidden", "ConflictBand must stay within conflict-candidate review vocabulary.");
        if (HasAuthorityFlag(request)) Add(issues, "authority_flags_forbidden", "Conflict candidates cannot decide, choose truth, reject, delete, correct, merge, accept, promote, retrieve, embed, write vector storage, satisfy policy, or transfer authority.");

        ScanText(issues, "conflict_candidate_text", request.ConflictCandidateKey, request.SafePrimarySummary, request.SafeConflictingSummary, request.SafeConflictSummary, request.SafeReviewRecommendation, request.CreatedByActorType, request.CreatedByActorId, request.MetadataJson);

        foreach (var evidence in request.EvidenceReferences ?? Array.Empty<MemoryProposalConflictEvidenceReference>())
        {
            if (string.IsNullOrWhiteSpace(evidence.EvidenceType)) Add(issues, "evidence_type_required", "EvidenceType is required.");
            if (string.IsNullOrWhiteSpace(evidence.EvidenceId)) Add(issues, "evidence_id_required", "EvidenceId is required.");
            if (evidence.EvidenceIsDecision || evidence.EvidenceChoosesTruth || evidence.EvidenceRejectsProposal || evidence.EvidenceDeletesProposal || evidence.EvidenceCorrectsProposal || evidence.EvidenceAcceptsMemory) Add(issues, "evidence_authority_forbidden", "Conflict evidence references cannot decide, choose truth, reject, delete, correct, or accept memory.");
            ScanText(issues, "evidence_reference_text", evidence.EvidenceType, evidence.EvidenceId, evidence.EvidenceLabel, evidence.SafeSummary, evidence.AllowedUse);
        }

        foreach (var note in request.ReviewNotes ?? Array.Empty<MemoryProposalConflictReviewNote>())
        {
            if (string.IsNullOrWhiteSpace(note.NoteType)) Add(issues, "review_note_type_required", "Review note type is required.");
            if (!string.IsNullOrWhiteSpace(note.NoteType) && !AllowedReviewNoteTypes.Contains(note.NoteType)) Add(issues, "review_note_type_forbidden", "Review note type must stay within conflict-review vocabulary.");
            if (string.IsNullOrWhiteSpace(note.SafeSummary)) Add(issues, "review_note_summary_required", "Review note SafeSummary is required.");
            if (string.IsNullOrWhiteSpace(note.Severity)) Add(issues, "review_note_severity_required", "Review note Severity is required.");
            if (note.NoteIsDecision || note.NoteChoosesTruth || note.NoteRejectsProposal || note.NoteDeletesProposal || note.NoteCorrectsProposal || note.NoteAcceptsMemory) Add(issues, "review_note_authority_forbidden", "Conflict review notes cannot decide, choose truth, reject, delete, correct, or accept memory.");
            ScanText(issues, "review_note_text", note.NoteType, note.SafeSummary, note.Severity);
        }

        return new MemoryProposalValidationResult { Issues = issues };
    }

    public MemoryProposalConflictCandidate Normalize(MemoryProposalConflictCandidateCreateRequest request)
    {
        var result = Validate(request);
        if (!result.IsValid) throw new InvalidOperationException("Memory proposal conflict candidate request is invalid: " + string.Join("; ", result.Issues.Select(issue => issue.Code)));

        return new MemoryProposalConflictCandidate
        {
            MemoryProposalConflictCandidateId = request.MemoryProposalConflictCandidateId,
            ProjectId = request.ProjectId,
            PrimaryMemoryProposalId = request.PrimaryMemoryProposalId,
            ConflictingMemoryProposalId = request.ConflictingMemoryProposalId,
            ConflictCandidateKey = NormalizeRequired(request.ConflictCandidateKey),
            Status = request.Status,
            ConflictType = request.ConflictType,
            ConflictScore = request.ConflictScore,
            ConflictBand = request.ConflictBand,
            SafePrimarySummary = NormalizeRequired(request.SafePrimarySummary),
            SafeConflictingSummary = NormalizeRequired(request.SafeConflictingSummary),
            SafeConflictSummary = NormalizeOptional(request.SafeConflictSummary),
            SafeReviewRecommendation = NormalizeOptional(request.SafeReviewRecommendation),
            EvidenceReferences = (request.EvidenceReferences ?? Array.Empty<MemoryProposalConflictEvidenceReference>()).Select(NormalizeEvidenceReference).ToList(),
            ReviewNotes = (request.ReviewNotes ?? Array.Empty<MemoryProposalConflictReviewNote>()).Select(NormalizeReviewNote).ToList(),
            WorkflowRunId = request.WorkflowRunId,
            WorkflowRunStepId = request.WorkflowRunStepId,
            WorkflowCheckpointId = request.WorkflowCheckpointId,
            CorrelationId = request.CorrelationId,
            CausationId = request.CausationId,
            CreatedByActorType = NormalizeRequired(request.CreatedByActorType),
            CreatedByActorId = NormalizeRequired(request.CreatedByActorId),
            MetadataVersion = request.MetadataVersion,
            MetadataJson = string.IsNullOrWhiteSpace(request.MetadataJson) ? "{}" : request.MetadataJson.Trim(),
            IsDecision = false,
            ChoosesTruth = false,
            RejectsProposal = false,
            DeletesProposal = false,
            CorrectsProposal = false,
            MergesProposal = false,
            AcceptsMemory = false,
            PromotesMemory = false,
            CreatesAcceptedMemory = false,
            ActivatesRetrieval = false,
            CreatesEmbedding = false,
            WritesVectorStore = false,
            SatisfiesPolicy = false,
            TransfersAuthority = false,
            CreatedUtc = DateTimeOffset.UtcNow
        };
    }

    private static MemoryProposalConflictEvidenceReference NormalizeEvidenceReference(MemoryProposalConflictEvidenceReference reference) => new()
    {
        EvidenceType = NormalizeRequired(reference.EvidenceType),
        EvidenceId = NormalizeRequired(reference.EvidenceId),
        EvidenceLabel = NormalizeOptional(reference.EvidenceLabel),
        SafeSummary = NormalizeOptional(reference.SafeSummary),
        AllowedUse = NormalizeOptional(reference.AllowedUse),
        PrimaryMemoryProposalId = reference.PrimaryMemoryProposalId,
        ConflictingMemoryProposalId = reference.ConflictingMemoryProposalId,
        WorkflowRunId = reference.WorkflowRunId,
        WorkflowRunStepId = reference.WorkflowRunStepId,
        WorkflowCheckpointId = reference.WorkflowCheckpointId,
        EvidenceIsDecision = false,
        EvidenceChoosesTruth = false,
        EvidenceRejectsProposal = false,
        EvidenceDeletesProposal = false,
        EvidenceCorrectsProposal = false,
        EvidenceAcceptsMemory = false
    };

    private static MemoryProposalConflictReviewNote NormalizeReviewNote(MemoryProposalConflictReviewNote note) => new()
    {
        NoteType = NormalizeRequired(note.NoteType),
        SafeSummary = NormalizeRequired(note.SafeSummary),
        Severity = NormalizeRequired(note.Severity),
        NoteIsDecision = false,
        NoteChoosesTruth = false,
        NoteRejectsProposal = false,
        NoteDeletesProposal = false,
        NoteCorrectsProposal = false,
        NoteAcceptsMemory = false
    };

    private static bool HasAuthorityFlag(MemoryProposalConflictCandidateCreateRequest request) =>
        request.IsDecision || request.ChoosesTruth || request.RejectsProposal || request.DeletesProposal || request.CorrectsProposal || request.MergesProposal || request.AcceptsMemory || request.PromotesMemory || request.CreatesAcceptedMemory || request.ActivatesRetrieval || request.CreatesEmbedding || request.WritesVectorStore || request.SatisfiesPolicy || request.TransfersAuthority;

    private static void ScanText(ICollection<MemoryProposalValidationIssue> issues, string code, params string?[] values)
    {
        var text = string.Join(' ', values.Where(value => !string.IsNullOrWhiteSpace(value))).ToLowerInvariant();
        if (UnsafeMarkers.Any(text.Contains)) Add(issues, code + "_unsafe", "Conflict candidate text contains raw/private reasoning, decision language, or authority language.");
    }

    private static bool IsJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { using var _ = JsonDocument.Parse(value); return true; }
        catch (JsonException) { return false; }
    }

    private static string NormalizeRequired(string value) => value.Trim();
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void Add(ICollection<MemoryProposalValidationIssue> issues, string code, string message) => issues.Add(new MemoryProposalValidationIssue(code, message));
}

public sealed class MemoryProposalConflictDetector
{
    public IReadOnlyList<MemoryProposalConflictCandidateCreateRequest> Detect(IReadOnlyList<MemoryProposal> proposals, MemoryProposalConflictDetectionOptions? options = null)
    {
        if (proposals is null || proposals.Count < 2) return Array.Empty<MemoryProposalConflictCandidateCreateRequest>();
        var effectiveOptions = options ?? new MemoryProposalConflictDetectionOptions();
        var maxCandidateCount = Math.Clamp(effectiveOptions.MaxCandidateCount, 0, 500);
        if (maxCandidateCount == 0) return Array.Empty<MemoryProposalConflictCandidateCreateRequest>();

        var candidates = new List<MemoryProposalConflictCandidateCreateRequest>();
        foreach (var group in proposals.Where(proposal => proposal.ProjectId != Guid.Empty).GroupBy(proposal => proposal.ProjectId).OrderBy(group => group.Key))
        {
            var ordered = group.OrderBy(proposal => proposal.MemoryProposalId).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                for (var j = i + 1; j < ordered.Count; j++)
                {
                    var candidate = Compare(ordered[i], ordered[j], effectiveOptions);
                    if (candidate is not null) candidates.Add(candidate);
                    if (candidates.Count >= maxCandidateCount) return candidates;
                }
            }
        }

        return candidates;
    }

    private static MemoryProposalConflictCandidateCreateRequest? Compare(MemoryProposal primary, MemoryProposal conflicting, MemoryProposalConflictDetectionOptions options)
    {
        if (primary.MemoryProposalId == Guid.Empty || conflicting.MemoryProposalId == Guid.Empty) return null;
        if (primary.MemoryProposalId == conflicting.MemoryProposalId) return null;
        if (primary.ProjectId != conflicting.ProjectId) return null;
        var primaryText = NormalizeForComparison(primary.SafeProposedMemory);
        var conflictingText = NormalizeForComparison(conflicting.SafeProposedMemory);
        if (string.IsNullOrWhiteSpace(primaryText) || string.IsNullOrWhiteSpace(conflictingText)) return null;
        var primaryTokens = Tokenize(primaryText);
        var conflictingTokens = Tokenize(conflictingText);
        if (primaryTokens.Count == 0 || conflictingTokens.Count == 0) return null;
        var sharedScore = CalculateOverlap(primaryTokens, conflictingTokens);
        if (sharedScore < options.MinimumSharedTokenScore && !HasSpecificConflictMarker(primaryText, conflictingText)) return null;
        var classification = Classify(primaryText, conflictingText, primaryTokens, conflictingTokens, sharedScore);
        if (classification is null) return null;
        var key = BuildCandidateKey(primary.ProjectId, primary.MemoryProposalId, conflicting.MemoryProposalId, classification.Value.ConflictType);
        return new MemoryProposalConflictCandidateCreateRequest
        {
            MemoryProposalConflictCandidateId = DeterministicGuid(key),
            ProjectId = primary.ProjectId,
            PrimaryMemoryProposalId = primary.MemoryProposalId,
            ConflictingMemoryProposalId = conflicting.MemoryProposalId,
            ConflictCandidateKey = key,
            Status = MemoryProposalConflictCandidateStatus.ReadyForReview,
            ConflictType = classification.Value.ConflictType,
            ConflictScore = decimal.Round(Math.Clamp(classification.Value.Score, 0m, 1m), 4),
            ConflictBand = classification.Value.Band,
            SafePrimarySummary = SafeDetectorSummary(primary.SafeProposedMemory),
            SafeConflictingSummary = SafeDetectorSummary(conflicting.SafeProposedMemory),
            SafeConflictSummary = classification.Value.Summary,
            SafeReviewRecommendation = "Human or governed review remains required before any truth, correction, rejection, deletion, acceptance, promotion, or retrieval decision.",
            EvidenceReferences = BuildEvidenceReferences(primary, conflicting),
            ReviewNotes = BuildReviewNotes(classification.Value.NoteType, classification.Value.Summary),
            WorkflowRunId = primary.WorkflowRunId ?? conflicting.WorkflowRunId,
            WorkflowRunStepId = primary.WorkflowRunStepId ?? conflicting.WorkflowRunStepId,
            WorkflowCheckpointId = primary.WorkflowCheckpointId ?? conflicting.WorkflowCheckpointId,
            CorrelationId = primary.CorrelationId ?? conflicting.CorrelationId,
            CausationId = primary.CausationId ?? conflicting.CausationId,
            CreatedByActorType = string.IsNullOrWhiteSpace(options.CreatedByActorType) ? "system" : options.CreatedByActorType,
            CreatedByActorId = string.IsNullOrWhiteSpace(options.CreatedByActorId) ? "memory-proposal-conflict-detector" : options.CreatedByActorId,
            MetadataVersion = 1,
            MetadataJson = "{\"source\":\"memory-proposal-conflict-detector\"}"
        };
    }

    private static (MemoryProposalConflictType ConflictType, MemoryProposalConflictBand Band, decimal Score, string Summary, string NoteType)? Classify(string primaryText, string conflictingText, IReadOnlySet<string> primaryTokens, IReadOnlySet<string> conflictingTokens, decimal sharedScore)
    {
        if (LooksDirectContradiction(primaryText, conflictingText, primaryTokens, conflictingTokens)) return (MemoryProposalConflictType.DirectContradictionCandidate, MemoryProposalConflictBand.DirectContradiction, Math.Max(sharedScore, 0.90m), "Staged proposals share subject terms and contain direct contradiction wording.", "ContradictionReason");
        if (LooksMemoryBoundaryMismatch(primaryText, conflictingText)) return (MemoryProposalConflictType.IncompatibleMemoryBoundaryCandidate, MemoryProposalConflictBand.HighConflictRisk, Math.Max(sharedScore, 0.82m), "Staged proposals may conflict on memory proposal, final memory, or review-only boundaries.", "BoundaryMismatchReason");
        if (LooksPolicyMismatch(primaryText, conflictingText)) return (MemoryProposalConflictType.IncompatiblePolicyCandidate, MemoryProposalConflictBand.PolicyMismatch, Math.Max(sharedScore, 0.84m), "Staged proposals may conflict on automatic policy behavior versus human or governed review requirements.", "PolicyMismatchReason");
        if (LooksScopeMismatch(primaryText, conflictingText)) return (MemoryProposalConflictType.IncompatibleScopeCandidate, MemoryProposalConflictBand.ScopeMismatch, Math.Max(sharedScore, 0.80m), "Staged proposals may conflict on portable, project-local, or confidentiality scope.", "ScopeMismatchReason");
        if (LooksWorkflowStateMismatch(primaryText, conflictingText)) return (MemoryProposalConflictType.IncompatibleWorkflowStateCandidate, MemoryProposalConflictBand.HighConflictRisk, Math.Max(sharedScore, 0.82m), "Staged proposals may conflict on workflow state semantics.", "BoundaryMismatchReason");
        if (LooksNegationCandidate(primaryText, conflictingText, primaryTokens, conflictingTokens)) return (MemoryProposalConflictType.NegationCandidate, MemoryProposalConflictBand.HighConflictRisk, Math.Max(sharedScore, 0.78m), "Staged proposals share terms and only one contains deterministic negation wording.", "ContradictionReason");
        if (LooksTerminologyMismatch(primaryText, conflictingText)) return (MemoryProposalConflictType.ConflictingTerminologyCandidate, MemoryProposalConflictBand.TerminologyMismatch, Math.Max(sharedScore, 0.60m), "Staged proposals may use conflicting boundary terminology.", "TerminologyMismatchReason");
        return null;
    }

    private static IReadOnlyList<MemoryProposalConflictEvidenceReference> BuildEvidenceReferences(MemoryProposal primary, MemoryProposal conflicting) => new[]
    {
        new MemoryProposalConflictEvidenceReference { EvidenceType = "MemoryProposal", EvidenceId = primary.MemoryProposalId.ToString(), EvidenceLabel = "Primary staged memory proposal", SafeSummary = "Primary staged memory proposal is evidence for conflict review only.", AllowedUse = "ConflictReview", PrimaryMemoryProposalId = primary.MemoryProposalId, WorkflowRunId = primary.WorkflowRunId, WorkflowRunStepId = primary.WorkflowRunStepId, WorkflowCheckpointId = primary.WorkflowCheckpointId },
        new MemoryProposalConflictEvidenceReference { EvidenceType = "MemoryProposal", EvidenceId = conflicting.MemoryProposalId.ToString(), EvidenceLabel = "Conflicting staged memory proposal", SafeSummary = "Conflicting staged memory proposal is evidence for conflict review only.", AllowedUse = "ConflictReview", ConflictingMemoryProposalId = conflicting.MemoryProposalId, WorkflowRunId = conflicting.WorkflowRunId, WorkflowRunStepId = conflicting.WorkflowRunStepId, WorkflowCheckpointId = conflicting.WorkflowCheckpointId }
    };

    private static IReadOnlyList<MemoryProposalConflictReviewNote> BuildReviewNotes(string noteType, string summary) => new[]
    {
        new MemoryProposalConflictReviewNote { NoteType = noteType, SafeSummary = summary, Severity = "warning" },
        new MemoryProposalConflictReviewNote { NoteType = "NeedsHumanReview", SafeSummary = "Conflict score is review evidence only and is not approval, truth, rejection, deletion, correction, acceptance, or promotion.", Severity = "warning" }
    };

    private static bool LooksDirectContradiction(string left, string right, IReadOnlySet<string> leftTokens, IReadOnlySet<string> rightTokens)
    {
        if (CalculateOverlap(leftTokens, rightTokens) < 0.25m) return false;
        return PhraseMismatch(left, right, "is", "is not") || PhraseMismatch(left, right, "are", "are not") || PhraseMismatch(left, right, "must", "must not") || PhraseMismatch(left, right, "may", "may not") || PhraseMismatch(left, right, "can", "cannot") || PhraseMismatch(left, right, "should", "should not");
    }

    private static bool LooksNegationCandidate(string left, string right, IReadOnlySet<string> leftTokens, IReadOnlySet<string> rightTokens) => CalculateOverlap(leftTokens, rightTokens) >= 0.25m && HasNegation(left) != HasNegation(right);
    private static bool LooksPolicyMismatch(string left, string right) => OppositeMarkers(left, right, new[] { "automatic", "automatically", "without review", "auto" }, new[] { "human review", "governed review", "approval before", "requires review", "manual review" }) || OppositeMarkers(left, right, new[] { "policy allows", "allowed by policy" }, new[] { "policy blocks", "policy denied", "policy requires" });
    private static bool LooksScopeMismatch(string left, string right) => OppositeMarkers(left, right, new[] { "portable", "cross project", "shared across projects" }, new[] { "project confidential", "project specific", "confidential project", "agent local" });
    private static bool LooksMemoryBoundaryMismatch(string left, string right) => OppositeMarkers(left, right, new[] { "review evidence only", "proposal only", "staged proposal only", "requires review" }, new[] { "final project memory", "final memory", "active project memory", "active memory", "automatically" }) || OppositeMarkers(left, right, new[] { "candidate", "proposal" }, new[] { "final memory", "project truth" });
    private static bool LooksWorkflowStateMismatch(string left, string right) => OppositeMarkers(left, right, new[] { "resumable", "resume token", "continues workflow" }, new[] { "bookmark", "not resume", "does not resume", "not continue", "cannot continue" });
    private static bool LooksTerminologyMismatch(string left, string right) => OppositeMarkers(left, right, new[] { "retrieval match", "match" }, new[] { "memory candidate", "candidate" });
    private static bool HasSpecificConflictMarker(string left, string right) => LooksPolicyMismatch(left, right) || LooksScopeMismatch(left, right) || LooksMemoryBoundaryMismatch(left, right) || LooksWorkflowStateMismatch(left, right) || LooksTerminologyMismatch(left, right);
    private static bool OppositeMarkers(string left, string right, IReadOnlyList<string> positiveMarkers, IReadOnlyList<string> negativeMarkers) => (ContainsAny(left, positiveMarkers) && ContainsAny(right, negativeMarkers)) || (ContainsAny(right, positiveMarkers) && ContainsAny(left, negativeMarkers));
    private static bool PhraseMismatch(string left, string right, string positive, string negative) => (ContainsPhrase(left, positive) && ContainsPhrase(right, negative) && !ContainsPhrase(left, negative)) || (ContainsPhrase(right, positive) && ContainsPhrase(left, negative) && !ContainsPhrase(right, negative));
    private static bool HasNegation(string text) => ContainsPhrase(text, "do not") || ContainsPhrase(text, "must not") || ContainsPhrase(text, "should not") || ContainsPhrase(text, "may not") || ContainsPhrase(text, "are not") || ContainsPhrase(text, "is not") || ContainsPhrase(text, "cannot") || ContainsPhrase(text, "never") || ContainsPhrase(text, "forbidden") || ContainsPhrase(text, "forbid") || ContainsPhrase(text, "block");
    private static bool ContainsAny(string text, IReadOnlyList<string> markers) => markers.Any(marker => ContainsPhrase(text, marker));
    private static bool ContainsPhrase(string text, string phrase) => text.Contains(phrase, StringComparison.Ordinal);

    private static decimal CalculateOverlap(IReadOnlySet<string> left, IReadOnlySet<string> right)
    {
        var intersection = left.Intersect(right).Count();
        if (intersection == 0) return 0m;
        return (2m * intersection) / (left.Count + right.Count);
    }

    private static string NormalizeForComparison(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant()) builder.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static IReadOnlySet<string> Tokenize(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(token => token.Length > 2).ToHashSet(StringComparer.Ordinal);

    private static string SafeDetectorSummary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Staged memory proposal text omitted for conflict review.";
        var summary = value.Trim();
        foreach (var marker in DetectorSummaryRedactions) summary = ReplaceOrdinalIgnoreCase(summary, marker, "review-boundary term");
        return summary;
    }

    private static readonly string[] DetectorSummaryRedactions =
    {
        "accepted memory", "promoted memory", "active memory", "retrieval active", "indexed for retrieval", "embedding created", "vector write", "weaviate write", "policy satisfied", "authority transferred", "approval granted", "source applied", "release approved", "truth selected", "truth decision", "primary wins"
    };

    private static string ReplaceOrdinalIgnoreCase(string value, string oldValue, string newValue)
    {
        var index = value.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            value = value.Remove(index, oldValue.Length).Insert(index, newValue);
            index = value.IndexOf(oldValue, index + newValue.Length, StringComparison.OrdinalIgnoreCase);
        }
        return value;
    }

    private static string BuildCandidateKey(Guid projectId, Guid primaryId, Guid conflictingId, MemoryProposalConflictType conflictType) => $"memory-proposal-conflict:{projectId:N}:{primaryId:N}:{conflictingId:N}:{conflictType}";

    private static Guid DeterministicGuid(string seed)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(bytes.Take(16).ToArray());
    }
}
