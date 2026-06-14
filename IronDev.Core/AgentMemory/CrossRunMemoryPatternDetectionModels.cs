using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IronDev.Core.AgentMemory;

public enum CrossRunMemoryPatternCandidateStatus
{
    Detected,
    ReadyForReview,
    NeedsEvidence,
    NeedsHumanReview,
    RequiresSanitizationReview,
    Quarantined,
    Superseded,
    Withdrawn
}

public enum CrossRunMemoryPatternType
{
    RepeatedFactCandidate,
    RepeatedDecisionCandidate,
    RepeatedBoundaryCandidate,
    RepeatedFailureModeCandidate,
    RepeatedRiskCandidate,
    RepeatedConventionCandidate,
    RepeatedDebuggingLessonCandidate,
    RepeatedValidationFindingCandidate,
    RepeatedReviewFindingCandidate,
    RepeatedWorkflowPatternCandidate,
    RepeatedPolicyInvariantCandidate,
    PortableEngineeringMemoryPatternCandidate,
    NeedsHumanPatternReview
}

public enum CrossRunMemoryPatternBand
{
    HighRecurrence,
    MediumRecurrence,
    LowRecurrence,
    CrossRunCandidate,
    PortableCandidateRequiresReview,
    Unknown
}

public sealed class CrossRunMemoryPatternDetectionOptions
{
    public int MaxCandidateCount { get; init; } = 50;
    public int MinimumRecurrenceCount { get; init; } = 2;
    public bool RequireMultipleWorkflowRuns { get; init; } = true;
    public bool IncludeWithdrawnOrQuarantined { get; init; }
    public string CreatedByActorType { get; init; } = "system";
    public string CreatedByActorId { get; init; } = "cross-run-memory-pattern-detector";
}

public sealed class CrossRunMemoryPatternEvidenceReference
{
    public string EvidenceType { get; init; } = string.Empty;
    public string EvidenceId { get; init; } = string.Empty;
    public string? EvidenceLabel { get; init; }
    public string? SafeSummary { get; init; }
    public string? AllowedUse { get; init; }
    public Guid? MemoryProposalId { get; init; }
    public Guid? WorkflowRunId { get; init; }
    public Guid? WorkflowRunStepId { get; init; }
    public Guid? WorkflowCheckpointId { get; init; }
    public bool EvidenceIsDecision { get; init; }
    public bool EvidenceAcceptsMemory { get; init; }
    public bool EvidencePromotesMemory { get; init; }
    public bool EvidenceActivatesRetrieval { get; init; }
}

public sealed class CrossRunMemoryPatternReviewNote
{
    public string NoteType { get; init; } = string.Empty;
    public string SafeSummary { get; init; } = string.Empty;
    public string? Severity { get; init; }
    public bool NoteIsDecision { get; init; }
    public bool NoteAcceptsMemory { get; init; }
    public bool NotePromotesMemory { get; init; }
    public bool NoteActivatesRetrieval { get; init; }
}

public sealed class CrossRunMemoryPatternCandidateCreateRequest
{
    public Guid? CrossRunMemoryPatternCandidateId { get; init; }
    public Guid ProjectId { get; init; }
    public string PatternCandidateKey { get; init; } = string.Empty;
    public CrossRunMemoryPatternCandidateStatus Status { get; init; } = CrossRunMemoryPatternCandidateStatus.ReadyForReview;
    public CrossRunMemoryPatternType PatternType { get; init; }
    public CrossRunMemoryPatternBand PatternBand { get; init; }
    public decimal RecurrenceScore { get; init; }
    public IReadOnlyList<Guid> MemoryProposalIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<Guid> WorkflowRunIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<Guid> WorkflowRunStepIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<Guid> WorkflowCheckpointIds { get; init; } = Array.Empty<Guid>();
    public string SafePatternSummary { get; init; } = string.Empty;
    public string? SafeEvidenceSummary { get; init; }
    public string? SafeReviewRecommendation { get; init; }
    public IReadOnlyList<CrossRunMemoryPatternEvidenceReference> EvidenceReferences { get; init; } = Array.Empty<CrossRunMemoryPatternEvidenceReference>();
    public IReadOnlyList<CrossRunMemoryPatternReviewNote> ReviewNotes { get; init; } = Array.Empty<CrossRunMemoryPatternReviewNote>();
    public string CreatedByActorType { get; init; } = string.Empty;
    public string CreatedByActorId { get; init; } = string.Empty;
    public int MetadataVersion { get; init; } = 1;
    public string MetadataJson { get; init; } = "{}";
    public bool ChoosesTruth { get; init; }
    public bool AcceptsMemory { get; init; }
    public bool PromotesMemory { get; init; }
    public bool ActivatesRetrieval { get; init; }
    public bool WritesVectorIndex { get; init; }
    public bool CreatesEmbedding { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool GrantsApproval { get; init; }
    public bool GrantsExecution { get; init; }
    public bool StartsWorkflow { get; init; }
    public bool ContinuesWorkflow { get; init; }
    public bool MutatesSource { get; init; }
    public bool ApprovesRelease { get; init; }
}

public sealed class CrossRunMemoryPatternCandidate
{
    public Guid CrossRunMemoryPatternCandidateId { get; init; }
    public Guid ProjectId { get; init; }
    public string PatternCandidateKey { get; init; } = string.Empty;
    public CrossRunMemoryPatternCandidateStatus Status { get; init; }
    public CrossRunMemoryPatternType PatternType { get; init; }
    public CrossRunMemoryPatternBand PatternBand { get; init; }
    public decimal RecurrenceScore { get; init; }
    public IReadOnlyList<Guid> MemoryProposalIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<Guid> WorkflowRunIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<Guid> WorkflowRunStepIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<Guid> WorkflowCheckpointIds { get; init; } = Array.Empty<Guid>();
    public string SafePatternSummary { get; init; } = string.Empty;
    public string? SafeEvidenceSummary { get; init; }
    public string? SafeReviewRecommendation { get; init; }
    public IReadOnlyList<CrossRunMemoryPatternEvidenceReference> EvidenceReferences { get; init; } = Array.Empty<CrossRunMemoryPatternEvidenceReference>();
    public IReadOnlyList<CrossRunMemoryPatternReviewNote> ReviewNotes { get; init; } = Array.Empty<CrossRunMemoryPatternReviewNote>();
    public string CreatedByActorType { get; init; } = string.Empty;
    public string CreatedByActorId { get; init; } = string.Empty;
    public int MetadataVersion { get; init; }
    public string MetadataJson { get; init; } = "{}";
    public bool ChoosesTruth { get; init; }
    public bool AcceptsMemory { get; init; }
    public bool PromotesMemory { get; init; }
    public bool ActivatesRetrieval { get; init; }
    public bool WritesVectorIndex { get; init; }
    public bool CreatesEmbedding { get; init; }
    public bool SatisfiesPolicy { get; init; }
    public bool GrantsApproval { get; init; }
    public bool GrantsExecution { get; init; }
    public bool StartsWorkflow { get; init; }
    public bool ContinuesWorkflow { get; init; }
    public bool MutatesSource { get; init; }
    public bool ApprovesRelease { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed record CrossRunMemoryPatternValidationIssue(string Code, string Message);

public sealed class CrossRunMemoryPatternValidationResult
{
    public IReadOnlyList<CrossRunMemoryPatternValidationIssue> Issues { get; init; } = Array.Empty<CrossRunMemoryPatternValidationIssue>();
    public bool IsValid => Issues.Count == 0;
}

public sealed class CrossRunMemoryPatternValidator
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
        "truth selected",
        "choose truth",
        "accepted memory",
        "memory accepted",
        "promoted memory",
        "memory promoted",
        "retrieval active",
        "activate retrieval",
        "embedding created",
        "create embedding",
        "vector write",
        "write vector",
        "portable approved",
        "policy satisfied",
        "satisfy policy",
        "approval granted",
        "grants approval",
        "execution allowed",
        "grants execution",
        "workflow started",
        "workflow continued",
        "source mutated",
        "release approved",
        "authority transferred"
    };

    public CrossRunMemoryPatternValidationResult Validate(CrossRunMemoryPatternCandidateCreateRequest request)
    {
        var issues = new List<CrossRunMemoryPatternValidationIssue>();

        if (request.ProjectId == Guid.Empty) Add(issues, "project_id_required", "ProjectId is required.");
        if (request.CrossRunMemoryPatternCandidateId == Guid.Empty) Add(issues, "pattern_candidate_id_empty", "CrossRunMemoryPatternCandidateId cannot be empty when supplied.");
        if (string.IsNullOrWhiteSpace(request.PatternCandidateKey)) Add(issues, "pattern_candidate_key_required", "PatternCandidateKey is required.");
        if (string.IsNullOrWhiteSpace(request.SafePatternSummary)) Add(issues, "safe_pattern_summary_required", "SafePatternSummary is required.");
        if (string.IsNullOrWhiteSpace(request.CreatedByActorType)) Add(issues, "created_by_actor_type_required", "CreatedByActorType is required.");
        if (string.IsNullOrWhiteSpace(request.CreatedByActorId)) Add(issues, "created_by_actor_id_required", "CreatedByActorId is required.");
        if (request.MetadataVersion <= 0) Add(issues, "metadata_version_invalid", "MetadataVersion must be positive.");
        if (!IsJson(request.MetadataJson)) Add(issues, "metadata_json_invalid", "MetadataJson must be valid JSON.");
        if (request.RecurrenceScore < 0m || request.RecurrenceScore > 1m) Add(issues, "recurrence_score_invalid", "RecurrenceScore must be between 0 and 1.");
        if (DistinctNonEmpty(request.MemoryProposalIds).Count < 2) Add(issues, "memory_proposal_ids_minimum_required", "At least two distinct memory proposal ids are required.");
        if (DistinctNonEmpty(request.WorkflowRunIds).Count < 2) Add(issues, "workflow_run_ids_minimum_required", "At least two distinct workflow run ids are required.");
        if (request.MemoryProposalIds.Any(id => id == Guid.Empty)) Add(issues, "memory_proposal_id_empty", "MemoryProposalIds cannot contain empty ids.");
        if (request.WorkflowRunIds.Any(id => id == Guid.Empty)) Add(issues, "workflow_run_id_empty", "WorkflowRunIds cannot contain empty ids.");
        if (request.WorkflowRunStepIds.Any(id => id == Guid.Empty)) Add(issues, "workflow_run_step_id_empty", "WorkflowRunStepIds cannot contain empty ids.");
        if (request.WorkflowCheckpointIds.Any(id => id == Guid.Empty)) Add(issues, "workflow_checkpoint_id_empty", "WorkflowCheckpointIds cannot contain empty ids.");

        ValidateEnum(request.Status, "status_forbidden", issues);
        ValidateEnum(request.PatternType, "pattern_type_forbidden", issues);
        ValidateEnum(request.PatternBand, "pattern_band_forbidden", issues);

        if (HasAuthorityFlag(request))
        {
            Add(issues, "authority_flags_forbidden", "Cross-run memory pattern candidates cannot choose truth, accept memory, promote memory, activate retrieval, write vectors, create embeddings, satisfy policy, grant approval or execution, continue workflow, mutate source, or approve release.");
        }

        ScanText(issues, "pattern_candidate_text_unsafe", request.PatternCandidateKey, request.SafePatternSummary, request.SafeEvidenceSummary, request.SafeReviewRecommendation, request.CreatedByActorType, request.CreatedByActorId, request.MetadataJson);

        foreach (var evidence in request.EvidenceReferences ?? Array.Empty<CrossRunMemoryPatternEvidenceReference>())
        {
            if (string.IsNullOrWhiteSpace(evidence.EvidenceType)) Add(issues, "evidence_type_required", "EvidenceType is required.");
            if (string.IsNullOrWhiteSpace(evidence.EvidenceId)) Add(issues, "evidence_id_required", "EvidenceId is required.");
            if (evidence.EvidenceIsDecision || evidence.EvidenceAcceptsMemory || evidence.EvidencePromotesMemory || evidence.EvidenceActivatesRetrieval)
            {
                Add(issues, "evidence_authority_forbidden", "Evidence references cannot be decisions, memory acceptance, memory promotion, or retrieval activation.");
            }

            ScanText(issues, "evidence_reference_text_unsafe", evidence.EvidenceType, evidence.EvidenceId, evidence.EvidenceLabel, evidence.SafeSummary, evidence.AllowedUse);
        }

        foreach (var note in request.ReviewNotes ?? Array.Empty<CrossRunMemoryPatternReviewNote>())
        {
            if (string.IsNullOrWhiteSpace(note.NoteType)) Add(issues, "review_note_type_required", "NoteType is required.");
            if (string.IsNullOrWhiteSpace(note.SafeSummary)) Add(issues, "review_note_summary_required", "Review note SafeSummary is required.");
            if (note.NoteIsDecision || note.NoteAcceptsMemory || note.NotePromotesMemory || note.NoteActivatesRetrieval)
            {
                Add(issues, "review_note_authority_forbidden", "Review notes cannot be decisions, memory acceptance, memory promotion, or retrieval activation.");
            }

            ScanText(issues, "review_note_text_unsafe", note.NoteType, note.SafeSummary, note.Severity);
        }

        return new CrossRunMemoryPatternValidationResult { Issues = issues };
    }

    public CrossRunMemoryPatternCandidate Normalize(CrossRunMemoryPatternCandidateCreateRequest request) => new()
    {
        CrossRunMemoryPatternCandidateId = request.CrossRunMemoryPatternCandidateId ?? Guid.NewGuid(),
        ProjectId = request.ProjectId,
        PatternCandidateKey = NormalizeRequired(request.PatternCandidateKey),
        Status = request.Status,
        PatternType = request.PatternType,
        PatternBand = request.PatternBand,
        RecurrenceScore = Clamp(request.RecurrenceScore),
        MemoryProposalIds = DistinctNonEmpty(request.MemoryProposalIds),
        WorkflowRunIds = DistinctNonEmpty(request.WorkflowRunIds),
        WorkflowRunStepIds = DistinctNonEmpty(request.WorkflowRunStepIds),
        WorkflowCheckpointIds = DistinctNonEmpty(request.WorkflowCheckpointIds),
        SafePatternSummary = NormalizeRequired(request.SafePatternSummary),
        SafeEvidenceSummary = NormalizeOptional(request.SafeEvidenceSummary),
        SafeReviewRecommendation = NormalizeOptional(request.SafeReviewRecommendation),
        EvidenceReferences = request.EvidenceReferences ?? Array.Empty<CrossRunMemoryPatternEvidenceReference>(),
        ReviewNotes = request.ReviewNotes ?? Array.Empty<CrossRunMemoryPatternReviewNote>(),
        CreatedByActorType = NormalizeRequired(request.CreatedByActorType),
        CreatedByActorId = NormalizeRequired(request.CreatedByActorId),
        MetadataVersion = request.MetadataVersion <= 0 ? 1 : request.MetadataVersion,
        MetadataJson = string.IsNullOrWhiteSpace(request.MetadataJson) ? "{}" : request.MetadataJson.Trim(),
        CreatedUtc = DateTimeOffset.UtcNow
    };

    private static bool HasAuthorityFlag(CrossRunMemoryPatternCandidateCreateRequest request) =>
        request.ChoosesTruth ||
        request.AcceptsMemory ||
        request.PromotesMemory ||
        request.ActivatesRetrieval ||
        request.WritesVectorIndex ||
        request.CreatesEmbedding ||
        request.SatisfiesPolicy ||
        request.GrantsApproval ||
        request.GrantsExecution ||
        request.StartsWorkflow ||
        request.ContinuesWorkflow ||
        request.MutatesSource ||
        request.ApprovesRelease;

    private static void ValidateEnum<TEnum>(TEnum value, string code, ICollection<CrossRunMemoryPatternValidationIssue> issues) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value)) Add(issues, code, $"{typeof(TEnum).Name} has an unsupported value.");
    }

    private static void ScanText(ICollection<CrossRunMemoryPatternValidationIssue> issues, string code, params string?[] values)
    {
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var normalized = NormalizeSearchText(value!);
            if (UnsafeMarkers.Any(marker => normalized.Contains(NormalizeSearchText(marker), StringComparison.Ordinal)))
            {
                Add(issues, code, "Text contains hidden reasoning, raw material, memory authority, retrieval activation, vector, approval, workflow, source mutation, release, or authority-transfer language.");
                return;
            }
        }
    }

    private static string NormalizeSearchText(string value) => Regex.Replace(value, "(?<!^)([A-Z])", " $1").ToLowerInvariant().Replace("_", " ").Replace("-", " ").Trim();
    private static List<Guid> DistinctNonEmpty(IEnumerable<Guid>? ids) => (ids ?? Array.Empty<Guid>()).Where(id => id != Guid.Empty).Distinct().OrderBy(id => id).ToList();
    private static decimal Clamp(decimal value) => Math.Min(1m, Math.Max(0m, value));
    private static bool IsJson(string? json) { try { _ = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json); return true; } catch (JsonException) { return false; } }
    private static string NormalizeRequired(string value) => value.Trim();
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void Add(ICollection<CrossRunMemoryPatternValidationIssue> issues, string code, string message) => issues.Add(new CrossRunMemoryPatternValidationIssue(code, message));
}

public sealed class CrossRunMemoryPatternDetector
{
    private static readonly HashSet<MemoryProposalStatus> ExcludedStatuses = new()
    {
        MemoryProposalStatus.Quarantined,
        MemoryProposalStatus.Superseded,
        MemoryProposalStatus.Withdrawn
    };

    private static readonly string[] BoundaryTerms = { "boundary", "approval", "audit", "authority", "gate", "executor", "critic", "governance", "review", "proposal" };
    private static readonly string[] WorkflowTerms = { "workflow", "step", "checkpoint", "run", "receipt", "retry", "failure", "repair", "dogfood" };
    private static readonly string[] PolicyTerms = { "policy", "approval", "human", "gate", "requirement", "satisfaction", "rule" };
    private static readonly string[] ValidationTerms = { "validation", "test", "build", "failure", "error", "regression", "guard" };

    public IReadOnlyList<CrossRunMemoryPatternCandidateCreateRequest> Detect(IEnumerable<MemoryProposal> proposals, CrossRunMemoryPatternDetectionOptions? options = null)
    {
        options ??= new CrossRunMemoryPatternDetectionOptions();
        var candidateLimit = Math.Clamp(options.MaxCandidateCount, 1, 500);
        var minimumRecurrence = Math.Max(2, options.MinimumRecurrenceCount);

        var eligible = (proposals ?? Array.Empty<MemoryProposal>())
            .Where(proposal => proposal.ProjectId != Guid.Empty)
            .Where(proposal => proposal.MemoryProposalId != Guid.Empty)
            .Where(proposal => !string.IsNullOrWhiteSpace(proposal.SafeProposedMemory))
            .Where(proposal => options.IncludeWithdrawnOrQuarantined || !ExcludedStatuses.Contains(proposal.ProposalStatus))
            .OrderBy(proposal => proposal.CreatedUtc)
            .ThenBy(proposal => proposal.MemoryProposalId)
            .ToList();

        var grouped = eligible
            .SelectMany(CreateSignals)
            .GroupBy(signal => new { signal.ProjectId, signal.SignalKey })
            .OrderByDescending(group => group.Select(signal => signal.Proposal.MemoryProposalId).Distinct().Count())
            .ThenBy(group => group.Key.SignalKey, StringComparer.Ordinal)
            .ToList();

        var candidates = new List<CrossRunMemoryPatternCandidateCreateRequest>();

        foreach (var group in grouped)
        {
            var signals = group.ToList();
            var distinctProposals = signals
                .Select(signal => signal.Proposal)
                .GroupBy(proposal => proposal.MemoryProposalId)
                .Select(grouping => grouping.First())
                .OrderBy(proposal => proposal.MemoryProposalId)
                .ToList();
            var workflowRunIds = distinctProposals.Select(proposal => proposal.WorkflowRunId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().OrderBy(id => id).ToList();

            if (distinctProposals.Count < minimumRecurrence) continue;
            if (options.RequireMultipleWorkflowRuns && workflowRunIds.Count < 2) continue;

            var signal = signals.OrderBy(s => s.Priority).ThenBy(s => s.SignalKey, StringComparer.Ordinal).First();
            candidates.Add(BuildCandidate(signal, distinctProposals, workflowRunIds, options));
            if (candidates.Count >= candidateLimit) break;
        }

        return candidates;
    }

    private static IEnumerable<PatternSignal> CreateSignals(MemoryProposal proposal)
    {
        var text = NormalizeText($"{proposal.ProposalType} {proposal.TargetMemoryScope} {proposal.SubjectType} {proposal.SubjectId} {proposal.SafeProposedMemory} {proposal.SafeRationaleSummary} {proposal.SafeRiskSummary}");
        var domainKey = BuildDomainKey(proposal, text);

        if (proposal.ProposalType == MemoryProposalType.PortableEngineeringMemoryCandidate || proposal.TargetMemoryScope == MemoryProposalTargetScope.PortableEngineeringMemoryCandidate)
        {
            yield return new PatternSignal(proposal.ProjectId, $"portable:{domainKey}", CrossRunMemoryPatternType.PortableEngineeringMemoryPatternCandidate, 0, proposal);
        }

        if (proposal.ProposalType == MemoryProposalType.FailureModeCandidate || ContainsAny(text, "failure", "error", "regression", "flaky", "test", "build"))
        {
            yield return new PatternSignal(proposal.ProjectId, $"failure:{domainKey}", CrossRunMemoryPatternType.RepeatedFailureModeCandidate, 1, proposal);
        }

        if (proposal.ProposalType == MemoryProposalType.ProjectRiskCandidate || ContainsAny(text, "risk", "danger", "unsafe", "leak", "bypass", "conflict"))
        {
            yield return new PatternSignal(proposal.ProjectId, $"risk:{domainKey}", CrossRunMemoryPatternType.RepeatedRiskCandidate, 2, proposal);
        }

        if (proposal.ProposalType == MemoryProposalType.ProjectConventionCandidate || ContainsAny(text, "convention", "naming", "style", "pattern", "should", "prefer"))
        {
            yield return new PatternSignal(proposal.ProjectId, $"convention:{domainKey}", CrossRunMemoryPatternType.RepeatedConventionCandidate, 3, proposal);
        }

        if (proposal.ProposalType == MemoryProposalType.DebuggingLessonCandidate || ContainsAny(text, "debug", "diagnostic", "investigate", "triage", "lesson"))
        {
            yield return new PatternSignal(proposal.ProjectId, $"debug:{domainKey}", CrossRunMemoryPatternType.RepeatedDebuggingLessonCandidate, 4, proposal);
        }

        if (proposal.ProposalType == MemoryProposalType.ProjectDecisionCandidate || ContainsAny(text, "decision", "decided", "choose", "choice"))
        {
            yield return new PatternSignal(proposal.ProjectId, $"decision:{domainKey}", CrossRunMemoryPatternType.RepeatedDecisionCandidate, 5, proposal);
        }

        if (proposal.ProposalType == MemoryProposalType.ProjectFactCandidate)
        {
            yield return new PatternSignal(proposal.ProjectId, $"fact:{domainKey}", CrossRunMemoryPatternType.RepeatedFactCandidate, 6, proposal);
        }

        if (ContainsAny(text, BoundaryTerms))
        {
            yield return new PatternSignal(proposal.ProjectId, $"boundary:{domainKey}", CrossRunMemoryPatternType.RepeatedBoundaryCandidate, 7, proposal);
        }

        if (ContainsAny(text, WorkflowTerms))
        {
            yield return new PatternSignal(proposal.ProjectId, $"workflow:{domainKey}", CrossRunMemoryPatternType.RepeatedWorkflowPatternCandidate, 8, proposal);
        }

        if (ContainsAny(text, PolicyTerms))
        {
            yield return new PatternSignal(proposal.ProjectId, $"policy:{domainKey}", CrossRunMemoryPatternType.RepeatedPolicyInvariantCandidate, 9, proposal);
        }

        if (ContainsAny(text, ValidationTerms))
        {
            yield return new PatternSignal(proposal.ProjectId, $"validation:{domainKey}", CrossRunMemoryPatternType.RepeatedValidationFindingCandidate, 10, proposal);
        }
    }

    private static CrossRunMemoryPatternCandidateCreateRequest BuildCandidate(PatternSignal signal, IReadOnlyList<MemoryProposal> proposals, IReadOnlyList<Guid> workflowRunIds, CrossRunMemoryPatternDetectionOptions options)
    {
        var proposalIds = proposals.Select(proposal => proposal.MemoryProposalId).Distinct().OrderBy(id => id).ToList();
        var stepIds = proposals.Select(proposal => proposal.WorkflowRunStepId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().OrderBy(id => id).ToList();
        var checkpointIds = proposals.Select(proposal => proposal.WorkflowCheckpointId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().OrderBy(id => id).ToList();
        var recurrenceScore = Math.Min(1m, decimal.Round((decimal)proposalIds.Count / Math.Max(2, options.MinimumRecurrenceCount + 2), 2, MidpointRounding.AwayFromZero));

        return new CrossRunMemoryPatternCandidateCreateRequest
        {
            CrossRunMemoryPatternCandidateId = DeterministicGuid(signal.ProjectId, signal.SignalKey),
            ProjectId = signal.ProjectId,
            PatternCandidateKey = $"cross-run-pattern:{signal.SignalKey}",
            Status = signal.PatternType == CrossRunMemoryPatternType.PortableEngineeringMemoryPatternCandidate ? CrossRunMemoryPatternCandidateStatus.RequiresSanitizationReview : CrossRunMemoryPatternCandidateStatus.ReadyForReview,
            PatternType = signal.PatternType,
            PatternBand = signal.PatternType == CrossRunMemoryPatternType.PortableEngineeringMemoryPatternCandidate ? CrossRunMemoryPatternBand.PortableCandidateRequiresReview : ClassifyBand(proposalIds.Count),
            RecurrenceScore = recurrenceScore,
            MemoryProposalIds = proposalIds,
            WorkflowRunIds = workflowRunIds,
            WorkflowRunStepIds = stepIds,
            WorkflowCheckpointIds = checkpointIds,
            SafePatternSummary = BuildPatternSummary(signal.PatternType, proposalIds.Count, workflowRunIds.Count),
            SafeEvidenceSummary = "Recurring staged proposals were observed across separate workflow runs. This is review evidence only.",
            SafeReviewRecommendation = signal.PatternType == CrossRunMemoryPatternType.PortableEngineeringMemoryPatternCandidate
                ? "Human review and sanitization review remain required before any later memory action."
                : "Human review remains required before any later memory action.",
            EvidenceReferences = proposals.Select(proposal => Evidence(proposal)).ToList(),
            ReviewNotes = new[] { ReviewNote(signal.PatternType, proposalIds.Count, workflowRunIds.Count) },
            CreatedByActorType = string.IsNullOrWhiteSpace(options.CreatedByActorType) ? "system" : options.CreatedByActorType.Trim(),
            CreatedByActorId = string.IsNullOrWhiteSpace(options.CreatedByActorId) ? "cross-run-memory-pattern-detector" : options.CreatedByActorId.Trim(),
            MetadataVersion = 1,
            MetadataJson = JsonSerializer.Serialize(new { signal = signal.SignalKey, recurrenceCount = proposalIds.Count, workflowRunCount = workflowRunIds.Count })
        };
    }

    private static CrossRunMemoryPatternEvidenceReference Evidence(MemoryProposal proposal) => new()
    {
        EvidenceType = "MemoryProposal",
        EvidenceId = proposal.MemoryProposalId.ToString(),
        EvidenceLabel = "Staged memory proposal",
        SafeSummary = "Staged proposal contributes to cross-run pattern review only.",
        AllowedUse = "PatternReview",
        MemoryProposalId = proposal.MemoryProposalId,
        WorkflowRunId = proposal.WorkflowRunId,
        WorkflowRunStepId = proposal.WorkflowRunStepId,
        WorkflowCheckpointId = proposal.WorkflowCheckpointId
    };

    private static CrossRunMemoryPatternReviewNote ReviewNote(CrossRunMemoryPatternType patternType, int proposalCount, int workflowRunCount) => new()
    {
        NoteType = patternType.ToString(),
        SafeSummary = $"Pattern candidate has {proposalCount.ToString(CultureInfo.InvariantCulture)} staged proposals across {workflowRunCount.ToString(CultureInfo.InvariantCulture)} workflow runs. It is advisory review evidence only.",
        Severity = patternType == CrossRunMemoryPatternType.PortableEngineeringMemoryPatternCandidate ? "warning" : "info"
    };

    private static CrossRunMemoryPatternBand ClassifyBand(int recurrenceCount) => recurrenceCount switch
    {
        >= 5 => CrossRunMemoryPatternBand.HighRecurrence,
        >= 3 => CrossRunMemoryPatternBand.MediumRecurrence,
        _ => CrossRunMemoryPatternBand.CrossRunCandidate
    };

    private static string BuildPatternSummary(CrossRunMemoryPatternType patternType, int proposalCount, int workflowRunCount) => patternType switch
    {
        CrossRunMemoryPatternType.PortableEngineeringMemoryPatternCandidate => $"A portable review pattern appears in {proposalCount.ToString(CultureInfo.InvariantCulture)} staged proposals across {workflowRunCount.ToString(CultureInfo.InvariantCulture)} workflow runs.",
        CrossRunMemoryPatternType.RepeatedFailureModeCandidate => $"A repeated failure-mode pattern appears in {proposalCount.ToString(CultureInfo.InvariantCulture)} staged proposals across {workflowRunCount.ToString(CultureInfo.InvariantCulture)} workflow runs.",
        CrossRunMemoryPatternType.RepeatedRiskCandidate => $"A repeated risk pattern appears in {proposalCount.ToString(CultureInfo.InvariantCulture)} staged proposals across {workflowRunCount.ToString(CultureInfo.InvariantCulture)} workflow runs.",
        CrossRunMemoryPatternType.RepeatedConventionCandidate => $"A repeated convention pattern appears in {proposalCount.ToString(CultureInfo.InvariantCulture)} staged proposals across {workflowRunCount.ToString(CultureInfo.InvariantCulture)} workflow runs.",
        CrossRunMemoryPatternType.RepeatedDebuggingLessonCandidate => $"A repeated debugging lesson pattern appears in {proposalCount.ToString(CultureInfo.InvariantCulture)} staged proposals across {workflowRunCount.ToString(CultureInfo.InvariantCulture)} workflow runs.",
        CrossRunMemoryPatternType.RepeatedDecisionCandidate => $"A repeated decision-themed pattern appears in {proposalCount.ToString(CultureInfo.InvariantCulture)} staged proposals across {workflowRunCount.ToString(CultureInfo.InvariantCulture)} workflow runs.",
        CrossRunMemoryPatternType.RepeatedBoundaryCandidate => $"A repeated boundary pattern appears in {proposalCount.ToString(CultureInfo.InvariantCulture)} staged proposals across {workflowRunCount.ToString(CultureInfo.InvariantCulture)} workflow runs.",
        CrossRunMemoryPatternType.RepeatedWorkflowPatternCandidate => $"A repeated workflow pattern appears in {proposalCount.ToString(CultureInfo.InvariantCulture)} staged proposals across {workflowRunCount.ToString(CultureInfo.InvariantCulture)} workflow runs.",
        CrossRunMemoryPatternType.RepeatedPolicyInvariantCandidate => $"A repeated policy-invariant pattern appears in {proposalCount.ToString(CultureInfo.InvariantCulture)} staged proposals across {workflowRunCount.ToString(CultureInfo.InvariantCulture)} workflow runs.",
        _ => $"A repeated staged-memory pattern appears in {proposalCount.ToString(CultureInfo.InvariantCulture)} staged proposals across {workflowRunCount.ToString(CultureInfo.InvariantCulture)} workflow runs."
    };

    private static string BuildDomainKey(MemoryProposal proposal, string text)
    {
        var subject = NormalizeKey($"{proposal.SubjectType}:{proposal.SubjectId}");
        if (!string.IsNullOrWhiteSpace(subject) && subject != ":") return subject;

        var terms = Regex.Matches(text, "[a-z0-9]+")
            .Select(match => match.Value)
            .Where(term => term.Length >= 4)
            .Where(term => !IsStopWord(term))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(term => term, StringComparer.Ordinal)
            .Take(6)
            .ToList();

        return terms.Count == 0 ? "unspecified" : string.Join("-", terms);
    }

    private static Guid DeterministicGuid(Guid projectId, string key)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{projectId:N}:{key}"));
        var guidBytes = bytes.Take(16).ToArray();
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x40);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private static bool ContainsAny(string text, params string[] terms) => terms.Any(term => text.Contains(term, StringComparison.Ordinal));
    private static string NormalizeKey(string value) => Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9:-]+", "-").Trim('-');
    private static string NormalizeText(string value) => Regex.Replace(value, "(?<!^)([A-Z])", " $1").ToLowerInvariant().Replace("_", " ").Replace("-", " ");
    private static bool IsStopWord(string term) => term is "memory" or "proposal" or "proposals" or "candidate" or "candidates" or "review" or "human" or "governed" or "staged" or "across" or "later" or "requires" or "required";

    private sealed record PatternSignal(Guid ProjectId, string SignalKey, CrossRunMemoryPatternType PatternType, int Priority, MemoryProposal Proposal);
}
