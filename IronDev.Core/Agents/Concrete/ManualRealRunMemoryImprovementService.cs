using Audit = IronDev.Core.Agents.Audit;
using AuditThoughtLedgerEntry = IronDev.Core.Agents.Audit.ThoughtLedgerEntry;

namespace IronDev.Core.Agents.Concrete;

public enum ManualRealRunMemoryImprovementStatus
{
    Succeeded = 1,
    NoProposalNeeded = 2,
    NeedsHumanReview = 3,
    InvalidRequest = 4,
    RejectedUnsafeEvidence = 5,
    Failed = 6
}

public sealed record ManualRealRunMemoryImprovementRequest
{
    public required string MemoryImprovementRunId { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public string? CampaignId { get; init; }
    public required string RequestedByUserId { get; init; }
    public required RealRunEvidenceBundle EvidenceBundle { get; init; }
    public bool UseModelBackedDetector { get; init; }
    public bool PersistProposal { get; init; }
    public DateTimeOffset RequestedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record RealRunEvidenceBundle
{
    public IReadOnlyList<RealRunEvidenceItem> Items { get; init; } = [];
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool ContainsSecret { get; init; }
    public bool ContainsAuthorityClaim { get; init; }
    public bool ContainsMemoryPromotionClaim { get; init; }
    public bool IsAuthoritativeForAction { get; init; }
}

public sealed record RealRunEvidenceItem
{
    public required string EvidenceId { get; init; }
    public required string RefType { get; init; }
    public required string RefId { get; init; }
    public string Source { get; init; } = string.Empty;
    public required string Summary { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool SupportsMemoryImprovement { get; init; }
    public bool IsFromRealRun { get; init; }
    public bool IsSanitised { get; init; }
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool ContainsSecret { get; init; }
    public bool IsAuthoritativeForAction { get; init; }
    public bool ClaimsMemoryPromotion { get; init; }
}

public sealed record RealRunMemoryPattern
{
    public required string PatternId { get; init; }
    public required string PatternType { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public int OccurrenceCount { get; init; }
    public bool IsActionable { get; init; }
    public bool RequiresHumanReview { get; init; }
    public bool CreatesAuthority { get; init; }
    public bool PromotesMemory { get; init; }
    public bool WritesCollectiveMemory { get; init; }
    public bool WritesWeaviate { get; init; }
}

public sealed record RealRunMemoryImprovementCandidate
{
    public required string CandidateId { get; init; }
    public required string CandidateType { get; init; }
    public required string ProposedTitle { get; init; }
    public required string ProposedSummary { get; init; }
    public IReadOnlyList<RealRunMemoryPattern> Patterns { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public MemoryImprovementProposalDraft? ProposalDraft { get; init; }
    public bool IsProposalOnly { get; init; }
    public bool RequiresHumanReview { get; init; }
    public bool CreatesAuthority { get; init; }
    public bool PromotesMemory { get; init; }
    public bool CreatesCollectiveMemory { get; init; }
    public bool WritesWeaviate { get; init; }
}

public sealed record RealRunMemoryImprovementStage
{
    public required bool Succeeded { get; init; }
    public IReadOnlyList<RealRunMemoryPattern> Patterns { get; init; } = [];
    public IReadOnlyList<RealRunMemoryImprovementCandidate> Candidates { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool IsProposalOnly { get; init; }
    public bool RequiresHumanReview { get; init; }
    public bool CreatesAuthority { get; init; }
    public bool PromotesMemory { get; init; }
    public bool CreatesCollectiveMemory { get; init; }
    public bool WritesWeaviate { get; init; }
}

public sealed record RealRunMemoryImprovementSummary
{
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<string> RecommendedNextActions { get; init; } = [];
    public IReadOnlyList<string> RequiredHumanDecisions { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool IsAdvisoryOnly { get; init; }
    public bool GrantsApproval { get; init; }
    public bool CreatesAuthority { get; init; }
    public bool PromotesMemory { get; init; }
    public bool CreatesCollectiveMemory { get; init; }
    public bool WritesWeaviate { get; init; }
}

public sealed record ManualRealRunMemoryImprovementIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

public sealed record ManualRealRunMemoryImprovementResult
{
    public required bool Succeeded { get; init; }
    public required ManualRealRunMemoryImprovementStatus Status { get; init; }
    public required string MemoryImprovementRunId { get; init; }
    public RealRunMemoryImprovementStage? ImprovementStage { get; init; }
    public RealRunMemoryImprovementSummary? Summary { get; init; }
    public Audit.AgentRunAuditEnvelope? AuditEnvelope { get; init; }
    public IReadOnlyList<ManualRealRunMemoryImprovementIssue> Issues { get; init; } = [];
}

public interface IManualRealRunMemoryImprovementService
{
    Task<ManualRealRunMemoryImprovementResult> RunAsync(
        ManualRealRunMemoryImprovementRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ManualRealRunMemoryImprovementValidator
{
    public const string RealRunMemoryRequestRequired = "REAL_RUN_MEMORY_REQUEST_REQUIRED";
    public const string RealRunMemoryScopeRequired = "REAL_RUN_MEMORY_SCOPE_REQUIRED";
    public const string RealRunMemoryUserRequired = "REAL_RUN_MEMORY_USER_REQUIRED";
    public const string RealRunMemoryEvidenceRequired = "REAL_RUN_MEMORY_EVIDENCE_REQUIRED";
    public const string RealRunMemoryUnsafeEvidence = "REAL_RUN_MEMORY_UNSAFE_EVIDENCE";
    public const string RealRunMemoryModelBackedForbidden = "REAL_RUN_MEMORY_MODEL_BACKED_FORBIDDEN";
    public const string RealRunMemoryPersistenceForbidden = "REAL_RUN_MEMORY_PERSISTENCE_FORBIDDEN";
    public const string RealRunMemoryPatternInvalid = "REAL_RUN_MEMORY_PATTERN_INVALID";
    public const string RealRunMemoryCandidateInvalid = "REAL_RUN_MEMORY_CANDIDATE_INVALID";
    public const string RealRunMemoryProposalUnsafe = "REAL_RUN_MEMORY_PROPOSAL_UNSAFE";
    public const string RealRunMemoryAuditInvalid = "REAL_RUN_MEMORY_AUDIT_INVALID";
    public const string RealRunMemoryThoughtLedgerInvalid = "REAL_RUN_MEMORY_THOUGHT_LEDGER_INVALID";
    public const string RealRunMemoryPromotionForbidden = "REAL_RUN_MEMORY_PROMOTION_FORBIDDEN";
    public const string RealRunIndexWriteForbidden = "REAL_RUN_MEMORY_INDEX_WRITE_FORBIDDEN";
    public const string RealRunMemoryRuntimeWiringForbidden = "REAL_RUN_MEMORY_RUNTIME_WIRING_FORBIDDEN";

    private static readonly IReadOnlySet<string> AllowedPatternTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "RepeatedFailureMode",
        "RepeatedGovernanceBlock",
        "RepeatedManualCorrection",
        "Contradiction",
        "StaleMemory",
        "RetrievalMiss",
        "DuplicateProposal",
        "MissingProjectConvention",
        "MissingOperationalRule"
    };

    private static readonly IReadOnlyList<string> RawPrivateReasoningMarkers =
    [
        "raw" + "prompt",
        "raw" + "completion",
        "chain" + "of" + "thought",
        "scratch" + "pad",
        "private" + "reasoning",
        "hidden" + "deliberation",
        "system" + "prompt",
        "developer" + "prompt"
    ];

    private static readonly IReadOnlyList<string> AuthorityMarkers =
    [
        "approved for execution",
        "human approved",
        "policy cleared",
        "authoritative for action",
        "grant authority",
        "bypass governance",
        "override policy"
    ];

    private static readonly IReadOnlyList<string> MemoryAuthorityMarkers =
    [
        "accepted memory",
        "promoted memory",
        "system rule",
        "collective memory created",
        "write weaviate"
    ];

    public IReadOnlyList<ManualRealRunMemoryImprovementIssue> ValidateRequest(ManualRealRunMemoryImprovementRequest? request)
    {
        var issues = new List<ManualRealRunMemoryImprovementIssue>();

        if (request is null)
        {
            AddError(issues, RealRunMemoryRequestRequired, "Manual real-run memory-improvement request is required.", "request");
            return issues;
        }

        if (string.IsNullOrWhiteSpace(request.MemoryImprovementRunId))
            AddError(issues, RealRunMemoryRequestRequired, "MemoryImprovementRunId is required.", nameof(request.MemoryImprovementRunId));

        if (string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.ProjectId))
            AddError(issues, RealRunMemoryScopeRequired, "TenantId and ProjectId are required.", "Scope");

        if (string.IsNullOrWhiteSpace(request.RequestedByUserId))
            AddError(issues, RealRunMemoryUserRequired, "RequestedByUserId is required.", nameof(request.RequestedByUserId));

        if (request.UseModelBackedDetector)
            AddError(issues, RealRunMemoryModelBackedForbidden, "Model-backed real-run memory-improvement detection is not enabled in this PR.", nameof(request.UseModelBackedDetector));

        if (request.PersistProposal)
            AddError(issues, RealRunMemoryPersistenceForbidden, "PersistProposal is forbidden for this PR; proposal drafts are returned only.", nameof(request.PersistProposal));

        ValidateEvidenceBundle(request.EvidenceBundle, issues);
        ValidateTextValues(issues, "Request", [request.MemoryImprovementRunId, request.TenantId, request.ProjectId, request.CampaignId, request.RequestedByUserId]);

        return issues;
    }

    public IReadOnlyList<ManualRealRunMemoryImprovementIssue> ValidatePattern(RealRunMemoryPattern pattern)
    {
        var issues = new List<ManualRealRunMemoryImprovementIssue>();
        ValidatePattern(pattern, issues);
        return issues;
    }

    public IReadOnlyList<ManualRealRunMemoryImprovementIssue> ValidateCandidate(RealRunMemoryImprovementCandidate candidate)
    {
        var issues = new List<ManualRealRunMemoryImprovementIssue>();
        ValidateCandidate(candidate, issues);
        return issues;
    }

    public IReadOnlyList<ManualRealRunMemoryImprovementIssue> ValidateStage(RealRunMemoryImprovementStage stage)
    {
        var issues = new List<ManualRealRunMemoryImprovementIssue>();

        foreach (var pattern in stage.Patterns)
            ValidatePattern(pattern, issues);

        foreach (var candidate in stage.Candidates)
            ValidateCandidate(candidate, issues);

        if (!stage.IsProposalOnly)
            AddError(issues, RealRunMemoryCandidateInvalid, "Improvement stage must be proposal-only.", nameof(stage.IsProposalOnly));

        if (stage.Candidates.Count > 0 && !stage.RequiresHumanReview)
            AddError(issues, RealRunMemoryCandidateInvalid, "Improvement candidates require human review.", nameof(stage.RequiresHumanReview));

        if (stage.CreatesAuthority)
            AddError(issues, RealRunMemoryCandidateInvalid, "Improvement stage must not create authority.", nameof(stage.CreatesAuthority));

        if (stage.PromotesMemory)
            AddError(issues, RealRunMemoryPromotionForbidden, "Improvement stage must not change memory authority.", nameof(stage.PromotesMemory));

        if (stage.CreatesCollectiveMemory)
            AddError(issues, RealRunMemoryPromotionForbidden, "Improvement stage must not create collective memory.", nameof(stage.CreatesCollectiveMemory));

        if (stage.WritesWeaviate)
            AddError(issues, RealRunIndexWriteForbidden, "Improvement stage must not write a vector index.", nameof(stage.WritesWeaviate));

        return issues;
    }

    private static void ValidateEvidenceBundle(RealRunEvidenceBundle? bundle, List<ManualRealRunMemoryImprovementIssue> issues)
    {
        if (bundle is null || bundle.Items.Count == 0)
        {
            AddError(issues, RealRunMemoryEvidenceRequired, "At least one real-run evidence item is required.", nameof(ManualRealRunMemoryImprovementRequest.EvidenceBundle));
            return;
        }

        if (bundle.ContainsRawPrivateReasoning || bundle.ContainsSecret || bundle.ContainsAuthorityClaim || bundle.ContainsMemoryPromotionClaim || bundle.IsAuthoritativeForAction)
        {
            AddError(issues, RealRunMemoryUnsafeEvidence, "Evidence bundle contains unsafe authority, secret, raw reasoning, or memory-authority claims.", nameof(RealRunEvidenceBundle));
        }

        foreach (var item in bundle.Items)
            ValidateEvidenceItem(item, issues);
    }

    private static void ValidateEvidenceItem(RealRunEvidenceItem item, List<ManualRealRunMemoryImprovementIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(item.EvidenceId) ||
            string.IsNullOrWhiteSpace(item.RefType) ||
            string.IsNullOrWhiteSpace(item.RefId) ||
            string.IsNullOrWhiteSpace(item.Summary))
        {
            AddError(issues, RealRunMemoryEvidenceRequired, "EvidenceId, RefType, RefId, and Summary are required for each evidence item.", nameof(RealRunEvidenceItem));
        }

        if (!item.SupportsMemoryImprovement)
            AddError(issues, RealRunMemoryEvidenceRequired, "Evidence item must explicitly support memory-improvement review.", nameof(item.SupportsMemoryImprovement));

        if (!item.IsFromRealRun)
            AddError(issues, RealRunMemoryUnsafeEvidence, "Evidence item must come from real governed run evidence.", nameof(item.IsFromRealRun));

        if (!item.IsSanitised)
            AddError(issues, RealRunMemoryUnsafeEvidence, "Evidence item must be sanitised before memory-improvement review.", nameof(item.IsSanitised));

        if (item.ContainsRawPrivateReasoning || item.ContainsSecret || item.IsAuthoritativeForAction || item.ClaimsMemoryPromotion)
        {
            AddError(issues, RealRunMemoryUnsafeEvidence, "Evidence item contains unsafe authority, secret, raw reasoning, or memory-authority claims.", item.EvidenceId);
        }

        ValidateTextValues(issues, "Evidence", [item.EvidenceId, item.RefType, item.RefId, item.Source, item.Summary, .. item.EvidenceRefs]);
    }

    private static void ValidatePattern(RealRunMemoryPattern pattern, List<ManualRealRunMemoryImprovementIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(pattern.PatternId) ||
            string.IsNullOrWhiteSpace(pattern.PatternType) ||
            string.IsNullOrWhiteSpace(pattern.Summary))
        {
            AddError(issues, RealRunMemoryPatternInvalid, "PatternId, PatternType, and Summary are required.", nameof(RealRunMemoryPattern));
        }

        if (!AllowedPatternTypes.Contains(pattern.PatternType))
            AddError(issues, RealRunMemoryPatternInvalid, $"PatternType '{pattern.PatternType}' is not supported.", nameof(pattern.PatternType));

        if (pattern.OccurrenceCount <= 0)
            AddError(issues, RealRunMemoryPatternInvalid, "Pattern OccurrenceCount must be positive.", nameof(pattern.OccurrenceCount));

        if (!pattern.RequiresHumanReview)
            AddError(issues, RealRunMemoryPatternInvalid, "Pattern requires human review.", nameof(pattern.RequiresHumanReview));

        if (pattern.CreatesAuthority)
            AddError(issues, RealRunMemoryPatternInvalid, "Pattern must not create authority.", nameof(pattern.CreatesAuthority));

        if (pattern.PromotesMemory)
            AddError(issues, RealRunMemoryPromotionForbidden, "Pattern must not change memory authority.", nameof(pattern.PromotesMemory));

        if (pattern.WritesCollectiveMemory)
            AddError(issues, RealRunMemoryPromotionForbidden, "Pattern must not write collective memory.", nameof(pattern.WritesCollectiveMemory));

        if (pattern.WritesWeaviate)
            AddError(issues, RealRunIndexWriteForbidden, "Pattern must not write a vector index.", nameof(pattern.WritesWeaviate));

        ValidateTextValues(issues, "Pattern", [pattern.PatternId, pattern.PatternType, pattern.Summary, .. pattern.EvidenceRefs]);
    }

    private static void ValidateCandidate(RealRunMemoryImprovementCandidate candidate, List<ManualRealRunMemoryImprovementIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(candidate.CandidateId) ||
            string.IsNullOrWhiteSpace(candidate.CandidateType) ||
            string.IsNullOrWhiteSpace(candidate.ProposedTitle) ||
            string.IsNullOrWhiteSpace(candidate.ProposedSummary))
        {
            AddError(issues, RealRunMemoryCandidateInvalid, "CandidateId, CandidateType, ProposedTitle, and ProposedSummary are required.", nameof(RealRunMemoryImprovementCandidate));
        }

        if (!candidate.IsProposalOnly)
            AddError(issues, RealRunMemoryCandidateInvalid, "Candidate must be proposal-only.", nameof(candidate.IsProposalOnly));

        if (!candidate.RequiresHumanReview)
            AddError(issues, RealRunMemoryCandidateInvalid, "Candidate requires human review.", nameof(candidate.RequiresHumanReview));

        if (candidate.CreatesAuthority)
            AddError(issues, RealRunMemoryCandidateInvalid, "Candidate must not create authority.", nameof(candidate.CreatesAuthority));

        if (candidate.PromotesMemory)
            AddError(issues, RealRunMemoryPromotionForbidden, "Candidate must not change memory authority.", nameof(candidate.PromotesMemory));

        if (candidate.CreatesCollectiveMemory)
            AddError(issues, RealRunMemoryPromotionForbidden, "Candidate must not create collective memory.", nameof(candidate.CreatesCollectiveMemory));

        if (candidate.WritesWeaviate)
            AddError(issues, RealRunIndexWriteForbidden, "Candidate must not write a vector index.", nameof(candidate.WritesWeaviate));

        if (candidate.Patterns.Count == 0)
            AddError(issues, RealRunMemoryCandidateInvalid, "Candidate must reference at least one detected pattern.", nameof(candidate.Patterns));

        if (candidate.EvidenceRefs.Count == 0)
            AddError(issues, RealRunMemoryCandidateInvalid, "Candidate must carry evidence refs.", nameof(candidate.EvidenceRefs));

        if (candidate.ProposalDraft is null)
        {
            AddError(issues, RealRunMemoryProposalUnsafe, "Candidate must include a proposal draft.", nameof(candidate.ProposalDraft));
        }
        else
        {
            var proposalIssues = new MemoryImprovementDetectionResultValidator().Validate(new MemoryImprovementDetectionResult
            {
                DetectionResultId = $"candidate-validation-{candidate.CandidateId}",
                Findings = [candidate.ProposalDraft.SourcePattern],
                ProposalDrafts = [candidate.ProposalDraft],
                DetectedAt = DateTimeOffset.UtcNow,
                DetectedByAgentId = AgentDefinitionCatalog.MemoryImprovementAgent.AgentId,
                Warnings = ManualRealRunMemoryImprovementService.BoundaryWarnings
            });

            foreach (var issue in proposalIssues)
                AddError(issues, RealRunMemoryProposalUnsafe, issue.Message, nameof(candidate.ProposalDraft));
        }

        ValidateTextValues(issues, "Candidate", [candidate.CandidateId, candidate.CandidateType, candidate.ProposedTitle, candidate.ProposedSummary, .. candidate.EvidenceRefs]);
    }

    private static void ValidateTextValues(List<ManualRealRunMemoryImprovementIssue> issues, string field, IEnumerable<string?> values)
    {
        foreach (var value in values)
            ValidateText(issues, field, value);
    }

    private static void ValidateText(List<ManualRealRunMemoryImprovementIssue> issues, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (var marker in RawPrivateReasoningMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                AddError(issues, RealRunMemoryUnsafeEvidence, $"{field} contains raw/private reasoning marker '{marker}'.", field);
        }

        foreach (var marker in AuthorityMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                AddError(issues, RealRunMemoryUnsafeEvidence, $"{field} contains authority marker '{marker}'.", field);
        }

        foreach (var marker in MemoryAuthorityMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                AddError(issues, RealRunMemoryPromotionForbidden, $"{field} contains memory-authority marker '{marker}'.", field);
        }
    }

    private static void AddError(List<ManualRealRunMemoryImprovementIssue> issues, string code, string message, string field) =>
        issues.Add(new ManualRealRunMemoryImprovementIssue
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityError,
            Message = message,
            Field = field
        });
}

public sealed class ManualRealRunMemoryImprovementService : IManualRealRunMemoryImprovementService
{
    internal static readonly IReadOnlyList<string> BoundaryWarnings =
    [
        "Memory improvement output is proposal-only.",
        "Proposal drafts do not create accepted memory.",
        "Proposal drafts require governed review before any memory authority change."
    ];

    private readonly ManualRealRunMemoryImprovementValidator _validator;
    private readonly MemoryImprovementDetectionResultValidator _detectionValidator;
    private readonly Audit.AgentRunAuditEnvelopeValidator _auditValidator;
    private readonly Audit.ThoughtLedgerSafetyValidator _thoughtLedgerValidator;

    public ManualRealRunMemoryImprovementService()
        : this(
            new ManualRealRunMemoryImprovementValidator(),
            new MemoryImprovementDetectionResultValidator(),
            new Audit.AgentRunAuditEnvelopeValidator(),
            new Audit.ThoughtLedgerSafetyValidator())
    {
    }

    public ManualRealRunMemoryImprovementService(
        ManualRealRunMemoryImprovementValidator validator,
        MemoryImprovementDetectionResultValidator detectionValidator,
        Audit.AgentRunAuditEnvelopeValidator auditValidator,
        Audit.ThoughtLedgerSafetyValidator thoughtLedgerValidator)
    {
        _validator = validator;
        _detectionValidator = detectionValidator;
        _auditValidator = auditValidator;
        _thoughtLedgerValidator = thoughtLedgerValidator;
    }

    public Task<ManualRealRunMemoryImprovementResult> RunAsync(
        ManualRealRunMemoryImprovementRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requestIssues = _validator.ValidateRequest(request);
        if (requestIssues.Count > 0)
        {
            var status = requestIssues.Any(issue => issue.Code is ManualRealRunMemoryImprovementValidator.RealRunMemoryUnsafeEvidence or
                ManualRealRunMemoryImprovementValidator.RealRunMemoryPromotionForbidden)
                ? ManualRealRunMemoryImprovementStatus.RejectedUnsafeEvidence
                : ManualRealRunMemoryImprovementStatus.InvalidRequest;

            return Task.FromResult(Failed(request?.MemoryImprovementRunId ?? string.Empty, status, requestIssues));
        }

        var stage = BuildStage(request);
        var stageIssues = _validator.ValidateStage(stage);
        if (stageIssues.Count > 0)
            return Task.FromResult(Failed(request.MemoryImprovementRunId, ManualRealRunMemoryImprovementStatus.NeedsHumanReview, stageIssues, stage));

        var detectionResult = BuildDetectionResult(request, stage);
        var detectionIssues = _detectionValidator.Validate(detectionResult);
        if (detectionIssues.Count > 0)
        {
            return Task.FromResult(Failed(
                request.MemoryImprovementRunId,
                ManualRealRunMemoryImprovementStatus.NeedsHumanReview,
                detectionIssues.Select(issue => ToIssue(ManualRealRunMemoryImprovementValidator.RealRunMemoryProposalUnsafe, issue.Message, nameof(MemoryImprovementDetectionResult))).ToArray(),
                stage));
        }

        var summary = BuildSummary(stage);
        var auditEnvelope = BuildAuditEnvelope(request, stage, summary, detectionResult);
        var auditIssues = _auditValidator.Validate(auditEnvelope);
        if (auditIssues.Count > 0)
        {
            return Task.FromResult(Failed(
                request.MemoryImprovementRunId,
                ManualRealRunMemoryImprovementStatus.Failed,
                auditIssues.Select(issue => ToIssue(ManualRealRunMemoryImprovementValidator.RealRunMemoryAuditInvalid, issue.Message, nameof(Audit.AgentRunAuditEnvelope))).ToArray(),
                stage,
                summary,
                auditEnvelope));
        }

        var thoughtIssues = _thoughtLedgerValidator.Validate(auditEnvelope.ThoughtLedger);
        if (thoughtIssues.Count > 0)
        {
            return Task.FromResult(Failed(
                request.MemoryImprovementRunId,
                ManualRealRunMemoryImprovementStatus.Failed,
                thoughtIssues.Select(issue => ToIssue(ManualRealRunMemoryImprovementValidator.RealRunMemoryThoughtLedgerInvalid, issue.Message, nameof(auditEnvelope.ThoughtLedger))).ToArray(),
                stage,
                summary,
                auditEnvelope));
        }

        var resultStatus = stage.Candidates.Count == 0
            ? ManualRealRunMemoryImprovementStatus.NoProposalNeeded
            : ManualRealRunMemoryImprovementStatus.Succeeded;

        return Task.FromResult(new ManualRealRunMemoryImprovementResult
        {
            Succeeded = true,
            Status = resultStatus,
            MemoryImprovementRunId = request.MemoryImprovementRunId,
            ImprovementStage = stage,
            Summary = summary,
            AuditEnvelope = auditEnvelope,
            Issues = []
        });
    }

    private static RealRunMemoryImprovementStage BuildStage(ManualRealRunMemoryImprovementRequest request)
    {
        var patterns = DetectPatterns(request.MemoryImprovementRunId, request.EvidenceBundle.Items);
        var candidates = patterns
            .Select((pattern, index) => BuildCandidate(request.MemoryImprovementRunId, pattern, index))
            .ToArray();
        var evidenceRefs = CollectEvidenceRefs(request.EvidenceBundle.Items, patterns.SelectMany(pattern => pattern.EvidenceRefs));

        return new RealRunMemoryImprovementStage
        {
            Succeeded = true,
            Patterns = patterns,
            Candidates = candidates,
            EvidenceRefs = evidenceRefs,
            IsProposalOnly = true,
            RequiresHumanReview = candidates.Length > 0,
            CreatesAuthority = false,
            PromotesMemory = false,
            CreatesCollectiveMemory = false,
            WritesWeaviate = false
        };
    }

    private static IReadOnlyList<RealRunMemoryPattern> DetectPatterns(string runId, IReadOnlyList<RealRunEvidenceItem> evidenceItems)
    {
        var patterns = new List<RealRunMemoryPattern>();
        var groups = evidenceItems
            .GroupBy(item => NormalizeSummary(item.Summary), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .ToArray();

        foreach (var group in groups)
        {
            var items = group.ToArray();
            patterns.Add(BuildPattern(
                runId,
                patterns.Count + 1,
                DetectPatternType(items.Select(item => item.Summary)),
                $"Repeated real-run evidence observed: {items[0].Summary}",
                items.Length,
                CollectEvidenceRefs(items, [])));
        }

        foreach (var item in evidenceItems)
        {
            var explicitType = DetectExplicitPatternType(item.Summary);
            if (explicitType is null)
                continue;

            var key = $"{explicitType}:{NormalizeSummary(item.Summary)}";
            if (patterns.Any(pattern => string.Equals($"{pattern.PatternType}:{NormalizeSummary(pattern.Summary)}", key, StringComparison.OrdinalIgnoreCase)))
                continue;

            patterns.Add(BuildPattern(
                runId,
                patterns.Count + 1,
                explicitType,
                $"Real-run evidence indicates {explicitType}: {item.Summary}",
                1,
                CollectEvidenceRefs([item], [])));
        }

        return patterns;
    }

    private static RealRunMemoryPattern BuildPattern(
        string runId,
        int index,
        string patternType,
        string summary,
        int occurrenceCount,
        IReadOnlyList<string> evidenceRefs) =>
        new()
        {
            PatternId = $"real-run-pattern-{runId}-{index:000}",
            PatternType = patternType,
            Summary = summary,
            EvidenceRefs = evidenceRefs,
            OccurrenceCount = occurrenceCount,
            IsActionable = true,
            RequiresHumanReview = true,
            CreatesAuthority = false,
            PromotesMemory = false,
            WritesCollectiveMemory = false,
            WritesWeaviate = false
        };

    private static RealRunMemoryImprovementCandidate BuildCandidate(string runId, RealRunMemoryPattern pattern, int index)
    {
        var sourcePattern = new MemoryImprovementPatternFinding
        {
            PatternFindingId = $"memory-pattern-{pattern.PatternId}",
            PatternType = ToMemoryImprovementPatternType(pattern.PatternType),
            Summary = pattern.Summary,
            Confidence = pattern.OccurrenceCount > 1 ? 0.8m : 0.68m,
            EvidenceRefs = pattern.EvidenceRefs,
            RelatedMemoryIds = [],
            RelatedProposalIds = [],
            IsDuplicateCandidate = string.Equals(pattern.PatternType, "DuplicateProposal", StringComparison.Ordinal),
            RequiresHumanReview = true
        };

        var proposalDraft = new MemoryImprovementProposalDraft
        {
            ProposalDraftId = $"memory-proposal-draft-{runId}-{index + 1:000}",
            Title = $"Review {SplitWords(pattern.PatternType)} evidence from governed runs",
            Summary = $"Create a proposal-only memory improvement draft for this observed {SplitWords(pattern.PatternType).ToLowerInvariant()} pattern.",
            Rationale = "Real governed run evidence shows a repeatable or explicit improvement candidate; governed human review remains required before any memory authority change.",
            SourcePattern = sourcePattern,
            EvidenceRefs = pattern.EvidenceRefs,
            IsProposalOnly = true,
            CreatesCollectiveMemory = false,
            PromotesMemory = false,
            RequiresHumanReview = true
        };

        return new RealRunMemoryImprovementCandidate
        {
            CandidateId = $"real-run-memory-candidate-{runId}-{index + 1:000}",
            CandidateType = pattern.PatternType,
            ProposedTitle = proposalDraft.Title,
            ProposedSummary = proposalDraft.Summary,
            Patterns = [pattern],
            EvidenceRefs = pattern.EvidenceRefs,
            ProposalDraft = proposalDraft,
            IsProposalOnly = true,
            RequiresHumanReview = true,
            CreatesAuthority = false,
            PromotesMemory = false,
            CreatesCollectiveMemory = false,
            WritesWeaviate = false
        };
    }

    private static MemoryImprovementDetectionResult BuildDetectionResult(
        ManualRealRunMemoryImprovementRequest request,
        RealRunMemoryImprovementStage stage) =>
        new()
        {
            DetectionResultId = $"real-run-memory-detection-{request.MemoryImprovementRunId}",
            Findings = stage.Candidates
                .Select(candidate => candidate.ProposalDraft!.SourcePattern)
                .ToArray(),
            ProposalDrafts = stage.Candidates
                .Select(candidate => candidate.ProposalDraft!)
                .ToArray(),
            NoProposalReason = stage.Candidates.Count == 0 ? MemoryImprovementNoProposalReason.InsufficientEvidence : null,
            DetectedAt = request.RequestedAtUtc,
            DetectedByAgentId = AgentDefinitionCatalog.MemoryImprovementAgent.AgentId,
            CorrelationId = request.MemoryImprovementRunId,
            Warnings = BoundaryWarnings
        };

    private static RealRunMemoryImprovementSummary BuildSummary(RealRunMemoryImprovementStage stage)
    {
        var hasCandidates = stage.Candidates.Count > 0;
        return new RealRunMemoryImprovementSummary
        {
            Title = hasCandidates ? "Memory improvement candidates detected from real runs" : "No memory improvement proposal needed",
            Summary = hasCandidates
                ? "Safe governed run evidence produced proposal-only memory improvement candidates for human review."
                : "Safe governed run evidence did not show a repeated, stale, contradictory, retrieval-miss, duplicate, convention, or operational-rule pattern.",
            RecommendedNextActions = hasCandidates
                ? ["Review proposal drafts manually before any memory authority change.", "Compare evidence refs against run audit and tool audit records."]
                : ["Keep evidence as audit context; do not create memory from this run."],
            RequiredHumanDecisions = hasCandidates
                ? ["Decide whether any proposal draft should enter a separate governed memory proposal review."]
                : [],
            EvidenceRefs = stage.EvidenceRefs,
            IsAdvisoryOnly = true,
            GrantsApproval = false,
            CreatesAuthority = false,
            PromotesMemory = false,
            CreatesCollectiveMemory = false,
            WritesWeaviate = false
        };
    }

    private static Audit.AgentRunAuditEnvelope BuildAuditEnvelope(
        ManualRealRunMemoryImprovementRequest request,
        RealRunMemoryImprovementStage stage,
        RealRunMemoryImprovementSummary summary,
        MemoryImprovementDetectionResult detectionResult)
    {
        var runId = request.MemoryImprovementRunId;
        var evidenceRefs = stage.EvidenceRefs;
        var status = stage.Candidates.Count > 0 ? Audit.AgentRunStatus.CompletedWithWarnings : Audit.AgentRunStatus.Completed;
        var outputs = new List<Audit.AgentRunOutputRef>
        {
            new()
            {
                OutputRefId = $"output-{runId}-stage",
                AgentRunId = runId,
                RefType = nameof(RealRunMemoryImprovementStage),
                RefId = $"stage-{runId}",
                Summary = "Real-run memory improvement stage produced proposal-only evidence.",
                IsReviewOnly = false,
                IsProposalOnly = true,
                CreatesAuthority = false,
                CreatesRuntimeAction = false,
                ContainsRawPrivateReasoning = false,
                EvidenceRefs = evidenceRefs
            },
            new()
            {
                OutputRefId = $"output-{runId}-summary",
                AgentRunId = runId,
                RefType = nameof(RealRunMemoryImprovementSummary),
                RefId = $"summary-{runId}",
                Summary = summary.Summary,
                IsReviewOnly = false,
                IsProposalOnly = true,
                CreatesAuthority = false,
                CreatesRuntimeAction = false,
                ContainsRawPrivateReasoning = false,
                EvidenceRefs = evidenceRefs
            },
            new()
            {
                OutputRefId = $"output-{runId}-detection",
                AgentRunId = runId,
                RefType = nameof(MemoryImprovementDetectionResult),
                RefId = detectionResult.DetectionResultId,
                Summary = "MemoryImprovementDetectionResult was produced as proposal-only output.",
                IsReviewOnly = false,
                IsProposalOnly = true,
                CreatesAuthority = false,
                CreatesRuntimeAction = false,
                ContainsRawPrivateReasoning = false,
                EvidenceRefs = evidenceRefs
            }
        };

        outputs.AddRange(stage.Candidates.Select(candidate => new Audit.AgentRunOutputRef
        {
            OutputRefId = $"output-{candidate.CandidateId}",
            AgentRunId = runId,
            RefType = nameof(MemoryImprovementProposalDraft),
            RefId = candidate.ProposalDraft?.ProposalDraftId ?? candidate.CandidateId,
            Summary = "Proposal-only memory improvement candidate produced without persistence.",
            IsReviewOnly = false,
            IsProposalOnly = true,
            CreatesAuthority = false,
            CreatesRuntimeAction = false,
            ContainsRawPrivateReasoning = false,
            EvidenceRefs = candidate.EvidenceRefs
        }));

        return new Audit.AgentRunAuditEnvelope
        {
            Run = new Audit.AgentRunRecord
            {
                AgentRunId = runId,
                TenantId = request.TenantId,
                ProjectId = request.ProjectId,
                CampaignId = request.CampaignId,
                RunId = runId,
                AgentId = AgentDefinitionCatalog.MemoryImprovementAgent.AgentId,
                AgentName = AgentDefinitionCatalog.MemoryImprovementAgent.Name,
                RequestedByUserId = request.RequestedByUserId,
                TriggerType = Audit.AgentRunTriggerType.ManualUserRequest,
                Status = status,
                RequestSummary = "Manual memory-improvement detection from real governed run evidence.",
                Purpose = "Detect proposal-only memory-improvement candidates from supplied real-run evidence.",
                CreatedAtUtc = request.RequestedAtUtc,
                StartedAtUtc = request.RequestedAtUtc,
                CompletedAtUtc = request.RequestedAtUtc
            },
            AgentDefinitionSnapshot = AgentDefinitionCatalog.MemoryImprovementAgent,
            Inputs = request.EvidenceBundle.Items.Select(item => new Audit.AgentRunInputRef
            {
                InputRefId = item.EvidenceId,
                AgentRunId = runId,
                RefType = item.RefType,
                RefId = item.RefId,
                Source = item.Source,
                Summary = item.Summary,
                IsAuthoritativeForAction = false,
                ContainsRawPrivateReasoning = false
            }).ToArray(),
            Outputs = outputs,
            Steps = BuildSteps(runId, evidenceRefs, request.RequestedAtUtc),
            CapabilityUses = BuildCapabilityUses(runId),
            BoundaryDecisions = BuildBoundaryDecisions(runId, evidenceRefs),
            ThoughtLedger = BuildThoughtLedger(runId, evidenceRefs, request.RequestedAtUtc)
        };
    }

    private static IReadOnlyList<Audit.AgentRunStep> BuildSteps(
        string runId,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset occurredAtUtc) =>
        [
            Step(runId, 1, Audit.AgentRunStepType.Created, "Manual real-run memory improvement run created.", evidenceRefs, occurredAtUtc),
            Step(runId, 2, Audit.AgentRunStepType.InputBound, "Real governed run evidence was validated as safe context.", evidenceRefs, occurredAtUtc),
            Step(runId, 3, Audit.AgentRunStepType.CapabilityEvaluated, "Memory-improvement proposal capability was limited to proposal-only output.", evidenceRefs, occurredAtUtc),
            Step(runId, 4, Audit.AgentRunStepType.OutputRecorded, "Pattern and candidate outputs were recorded without persistence.", evidenceRefs, occurredAtUtc),
            Step(runId, 5, Audit.AgentRunStepType.Completed, "Manual real-run memory improvement detection completed without authority.", evidenceRefs, occurredAtUtc)
        ];

    private static Audit.AgentRunStep Step(
        string runId,
        int sequence,
        Audit.AgentRunStepType stepType,
        string summary,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset occurredAtUtc) =>
        new()
        {
            StepId = $"step-{runId}-{sequence:000}",
            AgentRunId = runId,
            Sequence = sequence,
            StepType = stepType,
            OccurredAtUtc = occurredAtUtc,
            Summary = summary,
            EvidenceRefs = evidenceRefs
        };

    private static IReadOnlyList<Audit.AgentCapabilityUseRecord> BuildCapabilityUses(string runId) =>
        [
            CapabilityUse(runId, AgentCapability.CreateMemoryProposal, Audit.AgentCapabilityUseOutcome.Allowed),
            CapabilityUse(runId, AgentCapability.CreateReport, Audit.AgentCapabilityUseOutcome.Allowed),
            CapabilityUse(runId, AgentCapability.PromoteCollectiveMemory, Audit.AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.RunTool, Audit.AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.MutateSource, Audit.AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.CallExternalSystem, Audit.AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.RepresentHumanApproval, Audit.AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.RepresentHumanPromotionDecision, Audit.AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.BlockExecution, Audit.AgentCapabilityUseOutcome.Blocked)
        ];

    private static Audit.AgentCapabilityUseRecord CapabilityUse(
        string runId,
        AgentCapability capability,
        Audit.AgentCapabilityUseOutcome outcome)
    {
        var definition = AgentDefinitionCatalog.MemoryImprovementAgent;
        return new Audit.AgentCapabilityUseRecord
        {
            CapabilityUseId = $"capability-{runId}-{capability}",
            AgentRunId = runId,
            Capability = capability,
            Outcome = outcome,
            Summary = $"{capability} was {outcome} for manual real-run memory-improvement detection.",
            PolicyDecisionId = $"policy-{runId}",
            BoundaryDecisionId = $"boundary-{runId}-{capability}",
            EvidenceRef = $"evidence-{runId}",
            WasDeclaredOnAgent = definition.Capabilities?.Contains(capability) == true,
            WasForbiddenOnAgent = definition.ForbiddenCapabilities?.Contains(capability) == true
        };
    }

    private static IReadOnlyList<Audit.AgentBoundaryDecision> BuildBoundaryDecisions(
        string runId,
        IReadOnlyList<string> evidenceRefs) =>
        [
            BoundaryDecision(runId, "real-run-evidence-validated", Audit.AgentBoundaryDecisionType.OutputValidation, "allow", "Real-run evidence was validated as sanitised context.", evidenceRefs),
            BoundaryDecision(runId, "memory-pattern-detected", Audit.AgentBoundaryDecisionType.OutputValidation, "allow", "Detected memory-improvement patterns are advisory evidence only.", evidenceRefs),
            BoundaryDecision(runId, "memory-improvement-proposal-only", Audit.AgentBoundaryDecisionType.OutputValidation, "allow", "Candidates are proposal-only and require human review.", evidenceRefs),
            BoundaryDecision(runId, "human-review-required", Audit.AgentBoundaryDecisionType.Policy, "block", "Human review remains required before any memory authority change.", evidenceRefs),
            BoundaryDecision(runId, "collective-memory-blocked", Audit.AgentBoundaryDecisionType.Capability, "block", "Collective-memory writes remain unavailable.", evidenceRefs),
            BoundaryDecision(runId, "index-write-blocked", Audit.AgentBoundaryDecisionType.Capability, "block", "Vector index writes remain unavailable.", evidenceRefs),
            BoundaryDecision(runId, "source-mutation-blocked", Audit.AgentBoundaryDecisionType.Capability, "block", "Source mutation remains unavailable.", evidenceRefs),
            BoundaryDecision(runId, "tool-execution-blocked", Audit.AgentBoundaryDecisionType.Capability, "block", "Tool execution remains unavailable.", evidenceRefs)
        ];

    private static Audit.AgentBoundaryDecision BoundaryDecision(
        string runId,
        string suffix,
        Audit.AgentBoundaryDecisionType boundaryType,
        string decision,
        string reason,
        IReadOnlyList<string> evidenceRefs) =>
        new()
        {
            BoundaryDecisionId = $"boundary-{runId}-{suffix}",
            AgentRunId = runId,
            BoundaryType = boundaryType,
            Decision = decision,
            Reason = reason,
            SourceRefId = $"manual-real-run-memory-{suffix}",
            GrantsAuthority = false,
            GrantsHumanApproval = false,
            GrantsPolicyApproval = false,
            GrantsMemoryPromotion = false,
            EvidenceRefs = evidenceRefs
        };

    private static IReadOnlyList<AuditThoughtLedgerEntry> BuildThoughtLedger(
        string runId,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset recordedAtUtc) =>
        [
            Thought(runId, "evidence-used", Audit.ThoughtLedgerEntryType.EvidenceUsed, "Real run evidence was validated as safe evidence.", evidenceRefs, recordedAtUtc),
            Thought(runId, "pattern-detection", Audit.ThoughtLedgerEntryType.OutputRationale, "Repeated, stale, contradictory, missing-context, or duplicate patterns may produce proposal-only candidates.", evidenceRefs, recordedAtUtc),
            Thought(runId, "proposal-boundary", Audit.ThoughtLedgerEntryType.BoundaryDecision, "Memory improvement candidates were produced as proposal-only drafts.", evidenceRefs, recordedAtUtc),
            Thought(runId, "human-review", Audit.ThoughtLedgerEntryType.Assumption, "Human review remains required before any memory authority change.", evidenceRefs, recordedAtUtc),
            Thought(runId, "no-memory-write", Audit.ThoughtLedgerEntryType.RejectedAlternative, "No collective memory, vector index, source mutation, or tool execution occurred.", evidenceRefs, recordedAtUtc)
        ];

    private static AuditThoughtLedgerEntry Thought(
        string runId,
        string suffix,
        Audit.ThoughtLedgerEntryType entryType,
        string summary,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset recordedAtUtc) =>
        new()
        {
            ThoughtLedgerEntryId = $"thought-{runId}-{suffix}",
            AgentRunId = runId,
            EntryType = entryType,
            Summary = summary,
            EvidenceRefs = evidenceRefs,
            RecordedAtUtc = recordedAtUtc,
            ContainsRawPrivateReasoning = false,
            GrantsAuthority = false,
            GrantsApproval = false,
            GrantsMemoryPromotion = false
        };

    private static string DetectPatternType(IEnumerable<string> summaries)
    {
        foreach (var summary in summaries)
        {
            var explicitType = DetectExplicitPatternType(summary);
            if (explicitType is not null)
                return explicitType;
        }

        return "RepeatedFailureMode";
    }

    private static string? DetectExplicitPatternType(string summary)
    {
        if (summary.Contains("governance block", StringComparison.OrdinalIgnoreCase) || summary.Contains("blocked by governance", StringComparison.OrdinalIgnoreCase))
            return "RepeatedGovernanceBlock";
        if (summary.Contains("manual correction", StringComparison.OrdinalIgnoreCase) || summary.Contains("human corrected", StringComparison.OrdinalIgnoreCase))
            return "RepeatedManualCorrection";
        if (summary.Contains("stale memory", StringComparison.OrdinalIgnoreCase) || summary.Contains("outdated memory", StringComparison.OrdinalIgnoreCase))
            return "StaleMemory";
        if (summary.Contains("contradiction", StringComparison.OrdinalIgnoreCase) || summary.Contains("contradictory", StringComparison.OrdinalIgnoreCase))
            return "Contradiction";
        if (summary.Contains("retrieval miss", StringComparison.OrdinalIgnoreCase) || summary.Contains("missing retrieved context", StringComparison.OrdinalIgnoreCase))
            return "RetrievalMiss";
        if (summary.Contains("duplicate proposal", StringComparison.OrdinalIgnoreCase))
            return "DuplicateProposal";
        if (summary.Contains("missing project convention", StringComparison.OrdinalIgnoreCase))
            return "MissingProjectConvention";
        if (summary.Contains("missing operational rule", StringComparison.OrdinalIgnoreCase))
            return "MissingOperationalRule";

        return null;
    }

    private static MemoryImprovementPatternType ToMemoryImprovementPatternType(string patternType) =>
        patternType switch
        {
            "RepeatedGovernanceBlock" => MemoryImprovementPatternType.RepeatedGovernanceBlock,
            "RepeatedManualCorrection" => MemoryImprovementPatternType.RepeatedManualCorrection,
            "Contradiction" => MemoryImprovementPatternType.RepeatedContradiction,
            "StaleMemory" => MemoryImprovementPatternType.StaleMemoryPattern,
            "RetrievalMiss" => MemoryImprovementPatternType.RepeatedRetrievalMiss,
            "DuplicateProposal" => MemoryImprovementPatternType.DuplicateProposalPattern,
            "MissingProjectConvention" => MemoryImprovementPatternType.RepeatedSuccessfulDecision,
            "MissingOperationalRule" => MemoryImprovementPatternType.RepeatedSuccessfulDecision,
            _ => MemoryImprovementPatternType.RepeatedFailureMode
        };

    private static string NormalizeSummary(string summary) =>
        string.Join(' ', summary.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string SplitWords(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var characters = new List<char> { value[0] };
        for (var i = 1; i < value.Length; i++)
        {
            if (char.IsUpper(value[i]) && !char.IsWhiteSpace(value[i - 1]))
                characters.Add(' ');
            characters.Add(value[i]);
        }

        return new string(characters.ToArray());
    }

    private static IReadOnlyList<string> CollectEvidenceRefs(
        IEnumerable<RealRunEvidenceItem> evidenceItems,
        IEnumerable<string> extraRefs)
    {
        var refs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in evidenceItems)
        {
            refs.Add(item.EvidenceId);
            refs.Add(item.RefId);
            foreach (var evidenceRef in item.EvidenceRefs)
            {
                if (!string.IsNullOrWhiteSpace(evidenceRef))
                    refs.Add(evidenceRef);
            }
        }

        foreach (var extraRef in extraRefs)
        {
            if (!string.IsNullOrWhiteSpace(extraRef))
                refs.Add(extraRef);
        }

        return refs.Order(StringComparer.Ordinal).ToArray();
    }

    private static ManualRealRunMemoryImprovementIssue ToIssue(string code, string message, string field) =>
        new()
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityError,
            Message = message,
            Field = field
        };

    private static ManualRealRunMemoryImprovementResult Failed(
        string runId,
        ManualRealRunMemoryImprovementStatus status,
        IReadOnlyList<ManualRealRunMemoryImprovementIssue> issues,
        RealRunMemoryImprovementStage? stage = null,
        RealRunMemoryImprovementSummary? summary = null,
        Audit.AgentRunAuditEnvelope? auditEnvelope = null) =>
        new()
        {
            Succeeded = false,
            Status = status,
            MemoryImprovementRunId = runId,
            ImprovementStage = stage,
            Summary = summary,
            AuditEnvelope = auditEnvelope,
            Issues = issues
        };
}
