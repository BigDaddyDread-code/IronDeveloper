using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IronDev.Core.AgentMemory;

public enum MemoryProposalStaleCandidateStatus
{
    Detected,
    ReadyForReview,
    NeedsEvidence,
    NeedsHumanReview,
    Quarantined,
    Superseded,
    Withdrawn
}

public enum MemoryProposalStaleReasonType
{
    AgeCandidate,
    SupersededByProposalCandidate,
    ContradictedByProposalCandidate,
    DeprecatedTermCandidate,
    DeprecatedDecisionCandidate,
    ObsoleteWorkflowStateCandidate,
    MissingCurrentEvidenceCandidate,
    ConflictingEvidenceCandidate,
    ProjectScopeChangedCandidate,
    PolicyShapeChangedCandidate,
    ImplementationChangedCandidate,
    NeedsHumanFreshnessReview
}

public enum MemoryProposalStalenessBand
{
    HighStalenessRisk,
    MediumStalenessRisk,
    LowStalenessRisk,
    ContradictionCandidate,
    SupersessionCandidate,
    AgeOnlyCandidate,
    Unknown
}

public sealed class MemoryProposalStaleDetectionOptions
{
    public DateTimeOffset CurrentUtc { get; init; } = DateTimeOffset.UtcNow;
    public TimeSpan AgeThreshold { get; init; } = TimeSpan.FromDays(180);
    public TimeSpan CurrentEvidenceThreshold { get; init; } = TimeSpan.FromDays(120);
    public int MaxCandidateCount { get; init; } = 50;
    public bool IncludeWithdrawnOrQuarantined { get; init; }
    public string CreatedByActorType { get; init; } = "system";
    public string CreatedByActorId { get; init; } = "memory-proposal-stale-detector";
}

public sealed class MemoryProposalStaleCandidateCreateRequest
{
    public Guid MemoryProposalStaleCandidateId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid MemoryProposalId { get; init; }
    public string StaleCandidateKey { get; init; } = string.Empty;
    public MemoryProposalStaleCandidateStatus Status { get; init; } = MemoryProposalStaleCandidateStatus.Detected;
    public MemoryProposalStaleReasonType ReasonType { get; init; } = MemoryProposalStaleReasonType.NeedsHumanFreshnessReview;
    public decimal StalenessScore { get; init; }
    public MemoryProposalStalenessBand StalenessBand { get; init; } = MemoryProposalStalenessBand.Unknown;
    public string SafeProposalSummary { get; init; } = string.Empty;
    public string? SafeReasonSummary { get; init; }
    public string? SafeFreshnessRiskSummary { get; init; }
    public string? SafeReviewRecommendation { get; init; }
    public DateTimeOffset? ProposalCreatedUtc { get; init; }
    public DateTimeOffset? LastSupportingEvidenceUtc { get; init; }
    public DateTimeOffset? PossibleSupersededUtc { get; init; }
    public IReadOnlyList<MemoryProposalStaleEvidenceReference> EvidenceReferences { get; init; } = Array.Empty<MemoryProposalStaleEvidenceReference>();
    public IReadOnlyList<MemoryProposalStaleReviewNote> ReviewNotes { get; init; } = Array.Empty<MemoryProposalStaleReviewNote>();
    public Guid? SupersedingMemoryProposalId { get; init; }
    public Guid? ContradictingMemoryProposalId { get; init; }
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
    public bool DeletesProposal { get; init; }
    public bool CorrectsProposal { get; init; }
    public bool MarksProposalStale { get; init; }
    public bool AcceptsMemory { get; init; }
    public bool PromotesMemory { get; init; }
    public bool CreatesAcceptedMemory { get; init; }
    public bool ActivatesRetrieval { get; init; }
    public bool CreatesEmbedding { get; init; }
    public bool WritesVectorStore { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool TransfersAuthority { get; init; }
}

public sealed class MemoryProposalStaleCandidate
{
    public Guid MemoryProposalStaleCandidateId { get; init; }
    public Guid ProjectId { get; init; }
    public Guid MemoryProposalId { get; init; }
    public string StaleCandidateKey { get; init; } = string.Empty;
    public MemoryProposalStaleCandidateStatus Status { get; init; }
    public MemoryProposalStaleReasonType ReasonType { get; init; }
    public decimal StalenessScore { get; init; }
    public MemoryProposalStalenessBand StalenessBand { get; init; }
    public string SafeProposalSummary { get; init; } = string.Empty;
    public string? SafeReasonSummary { get; init; }
    public string? SafeFreshnessRiskSummary { get; init; }
    public string? SafeReviewRecommendation { get; init; }
    public DateTimeOffset? ProposalCreatedUtc { get; init; }
    public DateTimeOffset? LastSupportingEvidenceUtc { get; init; }
    public DateTimeOffset? PossibleSupersededUtc { get; init; }
    public IReadOnlyList<MemoryProposalStaleEvidenceReference> EvidenceReferences { get; init; } = Array.Empty<MemoryProposalStaleEvidenceReference>();
    public IReadOnlyList<MemoryProposalStaleReviewNote> ReviewNotes { get; init; } = Array.Empty<MemoryProposalStaleReviewNote>();
    public Guid? SupersedingMemoryProposalId { get; init; }
    public Guid? ContradictingMemoryProposalId { get; init; }
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
    public bool DeletesProposal { get; init; }
    public bool CorrectsProposal { get; init; }
    public bool MarksProposalStale { get; init; }
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

public sealed class MemoryProposalStaleEvidenceReference
{
    public string EvidenceType { get; init; } = string.Empty;
    public string EvidenceId { get; init; } = string.Empty;
    public string? EvidenceLabel { get; init; }
    public string? SafeSummary { get; init; }
    public string? AllowedUse { get; init; }
    public Guid? MemoryProposalId { get; init; }
    public Guid? SupersedingMemoryProposalId { get; init; }
    public Guid? ContradictingMemoryProposalId { get; init; }
    public Guid? WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public bool EvidenceIsDecision { get; init; }
    public bool EvidenceRejectsProposal { get; init; }
    public bool EvidenceDeletesProposal { get; init; }
    public bool EvidenceCorrectsProposal { get; init; }
    public bool EvidenceAcceptsMemory { get; init; }
}

public sealed class MemoryProposalStaleReviewNote
{
    public string NoteType { get; init; } = string.Empty;
    public string SafeSummary { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public bool NoteIsDecision { get; init; }
    public bool NoteRejectsProposal { get; init; }
    public bool NoteDeletesProposal { get; init; }
    public bool NoteCorrectsProposal { get; init; }
    public bool NoteAcceptsMemory { get; init; }
}

public sealed class MemoryProposalStaleCandidateValidator
{
    private static readonly HashSet<MemoryProposalStaleReasonType> AllowedReasons = new()
    {
        MemoryProposalStaleReasonType.AgeCandidate,
        MemoryProposalStaleReasonType.SupersededByProposalCandidate,
        MemoryProposalStaleReasonType.ContradictedByProposalCandidate,
        MemoryProposalStaleReasonType.DeprecatedTermCandidate,
        MemoryProposalStaleReasonType.DeprecatedDecisionCandidate,
        MemoryProposalStaleReasonType.ObsoleteWorkflowStateCandidate,
        MemoryProposalStaleReasonType.MissingCurrentEvidenceCandidate,
        MemoryProposalStaleReasonType.ConflictingEvidenceCandidate,
        MemoryProposalStaleReasonType.ProjectScopeChangedCandidate,
        MemoryProposalStaleReasonType.PolicyShapeChangedCandidate,
        MemoryProposalStaleReasonType.ImplementationChangedCandidate,
        MemoryProposalStaleReasonType.NeedsHumanFreshnessReview
    };

    private static readonly HashSet<MemoryProposalStaleCandidateStatus> AllowedStatuses = new()
    {
        MemoryProposalStaleCandidateStatus.Detected,
        MemoryProposalStaleCandidateStatus.ReadyForReview,
        MemoryProposalStaleCandidateStatus.NeedsEvidence,
        MemoryProposalStaleCandidateStatus.NeedsHumanReview,
        MemoryProposalStaleCandidateStatus.Quarantined,
        MemoryProposalStaleCandidateStatus.Superseded,
        MemoryProposalStaleCandidateStatus.Withdrawn
    };

    private static readonly HashSet<MemoryProposalStalenessBand> AllowedBands = new()
    {
        MemoryProposalStalenessBand.HighStalenessRisk,
        MemoryProposalStalenessBand.MediumStalenessRisk,
        MemoryProposalStalenessBand.LowStalenessRisk,
        MemoryProposalStalenessBand.ContradictionCandidate,
        MemoryProposalStalenessBand.SupersessionCandidate,
        MemoryProposalStalenessBand.AgeOnlyCandidate,
        MemoryProposalStalenessBand.Unknown
    };

    private static readonly HashSet<string> AllowedReviewNoteTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AgeRisk",
        "SupersessionRisk",
        "ContradictionRisk",
        "DeprecatedTermRisk",
        "DeprecatedDecisionRisk",
        "MissingFreshEvidence",
        "ConflictingEvidence",
        "ProjectScopeChanged",
        "PolicyShapeChanged",
        "ImplementationChanged",
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
        "confirmed stale",
        "stale decision",
        "reject as stale",
        "delete stale",
        "auto delete",
        "auto reject",
        "corrected memory",
        "truth updated",
        "truth changed",
        "accepted memory",
        "promoted memory",
        "active memory",
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

    public MemoryProposalValidationResult Validate(MemoryProposalStaleCandidateCreateRequest? request)
    {
        var issues = new List<MemoryProposalValidationIssue>();
        if (request is null)
        {
            Add(issues, "request_required", "Memory proposal stale candidate request is required.");
            return new MemoryProposalValidationResult { Issues = issues };
        }

        if (request.MemoryProposalStaleCandidateId == Guid.Empty) Add(issues, "stale_candidate_id_required", "MemoryProposalStaleCandidateId is required.");
        if (request.ProjectId == Guid.Empty) Add(issues, "project_id_required", "ProjectId is required.");
        if (request.MemoryProposalId == Guid.Empty) Add(issues, "memory_proposal_id_required", "MemoryProposalId is required.");
        if (string.IsNullOrWhiteSpace(request.StaleCandidateKey)) Add(issues, "stale_candidate_key_required", "StaleCandidateKey is required.");
        if (request.StalenessScore < 0m || request.StalenessScore > 1m) Add(issues, "staleness_score_invalid", "StalenessScore must be between 0 and 1.");
        if (string.IsNullOrWhiteSpace(request.SafeProposalSummary)) Add(issues, "safe_proposal_summary_required", "SafeProposalSummary is required.");
        if (string.IsNullOrWhiteSpace(request.CreatedByActorType)) Add(issues, "created_by_actor_type_required", "CreatedByActorType is required.");
        if (string.IsNullOrWhiteSpace(request.CreatedByActorId)) Add(issues, "created_by_actor_id_required", "CreatedByActorId is required.");
        if (request.MetadataVersion <= 0) Add(issues, "metadata_version_invalid", "MetadataVersion must be positive.");
        if (!IsJson(request.MetadataJson)) Add(issues, "metadata_json_invalid", "MetadataJson must be valid JSON.");

        if (!Enum.IsDefined(request.ReasonType) || !AllowedReasons.Contains(request.ReasonType)) Add(issues, "reason_type_forbidden", "ReasonType must stay within stale-candidate review vocabulary.");
        if (!Enum.IsDefined(request.Status) || !AllowedStatuses.Contains(request.Status)) Add(issues, "status_forbidden", "Status must stay within stale-candidate review vocabulary.");
        if (!Enum.IsDefined(request.StalenessBand) || !AllowedBands.Contains(request.StalenessBand)) Add(issues, "staleness_band_forbidden", "StalenessBand must stay within stale-candidate review vocabulary.");

        if (HasAuthorityFlag(request))
        {
            Add(issues, "authority_flags_forbidden", "Stale candidates cannot decide, reject, delete, correct, mark stale, accept, promote, retrieve, embed, write vector storage, satisfy policy, or transfer authority.");
        }

        ScanText(issues, "stale_candidate_text", request.StaleCandidateKey, request.SafeProposalSummary, request.SafeReasonSummary, request.SafeFreshnessRiskSummary, request.SafeReviewRecommendation, request.CreatedByActorType, request.CreatedByActorId, request.MetadataJson);

        foreach (var evidence in request.EvidenceReferences ?? Array.Empty<MemoryProposalStaleEvidenceReference>())
        {
            if (string.IsNullOrWhiteSpace(evidence.EvidenceType)) Add(issues, "evidence_type_required", "EvidenceType is required.");
            if (string.IsNullOrWhiteSpace(evidence.EvidenceId)) Add(issues, "evidence_id_required", "EvidenceId is required.");
            if (evidence.EvidenceIsDecision || evidence.EvidenceRejectsProposal || evidence.EvidenceDeletesProposal || evidence.EvidenceCorrectsProposal || evidence.EvidenceAcceptsMemory) Add(issues, "evidence_authority_forbidden", "Stale evidence references cannot decide, reject, delete, correct, or accept memory.");
            ScanText(issues, "evidence_reference_text", evidence.EvidenceType, evidence.EvidenceId, evidence.EvidenceLabel, evidence.SafeSummary, evidence.AllowedUse);
        }

        foreach (var note in request.ReviewNotes ?? Array.Empty<MemoryProposalStaleReviewNote>())
        {
            if (string.IsNullOrWhiteSpace(note.NoteType)) Add(issues, "review_note_type_required", "Review note type is required.");
            if (!string.IsNullOrWhiteSpace(note.NoteType) && !AllowedReviewNoteTypes.Contains(note.NoteType)) Add(issues, "review_note_type_forbidden", "Review note type must stay within stale-review vocabulary.");
            if (string.IsNullOrWhiteSpace(note.SafeSummary)) Add(issues, "review_note_summary_required", "Review note SafeSummary is required.");
            if (string.IsNullOrWhiteSpace(note.Severity)) Add(issues, "review_note_severity_required", "Review note Severity is required.");
            if (note.NoteIsDecision || note.NoteRejectsProposal || note.NoteDeletesProposal || note.NoteCorrectsProposal || note.NoteAcceptsMemory) Add(issues, "review_note_authority_forbidden", "Stale review notes cannot decide, reject, delete, correct, or accept memory.");
            ScanText(issues, "review_note_text", note.NoteType, note.SafeSummary, note.Severity);
        }

        return new MemoryProposalValidationResult { Issues = issues };
    }

    public MemoryProposalStaleCandidate Normalize(MemoryProposalStaleCandidateCreateRequest request)
    {
        var result = Validate(request);
        if (!result.IsValid)
        {
            throw new InvalidOperationException("Memory proposal stale candidate request is invalid: " + string.Join("; ", result.Issues.Select(issue => issue.Code)));
        }

        return new MemoryProposalStaleCandidate
        {
            MemoryProposalStaleCandidateId = request.MemoryProposalStaleCandidateId,
            ProjectId = request.ProjectId,
            MemoryProposalId = request.MemoryProposalId,
            StaleCandidateKey = NormalizeRequired(request.StaleCandidateKey),
            Status = request.Status,
            ReasonType = request.ReasonType,
            StalenessScore = request.StalenessScore,
            StalenessBand = request.StalenessBand,
            SafeProposalSummary = NormalizeRequired(request.SafeProposalSummary),
            SafeReasonSummary = NormalizeOptional(request.SafeReasonSummary),
            SafeFreshnessRiskSummary = NormalizeOptional(request.SafeFreshnessRiskSummary),
            SafeReviewRecommendation = NormalizeOptional(request.SafeReviewRecommendation),
            ProposalCreatedUtc = request.ProposalCreatedUtc,
            LastSupportingEvidenceUtc = request.LastSupportingEvidenceUtc,
            PossibleSupersededUtc = request.PossibleSupersededUtc,
            EvidenceReferences = (request.EvidenceReferences ?? Array.Empty<MemoryProposalStaleEvidenceReference>()).Select(NormalizeEvidenceReference).ToList(),
            ReviewNotes = (request.ReviewNotes ?? Array.Empty<MemoryProposalStaleReviewNote>()).Select(NormalizeReviewNote).ToList(),
            SupersedingMemoryProposalId = request.SupersedingMemoryProposalId,
            ContradictingMemoryProposalId = request.ContradictingMemoryProposalId,
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
            DeletesProposal = false,
            CorrectsProposal = false,
            MarksProposalStale = false,
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

    private static MemoryProposalStaleEvidenceReference NormalizeEvidenceReference(MemoryProposalStaleEvidenceReference reference) => new()
    {
        EvidenceType = NormalizeRequired(reference.EvidenceType),
        EvidenceId = NormalizeRequired(reference.EvidenceId),
        EvidenceLabel = NormalizeOptional(reference.EvidenceLabel),
        SafeSummary = NormalizeOptional(reference.SafeSummary),
        AllowedUse = NormalizeOptional(reference.AllowedUse),
        MemoryProposalId = reference.MemoryProposalId,
        SupersedingMemoryProposalId = reference.SupersedingMemoryProposalId,
        ContradictingMemoryProposalId = reference.ContradictingMemoryProposalId,
        WorkflowRunId = reference.WorkflowRunId,
        WorkflowRunStepId = reference.WorkflowRunStepId,
        WorkflowCheckpointId = reference.WorkflowCheckpointId,
        EvidenceIsDecision = false,
        EvidenceRejectsProposal = false,
        EvidenceDeletesProposal = false,
        EvidenceCorrectsProposal = false,
        EvidenceAcceptsMemory = false
    };

    private static MemoryProposalStaleReviewNote NormalizeReviewNote(MemoryProposalStaleReviewNote note) => new()
    {
        NoteType = NormalizeRequired(note.NoteType),
        SafeSummary = NormalizeRequired(note.SafeSummary),
        Severity = NormalizeRequired(note.Severity),
        NoteIsDecision = false,
        NoteRejectsProposal = false,
        NoteDeletesProposal = false,
        NoteCorrectsProposal = false,
        NoteAcceptsMemory = false
    };

    private static bool HasAuthorityFlag(MemoryProposalStaleCandidateCreateRequest request) =>
        request.IsDecision ||
        request.RejectsProposal ||
        request.DeletesProposal ||
        request.CorrectsProposal ||
        request.MarksProposalStale ||
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
        if (UnsafeMarkers.Any(text.Contains)) Add(issues, code + "_unsafe", "Stale candidate text contains raw/private reasoning, decision language, or authority language.");
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

public sealed class MemoryProposalStaleDetector
{
    public IReadOnlyList<MemoryProposalStaleCandidateCreateRequest> Detect(
        IReadOnlyList<MemoryProposal> proposals,
        MemoryProposalStaleDetectionOptions? options = null)
    {
        if (proposals is null || proposals.Count == 0) return Array.Empty<MemoryProposalStaleCandidateCreateRequest>();

        var effectiveOptions = options ?? new MemoryProposalStaleDetectionOptions();
        var maxCandidateCount = Math.Clamp(effectiveOptions.MaxCandidateCount, 0, 500);
        if (maxCandidateCount == 0) return Array.Empty<MemoryProposalStaleCandidateCreateRequest>();

        var candidates = new List<MemoryProposalStaleCandidateCreateRequest>();
        foreach (var group in proposals.Where(proposal => proposal.ProjectId != Guid.Empty).GroupBy(proposal => proposal.ProjectId).OrderBy(group => group.Key))
        {
            var ordered = group
                .Where(proposal => effectiveOptions.IncludeWithdrawnOrQuarantined || proposal.ProposalStatus is not (MemoryProposalStatus.Withdrawn or MemoryProposalStatus.Quarantined))
                .OrderBy(proposal => proposal.CreatedUtc)
                .ThenBy(proposal => proposal.MemoryProposalId)
                .ToList();

            foreach (var proposal in ordered)
            {
                foreach (var candidate in DetectForProposal(proposal, ordered, effectiveOptions))
                {
                    candidates.Add(candidate);
                    if (candidates.Count >= maxCandidateCount) return candidates;
                }
            }
        }

        return candidates;
    }

    private static IEnumerable<MemoryProposalStaleCandidateCreateRequest> DetectForProposal(
        MemoryProposal proposal,
        IReadOnlyList<MemoryProposal> sameProjectProposals,
        MemoryProposalStaleDetectionOptions options)
    {
        var newerRelated = sameProjectProposals
            .Where(other => other.MemoryProposalId != proposal.MemoryProposalId)
            .Where(other => other.CreatedUtc > proposal.CreatedUtc)
            .Where(other => SameSubject(proposal, other))
            .OrderBy(other => other.CreatedUtc)
            .ThenBy(other => other.MemoryProposalId)
            .ToList();

        var superseding = newerRelated.FirstOrDefault(other => LooksSuperseding(other.SafeProposedMemory));
        if (superseding is not null)
        {
            yield return BuildCandidate(
                proposal,
                options,
                MemoryProposalStaleReasonType.SupersededByProposalCandidate,
                MemoryProposalStalenessBand.SupersessionCandidate,
                0.85m,
                "A newer staged proposal with the same subject contains deterministic supersession wording.",
                "Possible supersession requires review before any correction, rejection, acceptance, or promotion decision.",
                supersedingProposal: superseding,
                noteType: "SupersessionRisk");
        }

        var contradicting = newerRelated.FirstOrDefault(other => LooksContradictory(proposal.SafeProposedMemory, other.SafeProposedMemory));
        if (contradicting is not null)
        {
            yield return BuildCandidate(
                proposal,
                options,
                MemoryProposalStaleReasonType.ContradictedByProposalCandidate,
                MemoryProposalStalenessBand.ContradictionCandidate,
                0.80m,
                "A newer staged proposal with the same subject contains deterministic contradiction wording.",
                "Possible contradiction requires review before any stale, correction, rejection, acceptance, or promotion decision.",
                contradictingProposal: contradicting,
                noteType: "ContradictionRisk");
        }

        if (LooksDeprecated(proposal.SafeProposedMemory))
        {
            yield return BuildCandidate(
                proposal,
                options,
                LooksObsoleteWorkflow(proposal.SafeProposedMemory) ? MemoryProposalStaleReasonType.ObsoleteWorkflowStateCandidate : MemoryProposalStaleReasonType.DeprecatedTermCandidate,
                MemoryProposalStalenessBand.HighStalenessRisk,
                0.70m,
                "Proposal text contains deterministic deprecated, legacy, old, or obsolete wording.",
                "Deprecated wording is review evidence only and does not delete, correct, or reject the proposal.",
                noteType: LooksObsoleteWorkflow(proposal.SafeProposedMemory) ? "ImplementationChanged" : "DeprecatedTermRisk");
        }

        var lastEvidence = LastEvidenceUtc(proposal);
        if (lastEvidence is null || lastEvidence < options.CurrentUtc.Subtract(options.CurrentEvidenceThreshold))
        {
            yield return BuildCandidate(
                proposal,
                options,
                MemoryProposalStaleReasonType.MissingCurrentEvidenceCandidate,
                MemoryProposalStalenessBand.MediumStalenessRisk,
                lastEvidence is null ? 0.60m : 0.55m,
                "Proposal lacks current supporting evidence inside the configured freshness window.",
                "Missing current evidence requires review before any memory acceptance or promotion decision.",
                lastSupportingEvidenceUtc: lastEvidence,
                noteType: "MissingFreshEvidence");
        }

        if (proposal.CreatedUtc != default && proposal.CreatedUtc < options.CurrentUtc.Subtract(options.AgeThreshold))
        {
            yield return BuildCandidate(
                proposal,
                options,
                MemoryProposalStaleReasonType.AgeCandidate,
                MemoryProposalStalenessBand.AgeOnlyCandidate,
                0.45m,
                "Proposal age exceeds the configured stale-detection threshold.",
                "Age is review evidence only and is not truth, rejection, deletion, correction, acceptance, or promotion.",
                noteType: "AgeRisk");
        }
    }

    private static MemoryProposalStaleCandidateCreateRequest BuildCandidate(
        MemoryProposal proposal,
        MemoryProposalStaleDetectionOptions options,
        MemoryProposalStaleReasonType reasonType,
        MemoryProposalStalenessBand band,
        decimal score,
        string reasonSummary,
        string freshnessRiskSummary,
        MemoryProposal? supersedingProposal = null,
        MemoryProposal? contradictingProposal = null,
        DateTimeOffset? lastSupportingEvidenceUtc = null,
        string noteType = "NeedsHumanReview")
    {
        var key = BuildCandidateKey(proposal.ProjectId, proposal.MemoryProposalId, reasonType, supersedingProposal?.MemoryProposalId, contradictingProposal?.MemoryProposalId);
        var relatedProposal = supersedingProposal ?? contradictingProposal;
        return new MemoryProposalStaleCandidateCreateRequest
        {
            MemoryProposalStaleCandidateId = DeterministicGuid(key),
            ProjectId = proposal.ProjectId,
            MemoryProposalId = proposal.MemoryProposalId,
            StaleCandidateKey = key,
            Status = MemoryProposalStaleCandidateStatus.ReadyForReview,
            ReasonType = reasonType,
            StalenessScore = decimal.Round(Math.Clamp(score, 0m, 1m), 4),
            StalenessBand = band,
            SafeProposalSummary = proposal.SafeProposedMemory,
            SafeReasonSummary = reasonSummary,
            SafeFreshnessRiskSummary = freshnessRiskSummary,
            SafeReviewRecommendation = "Human or governed review remains required before any correction, rejection, deletion, acceptance, or promotion decision.",
            ProposalCreatedUtc = proposal.CreatedUtc == default ? null : proposal.CreatedUtc,
            LastSupportingEvidenceUtc = lastSupportingEvidenceUtc ?? LastEvidenceUtc(proposal),
            PossibleSupersededUtc = supersedingProposal?.CreatedUtc,
            EvidenceReferences = BuildEvidenceReferences(proposal, relatedProposal, reasonType),
            ReviewNotes = BuildReviewNotes(noteType, freshnessRiskSummary),
            SupersedingMemoryProposalId = supersedingProposal?.MemoryProposalId,
            ContradictingMemoryProposalId = contradictingProposal?.MemoryProposalId,
            WorkflowRunId = proposal.WorkflowRunId,
            WorkflowRunStepId = proposal.WorkflowRunStepId,
            WorkflowCheckpointId = proposal.WorkflowCheckpointId,
            CorrelationId = proposal.CorrelationId,
            CausationId = proposal.CausationId,
            CreatedByActorType = string.IsNullOrWhiteSpace(options.CreatedByActorType) ? "system" : options.CreatedByActorType,
            CreatedByActorId = string.IsNullOrWhiteSpace(options.CreatedByActorId) ? "memory-proposal-stale-detector" : options.CreatedByActorId,
            MetadataVersion = 1,
            MetadataJson = "{\"source\":\"memory-proposal-stale-detector\"}"
        };
    }

    private static IReadOnlyList<MemoryProposalStaleEvidenceReference> BuildEvidenceReferences(MemoryProposal proposal, MemoryProposal? relatedProposal, MemoryProposalStaleReasonType reasonType)
    {
        var references = new List<MemoryProposalStaleEvidenceReference>
        {
            new()
            {
                EvidenceType = "MemoryProposal",
                EvidenceId = proposal.MemoryProposalId.ToString(),
                EvidenceLabel = "Staged memory proposal",
                SafeSummary = "Staged memory proposal is evidence for freshness review only.",
                AllowedUse = "StaleReview",
                MemoryProposalId = proposal.MemoryProposalId,
                WorkflowRunId = proposal.WorkflowRunId,
                WorkflowRunStepId = proposal.WorkflowRunStepId,
                WorkflowCheckpointId = proposal.WorkflowCheckpointId
            }
        };

        if (relatedProposal is not null)
        {
            references.Add(new MemoryProposalStaleEvidenceReference
            {
                EvidenceType = "MemoryProposal",
                EvidenceId = relatedProposal.MemoryProposalId.ToString(),
                EvidenceLabel = reasonType == MemoryProposalStaleReasonType.SupersededByProposalCandidate ? "Possible superseding staged proposal" : "Possible contradicting staged proposal",
                SafeSummary = "Related staged memory proposal is evidence for freshness review only.",
                AllowedUse = "StaleReview",
                SupersedingMemoryProposalId = reasonType == MemoryProposalStaleReasonType.SupersededByProposalCandidate ? relatedProposal.MemoryProposalId : null,
                ContradictingMemoryProposalId = reasonType == MemoryProposalStaleReasonType.ContradictedByProposalCandidate ? relatedProposal.MemoryProposalId : null,
                WorkflowRunId = relatedProposal.WorkflowRunId,
                WorkflowRunStepId = relatedProposal.WorkflowRunStepId,
                WorkflowCheckpointId = relatedProposal.WorkflowCheckpointId
            });
        }

        return references;
    }

    private static IReadOnlyList<MemoryProposalStaleReviewNote> BuildReviewNotes(string noteType, string summary) => new[]
    {
        new MemoryProposalStaleReviewNote
        {
            NoteType = noteType,
            SafeSummary = summary,
            Severity = "warning"
        },
        new MemoryProposalStaleReviewNote
        {
            NoteType = "NeedsHumanReview",
            SafeSummary = "Staleness score is review evidence only and is not approval, truth, rejection, deletion, correction, acceptance, or promotion.",
            Severity = "warning"
        }
    };

    private static DateTimeOffset? LastEvidenceUtc(MemoryProposal proposal)
    {
        var dates = proposal.EvidenceReferences?
            .Where(evidence => evidence.CreatedUtc != default)
            .Select(evidence => evidence.CreatedUtc)
            .ToList() ?? new List<DateTimeOffset>();

        return dates.Count == 0 ? null : dates.Max();
    }

    private static bool SameSubject(MemoryProposal left, MemoryProposal right)
    {
        if (!string.IsNullOrWhiteSpace(left.SubjectType) && !string.IsNullOrWhiteSpace(right.SubjectType) &&
            !string.Equals(left.SubjectType, right.SubjectType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(left.SubjectId) && !string.IsNullOrWhiteSpace(right.SubjectId))
        {
            return string.Equals(left.SubjectId, right.SubjectId, StringComparison.OrdinalIgnoreCase);
        }

        return TokenOverlap(left.SafeProposedMemory, right.SafeProposedMemory) >= 0.35m;
    }

    private static bool LooksSuperseding(string text)
    {
        var normalized = NormalizeForComparison(text);
        return normalized.Contains("now use", StringComparison.Ordinal) ||
               normalized.Contains("current", StringComparison.Ordinal) ||
               normalized.Contains("replaces", StringComparison.Ordinal) ||
               normalized.Contains("replacement", StringComparison.Ordinal) ||
               normalized.Contains("supersedes", StringComparison.Ordinal) ||
               normalized.Contains("new workflow", StringComparison.Ordinal) ||
               normalized.Contains("new policy", StringComparison.Ordinal);
    }

    private static bool LooksContradictory(string left, string right)
    {
        var overlap = TokenOverlap(left, right);
        if (overlap < 0.25m) return false;
        return HasNegation(left) != HasNegation(right);
    }

    private static bool LooksDeprecated(string text)
    {
        var normalized = NormalizeForComparison(text);
        return normalized.Contains("old ", StringComparison.Ordinal) ||
               normalized.Contains("legacy", StringComparison.Ordinal) ||
               normalized.Contains("deprecated", StringComparison.Ordinal) ||
               normalized.Contains("obsolete", StringComparison.Ordinal) ||
               normalized.Contains("workaround is still required", StringComparison.Ordinal) ||
               normalized.Contains("not implemented yet", StringComparison.Ordinal);
    }

    private static bool LooksObsoleteWorkflow(string text)
    {
        var normalized = NormalizeForComparison(text);
        return normalized.Contains("old workflow", StringComparison.Ordinal) ||
               normalized.Contains("obsolete workflow", StringComparison.Ordinal) ||
               normalized.Contains("legacy workflow", StringComparison.Ordinal);
    }

    private static bool HasNegation(string text)
    {
        var normalized = NormalizeForComparison(text);
        return normalized.Contains("do not", StringComparison.Ordinal) ||
               normalized.Contains("must not", StringComparison.Ordinal) ||
               normalized.Contains("should not", StringComparison.Ordinal) ||
               normalized.Contains("cannot", StringComparison.Ordinal) ||
               normalized.Contains("never", StringComparison.Ordinal) ||
               normalized.Contains("disable", StringComparison.Ordinal) ||
               normalized.Contains("forbid", StringComparison.Ordinal) ||
               normalized.Contains("block", StringComparison.Ordinal);
    }

    private static decimal TokenOverlap(string left, string right)
    {
        var leftTokens = Tokenize(NormalizeForComparison(left));
        var rightTokens = Tokenize(NormalizeForComparison(right));
        if (leftTokens.Count == 0 || rightTokens.Count == 0) return 0m;
        var intersection = leftTokens.Intersect(rightTokens).Count();
        return (2m * intersection) / (leftTokens.Count + rightTokens.Count);
    }

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

    private static string BuildCandidateKey(Guid projectId, Guid memoryProposalId, MemoryProposalStaleReasonType reasonType, Guid? supersedingId, Guid? contradictingId) =>
        $"memory-proposal-stale:{projectId:N}:{memoryProposalId:N}:{reasonType}:{supersedingId:N}:{contradictingId:N}";

    private static Guid DeterministicGuid(string seed)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(bytes.Take(16).ToArray());
    }
}
