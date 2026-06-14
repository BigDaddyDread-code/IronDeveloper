using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IronDev.Core.AgentMemory;

public enum MemoryProposalDuplicateCandidateStatus
{
    Detected,
    ReadyForReview,
    NeedsEvidence,
    NeedsHumanReview,
    Quarantined,
    Superseded,
    Withdrawn
}

public enum MemoryProposalDuplicateRelationshipType
{
    ExactTextCandidate,
    NearDuplicateCandidate,
    SameDecisionCandidate,
    SameFactCandidate,
    SameRiskCandidate,
    SameConventionCandidate,
    ContradictoryCandidate,
    OverlappingCandidate,
    RelatedButDistinctCandidate,
    NeedsHumanReview
}

public enum MemoryProposalDuplicateSimilarityBand
{
    ExactText,
    HighSimilarity,
    MediumSimilarity,
    LowSimilarity,
    RelatedOnly,
    ContradictionCandidate,
    Unknown
}

public sealed class MemoryProposalDuplicateDetectionOptions
{
    public int MaxCandidateCount { get; init; } = 50;
    public decimal MinimumSimilarityScore { get; init; } = 0.25m;
    public bool IncludeRelatedOnly { get; init; } = true;
    public bool IncludeContradictionCandidates { get; init; } = true;
    public string CreatedByActorType { get; init; } = "system";
    public string CreatedByActorId { get; init; } = "memory-proposal-duplicate-detector";
}

public sealed class MemoryProposalDuplicateCandidateCreateRequest
{
    public Guid MemoryProposalDuplicateCandidateId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid PrimaryMemoryProposalId { get; init; }
    public Guid CandidateMemoryProposalId { get; init; }
    public string DuplicateCandidateKey { get; init; } = string.Empty;
    public MemoryProposalDuplicateCandidateStatus Status { get; init; } = MemoryProposalDuplicateCandidateStatus.Detected;
    public MemoryProposalDuplicateRelationshipType RelationshipType { get; init; } = MemoryProposalDuplicateRelationshipType.NeedsHumanReview;
    public decimal SimilarityScore { get; init; }
    public MemoryProposalDuplicateSimilarityBand SimilarityBand { get; init; } = MemoryProposalDuplicateSimilarityBand.Unknown;
    public string SafePrimarySummary { get; init; } = string.Empty;
    public string SafeCandidateSummary { get; init; } = string.Empty;
    public string? SafeReasonSummary { get; init; }
    public string? SafeDifferenceSummary { get; init; }
    public IReadOnlyList<MemoryProposalDuplicateEvidenceReference> EvidenceReferences { get; init; } = Array.Empty<MemoryProposalDuplicateEvidenceReference>();
    public IReadOnlyList<MemoryProposalDuplicateReviewNote> ReviewNotes { get; init; } = Array.Empty<MemoryProposalDuplicateReviewNote>();
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
    public bool RejectsProposal { get; init; }
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

public sealed class MemoryProposalDuplicateCandidate
{
    public Guid MemoryProposalDuplicateCandidateId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid PrimaryMemoryProposalId { get; init; }
    public Guid CandidateMemoryProposalId { get; init; }
    public string DuplicateCandidateKey { get; init; } = string.Empty;
    public MemoryProposalDuplicateCandidateStatus Status { get; init; }
    public MemoryProposalDuplicateRelationshipType RelationshipType { get; init; }
    public decimal SimilarityScore { get; init; }
    public MemoryProposalDuplicateSimilarityBand SimilarityBand { get; init; }
    public string SafePrimarySummary { get; init; } = string.Empty;
    public string SafeCandidateSummary { get; init; } = string.Empty;
    public string? SafeReasonSummary { get; init; }
    public string? SafeDifferenceSummary { get; init; }
    public IReadOnlyList<MemoryProposalDuplicateEvidenceReference> EvidenceReferences { get; init; } = Array.Empty<MemoryProposalDuplicateEvidenceReference>();
    public IReadOnlyList<MemoryProposalDuplicateReviewNote> ReviewNotes { get; init; } = Array.Empty<MemoryProposalDuplicateReviewNote>();
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
    public bool RejectsProposal { get; init; }
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

public sealed class MemoryProposalDuplicateEvidenceReference
{
    public string EvidenceType { get; init; } = string.Empty;
    public string EvidenceId { get; init; } = string.Empty;
    public string? EvidenceLabel { get; init; }
    public string? SafeSummary { get; init; }
    public string? AllowedUse { get; init; }
    public Guid? PrimaryMemoryProposalId { get; init; }
    public Guid? CandidateMemoryProposalId { get; init; }
    public Guid? WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public bool EvidenceIsDecision { get; init; }
    public bool EvidenceMergesProposal { get; init; }
    public bool EvidenceRejectsProposal { get; init; }
    public bool EvidenceAcceptsMemory { get; init; }
}

public sealed class MemoryProposalDuplicateReviewNote
{
    public string NoteType { get; init; } = string.Empty;
    public string SafeSummary { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public bool NoteIsDecision { get; init; }
    public bool NoteMergesProposal { get; init; }
    public bool NoteRejectsProposal { get; init; }
    public bool NoteAcceptsMemory { get; init; }
}

public sealed class MemoryProposalDuplicateCandidateValidator
{
    private static readonly HashSet<MemoryProposalDuplicateRelationshipType> AllowedRelationships = new()
    {
        MemoryProposalDuplicateRelationshipType.ExactTextCandidate,
        MemoryProposalDuplicateRelationshipType.NearDuplicateCandidate,
        MemoryProposalDuplicateRelationshipType.SameDecisionCandidate,
        MemoryProposalDuplicateRelationshipType.SameFactCandidate,
        MemoryProposalDuplicateRelationshipType.SameRiskCandidate,
        MemoryProposalDuplicateRelationshipType.SameConventionCandidate,
        MemoryProposalDuplicateRelationshipType.ContradictoryCandidate,
        MemoryProposalDuplicateRelationshipType.OverlappingCandidate,
        MemoryProposalDuplicateRelationshipType.RelatedButDistinctCandidate,
        MemoryProposalDuplicateRelationshipType.NeedsHumanReview
    };

    private static readonly HashSet<MemoryProposalDuplicateCandidateStatus> AllowedStatuses = new()
    {
        MemoryProposalDuplicateCandidateStatus.Detected,
        MemoryProposalDuplicateCandidateStatus.ReadyForReview,
        MemoryProposalDuplicateCandidateStatus.NeedsEvidence,
        MemoryProposalDuplicateCandidateStatus.NeedsHumanReview,
        MemoryProposalDuplicateCandidateStatus.Quarantined,
        MemoryProposalDuplicateCandidateStatus.Superseded,
        MemoryProposalDuplicateCandidateStatus.Withdrawn
    };

    private static readonly HashSet<MemoryProposalDuplicateSimilarityBand> AllowedBands = new()
    {
        MemoryProposalDuplicateSimilarityBand.ExactText,
        MemoryProposalDuplicateSimilarityBand.HighSimilarity,
        MemoryProposalDuplicateSimilarityBand.MediumSimilarity,
        MemoryProposalDuplicateSimilarityBand.LowSimilarity,
        MemoryProposalDuplicateSimilarityBand.RelatedOnly,
        MemoryProposalDuplicateSimilarityBand.ContradictionCandidate,
        MemoryProposalDuplicateSimilarityBand.Unknown
    };

    private static readonly HashSet<string> AllowedReviewNoteTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SimilarityReason",
        "DifferenceReason",
        "ContradictionRisk",
        "StalenessRisk",
        "ScopeMismatch",
        "ConfidentialityMismatch",
        "NeedsHumanReview",
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
        "confirmed duplicate",
        "merge approved",
        "auto merge",
        "merged proposal",
        "reject duplicate",
        "auto reject",
        "accepted memory",
        "promoted memory",
        "active memory",
        "truth selected",
        "winner selected",
        "primary wins",
        "candidate discarded",
        "retrieval active",
        "indexed for retrieval",
        "embedding created",
        "vector write",
        "weaviate write",
        "policy satisfied",
        "authority transferred",
        "approval granted",
        "source applied",
        "release approved"
    };

    public MemoryProposalValidationResult Validate(MemoryProposalDuplicateCandidateCreateRequest? request)
    {
        var issues = new List<MemoryProposalValidationIssue>();
        if (request is null)
        {
            Add(issues, "request_required", "Memory proposal duplicate candidate request is required.");
            return new MemoryProposalValidationResult { Issues = issues };
        }

        if (request.MemoryProposalDuplicateCandidateId == Guid.Empty) Add(issues, "duplicate_candidate_id_required", "MemoryProposalDuplicateCandidateId is required.");
        if (request.ProjectId == Guid.Empty) Add(issues, "project_id_required", "ProjectId is required.");
        if (request.PrimaryMemoryProposalId == Guid.Empty) Add(issues, "primary_memory_proposal_id_required", "PrimaryMemoryProposalId is required.");
        if (request.CandidateMemoryProposalId == Guid.Empty) Add(issues, "candidate_memory_proposal_id_required", "CandidateMemoryProposalId is required.");
        if (request.PrimaryMemoryProposalId != Guid.Empty && request.PrimaryMemoryProposalId == request.CandidateMemoryProposalId) Add(issues, "proposal_ids_must_differ", "Primary and candidate memory proposal ids must differ.");
        if (string.IsNullOrWhiteSpace(request.DuplicateCandidateKey)) Add(issues, "duplicate_candidate_key_required", "DuplicateCandidateKey is required.");
        if (request.SimilarityScore < 0m || request.SimilarityScore > 1m) Add(issues, "similarity_score_invalid", "SimilarityScore must be between 0 and 1.");
        if (string.IsNullOrWhiteSpace(request.SafePrimarySummary)) Add(issues, "safe_primary_summary_required", "SafePrimarySummary is required.");
        if (string.IsNullOrWhiteSpace(request.SafeCandidateSummary)) Add(issues, "safe_candidate_summary_required", "SafeCandidateSummary is required.");
        if (string.IsNullOrWhiteSpace(request.CreatedByActorType)) Add(issues, "created_by_actor_type_required", "CreatedByActorType is required.");
        if (string.IsNullOrWhiteSpace(request.CreatedByActorId)) Add(issues, "created_by_actor_id_required", "CreatedByActorId is required.");
        if (request.MetadataVersion <= 0) Add(issues, "metadata_version_invalid", "MetadataVersion must be positive.");
        if (!IsJson(request.MetadataJson)) Add(issues, "metadata_json_invalid", "MetadataJson must be valid JSON.");

        if (!Enum.IsDefined(request.RelationshipType) || !AllowedRelationships.Contains(request.RelationshipType)) Add(issues, "relationship_type_forbidden", "RelationshipType must stay within duplicate-candidate review vocabulary.");
        if (!Enum.IsDefined(request.Status) || !AllowedStatuses.Contains(request.Status)) Add(issues, "status_forbidden", "Status must stay within duplicate-candidate review vocabulary.");
        if (!Enum.IsDefined(request.SimilarityBand) || !AllowedBands.Contains(request.SimilarityBand)) Add(issues, "similarity_band_forbidden", "SimilarityBand must stay within duplicate-candidate review vocabulary.");

        if (HasAuthorityFlag(request))
        {
            Add(issues, "authority_flags_forbidden", "Duplicate candidates cannot decide, reject, merge, accept, promote, retrieve, embed, write vector storage, satisfy policy, or transfer authority.");
        }

        ScanText(issues, "duplicate_candidate_text", request.DuplicateCandidateKey, request.SafePrimarySummary, request.SafeCandidateSummary, request.SafeReasonSummary, request.SafeDifferenceSummary, request.CreatedByActorType, request.CreatedByActorId, request.MetadataJson);

        foreach (var evidence in request.EvidenceReferences ?? Array.Empty<MemoryProposalDuplicateEvidenceReference>())
        {
            if (string.IsNullOrWhiteSpace(evidence.EvidenceType)) Add(issues, "evidence_type_required", "EvidenceType is required.");
            if (string.IsNullOrWhiteSpace(evidence.EvidenceId)) Add(issues, "evidence_id_required", "EvidenceId is required.");
            if (evidence.EvidenceIsDecision || evidence.EvidenceMergesProposal || evidence.EvidenceRejectsProposal || evidence.EvidenceAcceptsMemory) Add(issues, "evidence_authority_forbidden", "Duplicate evidence references cannot decide, merge, reject, or accept memory.");
            ScanText(issues, "evidence_reference_text", evidence.EvidenceType, evidence.EvidenceId, evidence.EvidenceLabel, evidence.SafeSummary, evidence.AllowedUse);
        }

        foreach (var note in request.ReviewNotes ?? Array.Empty<MemoryProposalDuplicateReviewNote>())
        {
            if (string.IsNullOrWhiteSpace(note.NoteType)) Add(issues, "review_note_type_required", "Review note type is required.");
            if (!string.IsNullOrWhiteSpace(note.NoteType) && !AllowedReviewNoteTypes.Contains(note.NoteType)) Add(issues, "review_note_type_forbidden", "Review note type must stay within duplicate-review vocabulary.");
            if (string.IsNullOrWhiteSpace(note.SafeSummary)) Add(issues, "review_note_summary_required", "Review note SafeSummary is required.");
            if (string.IsNullOrWhiteSpace(note.Severity)) Add(issues, "review_note_severity_required", "Review note Severity is required.");
            if (note.NoteIsDecision || note.NoteMergesProposal || note.NoteRejectsProposal || note.NoteAcceptsMemory) Add(issues, "review_note_authority_forbidden", "Duplicate review notes cannot decide, merge, reject, or accept memory.");
            ScanText(issues, "review_note_text", note.NoteType, note.SafeSummary, note.Severity);
        }

        return new MemoryProposalValidationResult { Issues = issues };
    }

    public MemoryProposalDuplicateCandidate Normalize(MemoryProposalDuplicateCandidateCreateRequest request)
    {
        var result = Validate(request);
        if (!result.IsValid)
        {
            throw new InvalidOperationException("Memory proposal duplicate candidate request is invalid: " + string.Join("; ", result.Issues.Select(issue => issue.Code)));
        }

        return new MemoryProposalDuplicateCandidate
        {
            MemoryProposalDuplicateCandidateId = request.MemoryProposalDuplicateCandidateId,
            ProjectId = request.ProjectId,
            PrimaryMemoryProposalId = request.PrimaryMemoryProposalId,
            CandidateMemoryProposalId = request.CandidateMemoryProposalId,
            DuplicateCandidateKey = NormalizeRequired(request.DuplicateCandidateKey),
            Status = request.Status,
            RelationshipType = request.RelationshipType,
            SimilarityScore = request.SimilarityScore,
            SimilarityBand = request.SimilarityBand,
            SafePrimarySummary = NormalizeRequired(request.SafePrimarySummary),
            SafeCandidateSummary = NormalizeRequired(request.SafeCandidateSummary),
            SafeReasonSummary = NormalizeOptional(request.SafeReasonSummary),
            SafeDifferenceSummary = NormalizeOptional(request.SafeDifferenceSummary),
            EvidenceReferences = (request.EvidenceReferences ?? Array.Empty<MemoryProposalDuplicateEvidenceReference>()).Select(NormalizeEvidenceReference).ToList(),
            ReviewNotes = (request.ReviewNotes ?? Array.Empty<MemoryProposalDuplicateReviewNote>()).Select(NormalizeReviewNote).ToList(),
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
            RejectsProposal = false,
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

    private static MemoryProposalDuplicateEvidenceReference NormalizeEvidenceReference(MemoryProposalDuplicateEvidenceReference reference) => new()
    {
        EvidenceType = NormalizeRequired(reference.EvidenceType),
        EvidenceId = NormalizeRequired(reference.EvidenceId),
        EvidenceLabel = NormalizeOptional(reference.EvidenceLabel),
        SafeSummary = NormalizeOptional(reference.SafeSummary),
        AllowedUse = NormalizeOptional(reference.AllowedUse),
        PrimaryMemoryProposalId = reference.PrimaryMemoryProposalId,
        CandidateMemoryProposalId = reference.CandidateMemoryProposalId,
        WorkflowRunId = reference.WorkflowRunId,
        WorkflowRunStepId = reference.WorkflowRunStepId,
        WorkflowCheckpointId = reference.WorkflowCheckpointId,
        EvidenceIsDecision = false,
        EvidenceMergesProposal = false,
        EvidenceRejectsProposal = false,
        EvidenceAcceptsMemory = false
    };

    private static MemoryProposalDuplicateReviewNote NormalizeReviewNote(MemoryProposalDuplicateReviewNote note) => new()
    {
        NoteType = NormalizeRequired(note.NoteType),
        SafeSummary = NormalizeRequired(note.SafeSummary),
        Severity = NormalizeRequired(note.Severity),
        NoteIsDecision = false,
        NoteMergesProposal = false,
        NoteRejectsProposal = false,
        NoteAcceptsMemory = false
    };

    private static bool HasAuthorityFlag(MemoryProposalDuplicateCandidateCreateRequest request) =>
        request.IsDecision ||
        request.RejectsProposal ||
        request.MergesProposal ||
        request.AcceptsMemory ||
        request.PromotesMemory ||
        request.CreatesAcceptedMemory ||
        request.ActivatesRetrieval ||
        request.CreatesEmbedding ||
        request.WritesVectorStore ||
        request.SatisfiesPolicy ||
        request.TransfersAuthority;

    private static void ScanText(ICollection<MemoryProposalValidationIssue> issues, string code, params string?[] values)
    {
        var text = string.Join(' ', values.Where(value => !string.IsNullOrWhiteSpace(value))).ToLowerInvariant();
        if (UnsafeMarkers.Any(text.Contains)) Add(issues, code + "_unsafe", "Duplicate candidate text contains raw/private reasoning, decision language, or authority language.");
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
    private static void Add(ICollection<MemoryProposalValidationIssue> issues, string code, string message) => issues.Add(new MemoryProposalValidationIssue(code, message));
}

public sealed class MemoryProposalDuplicateDetector
{
    private static readonly string[] RelatedTokens =
    {
        "approval",
        "audit",
        "candidate",
        "evidence",
        "failure",
        "gate",
        "governance",
        "handoff",
        "memory",
        "policy",
        "proposal",
        "receipt",
        "review",
        "source",
        "staging",
        "validation",
        "workflow"
    };

    public IReadOnlyList<MemoryProposalDuplicateCandidateCreateRequest> Detect(
        IReadOnlyList<MemoryProposal> proposals,
        MemoryProposalDuplicateDetectionOptions? options = null)
    {
        if (proposals is null || proposals.Count < 2) return Array.Empty<MemoryProposalDuplicateCandidateCreateRequest>();

        var effectiveOptions = options ?? new MemoryProposalDuplicateDetectionOptions();
        var maxCandidateCount = Math.Clamp(effectiveOptions.MaxCandidateCount, 0, 500);
        if (maxCandidateCount == 0) return Array.Empty<MemoryProposalDuplicateCandidateCreateRequest>();

        var candidates = new List<MemoryProposalDuplicateCandidateCreateRequest>();
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

    private static MemoryProposalDuplicateCandidateCreateRequest? Compare(
        MemoryProposal primary,
        MemoryProposal candidate,
        MemoryProposalDuplicateDetectionOptions options)
    {
        if (primary.MemoryProposalId == Guid.Empty || candidate.MemoryProposalId == Guid.Empty) return null;
        if (primary.MemoryProposalId == candidate.MemoryProposalId) return null;
        if (primary.ProjectId != candidate.ProjectId) return null;

        var primaryText = NormalizeForComparison(primary.SafeProposedMemory);
        var candidateText = NormalizeForComparison(candidate.SafeProposedMemory);
        if (string.IsNullOrWhiteSpace(primaryText) || string.IsNullOrWhiteSpace(candidateText)) return null;

        var primaryTokens = Tokenize(primaryText);
        var candidateTokens = Tokenize(candidateText);
        if (primaryTokens.Count == 0 || candidateTokens.Count == 0) return null;

        var overlap = CalculateOverlap(primaryTokens, candidateTokens);
        var contradiction = options.IncludeContradictionCandidates && LooksContradictory(primaryText, candidateText, primaryTokens, candidateTokens);
        var exact = string.Equals(primaryText, candidateText, StringComparison.Ordinal);
        var related = options.IncludeRelatedOnly && IsRelated(primaryTokens, candidateTokens);

        var band = ClassifyBand(exact, contradiction, overlap, related);
        if (band == MemoryProposalDuplicateSimilarityBand.Unknown) return null;
        if (band == MemoryProposalDuplicateSimilarityBand.RelatedOnly && !options.IncludeRelatedOnly) return null;
        if (band != MemoryProposalDuplicateSimilarityBand.ContradictionCandidate && band != MemoryProposalDuplicateSimilarityBand.RelatedOnly && overlap < options.MinimumSimilarityScore) return null;

        var relationship = ClassifyRelationship(band, overlap);
        var score = band == MemoryProposalDuplicateSimilarityBand.ContradictionCandidate ? Math.Max(overlap, 0.50m) : overlap;
        var key = BuildCandidateKey(primary.ProjectId, primary.MemoryProposalId, candidate.MemoryProposalId);

        return new MemoryProposalDuplicateCandidateCreateRequest
        {
            MemoryProposalDuplicateCandidateId = DeterministicGuid(key),
            ProjectId = primary.ProjectId,
            PrimaryMemoryProposalId = primary.MemoryProposalId,
            CandidateMemoryProposalId = candidate.MemoryProposalId,
            DuplicateCandidateKey = key,
            Status = MemoryProposalDuplicateCandidateStatus.ReadyForReview,
            RelationshipType = relationship,
            SimilarityScore = decimal.Round(Math.Clamp(score, 0m, 1m), 4),
            SimilarityBand = band,
            SafePrimarySummary = primary.SafeProposedMemory,
            SafeCandidateSummary = candidate.SafeProposedMemory,
            SafeReasonSummary = BuildReasonSummary(band, overlap),
            SafeDifferenceSummary = "Potential duplicate relationship needs human or governed review before any merge, rejection, acceptance, or promotion decision.",
            EvidenceReferences = BuildEvidenceReferences(primary, candidate),
            ReviewNotes = BuildReviewNotes(band),
            WorkflowRunId = primary.WorkflowRunId ?? candidate.WorkflowRunId,
            WorkflowRunStepId = primary.WorkflowRunStepId ?? candidate.WorkflowRunStepId,
            WorkflowCheckpointId = primary.WorkflowCheckpointId ?? candidate.WorkflowCheckpointId,
            CorrelationId = primary.CorrelationId ?? candidate.CorrelationId,
            CausationId = primary.CausationId ?? candidate.CausationId,
            CreatedByActorType = string.IsNullOrWhiteSpace(options.CreatedByActorType) ? "system" : options.CreatedByActorType,
            CreatedByActorId = string.IsNullOrWhiteSpace(options.CreatedByActorId) ? "memory-proposal-duplicate-detector" : options.CreatedByActorId,
            MetadataVersion = 1,
            MetadataJson = "{\"source\":\"memory-proposal-duplicate-detector\"}"
        };
    }

    private static IReadOnlyList<MemoryProposalDuplicateEvidenceReference> BuildEvidenceReferences(MemoryProposal primary, MemoryProposal candidate) => new[]
    {
        new MemoryProposalDuplicateEvidenceReference
        {
            EvidenceType = "MemoryProposal",
            EvidenceId = primary.MemoryProposalId.ToString(),
            EvidenceLabel = "Primary staged memory proposal",
            SafeSummary = "Primary staged memory proposal is evidence for duplicate review only.",
            AllowedUse = "DuplicateReview",
            PrimaryMemoryProposalId = primary.MemoryProposalId,
            WorkflowRunId = primary.WorkflowRunId,
            WorkflowRunStepId = primary.WorkflowRunStepId,
            WorkflowCheckpointId = primary.WorkflowCheckpointId
        },
        new MemoryProposalDuplicateEvidenceReference
        {
            EvidenceType = "MemoryProposal",
            EvidenceId = candidate.MemoryProposalId.ToString(),
            EvidenceLabel = "Candidate staged memory proposal",
            SafeSummary = "Candidate staged memory proposal is evidence for duplicate review only.",
            AllowedUse = "DuplicateReview",
            CandidateMemoryProposalId = candidate.MemoryProposalId,
            WorkflowRunId = candidate.WorkflowRunId,
            WorkflowRunStepId = candidate.WorkflowRunStepId,
            WorkflowCheckpointId = candidate.WorkflowCheckpointId
        }
    };

    private static IReadOnlyList<MemoryProposalDuplicateReviewNote> BuildReviewNotes(MemoryProposalDuplicateSimilarityBand band)
    {
        var notes = new List<MemoryProposalDuplicateReviewNote>
        {
            new()
            {
                NoteType = "SimilarityReason",
                SafeSummary = "Similarity score is review evidence only and is not approval or truth.",
                Severity = "info"
            },
            new()
            {
                NoteType = "NeedsHumanReview",
                SafeSummary = "Human or governed review remains required before any proposal merge, rejection, acceptance, or promotion decision.",
                Severity = "warning"
            }
        };

        if (band == MemoryProposalDuplicateSimilarityBand.ContradictionCandidate)
        {
            notes.Add(new MemoryProposalDuplicateReviewNote
            {
                NoteType = "ContradictionRisk",
                SafeSummary = "The proposals may conflict and require review before any memory decision.",
                Severity = "warning"
            });
        }

        return notes;
    }

    private static string BuildReasonSummary(MemoryProposalDuplicateSimilarityBand band, decimal overlap) => band switch
    {
        MemoryProposalDuplicateSimilarityBand.ExactText => "Normalized proposal text is identical.",
        MemoryProposalDuplicateSimilarityBand.HighSimilarity => "Normalized proposal text has high token overlap.",
        MemoryProposalDuplicateSimilarityBand.MediumSimilarity => "Normalized proposal text has medium token overlap.",
        MemoryProposalDuplicateSimilarityBand.LowSimilarity => "Normalized proposal text has low token overlap and needs review.",
        MemoryProposalDuplicateSimilarityBand.RelatedOnly => "Proposal text shares governance-domain terms and may be related but distinct.",
        MemoryProposalDuplicateSimilarityBand.ContradictionCandidate => "Proposal text shares terms but contains a possible contradiction marker.",
        _ => "Duplicate relationship is unknown."
    } + $" Similarity score: {decimal.Round(overlap, 4)}.";

    private static MemoryProposalDuplicateSimilarityBand ClassifyBand(bool exact, bool contradiction, decimal overlap, bool related)
    {
        if (exact) return MemoryProposalDuplicateSimilarityBand.ExactText;
        if (contradiction) return MemoryProposalDuplicateSimilarityBand.ContradictionCandidate;
        if (overlap >= 0.75m) return MemoryProposalDuplicateSimilarityBand.HighSimilarity;
        if (overlap >= 0.50m) return MemoryProposalDuplicateSimilarityBand.MediumSimilarity;
        if (overlap >= 0.30m) return MemoryProposalDuplicateSimilarityBand.LowSimilarity;
        if (related) return MemoryProposalDuplicateSimilarityBand.RelatedOnly;
        return MemoryProposalDuplicateSimilarityBand.Unknown;
    }

    private static MemoryProposalDuplicateRelationshipType ClassifyRelationship(MemoryProposalDuplicateSimilarityBand band, decimal overlap) => band switch
    {
        MemoryProposalDuplicateSimilarityBand.ExactText => MemoryProposalDuplicateRelationshipType.ExactTextCandidate,
        MemoryProposalDuplicateSimilarityBand.HighSimilarity => MemoryProposalDuplicateRelationshipType.NearDuplicateCandidate,
        MemoryProposalDuplicateSimilarityBand.MediumSimilarity => overlap >= 0.60m ? MemoryProposalDuplicateRelationshipType.SameFactCandidate : MemoryProposalDuplicateRelationshipType.OverlappingCandidate,
        MemoryProposalDuplicateSimilarityBand.LowSimilarity => MemoryProposalDuplicateRelationshipType.OverlappingCandidate,
        MemoryProposalDuplicateSimilarityBand.RelatedOnly => MemoryProposalDuplicateRelationshipType.RelatedButDistinctCandidate,
        MemoryProposalDuplicateSimilarityBand.ContradictionCandidate => MemoryProposalDuplicateRelationshipType.ContradictoryCandidate,
        _ => MemoryProposalDuplicateRelationshipType.NeedsHumanReview
    };

    private static decimal CalculateOverlap(IReadOnlySet<string> left, IReadOnlySet<string> right)
    {
        var intersection = left.Intersect(right).Count();
        if (intersection == 0) return 0m;
        return (2m * intersection) / (left.Count + right.Count);
    }

    private static bool IsRelated(IReadOnlySet<string> left, IReadOnlySet<string> right)
    {
        var sharedRelatedTokens = RelatedTokens.Count(token => left.Contains(token) && right.Contains(token));
        return sharedRelatedTokens >= 2;
    }

    private static bool LooksContradictory(string leftText, string rightText, IReadOnlySet<string> leftTokens, IReadOnlySet<string> rightTokens)
    {
        var overlap = CalculateOverlap(leftTokens, rightTokens);
        if (overlap < 0.25m) return false;

        return HasNegation(leftText) != HasNegation(rightText) && leftTokens.Intersect(rightTokens).Count() >= 2;
    }

    private static bool HasNegation(string text) =>
        ContainsPhrase(text, "do not") ||
        ContainsPhrase(text, "must not") ||
        ContainsPhrase(text, "should not") ||
        ContainsPhrase(text, "cannot") ||
        ContainsPhrase(text, "never") ||
        ContainsPhrase(text, "disable") ||
        ContainsPhrase(text, "forbid") ||
        ContainsPhrase(text, "block");

    private static bool ContainsPhrase(string text, string phrase) => text.Contains(phrase, StringComparison.Ordinal);

    private static string NormalizeForComparison(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static IReadOnlySet<string> Tokenize(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length > 2)
            .ToHashSet(StringComparer.Ordinal);

    private static string BuildCandidateKey(Guid projectId, Guid primaryId, Guid candidateId) =>
        $"memory-proposal-duplicate:{projectId:N}:{primaryId:N}:{candidateId:N}";

    private static Guid DeterministicGuid(string seed)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(bytes.Take(16).ToArray());
    }
}
